using DualSenseHub.Core;
using DualSenseHub.Core.Ets2;

namespace DualSenseHub;

/// <summary>Headless commands: --info, --apply-last, --set-color RRGGBB, --ets2-sim, --ets2-listen-test, --ets2-haptics-test, --rumble-test.</summary>
public static class Cli
{
    public static bool IsCliRun(string[] args) =>
        args.Contains("--status") || args.Contains("--quit") || args.Contains("--info") || args.Contains("--apply-last") || args.Contains("--set-color")
        || args.Contains("--ets2-sim") || args.Contains("--ets2-haptics-test") || args.Contains("--ets2-settings-test") || args.Contains("--rumble-test") || args.Contains("--ets2-listen-test") || args.Contains("--ets2-auto-test")
        || args.Contains("--record") || args.Contains("--analyze-rec");

    public static int Run(string[] args)
    {
        if (args.Contains("--status")) return SingleInstance.Query("status", out var st) ? Print(st) : NoInstance();
        if (args.Contains("--quit")) return SingleInstance.Query("quit", out var q) ? Print(q) : NoInstance();
        if (args.Contains("--record"))
        {
            int ri = Array.IndexOf(args, "--record");
            string arg = ri + 1 < args.Length && !args[ri + 1].StartsWith("--") ? args[ri + 1] : "";
            return SingleInstance.Query("record " + arg, out var rr) ? Print(rr) : NoInstance();
        }
        if (args.Contains("--analyze-rec"))
        {
            int ai = Array.IndexOf(args, "--analyze-rec");
            return ai + 1 < args.Length ? Ets2RecAnalyzer.Run(args[ai + 1]) : Print("usage: --analyze-rec <file.csv>");
        }
        if (args.Contains("--info")) return Info();
        if (args.Contains("--set-color")) return SetColor(args);
        if (args.Contains("--ets2-haptics-test")) return Ets2HapticsTest(args);
        if (args.Contains("--ets2-settings-test")) { var e = ConfigStore.LoadEts2(); Console.WriteLine($"Version={e.Version} Source={e.Source} Gas={e.GasTrigger}/{e.GasTriggerLevel} Kick={e.KickdownPoint} Brake={e.BrakeTrigger}/{e.BrakeTriggerLevel} MaxSpeed={e.MaxSpeedKmh} Blinker={e.BlinkerColor}"); return 0; }
        if (args.Contains("--rumble-test")) return RumbleTest();
        if (args.Contains("--ets2-sim")) return Ets2SimRun(args);
        if (args.Contains("--ets2-listen-test")) return Ets2ListenTest(args);
        if (args.Contains("--ets2-auto-test")) return Ets2AutoTest(args);
        return ApplyLast(args);
    }

    private static int Print(string s) { Console.WriteLine(s); return 0; }
    private static int NoInstance() { Console.Error.WriteLine("No running instance."); return 1; }

