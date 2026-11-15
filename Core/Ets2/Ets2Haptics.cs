namespace DualSenseHub.Core.Ets2;

/// <summary>One haptic output frame; <see cref="Active"/> names the features that contributed (for tests/diagnostics).</summary>
public sealed record HapticFrame(byte Strong, byte Weak, TriggerEffect Left, TriggerEffect Right, string Active)
{
    public static HapticFrame Idle() => new(0, 0, TriggerEffect.Off(), TriggerEffect.Off(), "");
}

/// <summary>
/// Pure haptics computation from the telemetry packet stream and <see cref="Ets2Settings"/>: no I/O, time is passed in.
/// Call <see cref="Update"/> at about 30 Hz. Features: road feel, gear shift kick, brake trigger, throttle kickdown, engine rumble, ABS.
/// </summary>
public sealed class Ets2Haptics
{
    private const double DecayMs = 150;       // road pulse envelope time constant
    private const double DeflectionDeadzone = 0.12; // m/s above the wheel's own running baseline that is ignored (flat road)
    private const double DeflectionFull = 0.8;      // m/s above baseline that maps to a full pulse
    private const double BaselineTau = 1.0;         // s, running average of suspension speed (normal body motion)
    private const double MinRoadSpeedKmh = 5, MinAirborneS = 0.3, MinDt = 0.033;
    private const long KickMs = 80, AbsHoldMs = 120;
    private const double PercentToUnit = 0.01;

    private readonly float[] _prevSusp = new float[Ets2Packet.MaxWheels];
    private readonly bool[] _spun = new bool[Ets2Packet.MaxWheels]; // wheel was seen rotating freely, so its data is trustworthy
    private byte _prevGround;
    private readonly double[] _suspBase = new double[Ets2Packet.MaxWheels];
    private readonly double[] _airS = new double[Ets2Packet.MaxWheels];
    private bool _hasPrev;
    private long _lastSeq = -1, _lastPktMs, _lastTickMs;
    private double _envL, _envR;
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
        _hasPrev = false; _hasPrevCrash = false; _crashStart = -1; _envL = _envR = 0; _kickUntil = _absUntil = -1; _lastSeq = -1;
    }

    public HapticFrame Update(Ets2Packet p, bool connected, Ets2Settings s, long nowMs)
    {
        if (!connected || p.Paused) { Reset(); _lastTickMs = nowMs; return HapticFrame.Idle(); }

        double tickDt = Math.Clamp(nowMs - _lastTickMs, 0, 200);
        _lastTickMs = nowMs;
        double decay = Math.Exp(-tickDt / DecayMs);
        _envL *= decay; _envR *= decay;

        if (p.Seq != _lastSeq)
        {
            double pktDt = (nowMs - _lastPktMs) / 1000.0;
            if (_hasPrev && pktDt > 0.005 && pktDt < 0.25) Road(p, pktDt);
            Crash(p, pktDt, nowMs);
            Gear(p, nowMs);
            for (int i = 0; i < Ets2Packet.MaxWheels; i++) _prevSusp[i] = p.Susp != null && i < p.Susp.Length ? p.Susp[i] : 0;
            _prevGround = p.GroundMask; _hasPrev = true; _lastSeq = p.Seq; _lastPktMs = nowMs;
        }

        double strong = 0, weak = 0;
        var active = new List<string>(4);
        double lvRoad = s.RoadFeelLevel * PercentToUnit;
        if (s.RoadFeel && (_envL > 0.02 || _envR > 0.02))
        {
            strong = Math.Max(strong, _envL * lvRoad);
            weak = Math.Max(weak, _envR * lvRoad * 0.8);
            active.Add("road");
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
            double k = 0.8 * s.GearShiftLevel * PercentToUnit;
            strong = Math.Max(strong, k); weak = Math.Max(weak, k);
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

    private void Road(Ets2Packet p, double dt)
    {
        if (p.Susp == null || p.WheelCount == 0) return;
        bool sidesKnown = p.LeftMask != 0 && p.LeftMask != (1 << p.WheelCount) - 1;
        bool moving = p.SpeedKmh >= MinRoadSpeedKmh;
        dt = Math.Max(dt, MinDt); // back-to-back packets (flag changes) would otherwise amplify tiny deltas
        double a = Math.Min(1, dt / BaselineTau);
        for (int i = 0; i < p.WheelCount && i < Ets2Packet.MaxWheels; i++)
        {
            bool now = (p.GroundMask >> i & 1) != 0, was = (_prevGround >> i & 1) != 0;
            double x = 0;
            if (now && was)
            {
                // Pulse only on suspension speed well above this wheel's normal motion, so steady body sway never buzzes.
                double v = Math.Abs(p.Susp[i] - _prevSusp[i]) / dt;
                double excess = v - _suspBase[i];
                _suspBase[i] += (v - _suspBase[i]) * a;
                if (moving) x = Math.Clamp((excess - DeflectionDeadzone) / (DeflectionFull - DeflectionDeadzone), 0, 1);
            }
            else if (now && !was && moving && _airS[i] >= MinAirborneS) x = 0.9; // real landing, not a lift axle or flicker
            _airS[i] = now ? 0 : _airS[i] + dt;
            if (x <= 0) continue;
            bool isLeft = (p.LeftMask >> i & 1) != 0;
            if (!sidesKnown || isLeft) _envL = Math.Max(_envL, x);
            if (!sidesKnown || !isLeft) _envR = Math.Max(_envR, x);
        }
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
