using System.Text.Json;
using Komorebi.Core;

namespace Komorebi.Desktop;
internal static class SettingsStore
{
    public static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KomorebiLayouts");
    public static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public static Settings Load()
    {
        if (!File.Exists(FilePath)) return new();
        return Read(FilePath);
    }
    public static Settings Read(string path)
    {
        if (new FileInfo(path).Length > 5_000_000) throw new InvalidDataException("Datei ist zu groß (maximal 5 MB).");
        var data = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("Leere Konfiguration.");
        if (data.Profiles is null || data.Rules is null) throw new InvalidDataException("Ungültige Konfiguration.");
        foreach (var profile in data.Profiles)
        {
            if (profile is null || string.IsNullOrWhiteSpace(profile.Name) || profile.Windows is null)
                throw new InvalidDataException("Ungültiges Profil.");
            foreach (var slot in profile.Windows)
            {
                if (slot is null || slot.Bounds is null || slot.Title is null || slot.Monitor is null) throw new InvalidDataException("Ungültiges Fenster.");
                ValidateExecutable(slot.Executable);
                slot.Bounds = Layouts.Clamp(slot.Bounds);
            }
        }
        foreach (var rule in data.Rules)
        {
            if (rule is null || rule.Bounds is null || rule.TitleContains is null || rule.Monitor is null) throw new InvalidDataException("Ungültige Regel.");
            ValidateExecutable(rule.Executable);
            rule.Bounds = Layouts.Clamp(rule.Bounds);
        }
        return data;
    }
    private static void ValidateExecutable(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Programme müssen absolute .exe-Pfade verwenden.");
    }
    public static void Save(Settings settings, string? path = null)
    {
        path ??= FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
        File.Move(temporary, path, true);
    }
}
