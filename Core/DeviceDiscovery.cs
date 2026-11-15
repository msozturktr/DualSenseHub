using System.Text.RegularExpressions;

namespace DualSenseHub.Core;

/// <summary>Finds DualSense controllers by scanning /sys; the input number in LED names changes on reconnect.</summary>
public static class DeviceDiscovery
{
    private const string Vendor = "054C";
    private static readonly string[] Products = ["0CE6", "0DF2"];

    public static List<ControllerInfo> Scan()
    {
        var result = new List<ControllerInfo>();
        string[] hidraws;
        try { hidraws = Directory.GetDirectories("/sys/class/hidraw", "hidraw*"); }
        catch { return result; }

        foreach (var hr in hidraws.OrderBy(p => p))
        {
            var uevent = ReadUevent(System.IO.Path.Combine(hr, "device", "uevent"));
            if (!uevent.TryGetValue("HID_ID", out var hidId)) continue;
            var parts = hidId.Split(':');
            if (parts.Length != 3) continue;
            string vid = parts[1].TrimStart('0').PadLeft(4, '0').ToUpperInvariant();
            string pid = parts[2].TrimStart('0').PadLeft(4, '0').ToUpperInvariant();
            if (vid != Vendor || !Products.Contains(pid)) continue;

            string bus = parts[0].TrimStart('0');
            var conn = bus == "3" ? ConnectionType.Usb : bus == "5" ? ConnectionType.Bluetooth : ConnectionType.Unknown;
            string hidDir = Resolve(System.IO.Path.Combine(hr, "device"));
            uevent.TryGetValue("HID_UNIQ", out var uniq);
            string? mac = IsMac(uniq) ? uniq!.ToLowerInvariant() : null;

            string? lightbar = null, mic = null;
            var players = new SortedDictionary<string, string>();
            foreach (var led in SafeDirs("/sys/class/leds"))
            {
                if (!Resolve(led).StartsWith(hidDir + "/", StringComparison.Ordinal)) continue;
                string name = System.IO.Path.GetFileName(led);
                if (name.EndsWith(":rgb:indicator")) lightbar = led;
                else if (name.Contains(":white:player-")) players[name] = led;
                else if (name.EndsWith(":mic") || name.Contains(":mic")) mic = led;
            }

            string? battery = null;
            foreach (var ps in SafeDirs("/sys/class/power_supply"))
            {
                string name = System.IO.Path.GetFileName(ps);
                if (!name.StartsWith("ps-controller-battery-")) continue;
                string bmac = name["ps-controller-battery-".Length..].ToLowerInvariant();
                if (Resolve(ps).StartsWith(hidDir + "/", StringComparison.Ordinal) || bmac == mac)
                {
                    battery = ps;
                    mac ??= bmac;
                    break;
                }
            }

            result.Add(new ControllerInfo("/dev/" + System.IO.Path.GetFileName(hr), hidDir, conn, pid, mac,
                lightbar, players.Values.ToArray(), mic, battery));
        }
        return result;
    }

    public static (int? Percent, string? Status) ReadBattery(ControllerInfo info)
    {
        if (info.BatteryDir == null) return (null, null);
        int? cap = int.TryParse(ReadText(System.IO.Path.Combine(info.BatteryDir, "capacity")), out var c) ? c : null;
        return (cap, ReadText(System.IO.Path.Combine(info.BatteryDir, "status")));
    }

    public static string? ReadText(string path)
    {
        try { return File.ReadAllText(path).Trim(); } catch { return null; }
    }

    private static bool IsMac(string? s) => s != null && Regex.IsMatch(s, "^([0-9a-fA-F]{2}:){5}[0-9a-fA-F]{2}$");

    private static IEnumerable<string> SafeDirs(string dir)
    {
        try { return Directory.GetDirectories(dir); } catch { return []; }
    }

    private static string Resolve(string path)
    {
        return Native.RealPath(path);
    }

    private static Dictionary<string, string> ReadUevent(string path)
    {
        var d = new Dictionary<string, string>();
        try
        {
            foreach (var line in File.ReadAllLines(path))
            {
                int i = line.IndexOf('=');
                if (i > 0) d[line[..i]] = line[(i + 1)..];
            }
        }
        catch { }
        return d;
    }
}
