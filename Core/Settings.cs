using System.Text.Json;
using System.Text.Json.Serialization;
using DualSenseHub.Core.Ets2;

namespace DualSenseHub.Core;

public enum LightEffect { Static, Breathing, Rainbow, Blink, Battery }

/// <summary>Lighting configuration; also the content of a named profile.</summary>
public sealed class LightSettings
{
    public LightEffect Effect { get; set; } = LightEffect.Static;
    public string ColorHex { get; set; } = "#0050FF";
    /// <summary>0-100 percent.</summary>
    public int Brightness { get; set; } = 100;
    /// <summary>Effect period in seconds.</summary>
    public double Speed { get; set; } = 3;
    public int PlayerMask { get; set; }
    /// <summary>0 high, 1 medium, 2 low.</summary>
    public int PlayerBrightness { get; set; }
    public bool MicLed { get; set; }

    public LightSettings Clone() => (LightSettings)MemberwiseClone();

    public (byte R, byte G, byte B) ParseColor()
    {
        var h = ColorHex.TrimStart('#');
        if (h.Length >= 6 && int.TryParse(h[..6], System.Globalization.NumberStyles.HexNumber, null, out var v))
            return ((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return (0, 80, 255);
    }
}

public sealed class AppSettings
{
    public LightSettings Last { get; set; } = new();
    public bool RestoreOnStartup { get; set; } = true;
    public bool HasSaved { get; set; }
}

public sealed class NamedProfile
{
    public string Name { get; set; } = "";
    public LightSettings Settings { get; set; } = new();
}

/// <summary>JSON persistence under ~/.config/dualsensehub/.</summary>
public static class ConfigStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Dir
    {
        get
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var root = string.IsNullOrEmpty(xdg) ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config") : xdg;
            return System.IO.Path.Combine(root, "dualsensehub");
        }
    }

    private static string SettingsPath => System.IO.Path.Combine(Dir, "settings.json");
    private static string ProfilesPath => System.IO.Path.Combine(Dir, "profiles.json");

    private static string Ets2Path => System.IO.Path.Combine(Dir, "ets2.json");
    public static Ets2Settings LoadEts2() => Load<Ets2Settings>(Ets2Path) ?? new Ets2Settings();
    public static void SaveEts2(Ets2Settings s) => Save(Ets2Path, s);

    public static AppSettings LoadSettings() => Load<AppSettings>(SettingsPath) ?? new AppSettings();
    public static void SaveSettings(AppSettings s) => Save(SettingsPath, s);
    public static List<NamedProfile> LoadProfiles() => Load<List<NamedProfile>>(ProfilesPath) ?? [];
    public static void SaveProfiles(List<NamedProfile> p) => Save(ProfilesPath, p);

    private static T? Load<T>(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) : default; }
        catch { return default; }
    }

    private static void Save<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json));
            File.Move(tmp, path, true);
        }
        catch { /* best effort */ }
    }
}
