# Plan & Status: C#/.NET-Portierung von Tischkicker (Blazor Server)

## Kontext

Zweite, vollständige Umsetzung der TypeScript-App (`VictorPlumettaz/Tischkicker`)
**komplett in C#** (Blazor Server, kein eigenes JavaScript), vollem Umfang (inkl.
MIRA Stufe B über das Anthropic C#-SDK und Verpackung als Single-File-`.exe`), als
eigenständiges Repo mit eigener Git-Historie. .NET SDK 10.

## Zielarchitektur

Schichten spiegeln das TS-Projekt (siehe `CLAUDE.md` → Projektstruktur für Details):
`Tischkicker.Core` (reine Logik) · `Tischkicker.Data` (EF Core/SQLite) ·
`Tischkicker.Services` (Orchestrierung) · `Tischkicker.Web` (Blazor Server) ·
`Tischkicker.Tests` (xUnit).

Echtzeit über `LiveNotifier` (statt SSE); MIRA Stufe A offline + Stufe B via
Anthropic C#-SDK mit automatischem Fallback; DB unter `%LOCALAPPDATA%\Tischkicker`.

## Meilensteine

- ✅ **C1 Gerüst** – Solution + 5 Projekte, `net10.0`, NuGet, `.gitignore`,
  lokale `nuget.config` (nur nuget.org).
- ✅ **C2 Core** – Domänentypen + Logik portiert; ELO-Zahlen 1:1 gegen die
  TS-Tests gegengeprüft.
- ✅ **C3 Data** – `AppDbContext` + Entities + EF-Migration + Repositories/Zugriff.
- ✅ **C4 Services** – `MatchControl`, `TournamentSetup`, `LiveNotifier`
  (+ Service-Tests).
- ✅ **C5 Blazor-UI** – Layout + alle Seiten inkl. Live-Tafel (schwarz, großer
  Score, Tor-Animation, rollender Ball, Live-Tabelle, Siegerehrung) und
  Bedien-Korrekturen (Uhr, Ergebnis).
- ✅ **C6 MIRA Stufe B** – `SettingsService` (MIRA-Config, Key nur intern →
  `HasApiKey`), `MiraService.CommentAsync` über das Anthropic C#-SDK mit
  automatischem Fallback auf Stufe A; Einstellungen-Seite (Umschalter,
  Modell-Dropdown, API-Key-Feld, Speichern, Verbindungstest).
- ✅ **C7 Verpackung** – `dotnet publish -c Release` → self-contained Single-File
  `Tischkicker.exe`; README; EF-Logging auf `Warning`.

## Nach C1–C7 ergänzt

- ✅ **Browser-Auto-Start** beim App-Start (`ApplicationStarted`-Hook,
  abschaltbar mit `--no-browser`).
- ✅ **Turnier bearbeiten** (`TournamentSetup.UpdateTournament`): Name/Halbzeiten/
  Spielzeit jederzeit; Teams/Format nur, solange noch nicht gespielt wurde
  (sonst gesperrt), dann Spielplan-Neuerzeugung.
- ✅ **Turnier zurücksetzen** (`TournamentSetup.ResetResults` +
  `MatchControl.RecomputeStats`): Ergebnisse löschen, Spiele auf „geplant",
  vorgerückte K.o.-Teams leeren, Gruppen-K.o.-Phase entfernen, ELO/Bilanz
  zurückrechnen.
- ✅ **Anzeigetafel-Ruheansicht**: kein laufendes Spiel → Mitte zeigt den
  **Spielplan** (nach Gruppe/Runde, mit Ergebnissen, nächstes Spiel markiert),
  Seitenspalte weiter die Live-Tabelle.
- ✅ **Verteilung**: kompletter `publish/`-Ordner als `Tischkicker.zip`
  (die `.exe` allein läuft nicht – braucht Static-Assets-Manifest + `wwwroot/`).
- ✅ **Golden-Goal-Sperre**: K.o.-Spiele (K.o.-Turnier bzw. K.o.-Phase eines
  Gruppen-Turniers) können nicht unentschieden enden – Beenden bei Gleichstand
  ist in der UI (`Matches.razor`) deaktiviert und in `MatchControl.Finish`
  serverseitig abgesichert (Liga/Gruppenphase-Remis bleibt erlaubt).
- ✅ **Bugfixes** (nach Gesamtprüfung): Format-Wahl im Setup blieb wirkungslos
  (`_overridden` nie gesetzt); `ResetResults` löschte Freilos-Teams aus
  K.o.-Bäumen (nur noch der vorgerückte Slot wird geleert); Tor-Animation der
  Tafel hing bis zu 8 s hinter dem MIRA-Stufe-B-Aufruf (`DetectGoal` vorgezogen).
- ✅ **„Nächstes Spiel"-Button**: nach dem Beenden eines Spiels springt die
  Steuerung (`Matches.razor`) direkt ins nächste anstehende Spiel desselben
  Turniers – kein Umweg über die Übersicht. Neue Service-Methode
  `MatchControl.GetNextScheduled` (nach Runde/Id, bevorzugt Paarungen mit
  feststehenden Teams).
- ✅ **Live-Tabelle rechnet das laufende Spiel mit**: `Standings.Compute`
  (+`GetStandings`) bekommt `includeLive` – der aktuelle Zwischenstand fließt
  provisorisch in Platz/Punkte/Tordifferenz ein; geplante Spiele bleiben außen
  vor. Zusätzlich Latenz-Fix in `Live.razor`: Tabelle/Spielstand werden vor dem
  (evtl. langsamen) MIRA-Aufruf gepusht (`StateHasChanged`).
- ✅ **MIRA zeit- & turnierbezogen**: neuer Ticker-Trigger (`DetectTimeContext`)
  für Schlussphase (letzte Minute je Halbzeit), Golden Goal (K.o.-Gleichstand bei
  Zeitablauf) und periodische Zwischenkommentare (alle 2 Min) – entprellt und
  überlappungsgeschützt (`_miraBusy`). `BuildSituation` liefert Claude bei jedem
  Kommentar kompakten Turnierkontext (Tabelle, letzte Ergebnisse, offene Spiele);
  `MiraContext` um `RemainingSec`/`Half`/`Halves`/`Situation` erweitert, neue
  Moods `FinalMinute`/`GoldenGoal`/`Interlude` (Stufe A + B).

## Verifikation

- `dotnet build` + `dotnet test` grün (**33 Tests**: ELO-Werte, 3-1-0-Tiebreaker,
  `CorrectResult`-Flip, Uhr-Clamp, `ResetResults`, K.o.-Reset erhält Freilos-Teams,
  Golden-Goal-Sperre (K.o.-Remis wirft, Liga-Remis erlaubt), `UpdateTournament`,
  `SettingsService`, `includeLive` (laufendes Spiel zählt mit / geplante nicht)).
- End-to-End geprüft: Boot/Migration, alle Routen 200, `--seed`, Live-Tafel,
  Ruhe-Ansicht mit Spielplan, publizierte `.exe` inkl. Static Assets. Zuletzt
  bestätigt: Live-Tabelle zählt ein laufendes Gruppenspiel provisorisch mit.

## Offen / optional

- Echter Test am Veranstaltungstag.
- Optionaler nativer Fensteraufsatz (z. B. Photino) – nicht Teil dieser Version.
- Code-Signatur der `.exe` (derzeit unsigniert → SmartScreen-Hinweis).
