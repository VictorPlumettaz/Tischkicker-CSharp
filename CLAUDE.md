# CLAUDE.md

Arbeitsrichtlinien für Claude Code in diesem Projekt.

## Projekt

Tischkicker-Turnier- und Live-Anzeigetafel-App für das **A+W Sommerfest 2026**,
moderiert vom A+W-KI-Maskottchen **MIRA**. Dies ist die **C#/.NET-Portierung**
der TypeScript-App (Schwester-Repo, `VictorPlumettaz/Tischkicker`) – funktionsgleich,
aber **komplett in C#** (Blazor Server, kein eigenes JavaScript). Detaillierter
Plan + Status in [`PLAN.md`](./PLAN.md), Nutzer-Doku in [`README.md`](./README.md).

## Kommunikation

- **Antworten immer auf Deutsch.** Technische Begriffe und Code-Bezeichner
  bleiben in ihrer Originalform.

## Technischer Stack

- **.NET 10**, Solution `Tischkicker.slnx`.
- **UI:** ASP.NET Core **Blazor Server** (InteractiveServer). Komponenten rufen
  die Services direkt per DI auf – kein separates REST nötig.
- **Datenschicht:** **EF Core + SQLite** (`Microsoft.EntityFrameworkCore.Sqlite`,
  `SQLitePCLRaw.bundle_e_sqlite3`); versionierte EF-Core-Migrationen ersetzen das
  handgeschriebene Migrationssystem der TS-Version.
- **Echtzeit:** Singleton **`LiveNotifier`** (C#-Event) statt SSE; der Blazor-
  Server-Circuit pusht Änderungen über SignalR an alle Browser (inkl. Tafel).
- **KI (Stufe B):** offizielles **Anthropic C#-SDK** (NuGet `Anthropic`,
  `AnthropicClient` → `Messages.Create`), Default-Modell `claude-haiku-4-5`.
- **Tests:** **xUnit** (Kernlogik + Services über `:memory:`-SQLite).
- **Verpackung:** `dotnet publish -c Release` → eine self-contained Single-File
  **`Tischkicker.exe`** (win-x64), DB unter `%LOCALAPPDATA%\Tischkicker`.

## Wichtige Fakten / Rahmenbedingungen

- App läuft **offline / lokal** – keine Cloud-Abhängigkeit am Veranstaltungsort.
- Zielgerät: **Windows-PC** (win-x64).
- Es findet immer **nur ein Spiel gleichzeitig** statt, auf einem **großen
  Monitor** im **Anzeigetafel-Stil** (`/live`); läuft nichts, zeigt die Tafel
  **Tabelle + Spielplan**.
- Teams werden **am Veranstaltungstag** manuell angelegt (CRUD muss schnell sein).
- **Spielmodus:** Zeitmodus mit einstellbarer Halbzeitdauer (Standard 6 Min) und
  1 oder 2 Halbzeiten. Gleichstand: Liga/Gruppe = Remis; K.o. = Golden Goal
  (Beenden bei Gleichstand in UI **und** `MatchControl.Finish` gesperrt).
- **Tabellenwertung:** 3-1-0, Tiebreaker Tordifferenz → erzielte Tore → Team-ID.
- **ELO:** K=32, JS-identische Rundung (`(int)Math.Floor(x + 0.5)`), Startwert 1000.
- **MIRA Stufe B** darf **nie** ein harter Abhängigkeitsfaktor sein: ohne
  Internet / API-Key läuft alles über Stufe A (Offline-Sprüche). Der API-Key
  bleibt serverintern – nach außen nur `HasApiKey`.
- SQLite-Datei bleibt lokal und wird **nicht** eingecheckt (`.gitignore`).
- Deutschsprachige UI-Texte.

## Projektstruktur (Schichten spiegeln das TS-Projekt)

- **`src/Tischkicker.Core`** – Domänentypen (`Domain/`) + **reine, testbare
  Logik**: `Elo`, `Schedule`, `Standings`, `MatchClock`, `MiraPhrases`
  (Stufe A), `FormatRecommendation`, `MiraTip`, `MiraNarrator`.
- **`src/Tischkicker.Data`** – `AppDbContext` (DbSets + `OnModelCreating`:
  Enums als String, Indizes, Composite Keys), EF-Core-Migrationen,
  `DesignTimeDbContextFactory`.
- **`src/Tischkicker.Services`** – Orchestrierung über `IDbContextFactory`:
  `MatchControl` (Uhr, Tore, Halbzeit, Beenden inkl. ELO, `AdjustClock`,
  `CorrectResult`+`RecomputeStats`), `TournamentSetup` (Erstellen, Kader,
  Generieren, K.o.-Phase, `UpdateTournament`, `ResetResults`, Tabellen),
  `SettingsService` (MIRA-Config in `Settings`-Tabelle), `MiraService`
  (Stufe A + B mit automatischem Fallback), `LiveNotifier`.
- **`src/Tischkicker.Web`** – Blazor Server. Seiten unter `Components/Pages/`
  (Dashboard, Teams, **SetupPage** `@page "/setup"`, Matches, Rankings,
  Settings, **Live** `@page "/live"` mit `EmptyLayout`), Layouts unter
  `Components/Layout/`, `wwwroot/app.css` (A+W-Theme + Tafel), `wwwroot/assets/`
  (`mira/`, `logos/`), `DemoSeeder` (`--seed`), `Program.cs`
  (DI, Kestrel `0.0.0.0:5088`, Migrate beim Start, Browser-Auto-Start).
- **`tests/Tischkicker.Tests`** – xUnit; `TestDbFactory` über offene
  `:memory:`-Verbindung.

