using System.Diagnostics;
using Komorebi.Core;

namespace Komorebi.Desktop;
internal sealed class MainForm : Form
{
    private Settings settings = new();
    private readonly ListBox profiles = new() { Dock = DockStyle.Fill };
    private readonly CheckedListBox windows = new() { Dock = DockStyle.Fill, CheckOnClick = true, HorizontalScrollbar = true };
    private readonly ListBox rules = new() { Dock = DockStyle.Fill };
    private readonly ComboBox monitors = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
    private readonly ComboBox layouts = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly Panel preview = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(25, 32, 47) };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 48, Padding = new(12), Text = "Bereit. Fenster auswählen oder eine Arbeitsumgebung speichern." };
    private readonly CheckBox launch = new() { Text = "Fehlende Programme starten", AutoSize = true, Checked = false };
    private readonly CheckBox agent = new() { Text = "Agent: neue Fenster automatisch einordnen", AutoSize = true };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1500 };
    private readonly NotifyIcon tray;
    private readonly HashSet<(nint, string)> seen = [];
    private List<(LiveWindow Window, WindowSlot Slot)> undo = [];
    private bool busy;
    private bool storageReady = true;
    private readonly CancellationTokenSource lifetime = new();
    private static readonly string[] LayoutNames = ["Raster", "Spalten", "Zeilen", "Fokus 70/30", "Breit links 60/40", "Kaskade"];

    public MainForm()
    {
        Text = "Komorebi · Arbeitsumgebungen";
        MinimumSize = new(960, 700); Size = new(1180, 820); StartPosition = FormStartPosition.CenterScreen;
        Font = new("Segoe UI", 10);
        var menu = new MenuStrip();
        var file = new ToolStripMenuItem("Datei");
        file.DropDownItems.Add("Profile importieren …", null, (_, _) => Run(Import));
        file.DropDownItems.Add("Profile exportieren …", null, (_, _) => Run(Export));
        file.DropDownItems.Add("Datenordner öffnen", null, (_, _) => Run(() =>
        {
            Directory.CreateDirectory(SettingsStore.DirectoryPath);
            Process.Start(new ProcessStartInfo(SettingsStore.DirectoryPath) { UseShellExecute = true });
        }));
        file.DropDownItems.Add("Beenden", null, (_, _) => Close());
        menu.Items.Add(file);
        menu.Items.Add("Rückgängig", null, (_, _) => Run(Undo));
        menu.Items.Add("Hilfe", null, (_, _) => MessageBox.Show(this,
            "1. Fenster auswählen (Häkchen).\n2. Raster und Monitor wählen oder Position manuell bearbeiten.\n3. Auswahl als Arbeitsumgebung speichern.\n4. Profil doppelklicken zum Wiederherstellen.\n\nRegeln verwenden EXE-Pfad und optional einen Titelteil. Der Agent ordnet jedes neue Fenster einmal ein; die erste passende Regel gewinnt.\n\nProfile starten Programme nur mit aktivierter Option. Dokumente, Browser-Tabs und echte virtuelle Desktops werden nicht gespeichert. F5 aktualisiert, Strg+Z macht die letzte Anordnung rückgängig.", "So funktioniert’s"));
        MainMenuStrip = menu;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var workspace = new TabPage("Arbeitsumgebungen & Raster");
        var rulePage = new TabPage("Fenster-Agent");
        tabs.TabPages.AddRange([workspace, rulePage]);
        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new(1100, 650), SplitterDistance = 290, FixedPanel = FixedPanel.Panel1 };
        workspace.Controls.Add(split);
        var profileButtons = Bar();
        Add(profileButtons, "Speichern …", () => SaveProfile(false));
        Add(profileButtons, "Ersetzen", () => SaveProfile(true));
        Add(profileButtons, "Umbenennen", Rename);
        Add(profileButtons, "Löschen", Delete);
        var restore = new Button { Text = "▶ Arbeitsumgebung laden", Dock = DockStyle.Bottom, Height = 45 };
        restore.Click += async (_, _) => await Restore();
        split.Panel1.Controls.Add(profiles);
        split.Panel1.Controls.Add(restore);
        split.Panel1.Controls.Add(profileButtons);
        split.Panel1.Controls.Add(new Label { Text = "Gespeicherte Programmgruppen", Dock = DockStyle.Top, Height = 32 });
        profiles.DoubleClick += async (_, _) => await Restore();
        profiles.SelectedIndexChanged += (_, _) => preview.Invalidate();

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        right.RowStyles.Add(new(SizeType.Absolute, 80));
        right.RowStyles.Add(new(SizeType.Percent, 50));
        right.RowStyles.Add(new(SizeType.Absolute, 85));
        right.RowStyles.Add(new(SizeType.Percent, 50));
        right.RowStyles.Add(new(SizeType.Absolute, 36));
        split.Panel2.Controls.Add(right);
        var selection = Bar();
        Add(selection, "↻ Fenster aktualisieren", RefreshWindows);
        Add(selection, "Alle auswählen", () => SetChecks(true));
        Add(selection, "Keine", () => SetChecks(false));
        Add(selection, "Position / Größe …", EditWindow);
        right.Controls.Add(selection, 0, 0);
        right.Controls.Add(windows, 0, 1);
        var layoutBar = Bar();
        layouts.Items.AddRange(LayoutNames); layouts.SelectedIndex = 0;
        layoutBar.Controls.Add(layouts); layoutBar.Controls.Add(monitors);
        Add(layoutBar, "Raster anwenden", ApplyLayout);
        Add(layoutBar, "Rückgängig", Undo);
        right.Controls.Add(layoutBar, 0, 2);
        right.Controls.Add(preview, 0, 3);
        right.Controls.Add(launch, 0, 4);
        preview.Paint += PaintPreview;
        layouts.SelectedIndexChanged += (_, _) => preview.Invalidate();
        windows.ItemCheck += (_, _) => BeginInvoke(new Action(() => preview.Invalidate()));

        var ruleButtons = Bar();
        Add(ruleButtons, "Aus ausgewähltem Fenster …", AddRule);
        Add(ruleButtons, "Aktiv / Inaktiv", () => { if (rules.SelectedItem is Rule r) { r.Enabled = !r.Enabled; Persist(); RefreshRules(); } });
        Add(ruleButtons, "Löschen", () => { if (rules.SelectedItem is Rule r) { settings.Rules.Remove(r); Persist(); RefreshRules(); } });
        Add(ruleButtons, "Jetzt auf alle anwenden", () => ApplyRules(NativeWindows.List(), true));
        ruleButtons.Controls.Add(agent);
        rulePage.Controls.Add(rules); rulePage.Controls.Add(ruleButtons);
        rulePage.Controls.Add(new Label { Text = "Regeln aus der aktuellen Fensterposition erstellen. Reihenfolge: erste passende Regel gewinnt.", Dock = DockStyle.Top, Height = 40 });

        Controls.Add(tabs); Controls.Add(status); Controls.Add(menu);
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Menü öffnen", null, (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); });
        trayMenu.Items.Add("Beenden", null, (_, _) => Close());
        tray = new() { Icon = SystemIcons.Application, Text = "Komorebi Layouts", Visible = true, ContextMenuStrip = trayMenu };
        tray.DoubleClick += (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); };
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); };
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.F5) Run(RefreshWindows); if (e.Control && e.KeyCode == Keys.Z) Run(Undo); };
        agent.CheckedChanged += (_, _) => { seen.Clear(); foreach (var w in NativeWindows.List()) seen.Add((w.Handle, w.Executable)); };
        timer.Tick += (_, _) => Run(AgentTick);
        Shown += (_, _) =>
        {
            try { settings = SettingsStore.Load(); }
            catch (Exception ex) { storageReady = false; MessageBox.Show(this, $"Konfiguration konnte nicht geladen werden: {ex.Message}\nDie Datei bleibt erhalten. Bitte im Datenordner korrigieren oder umbenennen und neu starten.", "Konfigurationsfehler"); }
            RefreshProfiles(); RefreshRules(); RefreshWindows(); timer.Start();
        };
        FormClosed += (_, _) => { lifetime.Cancel(); timer.Dispose(); tray.Dispose(); };
    }
    private static FlowLayoutPanel Bar() => new() { Dock = DockStyle.Top, AutoSize = false, Height = 100, Padding = new(4), WrapContents = true };
    private void Add(FlowLayoutPanel bar, string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 32, Margin = new(3) };
        button.Click += (_, _) => Run(action); bar.Controls.Add(button);
    }
    private void Run(Action action)
    {
        if (busy) { status.Text = "Wiederherstellung läuft …"; return; }
        try { action(); } catch (Exception ex) { status.Text = ex.Message; MessageBox.Show(this, ex.Message, "Aktion nicht möglich"); }
    }
    private void Persist()
    {
        if (!storageReady) throw new InvalidOperationException("Speichern ist wegen der beschädigten Konfiguration gesperrt. Bitte zuerst die Datei reparieren und neu starten.");
        SettingsStore.Save(settings);
    }
    private List<LiveWindow> Selected() => windows.CheckedItems.Cast<LiveWindow>().ToList();
    private void SetChecks(bool value) { for (int i = 0; i < windows.Items.Count; i++) windows.SetItemChecked(i, value); }
    private string MonitorName => monitors.SelectedItem as string ?? NativeWindows.Monitor("").DeviceName;
    private void RefreshWindows()
    {
        var selected = Selected().Select(w => w.Handle).ToHashSet();
        windows.Items.Clear();
        foreach (var w in NativeWindows.List()) windows.Items.Add(w, selected.Contains(w.Handle));
        string previous = MonitorName;
        monitors.Items.Clear(); monitors.Items.AddRange(Screen.AllScreens.Select(s => s.DeviceName).ToArray());
        monitors.SelectedItem = previous;
        if (monitors.SelectedIndex < 0) monitors.SelectedIndex = 0;
        status.Text = $"{windows.Items.Count} zugängliche Fenster gefunden. Häkchen wählen die Programmgruppe.";
    }
    private void RefreshProfiles() { var current = profiles.SelectedItem; profiles.Items.Clear(); profiles.Items.AddRange(settings.Profiles.ToArray()); profiles.SelectedItem = current; }
    private void RefreshRules() { rules.Items.Clear(); rules.Items.AddRange(settings.Rules.ToArray()); }
    private static string? Ask(IWin32Window owner, string title, string initial)
    {
        using var dialog = new Form { Text = title, Size = new(460, 160), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false };
        var input = new TextBox { Text = initial, Dock = DockStyle.Top };
        var ok = new Button { Text = "Übernehmen", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        dialog.Controls.Add(input); dialog.Controls.Add(ok); dialog.AcceptButton = ok;
        return dialog.ShowDialog(owner) == DialogResult.OK ? input.Text.Trim() : null;
    }
    private void SaveProfile(bool replace)
    {
        var selected = Selected();
        if (selected.Count == 0) throw new InvalidOperationException("Bitte zuerst Fenster mit Häkchen auswählen.");
        var existing = replace ? profiles.SelectedItem as Profile : null;
        if (replace && existing is null) return;
        string? name = replace ? existing!.Name : Ask(this, "Name der Arbeitsumgebung", "Arbeit");
        if (string.IsNullOrWhiteSpace(name)) return;
        var saved = new Profile { Name = name, Windows = selected.Select(NativeWindows.Capture).ToList() };
        if (existing is not null && MessageBox.Show(this, $"„{name}“ ersetzen?", "Profil ersetzen", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        if (existing is not null) settings.Profiles.Remove(existing);
        settings.Profiles.Add(saved); Persist(); RefreshProfiles(); profiles.SelectedItem = saved;
        status.Text = $"„{name}“ gespeichert: {saved.Windows.Count} Fenster inklusive Monitor, Position und Größe.";
    }
    private void Rename() { if (profiles.SelectedItem is Profile p) { var name = Ask(this, "Profil umbenennen", p.Name); if (!string.IsNullOrWhiteSpace(name)) { p.Name = name; Persist(); RefreshProfiles(); } } }
    private void Delete() { if (profiles.SelectedItem is Profile p && MessageBox.Show(this, $"„{p.Name}“ löschen?", "Löschen", MessageBoxButtons.YesNo) == DialogResult.Yes) { settings.Profiles.Remove(p); Persist(); RefreshProfiles(); } }
    private void Snapshot(List<LiveWindow> list) => undo = list.Select(w => (w, NativeWindows.Capture(w))).ToList();
    private void MoveMany(IEnumerable<(LiveWindow Window, WindowSlot Slot)> items)
    {
        int success = 0; var errors = new List<string>();
        foreach (var (w, slot) in items)
            try { NativeWindows.Move(w, slot.Monitor, slot.Bounds, slot.Maximized); success++; }
            catch (Exception ex) { errors.Add($"{w.Title}: {ex.Message}"); }
        status.Text = $"{success} Fenster angeordnet. {errors.Count} Fehler.";
        if (errors.Count > 0) MessageBox.Show(this, string.Join("\n", errors), "Nicht alle Fenster konnten verschoben werden");
    }
    private void ApplyLayout()
    {
        var selected = Selected(); if (selected.Count == 0) throw new InvalidOperationException("Bitte Fenster auswählen.");
        Snapshot(selected);
        var boxes = Layouts.Create((LayoutKind)layouts.SelectedIndex, selected.Count);
        MoveMany(selected.Select((w, i) => (w, new WindowSlot { Monitor = MonitorName, Bounds = boxes[i] })));
    }
    private void Undo() { var previous = undo; undo = []; MoveMany(previous); }
    private void EditWindow()
    {
        if (windows.SelectedItem is not LiveWindow w) throw new InvalidOperationException("Bitte eine Fensterzeile markieren.");
        var slot = NativeWindows.Capture(w);
        using var dialog = new PositionDialog(slot.Bounds);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Snapshot([w]); slot.Bounds = dialog.BoundsValue; slot.Monitor = MonitorName; slot.Maximized = false;
        MoveMany([(w, slot)]);
    }
    private void AddRule()
    {
        if (windows.SelectedItem is not LiveWindow w) throw new InvalidOperationException("Zuerst unter Arbeitsumgebungen eine Fensterzeile markieren.");
        var title = Ask(this, "Titel enthält (leer = alle Fenster dieses Programms)", "");
        if (title is null) return;
        var slot = NativeWindows.Capture(w);
        using var dialog = new PositionDialog(slot.Bounds);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        settings.Rules.Add(new() { Executable = w.Executable, TitleContains = title, Monitor = MonitorName, Bounds = dialog.BoundsValue });
        Persist(); RefreshRules(); status.Text = "Regel gespeichert. Zielmonitor entspricht der Monitorauswahl im Rastermenü.";
    }
    private void ApplyRules(List<LiveWindow> candidates, bool remember)
    {
        var matches = candidates.Select(w => (Window: w, Rule: settings.Rules.FirstOrDefault(r => r.Matches(w.Executable, w.Title))))
            .Where(x => x.Rule is not null).ToList();
        if (matches.Count == 0) return;
        if (remember) Snapshot(matches.Select(x => x.Window).ToList());
        MoveMany(matches.Select(x => (x.Window, new WindowSlot { Monitor = x.Rule!.Monitor, Bounds = x.Rule.Bounds })));
    }
    private void AgentTick()
    {
        if (!agent.Checked || busy) return;
        var current = NativeWindows.List();
        seen.IntersectWith(current.Select(w => (w.Handle, w.Executable)));
        // Wait for a matching title before marking a window handled (apps often set titles later).
        var fresh = current.Where(w => !seen.Contains((w.Handle, w.Executable)) && settings.Rules.Any(r => r.Matches(w.Executable, w.Title))).ToList();
        foreach (var w in fresh) seen.Add((w.Handle, w.Executable));
        ApplyRules(fresh, false);
    }
    private async Task Restore()
    {
        if (busy || profiles.SelectedItem is not Profile profile) return;
        busy = true;
        var issues = new List<string>();
        var used = new HashSet<nint>();
        var started = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            Snapshot(NativeWindows.List());
            foreach (var slot in profile.Windows)
            {
                lifetime.Token.ThrowIfCancellationRequested();
                status.Text = $"Lade „{profile.Name}“: {Path.GetFileName(slot.Executable)} …";
                LiveWindow? Find() => NativeWindows.List().Where(w => !used.Contains(w.Handle) && string.Equals(w.Executable, slot.Executable, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(w => string.Equals(w.Title, slot.Title, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                var found = Find();
                if (found is null && launch.Checked && started.Add(slot.Executable))
                {
                    try
                    {
                        if (!File.Exists(slot.Executable)) throw new FileNotFoundException("Programmdatei fehlt.");
                        using var process = Process.Start(new ProcessStartInfo(slot.Executable) { UseShellExecute = false });
                        for (int i = 0; i < 40 && found is null; i++) { await Task.Delay(250, lifetime.Token); found = Find(); }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { issues.Add($"{slot.Title}: Start fehlgeschlagen: {ex.Message}"); }
                }
                if (found is null) { issues.Add($"{slot.Title}: kein passendes Fenster gefunden."); continue; }
                used.Add(found.Handle);
                try { NativeWindows.Move(found, slot.Monitor, slot.Bounds, slot.Maximized); }
                catch (Exception ex) { issues.Add($"{slot.Title}: {ex.Message}"); }
            }
            // Newly launched windows must not immediately be moved again by the agent.
            foreach (var w in NativeWindows.List()) seen.Add((w.Handle, w.Executable));
            RefreshWindows(); status.Text = $"„{profile.Name}“ geladen. {issues.Count} Hinweise.";
            if (issues.Count > 0) MessageBox.Show(this, string.Join("\n", issues), "Wiederherstellung");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!IsDisposed) MessageBox.Show(this, ex.Message, "Wiederherstellung fehlgeschlagen"); }
        finally { busy = false; }
    }
    private void Import()
    {
        using var dialog = new OpenFileDialog { Filter = "Komorebi-Konfiguration (*.json)|*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var imported = SettingsStore.Read(dialog.FileName);
        var executables = imported.Profiles.SelectMany(p => p.Windows).Select(w => w.Executable).Distinct();
        if (MessageBox.Show(this, $"{imported.Profiles.Count} Profile und {imported.Rules.Count} Regeln hinzufügen?\n\nEnthaltene Programme:\n{string.Join("\n", executables)}\n\nProgramme werden nur mit aktivierter Startoption ausgeführt.", "Import prüfen", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        settings.Profiles.AddRange(imported.Profiles); settings.Rules.AddRange(imported.Rules); Persist(); RefreshProfiles(); RefreshRules();
    }
    private void Export()
    {
        using var dialog = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = "komorebi-profile.json" };
        if (dialog.ShowDialog(this) == DialogResult.OK) SettingsStore.Save(settings, dialog.FileName);
    }
    private void PaintPreview(object? sender, PaintEventArgs e)
    {
        var selected = Selected();
        var boxes = selected.Count > 0 ? Layouts.Create((LayoutKind)layouts.SelectedIndex, selected.Count)
            : (profiles.SelectedItem as Profile)?.Windows.Select(w => w.Bounds).ToList() ?? [];
        e.Graphics.DrawString(selected.Count > 0 ? "Rastervorschau · ausgewählter Monitor" : "Profilvorschau · relative Positionen aller Monitore", Font, Brushes.White, 10, 8);
        for (int i = 0; i < boxes.Count; i++)
        {
            var b = boxes[i];
            var rect = new Rectangle(10 + (int)(b.X * (preview.Width - 20)), 38 + (int)(b.Y * (preview.Height - 50)), Math.Max(1, (int)(b.Width * (preview.Width - 20)) - 4), Math.Max(1, (int)(b.Height * (preview.Height - 50)) - 4));
            using var brush = new SolidBrush(Color.FromArgb(180, 45 + i * 23 % 100, 110 + i * 17 % 100, 180));
            e.Graphics.FillRectangle(brush, rect); e.Graphics.DrawRectangle(Pens.LightSkyBlue, rect);
            e.Graphics.DrawString($"{i + 1}", Font, Brushes.White, rect);
        }
    }
}
internal sealed class PositionDialog : Form
{
    private readonly NumericUpDown[] values;
    public Box BoundsValue => Layouts.Clamp(new((double)values[0].Value / 100, (double)values[1].Value / 100, (double)values[2].Value / 100, (double)values[3].Value / 100));
    public PositionDialog(Box initial)
    {
        Text = "Position und Größe in % der Arbeitsfläche"; Size = new(450, 270); StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, Padding = new(12) };
        values = new[] { initial.X, initial.Y, initial.Width, initial.Height }.Select((x, i) => new NumericUpDown { Minimum = i < 2 ? 0 : 5, Maximum = 100, DecimalPlaces = 1, Value = (decimal)Math.Clamp(x * 100, i < 2 ? 0 : 5, 100), Width = 130 }).ToArray();
        string[] labels = ["Links (%)", "Oben (%)", "Breite (%)", "Höhe (%)"];
        for (int i = 0; i < 4; i++) { table.Controls.Add(new Label { Text = labels[i], AutoSize = true }, 0, i); table.Controls.Add(values[i], 1, i); }
        var ok = new Button { Text = "Übernehmen", AutoSize = true };
        ok.Click += (_, _) =>
        {
            if (values[0].Value + values[2].Value > 100 || values[1].Value + values[3].Value > 100)
            { MessageBox.Show(this, "Position und Größe müssen zusammen innerhalb von 100 % liegen."); return; }
            DialogResult = DialogResult.OK;
        };
        table.Controls.Add(ok, 1, 4); Controls.Add(table); AcceptButton = ok;
    }
}