    private static int Info()
    {
        var found = DeviceDiscovery.Scan();
        if (found.Count == 0) { Console.WriteLine("No DualSense controller found."); return 1; }
        foreach (var d in found)
        {
            Console.WriteLine($"Controller:      {d.ModelName} (054C:{d.ProductId})");
            Console.WriteLine($"Connection:      {d.Connection}");
            Console.WriteLine($"hidraw:          {d.HidrawPath}");
            Console.WriteLine($"HID sysfs:       {d.HidSysPath}");
            Console.WriteLine($"MAC:             {d.Mac ?? "(unknown)"}");
            Console.WriteLine($"Lightbar LED:    {d.LightbarLed ?? "(none)"}");
            if (d.LightbarLed != null)
            {
                bool w = false;
                try { using var f = new FileStream(Path.Combine(d.LightbarLed, "multi_intensity"), FileMode.Open, FileAccess.Write); w = true; } catch { }
                Console.WriteLine($"  writable:      {(w ? "yes" : "NO (permission denied; run scripts/install-udev.sh)")}");
                Console.WriteLine($"  multi_intens.: {DeviceDiscovery.ReadText(Path.Combine(d.LightbarLed, "multi_intensity"))}");
                Console.WriteLine($"  brightness:    {DeviceDiscovery.ReadText(Path.Combine(d.LightbarLed, "brightness"))}/{DeviceDiscovery.ReadText(Path.Combine(d.LightbarLed, "max_brightness"))}");
            }
            Console.WriteLine($"Player LEDs:     {d.PlayerLeds.Length}");
            Console.WriteLine($"Mic LED:         {d.MicLed ?? "(none)"}");
            Console.WriteLine($"Battery sysfs:   {d.BatteryDir ?? "(none)"}");
            var (pct, st) = DeviceDiscovery.ReadBattery(d);
            Console.WriteLine($"Battery:         {(pct.HasValue ? pct + "%" : "?")} ({st ?? "?"})");

            using var hid = HidrawDevice.TryOpen(d.HidrawPath, d.Connection == ConnectionType.Bluetooth, out var err, out var denied);
            if (hid == null)
            {
                Console.WriteLine($"hidraw access:   FAILED - {err}{(denied ? " (run scripts/install-udev.sh)" : "")}");
                continue;
            }
            Console.WriteLine("hidraw access:   OK");
            var fw = hid.ReadFirmwareInfo();
            Console.WriteLine(fw == null
                ? "Firmware:        (feature report 0x20 failed)"
                : $"Firmware:        build {fw.BuildDate} {fw.BuildTime}, hw {fw.HardwareVersion}, fw {fw.FirmwareVersion}");
            Console.WriteLine($"Pairing MAC:     {hid.ReadMac() ?? "(feature report 0x09 failed)"}");
        }
        return 0;
    }

    private static int ApplyLast(string[] args)
    {
        int wait = 15;
        foreach (var a in args)
            if (a.StartsWith("--wait=") && int.TryParse(a[7..], out var w)) wait = w;

        var settings = ConfigStore.LoadSettings();
        if (!settings.HasSaved) { Console.WriteLine("No saved color; nothing to apply."); return 0; }

        using var svc = new ControllerService();
        var deadline = DateTime.UtcNow.AddSeconds(wait);
        while (true)
        {
            svc.Poll();
            if (svc.Connected) break;
            if (DateTime.UtcNow >= deadline) { Console.Error.WriteLine("No DualSense controller found."); return 1; }
            Thread.Sleep(1000);
        }

        var s = settings.Last;
        var (r, g, b) = EffectEngine.Compute(s, 0, svc.BatteryPercent);
        if (s.Effect == LightEffect.Breathing) (r, g, b) = EffectEngine.Compute(new LightSettings { ColorHex = s.ColorHex, Brightness = s.Brightness }, 0, null);
        svc.SetLightbar(r, g, b);
        svc.SetPlayerLeds((byte)s.PlayerMask, (byte)s.PlayerBrightness);
        if (s.MicLed) svc.SetMicLed(true);
        if (svc.PermissionDenied) { Console.Error.WriteLine("Permission denied; run scripts/install-udev.sh."); return 2; }
        Console.WriteLine($"Applied lightbar {r} {g} {b}.");
        return 0;
    }

    private static int SetColor(string[] args)
    {
        int i = Array.IndexOf(args, "--set-color");
        string hex = i + 1 < args.Length ? args[i + 1] : "";
        var (r, g, b) = Ets2Settings.ParseHex(hex, (0, 0, 0));
        if (hex.TrimStart('#').Length != 6 || !int.TryParse(hex.TrimStart('#'), System.Globalization.NumberStyles.HexNumber, null, out _))
        { Console.Error.WriteLine("Usage: --set-color RRGGBB"); return 64; }

        using var svc = new ControllerService();
        svc.Poll();
        if (!svc.Connected) { Console.Error.WriteLine("No DualSense controller found."); return 1; }
        Console.WriteLine($"Controller: {svc.Current!.ModelName} via {svc.Current.Connection}, hidraw {(svc.Hid != null ? "open" : "NOT open: " + svc.HidError)}, sysfs LED {(svc.Current.LightbarLed ?? "none")}");
        svc.SetLightbar(r, g, b);
        bool raw = svc.Hid != null && svc.Hid.Send(OutputGroups.Lightbar);
        Console.WriteLine($"Set lightbar {r} {g} {b}. sysfs writable: {(svc.Current.LightbarLed != null && !svc.PermissionDenied ? "yes" : "no")}; raw hidraw output report ({(svc.Current.Connection == ConnectionType.Bluetooth ? "0x31 + CRC32" : "0x02")}) write: {(raw ? "OK" : "FAILED")}.");
        return raw || (svc.Current.LightbarLed != null && !svc.PermissionDenied) ? 0 : 2;
    }

