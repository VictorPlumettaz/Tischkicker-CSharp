# Tischkicker-Cup (C#/.NET)

Turnierverwaltung **und** Live-Anzeigetafel für das A+W-Sommerfest 2026 –
moderiert vom KI-Maskottchen **MIRA**. Vollständige C#-Portierung der
TypeScript-App (`../Tischkicker`): alles in **Blazor Server**, ohne eigenes
JavaScript.

## Funktionen

- **Teamverwaltung** (Name, Spieler, ELO-Startwert 1000).
- **Turniere**: Liga (jeder gegen jeden), K.o., Gruppen + K.o.
- **Spielerfassung** mit Spieluhr (Start/Pause/Fortsetzen), Halbzeiten,
  Tor-Buttons; **Uhr-Korrektur** (±10 s / ±1 min) und **Ergebnis-Korrektur**
  nachträglich (Tabelle und ELO werden neu berechnet).
- **Wertung**: ELO (K=32, JS-identische Rundung) + 3-1-0-Tabellen mit
  Tiebreakern (Punkte → Tordifferenz → Tore → Team-ID).
- **Live-Anzeigetafel** (`/live`, schwarzer Hintergrund): großes Ergebnis,
  Tor-Animation, gelegentlich rollender Ball, **live gekoppelte Tabelle**,
  **Siegerehrung** am Turnierende.
- **MIRA**:
  - **Stufe A** – Offline-Spruch-Engine (immer verfügbar).
  - **Stufe B** – frei formulierte Kommentare über die **Claude API**
    (offizielles Anthropic C#-SDK). Fällt bei jedem Problem (kein Internet,
    ungültiger Key, Timeout) automatisch auf Stufe A zurück.

## Architektur

| Projekt | Inhalt |
| --- | --- |
| `Tischkicker.Core` | Domänentypen + reine Logik (ELO, Spielplan, Tabellen, Spieluhr, MIRA-Sprüche) |
| `Tischkicker.Data` | EF Core + SQLite (`AppDbContext`, Entities, Migrationen) |
| `Tischkicker.Services` | `MatchControl`, `TournamentSetup`, `SettingsService`, `MiraService`, `LiveNotifier` |
| `Tischkicker.Web` | ASP.NET Core + Blazor Server (Bedien-Oberfläche + `/live`) |
| `Tischkicker.Tests` | xUnit (26 Tests) |

**Echtzeit**: statt SSE ein Singleton `LiveNotifier` (C#-Event). Steuer-Aktionen
lösen es aus; die Live-Komponente rendert neu, der Blazor-Server-Circuit pusht
das über SignalR an **alle** verbundenen Browser (inkl. TV).

Die SQLite-Datenbank liegt unter `%LOCALAPPDATA%\Tischkicker\tischkicker.db`
(auch als installierte `.exe` schreibbar).

## Starten (Entwicklung)

```
dotnet run --project src/Tischkicker.Web
```

- Bediener-Oberfläche: `http://localhost:5088`
- Anzeigetafel (zweites Gerät im LAN): `http://<PC-IP>:5088/live`

Beispieldaten für eine Generalprobe befüllen (und beenden):

```
dotnet run --project src/Tischkicker.Web -- --seed
```

## Als Windows-`.exe` verpacken

```
dotnet publish src/Tischkicker.Web -c Release -o publish
```

Ergebnis: eine einzelne, eigenständige **`publish/Tischkicker.exe`**
(win-x64, self-contained, kein installiertes .NET nötig). Starten per
Doppelklick oder `Tischkicker.exe`; die Konsole zeigt die LAN-URLs.

## MIRA Stufe B einrichten

In der App unter **Einstellungen**: Stufe B aktivieren, Modell wählen
(Standard `claude-haiku-4-5`), Anthropic-API-Key eintragen, **Verbindung
testen**. Der Key wird nur lokal in der `Settings`-Tabelle gespeichert und
nie angezeigt (nach außen nur „Key hinterlegt: ja/nein").

## Tests

```
dotnet test
```
