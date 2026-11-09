namespace Komorebi.Core;

public record Box(double X, double Y, double Width, double Height);
public enum LayoutKind { Grid, Columns, Rows, Focus, WideLeft, Cascade }
public sealed class WindowSlot
{
    public string Executable { get; set; } = "";
    public string Title { get; set; } = "";
    public string Monitor { get; set; } = "";
    public Box Bounds { get; set; } = new(0, 0, 1, 1);
    public bool Maximized { get; set; }
}
public sealed class Profile
{
    public string Name { get; set; } = "Arbeitsumgebung";
    public List<WindowSlot> Windows { get; set; } = [];
    public override string ToString() => $"{Name} ({Windows.Count} Fenster)";
}
public sealed class Rule
{
    public string Executable { get; set; } = "";
    public string TitleContains { get; set; } = "";
    public string Monitor { get; set; } = "";
    public Box Bounds { get; set; } = new(0, 0, .5, 1);
    public bool Enabled { get; set; } = true;
    public bool Matches(string executable, string title) => Enabled &&
        string.Equals(Executable, executable, StringComparison.OrdinalIgnoreCase) &&
        title.Contains(TitleContains, StringComparison.OrdinalIgnoreCase);
    public override string ToString() => $"{(Enabled ? "✓" : "–")} {Path.GetFileName(Executable)} · {TitleContains}";
}
public sealed class Settings
{
    public List<Profile> Profiles { get; set; } = [];
    public List<Rule> Rules { get; set; } = [];
}
public static class Layouts
{
    public static IReadOnlyList<Box> Create(LayoutKind kind, int count)
    {
        if (count < 0 || count > 1000) throw new ArgumentOutOfRangeException(nameof(count));
        if (count == 0) return [];
        if (count == 1) return [new(0, 0, 1, 1)];
        var result = new List<Box>();
        int columns = (int)Math.Ceiling(Math.Sqrt(count));
        int rows = (int)Math.Ceiling((double)count / columns);
        for (int i = 0; i < count; i++)
            result.Add(kind switch
            {
                LayoutKind.Columns => new((double)i / count, 0, 1d / count, 1),
                LayoutKind.Rows => new(0, (double)i / count, 1, 1d / count),
                LayoutKind.Focus or LayoutKind.WideLeft => i == 0
                    ? new(0, 0, kind == LayoutKind.Focus ? .7 : .6, 1)
                    : new(kind == LayoutKind.Focus ? .7 : .6, (double)(i - 1) / (count - 1),
                        kind == LayoutKind.Focus ? .3 : .4, 1d / (count - 1)),
                LayoutKind.Cascade => new(.2 * i / (count - 1), .2 * i / (count - 1), .8, .8),
                _ => new((double)(i % columns) / columns, (double)(i / columns) / rows, 1d / columns, 1d / rows)
            });
        return result;
    }
    public static Box Clamp(Box box)
    {
        if (!double.IsFinite(box.X + box.Y + box.Width + box.Height))
            throw new ArgumentException("Ungültige Fensterkoordinaten.");
        double width = Math.Clamp(box.Width, .05, 1), height = Math.Clamp(box.Height, .05, 1);
        return new(Math.Clamp(box.X, 0, 1 - width), Math.Clamp(box.Y, 0, 1 - height), width, height);
    }
}