    private static int Ets2SimRun(string[] args)
    {
        int secs = 15;
        foreach (var a in args) if (a.StartsWith("--seconds=") && int.TryParse(a[10..], out var s)) secs = s;
        Console.WriteLine($"Sending simulated ETS2 packets to 127.0.0.1:{Ets2Packet.Port} for {secs} s...");
        Ets2Sim.RunAsync(secs, default, PortArg(args)).GetAwaiter().GetResult();
        return 0;
    }

    /// <summary>Runs the shared listener + auto-start logic without UI and prints the transitions.</summary>
    private static int Ets2AutoTest(string[] args)
    {
        int secs = 15;
        foreach (var a in args) if (a.StartsWith("--seconds=") && int.TryParse(a[10..], out var s)) secs = s;
        using var l = new Ets2TelemetryListener();
        using var mode = new Ets2Mode(l);
        bool running = false;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        void Log(string m) => Console.WriteLine($"[{sw.Elapsed.TotalSeconds:F1}s] {m}");
        var auto = new Ets2AutoStart(l, () => running, () => { running = true; Log("mode started"); }, () => { running = false; Log("mode stopped"); });
        auto.Transition += t => Log(t);
        l.Start(PortArg(args));
        Thread.Sleep(300);
        Log(l.Listening ? "listening" : "listen error: " + l.Error);
        var seenRunning = false; bool seenStopped = false;
        while (sw.Elapsed.TotalSeconds < secs)
        {
            auto.Tick();
            if (running) seenRunning = true; else if (seenRunning) seenStopped = true;
            Thread.Sleep(100);
        }
        Log($"packets={l.PacketCount} version=v{l.ProtocolVersion} started={seenRunning} stopped={seenStopped}");
        return seenRunning && seenStopped ? 0 : 1;
    }

    private static int PortArg(string[] args)
    {
        foreach (var a in args) if (a.StartsWith("--port=") && int.TryParse(a[7..], out var p)) return p;
        return Ets2Packet.Port;
    }

    private static int Ets2ListenTest(string[] args)
    {
        int secs = 3;
        foreach (var a in args) if (a.StartsWith("--seconds=") && int.TryParse(a[10..], out var s)) secs = s;
        using var l = new Ets2TelemetryListener();
        int count = 0;
        long lastPrint = 0;
        l.PacketReceived += p =>
        {
            count++;
            long now = Environment.TickCount64;
            if (now - lastPrint < 400) return;
            lastPrint = now;
            Console.WriteLine($"#{p.Seq} flags=0x{p.Flags:X2} L={p.LeftBlinker} R={p.RightBlinker} haz={p.Hazard} brake={p.Brake} eng={p.EngineOn} speed={p.SpeedKmh:F1}km/h rpm={p.Rpm:F0}/{p.RpmLimit:F0} fuel={p.Fuel:F0}/{p.FuelCapacity:F0}");
        };
        l.Start();
        if (l.Error != null) { Console.Error.WriteLine("Listen failed: " + l.Error); return 1; }
        Thread.Sleep(secs * 1000);
        Console.WriteLine($"Received {count} packets in {secs} s; connected={l.Connected}.");
        return count > 0 ? 0 : 1;
    }

