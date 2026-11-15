namespace DualSenseHub.Core.Ets2;

/// <summary>One haptic output frame; <see cref="Active"/> names the features that contributed (for tests/diagnostics).</summary>
public sealed record HapticFrame(byte Strong, byte Weak, TriggerEffect Left, TriggerEffect Right, string Active)
{
    public static HapticFrame Idle() => new(0, 0, TriggerEffect.Off(), TriggerEffect.Off(), "");
}

/// <summary>Per-wheel road-feel internals of the last packet (suspension speed, running baseline, excess); for recording/tuning only.</summary>
public sealed class WheelDiagnostics
{
    public readonly double[] Vel = new double[Ets2Packet.MaxWheels], Base = new double[Ets2Packet.MaxWheels], Excess = new double[Ets2Packet.MaxWheels];
    public void Clear() { Array.Clear(Vel); Array.Clear(Base); Array.Clear(Excess); }
}

/// <summary>
/// Pure haptics computation from the telemetry packet stream and <see cref="Ets2Settings"/>: no I/O, time is passed in.
/// Call <see cref="Update"/> at about 30 Hz. Features: road feel (surface texture + hard hits), gear shift kick, brake trigger, throttle kickdown, engine rumble, ABS.
/// </summary>
public sealed class Ets2Haptics
{
    // Road feel has two parts: a surface texture chosen by the substance under the wheels (suspension deflection does not track
    // perceived roughness: highway seams give 0.4-2 m/s spikes), and rare hard hits from suspension speed (kerbs, potholes, landings).
    private const double HitThresholdMax = 3.0, HitThresholdMin = 1.4; // m/s above baseline at sensitivity 0 / 100 %
    // Broken road = sustained density of moderate suspension spikes. Highway seams are isolated: recorded 1 s density
    // (share of one side's wheel samples with excess > 0.15 m/s) peaked at 0.09 on highways, so rough texture starts above that.
    private const double RoughSpike = 0.15, RoughWindowMs = 1000, RoughStart = 0.12, RoughFull = 0.4, RoughGain = 0.6;
    private readonly Queue<(long T, int L, int NL, int R, int NR)> _rough = new();
    private int _rL, _rNL, _rR, _rNR;
    private double _roughL, _roughR;
    /// <summary>1 s spike density per side (diagnostics).</summary>
    public (double L, double R) Roughness => (_roughL, _roughR);
    private const double HitFull = 5.0;             // m/s above baseline that maps to a full hit
    private const long HitMs = 70;                  // crisp pulse on the strong motor
    private const double BaselineTau = 1.0;         // s, running average of suspension speed (normal body motion)
    private const double TextureMinKmh = 5, TextureFullKmh = 60;
    private const double RoughBase = 0.25, RoughSpeedGain = 0.45;
    private const double RumbleBase = 0.45, RumbleSpeedGain = 0.5;
    private const long RumblePeriodMs = 67;         // ~15 Hz rumble strip
    private const double MinRoadSpeedKmh = 5, MinAirborneS = 0.3, MinDt = 0.033;
    private const long KickMs = 45, AbsHoldMs = 120;
    private const double GearKickPeak = 0.35;        // strong motor (both sides of the grip), kept short so it reads as a tick
    private const long HitRefractoryMs = 120;        // minimum gap between hits so they read as discrete bumps
    private const double PercentToUnit = 0.01;

    private readonly float[] _prevSusp = new float[Ets2Packet.MaxWheels];
    private readonly bool[] _spun = new bool[Ets2Packet.MaxWheels]; // wheel was seen rotating freely, so its data is trustworthy
    private byte _prevGround;
    private readonly double[] _suspBase = new double[Ets2Packet.MaxWheels];
    private readonly double[] _airS = new double[Ets2Packet.MaxWheels];
    private bool _hasPrev;
    private long _lastSeq = -1, _lastPktMs, _lastTickMs;
    private enum Surface { Road, Rough, Rumble }
    private Surface _surfL, _surfR;                  // worst surface under the wheels on each side
    private long _hitUntil = -1, _lastHit = -100000;
    private double _hitAmp;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> LoggedUnknown = new();

    /// <summary>Substance name table (index = substance id) from the plugin's DSHS datagram; empty until received.</summary>
    public string[] SubstanceNames { get; set; } = [];
    /// <summary>Substance under the wheels in the last packet (the first non-road one, else the first wheel's), "" when unknown.</summary>
    public string CurrentSurface { get; private set; } = "";
    private int _lastGear = int.MinValue;
    private long _kickUntil = -1, _absUntil = -1;
    // Crash detection state
    private const double WearJump = 0.002;      // minimum rise of the summed wear that counts as damage
    private const double WearFull = 0.04;       // wear rise that maps to a full impact
    private const double AccelThreshold = 25;   // m/s^2; braking (~8) and potholes (~15) stay below
    private const double AccelFull = 60;
    private bool _hasPrevCrash;
    private double _prevWear;
    private int _prevWheels;
    private float _prevRpmLimit, _prevFuelCap;
    private long _crashStart = -1, _crashDur;
    private double _crashPeak;

