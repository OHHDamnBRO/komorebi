using Komorebi.Core;
using System.Text.Json;

int checks = 0;
void Check(bool valid, string message) { checks++; if (!valid) throw new Exception(message); }
foreach (var kind in Enum.GetValues<LayoutKind>())
foreach (int count in new[] { 0, 1, 2, 3, 4, 7, 12, 100 })
{
    var boxes = Layouts.Create(kind, count);
    Check(boxes.Count == count, $"Count: {kind}/{count}");
    foreach (var box in boxes)
        Check(box.X >= 0 && box.Y >= 0 && box.Width > 0 && box.Height > 0 && box.X + box.Width <= 1.000001 && box.Y + box.Height <= 1.000001, $"Bounds: {kind}");
    if (kind != LayoutKind.Cascade)
        for (int i = 0; i < boxes.Count; i++)
        for (int j = i + 1; j < boxes.Count; j++)
        {
            var a = boxes[i]; var b = boxes[j];
            Check(Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X) < .000001 ||
                Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y) < .000001, $"Overlap: {kind}");
        }
}
Check(Layouts.Clamp(new(-1, 4, 2, -1)) == new Box(0, .95, 1, .05), "Offscreen clamp");
try { Layouts.Clamp(new(double.NaN, 0, 1, 1)); throw new Exception("NaN accepted"); } catch (ArgumentException) { checks++; }
try { Layouts.Create(LayoutKind.Grid, -1); throw new Exception("Negative count accepted"); } catch (ArgumentOutOfRangeException) { checks++; }
var rule = new Rule { Executable = @"C:\Apps\Editor.exe", TitleContains = "project" };
Check(rule.Matches(@"c:\apps\EDITOR.EXE", "My PROJECT"), "Case-insensitive rule");
Check(!rule.Matches(@"C:\Other\Editor.exe", "project"), "Rule must match full executable");
Check(!rule.Matches(rule.Executable, "Other"), "Title filter");
rule.Enabled = false;
Check(!rule.Matches(rule.Executable, "project"), "Disabled rule");
var settings = new Settings { Profiles = [new Profile { Name = "Arbeit", Windows = [new WindowSlot { Executable = rule.Executable, Monitor = "Secondary", Bounds = new(.5, 0, .5, 1), Maximized = true }] }], Rules = [rule] };
var restored = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings))!;
Check(restored.Profiles[0].Windows[0].Bounds == settings.Profiles[0].Windows[0].Bounds && restored.Profiles[0].Windows[0].Maximized && restored.Rules[0].TitleContains == "project", "Settings round trip");
Console.WriteLine($"Passed {checks} checks: layouts, non-overlap, bounds, rules and persistence.");
