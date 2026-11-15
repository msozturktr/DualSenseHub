using DualSenseHub.Core;
using DualSenseHub.Core.Ets2;

namespace DualSenseHub;

/// <summary>Headless commands: --info, --apply-last, --set-color RRGGBB, --ets2-sim, --ets2-listen-test.</summary>
public static class Cli
{
    public static bool IsCliRun(string[] args) =>
        args.Contains("--info") || args.Contains("--apply-last") || args.Contains("--set-color")
        || args.Contains("--ets2-sim") || args.Contains("--ets2-listen-test");

    public static int Run(string[] args)
    {
        if (args.Contains("--info")) return Info();
        if (args.Contains("--set-color")) return SetColor(args);
        if (args.Contains("--ets2-sim")) return Ets2SimRun(args);
        if (args.Contains("--ets2-listen-test")) return Ets2ListenTest(args);
        return ApplyLast(args);
    }

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
        Ets2Sim.RunAsync(secs).GetAwaiter().GetResult();
        return 0;
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
}