    /// <summary>Feeds simulated packets directly through <see cref="Ets2Haptics"/> (no UDP, no controller) and summarizes the outputs.</summary>
    private static int Ets2HapticsTest(string[] args)
    {
        int secs = 30;
        foreach (var a in args) if (a.StartsWith("--seconds=") && int.TryParse(a[10..], out var s)) secs = s;
        var settings = new Ets2Settings { EngineRumble = true };
        var h = new Ets2Haptics();
        var totals = new SortedDictionary<string, int>();
        int ticks = 0, rumbleSends = 0, triggerSends = 0, rSends = 0;
        byte lastS = 0, lastW = 0;
        TriggerEffect? lastL = null, lastR = null;
        int maxS = 0, maxW = 0;
        string L = "off", R = "off";
        var secFeatures = new SortedSet<string>();
        uint seq = 0;
        int strayCrash = 0, crashTicks = 0;
        bool crashOnFirstFrame = false;
        Console.WriteLine("t(s)  strongMax weakMax  L2        R2        features");
        for (int ms = 0; ms < secs * 1000; ms += 33)
        {
            var p = Ets2Sim.Frame(ms / 1000.0, ++seq);
            var f = h.Update(p, true, settings, ms);
            ticks++;
            if (f.Strong != lastS || f.Weak != lastW) { rumbleSends++; lastS = f.Strong; lastW = f.Weak; }
            if (!f.Left.SameAs(lastL) || !f.Right.SameAs(lastR)) { triggerSends++; if (!f.Right.SameAs(lastR)) rSends++; lastL = f.Left; lastR = f.Right; }
            maxS = Math.Max(maxS, f.Strong); maxW = Math.Max(maxW, f.Weak);
            L = Describe(f.Left); R = Describe(f.Right);
            double tt = ms / 1000.0, cw = tt % 12 - 9;
            bool inCrashWindow = cw is >= 0 and < 0.7;
            if (ms == 0 && f.Active.Contains("crash")) crashOnFirstFrame = true;
            if (f.Active.Contains("crash") && !inCrashWindow) strayCrash++;
            if (f.Active.Contains("crash")) crashTicks++;
            foreach (var n in f.Active.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                secFeatures.Add(n);
                totals[n] = totals.GetValueOrDefault(n) + 1;
            }
            if ((ms + 33) / 1000 != ms / 1000)
            {
                Console.WriteLine($"{ms / 1000,4}  {maxS,9} {maxW,7}  {L,-9} {R,-9} {string.Join(",", secFeatures)}");
                maxS = maxW = 0; secFeatures.Clear();
            }
        }
        var paused = h.Update(Ets2Sim.Frame(1, ++seq) with { Flags = (ushort)(Ets2Sim.Frame(1, 0).Flags | 64) }, true, settings, secs * 1000 + 33);
        var gone = h.Update(default, false, settings, secs * 1000 + 66);
        Console.WriteLine($"Ticks {ticks}, rumble sends {rumbleSends}, trigger sends {triggerSends}");
        Console.WriteLine("Active ticks per feature: " + string.Join(", ", totals.Select(kv => $"{kv.Key}={kv.Value}")));
        bool idleOk(HapticFrame x) => x.Strong == 0 && x.Weak == 0 && x.Left.SameAs(TriggerEffect.Off()) && x.Right.SameAs(TriggerEffect.Off());
        Console.WriteLine($"Paused -> idle: {idleOk(paused)}; disconnected -> idle: {idleOk(gone)}");
        var rt = Ets2Sim.Frame(5.5, 7);
        var bytes = rt.ToBytes();
        bool v2ok = Ets2Packet.TryParse(bytes, out var back) && back.Gear == rt.Gear && back.WheelCount == rt.WheelCount && back.Susp![2] == rt.Susp![2] && back.Radius![0] == rt.Radius![0] && back.LeftMask == rt.LeftMask;
        bool v1ok = Ets2Packet.TryParse(bytes.AsSpan(0, 36).ToArray().Select((b, i) => i == 4 ? (byte)1 : b).ToArray(), out var old) && old.Rpm == rt.Rpm && old.WheelCount == 0;
        bool v1short = Ets2Packet.TryParse(bytes.AsSpan(0, 32).ToArray().Select((b, i) => i == 4 ? (byte)1 : b).ToArray(), out _);
        var v4 = new byte[200]; bytes.CopyTo(v4, 0); v4[4] = 5;
        bool v4ok = Ets2Packet.TryParse(v4, out var fut) && fut.Gear == rt.Gear && fut.InputThrottle >= 0 && !Ets2Packet.TryParse(new byte[10], out _);
        Console.WriteLine($"Packet round trip v2: {v2ok}; v1/36 bytes: {v1ok}; v1/32 bytes: {v1short}; future v5: {v4ok}");
        bool v3ok = Ets2Packet.TryParse(bytes.AsSpan(0, 152).ToArray().Select((b, i) => i == 4 ? (byte)3 : b).ToArray(), out var v3p) && v3p.InputThrottle >= 0 && v3p.WearTotal == 0 && v3p.RetarderSteps == 0;
        var crashFrame = Ets2Sim.Frame(9.01, 1);
        bool v4rt = Ets2Packet.TryParse(crashFrame.ToBytes(), out var v4p) && v4p.WearTotal == crashFrame.WearTotal && v4p.AccelX == crashFrame.AccelX && v4p.RetarderSteps == 4 && v4p.Retarder == crashFrame.Retarder && v4p.CruiseSpeed == crashFrame.CruiseSpeed;
        Console.WriteLine($"Packet v3/152 bytes: {v3ok}; v4 round trip: {v4rt}");
        bool crashOk = crashTicks > 0 && strayCrash == 0 && !crashOnFirstFrame;
        Console.WriteLine($"Crash jolt: {crashTicks} ticks, outside crash windows: {strayCrash}, on first frame: {crashOnFirstFrame} -> {crashOk}");
        bool detOk = CrashDetectionChecks(settings);
        bool gaugeOk = GaugeChecks();
        string[] need = ["road", "gear", "brake", "parking", "kickdown", "engine", "abs"];
        var missing = need.Where(n => !totals.ContainsKey(n)).ToArray();
        bool r2static = rSends == 1 && lastR != null && lastR.Mode == 0x25 && !totals.ContainsKey("rpm-limit");
        Console.WriteLine($"R2 static kickdown (1 send, weapon, no rpm-limit): {r2static}");
        Console.WriteLine(missing.Length == 0 ? "All features triggered." : "NOT triggered: " + string.Join(", ", missing));
        return missing.Length == 0 && idleOk(paused) && idleOk(gone) && r2static && v2ok && v1ok && v1short && v4ok && v3ok && v4rt && crashOk && detOk && gaugeOk ? 0 : 1;
    }