## Konventionen

- C# überall, `Nullable` + `ImplicitUsings` aktiv. Kernlogik rein & testbar
  halten – getrennt von UI und Datenzugriff.
- **Razor-Fallstrick:** Eine `@page`-Komponente darf nicht denselben Namen wie
  ein injizierter Service tragen (z. B. Seite `Settings` + `@inject
  SettingsService Settings` → CS0542). Injektion umbenennen (`@inject
  SettingsService Cfg`).
- Anthropic-SDK-Nutzung nie raten – gegen das installierte Assembly / die
  `Anthropic.xml` prüfen (Compile-Fix-Loop). `Messages.Create` (nicht
  `CreateAsync`), Text via `ContentBlock.TryPickText`.
- Neue EF-Migration = neue Migration anhängen, bestehende nie ändern.

## Nützliche Befehle

- `dotnet build` – Solution bauen.
- `dotnet test` – xUnit (46 Tests). **Achtung:** baut das Web-Projekt nicht mit;
  für Razor-Prüfung `dotnet build src/Tischkicker.Web/...` ausführen.
- `dotnet run --project src/Tischkicker.Web` – App (`:5088`).
- `dotnet run --project src/Tischkicker.Web -- --seed` – Demodaten befüllen.
- `dotnet publish src/Tischkicker.Web -c Release -o publish` – Single-File-`.exe`.

## Aktueller Status

🟢 Meilensteine **C1–C7 abgeschlossen** (Gerüst, Core, Datenschicht, Services,
Blazor-UI, MIRA A+B, Verpackung). Zusätzlich nachgezogen: Browser-Auto-Start,
Turnier **bearbeiten** + **zurücksetzen**, Tafel-**Spielplan** im Ruhezustand,
Verteil-ZIP, **Golden-Goal-Sperre** (K.o.-Remis nicht beendbar), drei
Bugfixes (Format-Override im Setup, K.o.-Reset erhält Freilos-Teams,
Tor-Animation vor MIRA-Aufruf) sowie drei UX-Erweiterungen:
**„Nächstes Spiel"-Button** in der Steuerung (`GetNextScheduled`),
**Live-Tabelle** rechnet das laufende Spiel provisorisch mit (`Standings.Compute`
`includeLive`) + sofortiger Push vor dem MIRA-Aufruf, und **MIRA** kommentiert
jetzt zeitbezogen (Schlussphase, Golden Goal, periodische Zwischenkommentare)
sowie mit Turnier-/Tabellenkontext (`BuildSituation`). **MIRA-Ausbau:**
überdrehte, witzige A+W-Persona (Stufe-B-System-Prompt), Anpfiff-Begrüßung mit
**Duell-Historie** (Head-to-Head in `BuildSituation`), Zwischenkommentare jetzt
**alle 30 s** (Interlude-Uhr wird nach jedem Kommentar zurückgesetzt, feuert nie
direkt nach einem Tor), neuer Mood **`CloseGap`** (Anschlusstreffer – behebt
falsches „geht in Führung" beim Verkürzen aus dem Rückstand). Der
Zwischenkommentar-Abstand ist über die **MIRA-Settings** konfigurierbar
(`mira.interludeSec`, 10–600 s, Standard 30 s; `Live.razor` liest ihn je Reload).
**Kommentar-Feinschliff:** Anpfiff nennt jetzt die **Perspektiven** (was jedes
Team erreichen kann), Abpfiff liefert **Zusammenfassung + Ausblick** (Endstand im
Kontext, Tabellen-/Turnierfolgen), A+W-Software-Witze deutlich reduziert, und der
`Comeback`-Mood („Spiel gedreht") feuert korrekt, indem `Live.razor` die
Rückstands-Historie je Team verfolgt (`Derive`-Flags `scorerAWasBehind/BWasBehind`).
Neuer Mood **`TimeUp`**: läuft die reguläre Zeit der letzten Halbzeit ab, liefert
MIRA eine **kurze Spielzusammenfassung** (Ticker-Trigger in `Live.razor`, außer
beim Golden Goal). **Auto-K.o.-Phase:** `MatchControl.Finish` erzeugt bei
Gruppen-Turnieren automatisch die K.o.-Phase, sobald das letzte Gruppenspiel
beendet ist (`TryGenerateKnockout`; manueller Button auf `/rankings` bleibt als
Fallback). MIRA sagt den Übergang an (neuer Mood **`KnockoutStart`**), inkl.
K.o.-Baum in `BuildSituation` (Runden/Paarungen, mögliche Finalpaarungen).
Neuer Mood **`Champion`**: sobald der Turniersieger feststeht (Siegerehrung),
feiert MIRA ihn einmalig (`_championAnnouncedTid` in `Live.razor`; gilt auch für
Liga-Sieger). **Turnierphase-Kontext:** `MiraContext.Phase` sagt MIRA die Art des
aktuellen Spiels (Gruppenspiel/Halbfinale/Finale/Spiel um Platz 3/Ligaspiel),
damit sie K.o.-Spiele nicht mit Gruppenspielen verwechselt (`PhaseOf`). **Spiel um
Platz 3** ist im Setup **wählbar** (`Tournament.ThirdPlaceMatch`); der K.o.-Baum
verdrahtet die Halbfinal-Verlierer über neue Match-Felder `LoserNextMatchId`/
`LoserNextSlot` ins Bronze-Spiel (`IsThirdPlace`). Die **Siegerehrung** ist
langsamer/epischer animiert und zeigt dauerhaft das **Podium 1./2./3. Platz**.
46 Tests grün.
Details/History: `PLAN.md`.