    private void Reset()
    {
        _hasPrev = false; _hasPrevCrash = false; _crashStart = -1; _surfL = _surfR = Surface.Road; _hitUntil = -1; _lastHit = -100000; CurrentSurface = ""; _kickUntil = _absUntil = -1; _lastSeq = -1;
    }

    public HapticFrame Update(Ets2Packet p, bool connected, Ets2Settings s, long nowMs)
    {
        if (!connected || p.Paused) { Reset(); _lastTickMs = nowMs; return HapticFrame.Idle(); }

        double tickDt = Math.Clamp(nowMs - _lastTickMs, 0, 200);
        _lastTickMs = nowMs;

        if (p.Seq != _lastSeq)
        {
            double pktDt = (nowMs - _lastPktMs) / 1000.0;
            Diag.Clear();
            if (_hasPrev && pktDt > 0.005 && pktDt < 0.25) Road(p, pktDt, nowMs, s.RoadSensitivity * PercentToUnit);
            Crash(p, pktDt, nowMs);
            Gear(p, nowMs);
            for (int i = 0; i < Ets2Packet.MaxWheels; i++) _prevSusp[i] = p.Susp != null && i < p.Susp.Length ? p.Susp[i] : 0;
            _prevGround = p.GroundMask; _hasPrev = true; _lastSeq = p.Seq; _lastPktMs = nowMs;
        }

        double strong = 0, weak = 0;
        var active = new List<string>(4);
        double lvRoad = s.RoadFeelLevel * PercentToUnit;
        if (s.RoadFeel)
        {
            double sf = Math.Clamp((p.SpeedKmh - TextureMinKmh) / (TextureFullKmh - TextureMinKmh), 0, 1);
            double tl = Texture(_surfL, sf, nowMs), tr = Texture(_surfR, sf, nowMs);
            // Broken asphalt: continuous texture scaled by spike density; side by wheel side.
            if (_surfL == Surface.Road) tl = Math.Max(tl, RoughGain * sf * Math.Clamp((_roughL - RoughStart) / (RoughFull - RoughStart), 0, 1));
            if (_surfR == Surface.Road) tr = Math.Max(tr, RoughGain * sf * Math.Clamp((_roughR - RoughStart) / (RoughFull - RoughStart), 0, 1));
            if (tl > 0 || tr > 0)
            {
                strong = Math.Max(strong, tl * lvRoad); weak = Math.Max(weak, tr * lvRoad);
                active.Add("tex");
            }
            if (nowMs < _hitUntil)
            {
                strong = Math.Max(strong, _hitAmp * lvRoad);
                active.Add("hit");
            }
        }
        if (s.CrashRumble && _crashStart >= 0 && nowMs < _crashStart + _crashDur)
        {
            double frac = (nowMs - _crashStart) / (double)_crashDur;
            double k = Math.Clamp(_crashPeak * (1 - frac) * s.CrashRumbleLevel * PercentToUnit, 0, 1);
            strong = Math.Max(strong, k); weak = Math.Max(weak, k); // above road feel and gear kick
            active.Add("crash");
        }
        if (s.GearShift && nowMs < _kickUntil)
        {
            strong = Math.Max(strong, GearKickPeak * s.GearShiftLevel * PercentToUnit);
            active.Add("gear");
        }
        if (s.EngineRumble && p.EngineOn && p.Rpm > 50)
        {
            double ratio = Math.Clamp(p.Rpm / (p.RpmLimit > 0 ? p.RpmLimit : 2500), 0, 1);
            strong = Math.Min(1, strong + s.EngineRumbleLevel * PercentToUnit * (0.15 + 0.25 * ratio));
            active.Add("engine");
        }

        var left = TriggerEffect.Off();
        var right = TriggerEffect.Off();
        // R2 is a static effect (light travel, kickdown detent), so it only changes with the settings.
        if (s.GasTrigger) { right = Kickdown(s); active.Add("kickdown"); }
        if (s.BrakeTrigger && p.EngineOn)
        {
            double lvBrake = s.BrakeTriggerLevel * PercentToUnit;
            if (p.ParkingBrake) { left = TriggerEffect.Continuous(0, Force(255, lvBrake)); active.Add("parking"); }
            else { left = TriggerEffect.Continuous(1, Force(30 + 190 * Math.Clamp(p.EffBrake, 0, 1), lvBrake)); active.Add("brake"); }
        }
        if (s.Abs)
        {
            if (Lockup(p)) _absUntil = nowMs + AbsHoldMs;
            if (nowMs < _absUntil)
            {
                int amp = 1 + (int)Math.Round(s.AbsLevel * PercentToUnit * 7);
                left = TriggerEffect.Vibration(2, amp, 30);
                weak = Math.Max(weak, 0.4 * s.AbsLevel * PercentToUnit);
                active.Add("abs");
            }
        }
        return new HapticFrame(Motor(strong), Motor(weak), left, right, string.Join(",", active));
    }