    private static HapticFrame Run(Ets2Haptics h, Ets2Packet p, Ets2Settings s, ref long ms, ref uint seq)
    {
        ms += 33;
        return h.Update(p with { Seq = ++seq }, true, s, ms);
    }

    /// <summary>Crash must trigger on a damage jump and on an acceleration spike, not on braking, bumps, repairs or the first frame.</summary>
    private static bool CrashDetectionChecks(Ets2Settings settings)
    {
        var s = new Ets2Settings { RoadFeel = false, GearShift = false, EngineRumble = false, Abs = false, GasTrigger = false, BrakeTrigger = false, CrashRumble = true };
        var baseP = Ets2Sim.Frame(1, 0) with { WearTotal = 0.2f, AccelX = 0, AccelY = 0, AccelZ = 0 };
        bool Crash(HapticFrame f) => f.Active.Contains("crash") && f.Strong > 0 && f.Weak > 0;
        long ms = 0; uint seq = 0; var h = new Ets2Haptics();
        bool first = !Crash(Run(h, baseP with { WearTotal = 0.5f }, s, ref ms, ref seq));      // first frame: baseline only
        bool brake = !Crash(Run(h, baseP with { WearTotal = 0.5f, AccelZ = -8 }, s, ref ms, ref seq));   // hard braking
        bool bump = !Crash(Run(h, baseP with { WearTotal = 0.5f, AccelY = 15 }, s, ref ms, ref seq));    // pothole
        bool repair = !Crash(Run(h, baseP with { WearTotal = 0.1f }, s, ref ms, ref seq));        // repair lowers wear
        bool wear = Crash(Run(h, baseP with { WearTotal = 0.13f }, s, ref ms, ref seq));          // damage jump
        for (int i = 0; i < 20; i++) Run(h, baseP with { WearTotal = 0.13f }, s, ref ms, ref seq);
        bool quiet = !Crash(Run(h, baseP with { WearTotal = 0.13f }, s, ref ms, ref seq));        // jolt decayed
        bool accel = Crash(Run(h, baseP with { WearTotal = 0.13f, AccelX = -55, AccelY = 10 }, s, ref ms, ref seq)); // spike
        for (int i = 0; i < 20; i++) Run(h, baseP with { WearTotal = 0.13f }, s, ref ms, ref seq);
        bool cfg = !Crash(Run(h, baseP with { WearTotal = 0.9f, RpmLimit = 2200 }, s, ref ms, ref seq)); // new truck: baseline only
        var off = new Ets2Settings { CrashRumble = false, RoadFeel = false };
        var h2 = new Ets2Haptics(); long m2 = 0; uint q2 = 0;
        Run(h2, baseP, off, ref m2, ref q2);
        bool disabled = !Crash(Run(h2, baseP with { WearTotal = 0.5f }, off, ref m2, ref q2));
        bool ok = first && brake && bump && repair && wear && quiet && accel && cfg && disabled;
        Console.WriteLine($"Crash detection: first-frame {first}, braking {brake}, bump {bump}, repair {repair}, wear-jump {wear}, decay {quiet}, accel-spike {accel}, config-change {cfg}, disabled {disabled} -> {ok}");
        return ok;
    }

