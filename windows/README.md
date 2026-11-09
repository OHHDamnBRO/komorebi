# Komorebi Arbeitsumgebungen für Windows

Eigenständige deutsche Windows-Anwendung neben dem bestehenden Linux-Wallpaper-Projekt.

## Starten

Windows 10/11 (x64) und die **.NET 8 Desktop Runtime (x64)** installieren:
https://dotnet.microsoft.com/download/dotnet/8.0 (unter „.NET Desktop Runtime“).
Das gesamte ZIP entpacken und `Komorebi.Desktop.exe` öffnen. Die Anwendung braucht weder einen Server noch API-Schlüssel.

## Funktionen

- **Arbeitsumgebungen / Programmgruppen:** Fenster mit Häkchen auswählen, unter einem frei gewählten Namen speichern, später per Doppelklick laden. Beispielsweise „Arbeit“, „Recherche“, „Gaming“ oder „Kommunikation“.
- **Snapshots:** EXE-Pfad, Fenstertitel, Monitor, relative Position, Größe und maximierter Zustand werden gespeichert. Ein fehlender Monitor wird durch den Hauptmonitor ersetzt. Die Taskleiste bleibt außerhalb der Zielarbeitsfläche.
- **Programmstart:** „Fehlende Programme starten“ aktivieren. Es wird pro EXE höchstens ein Start pro Wiederherstellung versucht und bis zu zehn Sekunden auf ein Fenster gewartet. Ohne diese Option werden nur vorhandene Fenster angeordnet.
- **Sechs Anordnungen:** Raster, gleichmäßige Spalten, Zeilen, Fokus mit 70 % Hauptfenster, 60/40-Aufteilung und Kaskade. Das erste angehakte Fenster in der Liste wird zum Hauptfenster. Monitorauswahl und Vorschau stehen im Rastermenü.
- **Manuelle Geometrie:** Fensterzeile markieren, „Position / Größe“ öffnen und Prozentwerte bearbeiten. Der ausgewählte Monitor bestimmt das Ziel.
- **Einfacher lokaler Agent:** Fensterzeile markieren, zur Agent-Seite wechseln und eine Regel erzeugen. EXE-Pfad wird exakt (ohne Groß-/Kleinschreibung) abgeglichen, ein optionaler Titelteil grenzt Fenster ein. Erste passende aktive Regel gewinnt. Regeln können deaktiviert oder gelöscht und neu angelegt werden. Der Zielmonitor kommt aus der Rasterauswahl.
- **Automatik:** Der aktivierte Agent prüft alle 1,5 Sekunden neue Fenster und ordnet sie einmal ein. Bereits offene Fenster werden erst mit „Jetzt auf alle anwenden“ verschoben. Nach Wiederherstellung eines Profils werden dessen Fenster nicht sofort durch den Agenten überschrieben.
- **Rückgängig:** Letzte manuelle Anordnung oder Profilwiederherstellung zurücksetzen; gestartete Programme bleiben geöffnet. Automatische Agent-Aktionen gehören nicht zum Rückgängig-Verlauf.
- **Tray-Menü:** Minimieren blendet das Menü aus. Über das Taskleistensymbol wieder öffnen. Schließen beendet die Anwendung einschließlich Agent.
- **Import / Export:** Profile und Regeln als JSON teilen. Import zeigt die enthaltenen Programmdateien und fügt Einträge hinzu. Nur vertrauenswürdige Programme starten.
- **Tastatur:** F5 aktualisiert Fenster, Strg+Z macht rückgängig, solange das Menü den Tastaturfokus hat.

Daten: `%LOCALAPPDATA%\KomorebiLayouts\settings.json`. Schreiben erfolgt über eine temporäre Datei mit anschließendem Austausch. Bei ungültiger Konfiguration wird automatisches Überschreiben gesperrt; Datei reparieren oder umbenennen und neu starten.

## Bewusste Grenzen

Profile sind Layout-Arbeitsumgebungen, keine echten virtuellen Windows-Desktops. Nicht ausgewählte Fenster bleiben unverändert. Es werden keine Dokumente, Browser-Tabs, Startargumente oder App-Sitzungen rekonstruiert. Bei mehreren Fenstern derselben EXE wird zuerst der exakte gespeicherte Titel gesucht, dann ein noch unbenutztes Fenster derselben EXE. Zusätzliche Fenster einer bereits gestarteten Anwendung können nicht zuverlässig automatisch erzeugt werden.

Apps dürfen Mindestgrößen erzwingen oder Verschieben ablehnen. Geschützte/erhöhte Prozesse und manche Store-Apps sind eventuell nicht zugänglich. Vollbildspiele und Fenster anderer virtueller Desktops werden nicht zuverlässig unterstützt. Der Agent ist regelbasiert, ohne KI-Dienst. Er startet standardmäßig ausgeschaltet und verändert Fenster nicht dauerhaft gegen manuelle Verschiebungen.

## Entwickeln und bauen

.NET 8 SDK:

```powershell
dotnet run --project windows/Komorebi.Tests
dotnet build windows/Komorebi.Desktop -c Release
dotnet run --project windows/Komorebi.Desktop
```

Portable Ausgabe mit vorausgesetzter Desktop Runtime:

```powershell
dotnet publish windows/Komorebi.Desktop -c Release -r win-x64 --self-contained false -o windows/artifacts/win-x64
```

Alternativ mit gebündelter Runtime (größer, keine separate Runtime-Installation):

```powershell
dotnet publish windows/Komorebi.Desktop -c Release -r win-x64 --self-contained true -o windows/artifacts/standalone
```

Der Build und die plattformunabhängigen Tests können unter Linux laufen. Die tatsächliche Fenstersteuerung benötigt Windows.

## Manuelle Windows-Abnahme

1. Zwei Editor-Fenster und einen Browser öffnen, aktualisieren und auswählen.
2. Jedes Raster anwenden; Taskleiste, Vorschau und Rückgängig prüfen.
3. Manuell 50 % Breite und rechte Monitorhälfte einstellen; Profil speichern.
4. Fenster verschieben, eines maximieren, Profil laden und gespeicherte Positionen prüfen.
5. Ein Programm schließen, Startoption aktivieren, Profil laden; Timeout/Hinweise überprüfen.
6. Zweiten Monitor nutzen, Profil speichern, Monitor trennen und Wiederherstellung auf dem Hauptmonitor prüfen. Unterschiedliche DPI-Skalierung prüfen.
7. Regel mit Titelteil speichern, Agent aktivieren, neues passendes Fenster öffnen. Manuelles Verschieben soll danach bestehen bleiben.
8. Exportieren, importieren, Anwendung neu starten, gespeicherte Profile/Regeln prüfen.
9. Ungültige JSON-Datei einsetzen: Fehlermeldung und Schutz vor Überschreiben prüfen.
10. Minimieren, über Tray wieder öffnen, während eines Programmstarts schließen und sauberes Beenden prüfen.

Diese interaktive Abnahme wurde im Linux-Sandbox nicht ausgeführt.
