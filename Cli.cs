using DualSenseHub.Core;

namespace DualSenseHub;

/// <summary>Headless commands: --info and --apply-last.</summary>
public static class Cli
{
    public static bool IsCliRun(string[] args) => args.Contains("--info") || args.Contains("--apply-last");

    public static int Run(string[] args)
    {
        if (args.Contains("--info")) return Info();
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
}