    private static bool GaugeChecks()
    {
        var p = Ets2Sim.Frame(1, 0) with { Flags = 16 | 32, FuelCapacity = 600, Fuel = 600, CruiseSpeed = 0, Retarder = 0, RetarderSteps = 4 };
        byte M(Ets2Settings s, Ets2Packet q, long t = 0) { long lb = -100000; return Ets2Mode.Compute(q, s, t, ref lb).Mask; }
        var off = new Ets2Settings();
        var fuel = new Ets2Settings { Gauge = LedGauge.Fuel };
        var cruise = new Ets2Settings { Gauge = LedGauge.Cruise, MaxSpeedKmh = 110 };
        var ret = new Ets2Settings { Gauge = LedGauge.Retarder };
        var checks = new (string, bool)[]
        {
            ("off", M(off, p) == 0),
            ("fuel full", M(fuel, p) == 0b11111),
            ("fuel 50%", M(fuel, p with { Fuel = 300 }) == 0b00111),
            ("fuel 25%", M(fuel, p with { Fuel = 150 }) == 0b00011),
            ("fuel low blink on", M(fuel, p with { Fuel = 30 }, 0) == 0b00001),
            ("fuel low blink off", M(fuel, p with { Fuel = 30 }, 400) == 0),
            ("cruise off", M(cruise, p) == 0),
            ("cruise 55 of 110", M(cruise, p with { CruiseSpeed = 55 / 3.6f }) == 0b00111),
            ("cruise 110", M(cruise, p with { CruiseSpeed = 110 / 3.6f }) == 0b11111),
            ("retarder 0", M(ret, p) == 0),
            ("retarder 2/4", M(ret, p with { Retarder = 2 }) == 0b00111),
            ("retarder 4/4", M(ret, p with { Retarder = 4 }) == 0b11111),
            ("retarder none", M(ret, p with { Retarder = 2, RetarderSteps = 0 }) == 0),
            ("blinker overrides", M(fuel, p with { Flags = (ushort)(p.Flags | 1) }) == 0b00011),
            ("hazard overrides", M(fuel, p with { Flags = (ushort)(p.Flags | 4 | 3) }) == 0b11111),
        };
        var bad = checks.Where(c => !c.Item2).Select(c => c.Item1).ToArray();
        Console.WriteLine(bad.Length == 0 ? $"LED gauge masks: {checks.Length}/{checks.Length} OK" : "LED gauge FAILED: " + string.Join(", ", bad));
        return bad.Length == 0;
    }

    private static string Describe(TriggerEffect e) => e.Mode switch
    {
        0x05 => "off",
        0x01 => $"cont f{e.Params[1]}",
        0x25 => $"weapon z{e.Params[0] | e.Params[1] << 8:X} s{e.Params[2]}",
        0x26 => $"vib f{e.Params[8]}Hz",
        _ => $"mode {e.Mode:X2}",
    };

    /// <summary>One ~200 ms rumble pulse on the real controller to verify the raw output report path.</summary>
    private static int RumbleTest()
    {
        using var svc = new ControllerService();
        svc.Poll();
        if (!svc.Connected) { Console.Error.WriteLine("No DualSense controller found."); return 1; }
        if (svc.Hid == null) { Console.Error.WriteLine("hidraw not open: " + svc.HidError); return 2; }
        bool on = svc.SetRumble(160, 160);
        Thread.Sleep(200);
        bool off = svc.SetRumble(0, 0);
        Console.WriteLine($"Rumble on write: {(on ? "OK" : "FAILED")}; off write: {(off ? "OK" : "FAILED")} ({svc.Current!.Connection}).");
        return on && off ? 0 : 2;
    }
}