    /// <summary>Weapon-style detent that builds resistance and snaps through near the end of travel; nothing before it keeps the pedal light.</summary>
    private static TriggerEffect Kickdown(Ets2Settings s)
    {
        int zone = (int)Math.Round(Math.Clamp(s.KickdownPoint, 0, 100) / 100.0 * 9);
        int start = Math.Clamp(zone, 2, 6);
        int strength = 1 + (int)Math.Round(Math.Clamp(s.GasTriggerLevel, 0, 100) * PercentToUnit * 7);
        return TriggerEffect.Weapon(start, start + 1, strength);
    }

    public WheelDiagnostics Diag { get; } = new();

    /// <summary>Maps a substance name to its feel by substring; unknown names count as road (logged once to stderr).</summary>
    public static (bool Known, bool Rumble, bool Rough) Classify(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("rumble") || n.Contains("kerb") || n.Contains("curb")) return (true, true, false);
        foreach (var k in new[] { "dirt", "gravel", "mud", "grass", "sand", "snow", "offroad" })
            if (n.Contains(k)) return (true, false, true);
        foreach (var k in new[] { "road", "asphalt", "concrete", "tarmac", "highway", "street", "pavement", "none" })
            if (n.Contains(k)) return (true, false, false);
        return (false, false, false);
    }

    private string NameOf(Ets2Packet p, int wheel)
    {
        if (p.Substance == null || wheel >= p.Substance.Length) return "";
        int idx = p.Substance[wheel];
        var names = SubstanceNames;
        return idx < names.Length ? names[idx] : "";
    }

    /// <summary>Amplitude (before the level setting) of the texture for a side; rumble strips pulse at about 15 Hz.</summary>
    private static double Texture(Surface k, double speedFactor, long nowMs)
    {
        if (speedFactor <= 0) return 0;
        return k switch
        {
            Surface.Rough => RoughBase + RoughSpeedGain * speedFactor,
            Surface.Rumble => nowMs % RumblePeriodMs < RumblePeriodMs / 2 ? RumbleBase + RumbleSpeedGain * speedFactor : 0,
            _ => 0,
        };
    }

    private void Road(Ets2Packet p, double dt, long nowMs, double sensitivity)
    {
        if (p.Susp == null || p.WheelCount == 0) return;
        bool sidesKnown = p.LeftMask != 0 && p.LeftMask != (1 << p.WheelCount) - 1;
        bool moving = p.SpeedKmh >= MinRoadSpeedKmh;
        dt = Math.Max(dt, MinDt); // back-to-back packets (flag changes) would otherwise amplify tiny deltas
        double a = Math.Min(1, dt / BaselineTau);
        double threshold = HitThresholdMax - (HitThresholdMax - HitThresholdMin) * Math.Clamp(sensitivity, 0, 1);
        Surface sl = Surface.Road, sr = Surface.Road;
        string primary = "", first = "";
        int cl = 0, nl = 0, cr = 0, nr = 0;
        for (int i = 0; i < p.WheelCount && i < Ets2Packet.MaxWheels; i++)
        {
            bool now = (p.GroundMask >> i & 1) != 0, was = (_prevGround >> i & 1) != 0;
            bool isLeft = (p.LeftMask >> i & 1) != 0;
            if (now)
            {
                string name = NameOf(p, i);
                var (known, rumble, rough) = Classify(name);
                if (name.Length > 0 && !known && LoggedUnknown.TryAdd(name, 0))
                    Console.Error.WriteLine($"ETS2: unknown substance '{name}', treated as road");
                if (first.Length == 0) first = name;
                var k = rumble ? Surface.Rumble : rough ? Surface.Rough : Surface.Road;
                if (k != Surface.Road && primary.Length == 0) primary = name;
                if (!sidesKnown || isLeft) sl = (Surface)Math.Max((int)sl, (int)k);
                if (!sidesKnown || !isLeft) sr = (Surface)Math.Max((int)sr, (int)k);
            }
            double x = 0;
            if (now && was)
            {
                // A hit is suspension speed far above this wheel's normal motion; seams and body sway stay below the threshold.
                double v = Math.Abs(p.Susp[i] - _prevSusp[i]) / dt;
                double excess = v - _suspBase[i];
                Diag.Vel[i] = v; Diag.Base[i] = _suspBase[i]; Diag.Excess[i] = excess;
                bool spike = moving && excess > RoughSpike;
                if (!sidesKnown || isLeft) { nl++; if (spike) cl++; }
                if (!sidesKnown || !isLeft) { nr++; if (spike) cr++; }
                _suspBase[i] += (v - _suspBase[i]) * a;
                if (moving && excess > threshold) x = 0.6 + 0.4 * Math.Clamp((excess - threshold) / (HitFull - threshold), 0, 1);
            }
            else if (now && !was && moving && _airS[i] >= MinAirborneS) x = 0.9; // real landing, not a lift axle or flicker
            _airS[i] = now ? 0 : _airS[i] + dt;
            if (x > 0 && nowMs - _lastHit >= HitRefractoryMs)
            { _hitAmp = x; _hitUntil = nowMs + HitMs; _lastHit = nowMs; }
        }
        _surfL = sl; _surfR = sr;
        _rough.Enqueue((nowMs, cl, nl, cr, nr)); _rL += cl; _rNL += nl; _rR += cr; _rNR += nr;
        while (_rough.Count > 0 && nowMs - _rough.Peek().T > RoughWindowMs)
        { var o = _rough.Dequeue(); _rL -= o.L; _rNL -= o.NL; _rR -= o.R; _rNR -= o.NR; }
        _roughL = _rNL > 0 ? (double)_rL / _rNL : 0; _roughR = _rNR > 0 ? (double)_rR / _rNR : 0;
        CurrentSurface = primary.Length > 0 ? primary : first;
    }

    /// <summary>
    /// Detects an impact from a jump in summed wear (repairs, i.e. decreases, are ignored) or an acceleration spike.
    /// The first frame, a long packet gap and a truck/config change only update the baseline.
    /// </summary>
    private void Crash(Ets2Packet p, double pktDt, long nowMs)
    {
        bool cfgSame = p.WheelCount == _prevWheels && p.RpmLimit == _prevRpmLimit && p.FuelCapacity == _prevFuelCap;
        if (_hasPrevCrash && cfgSame && pktDt < 0.25)
        {
            double impact = 0;
            double dw = p.WearTotal - _prevWear;
            if (dw > WearJump) impact = Math.Max(impact, Math.Clamp(0.25 + dw / WearFull, 0, 1));
            double mag = p.AccelMagnitude;
            if (mag > AccelThreshold) impact = Math.Max(impact, Math.Clamp(0.2 + (mag - AccelThreshold) / (AccelFull - AccelThreshold), 0, 1));
            if (impact > 0)
            {
                double remaining = _crashStart >= 0 && nowMs < _crashStart + _crashDur
                    ? _crashPeak * (1 - (nowMs - _crashStart) / (double)_crashDur) : 0;
                _crashPeak = Math.Max(remaining, 0.5 + 0.5 * impact);
                _crashStart = nowMs;
                _crashDur = (long)(150 + 250 * impact);
            }
        }
        _prevWear = p.WearTotal; _prevWheels = p.WheelCount; _prevRpmLimit = p.RpmLimit; _prevFuelCap = p.FuelCapacity;
        _hasPrevCrash = true;
    }

    private void Gear(Ets2Packet p, long nowMs)
    {
        // Neutral (0) is ignored so brief neutral flaps during a shift do not count as a new gear.
        if (p.Gear == 0) return;
        if (_lastGear != int.MinValue && p.Gear != _lastGear) _kickUntil = nowMs + KickMs;
        _lastGear = p.Gear;
    }

    /// <summary>Braking while a wheel that normally spins freely turns much slower than the truck moves.</summary>
    private bool Lockup(Ets2Packet p)
    {
        if (p.Radius == null || p.WheelVel == null || p.WheelCount == 0) return false;
        double speed = Math.Abs(p.Speed);
        bool braking = p.EffBrake > 0.3 && speed > 3;
        bool locked = false;
        for (int i = 0; i < p.WheelCount && i < Ets2Packet.MaxWheels; i++)
        {
            if (p.Radius[i] <= 0 || (p.GroundMask >> i & 1) == 0) continue;
            double surf = Math.Abs(p.WheelVel[i]) * 2 * Math.PI * p.Radius[i]; // rotations/s to m/s
            if (surf > 0.7 * speed && speed > 3) _spun[i] = true;
            if (braking && _spun[i] && surf < 0.5 * speed) locked = true;
        }
        return locked;
    }

    /// <summary>Quantized to steps of 16 so small changes do not re-send the trigger effect.</summary>
    private static byte Force(double v, double level) => (byte)Math.Min(255, (int)Math.Round(Math.Clamp(v * level, 0, 255) / 16) * 16);

    /// <summary>Quantized to steps of 4 to limit report traffic while the envelope decays.</summary>
    private static byte Motor(double v) => (byte)Math.Min(255, (int)Math.Round(Math.Clamp(v, 0, 1) * 255 / 4) * 4);
}
