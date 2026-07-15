using Microsoft.EntityFrameworkCore;
using Tischkicker.Core;
using Tischkicker.Core.Domain;
using Tischkicker.Data;

namespace Tischkicker.Services;

/// <summary>Ungültige Steuer-Aktion (UI zeigt die Meldung).</summary>
public sealed class MatchControlException(string message) : Exception(message);

/// <summary>
/// Live-Steuerung eines Spiels: Start/Pause, Tore, Uhr verstellen, Halbzeit,
/// Beenden (inkl. ELO-/Bilanz-Verbuchung) sowie nachträgliche Ergebnis-Korrektur
/// (mit vollständigem Neu-Einspielen). Portiert aus services/matchControl.ts.
/// </summary>
public sealed class MatchControl(IDbContextFactory<AppDbContext> dbf, LiveNotifier notifier, TournamentSetup setup)
{
    private const string FriendlyName = "Freundschaftsspiele";

    public static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static ClockState ClockOf(Match m) => new(m.ElapsedSec, m.TimerRunning, m.TimerStartedAtMs);

    private static void ApplyClock(Match m, ClockState c)
    {
        m.ElapsedSec = c.ElapsedSec;
        m.TimerRunning = c.Running;
        m.TimerStartedAtMs = c.StartedAtMs;
    }

    private static Match Require(AppDbContext db, int id) =>
        db.Matches.Find(id) ?? throw new MatchControlException("Spiel nicht gefunden.");

    private Match Commit(AppDbContext db, Match m)
    {
        db.SaveChanges();
        notifier.NotifyChanged();
        return m;
    }

    public Match Start(int id, long nowMs)
    {
        using var db = dbf.CreateDbContext();
        var m = Require(db, id);
        if (m.Status == MatchStatus.Finished) throw new MatchControlException("Spiel ist bereits beendet.");
        ApplyClock(m, MatchClock.Start(ClockOf(m), nowMs));
        m.Status = MatchStatus.Live;
        m.StartedAt ??= DateTimeOffset.FromUnixTimeMilliseconds(nowMs).UtcDateTime;
        return Commit(db, m);
    }

    public Match Pause(int id, long nowMs)
    {
        using var db = dbf.CreateDbContext();
        var m = Require(db, id);
        ApplyClock(m, MatchClock.Pause(ClockOf(m), nowMs));
        return Commit(db, m);
    }

    public Match Resume(int id, long nowMs)
    {
        using var db = dbf.CreateDbContext();
        var m = Require(db, id);
        if (m.Status == MatchStatus.Finished) throw new MatchControlException("Spiel ist bereits beendet.");
        ApplyClock(m, MatchClock.Start(ClockOf(m), nowMs));
        return Commit(db, m);
    }

    /// <summary>Ändert den Spielstand eines Teams um <paramref name="delta"/> (nie unter 0).</summary>
    public Match AdjustScore(int id, string team, int delta)
    {
        using var db = dbf.CreateDbContext();
        var m = Require(db, id);
        if (team == "a") m.ScoreA = Math.Max(0, m.ScoreA + delta);
        else m.ScoreB = Math.Max(0, m.ScoreB + delta);
        return Commit(db, m);
    }

    /// <summary>Uhr vorstellen (deltaSec &gt; 0) oder zurückstellen (&lt; 0). Nicht bei beendetem Spiel.</summary>
    public Match AdjustClock(int id, double deltaSec, long nowMs)
    {
        using var db = dbf.CreateDbContext();
        var m = Require(db, id);
        if (m.Status == MatchStatus.Finished) throw new MatchControlException("Spiel ist bereits beendet.");
        var next = Math.Max(0, MatchClock.ElapsedSeconds(ClockOf(m), nowMs) + deltaSec);
        var running = m.TimerRunning;
        ApplyClock(m, new ClockState(next, running, running ? nowMs : null));
        return Commit(db, m);
    }

    public Match NextHalf(int id, long nowMs)
    {
        using var db = dbf.CreateDbContext();
        var m = Require(db, id);
        var halves = db.Tournaments.Find(m.TournamentId)?.Halves ?? 1;
        if (m.CurrentHalf >= halves) throw new MatchControlException("Keine weitere Halbzeit vorgesehen.");
        m.CurrentHalf++;
        ApplyClock(m, MatchClock.Reset());
        return Commit(db, m);
    }

    /// <summary>
    /// Ein K.o.-Spiel braucht einen Sieger: reines K.o.-Turnier oder die K.o.-Phase
    /// eines Gruppen-Turniers (Match ohne Gruppe). Liga/Gruppenphase = Remis erlaubt.
    /// </summary>
    private static bool IsKnockoutMatch(AppDbContext db, Match m)
    {
        var t = db.Tournaments.Find(m.TournamentId);
        return t is not null && (t.Format == TournamentFormat.Knockout
            || (t.Format == TournamentFormat.Groups && m.GroupName is null));
    }

    /// <summary>Beendet das Spiel und verbucht Bilanz + ELO (idempotent).</summary>
    public Match Finish(int id, long nowMs)
    {
        using var db = dbf.CreateDbContext();
        var m = Require(db, id);
        if (m.Status == MatchStatus.Finished) return m;

        if (m.ScoreA == m.ScoreB && IsKnockoutMatch(db, m))
            throw new MatchControlException(
                "K.o.-Spiel kann nicht unentschieden enden – Golden Goal: weiterspielen, bis ein Team trifft.");

        ApplyClock(m, MatchClock.Pause(ClockOf(m), nowMs));
        m.Status = MatchStatus.Finished;
        m.FinishedAt = DateTimeOffset.FromUnixTimeMilliseconds(nowMs).UtcDateTime;

        BookMatch(db, m);

        // K.o.: Sieger ins Folgespiel vorrücken (Remis → offen lassen, Golden Goal).
        if (m.NextMatchId is { } nextId && m.NextSlot is { } slot)
        {
            var winnerId = m.ScoreA > m.ScoreB ? m.TeamAId : m.ScoreB > m.ScoreA ? m.TeamBId : null;
            if (winnerId is not null && db.Matches.Find(nextId) is { } nextM)
            {
                if (slot == "a") nextM.TeamAId = winnerId; else nextM.TeamBId = winnerId;
            }
        }

        // K.o.: Verlierer ins Spiel um Platz 3 vorrücken (nur bei gesetztem Verlierer-Weg).
        if (m.LoserNextMatchId is { } lNextId && m.LoserNextSlot is { } lSlot)
        {
            var loserId = m.ScoreA > m.ScoreB ? m.TeamBId : m.ScoreB > m.ScoreA ? m.TeamAId : null;
            if (loserId is not null && db.Matches.Find(lNextId) is { } lNextM)
            {
                if (lSlot == "a") lNextM.TeamAId = loserId; else lNextM.TeamBId = loserId;
            }
        }
        db.SaveChanges();

        // War das das letzte offene Gruppenspiel? Dann die K.o.-Phase automatisch erzeugen.
        TryGenerateKnockout(db, m);

        notifier.NotifyChanged();
        return m;
    }

    /// <summary>
    /// Erzeugt die K.o.-Phase automatisch, sobald mit diesem Spiel die letzte offene
    /// Gruppenpartie beendet wurde (Gruppen-Turnier, Baum noch nicht vorhanden).
    /// Fehler werden geschluckt, damit das Beenden des Spiels nie scheitert.
    /// </summary>
    private void TryGenerateKnockout(AppDbContext db, Match finished)
    {
        if (finished.GroupName is null) return; // kein Gruppenspiel → nichts zu tun

        var t = db.Tournaments.Find(finished.TournamentId);
        if (t is null || t.Format != TournamentFormat.Groups) return;

        var all = db.Matches.Where(m => m.TournamentId == finished.TournamentId).ToList();
        var group = all.Where(m => m.GroupName != null).ToList();
        if (group.Count == 0 || group.Any(m => m.Status != MatchStatus.Finished)) return; // noch nicht fertig
        if (all.Any(m => m.GroupName == null)) return; // K.o.-Phase existiert bereits

        try { setup.GenerateKnockoutPhase(finished.TournamentId); }
        catch (Exception e) { Console.WriteLine($"[matchcontrol] Auto-K.o.-Phase fehlgeschlagen: {e.Message}"); }
    }

    /// <summary>
    /// Nächstes noch nicht gestartetes Spiel im selben Turnier (nach Runde, dann Id).
    /// Bevorzugt Paarungen, bei denen bereits beide Teams feststehen. <c>null</c>,
    /// wenn kein weiteres Spiel ansteht.
    /// </summary>
    public Match? GetNextScheduled(int tournamentId, int excludeMatchId)
    {
        using var db = dbf.CreateDbContext();
        var scheduled = db.Matches
            .Where(m => m.TournamentId == tournamentId
                && m.Status == MatchStatus.Scheduled && m.Id != excludeMatchId)
            .OrderBy(m => m.Round ?? 0).ThenBy(m => m.Id)
            .ToList();
        return scheduled.FirstOrDefault(m => m.TeamAId is not null && m.TeamBId is not null)
            ?? scheduled.FirstOrDefault();
    }

    /// <summary>Legt ein schnelles Freundschaftsspiel an (eigenes Sammel-Turnier).</summary>
    public Match CreateFriendly(int teamAId, int teamBId)
    {
        using var db = dbf.CreateDbContext();
        if (teamAId == teamBId) throw new MatchControlException("Ein Team kann nicht gegen sich selbst spielen.");
        if (db.Teams.Find(teamAId) is null || db.Teams.Find(teamBId) is null)
            throw new MatchControlException("Team nicht gefunden.");

        var tournament = db.Tournaments.FirstOrDefault(t => t.Name == FriendlyName);
        if (tournament is null)
        {
            tournament = new Tournament { Name = FriendlyName, Format = TournamentFormat.RoundRobin };
            db.Tournaments.Add(tournament);
            db.SaveChanges();
        }
        var match = new Match { TournamentId = tournament.Id, TeamAId = teamAId, TeamBId = teamBId };
        db.Matches.Add(match);
        return Commit(db, match);
    }

    /// <summary>
    /// Korrigiert das Ergebnis nachträglich (absolute Werte). Bei einem beendeten
    /// Spiel werden ELO/Bilanz aller Teams neu berechnet und – falls der Sieger eines
    /// K.o.-Spiels wechselt und das Folgespiel noch nicht begonnen hat – dessen Slot
    /// aktualisiert.
    /// </summary>
    public Match CorrectResult(int id, int scoreA, int scoreB)
    {
        using var db = dbf.CreateDbContext();
        var m = Require(db, id);
        m.ScoreA = Math.Max(0, scoreA);
        m.ScoreB = Math.Max(0, scoreB);

        if (m.Status == MatchStatus.Finished)
        {
            if (m.NextMatchId is { } nextId && m.NextSlot is { } slot &&
                db.Matches.Find(nextId) is { Status: MatchStatus.Scheduled } nextM)
            {
                var winnerId = m.ScoreA > m.ScoreB ? m.TeamAId : m.ScoreB > m.ScoreA ? m.TeamBId : null;
                if (slot == "a") nextM.TeamAId = winnerId; else nextM.TeamBId = winnerId;
            }
            RecomputeInternal(db);
        }
        return Commit(db, m);
    }

    /// <summary>ELO + Bilanz aller Teams neu berechnen (Startwerte + Replay).</summary>
    public void RecomputeStats()
    {
        using var db = dbf.CreateDbContext();
        RecomputeInternal(db);
        db.SaveChanges();
        notifier.NotifyChanged();
    }

    private static void RecomputeInternal(AppDbContext db)
    {
        foreach (var t in db.Teams)
        {
            t.Elo = Elo.DefaultElo;
            t.Wins = t.Losses = t.Draws = t.GoalsFor = t.GoalsAgainst = 0;
        }
        var finished = db.Matches
            .Where(m => m.Status == MatchStatus.Finished)
            .OrderBy(m => m.FinishedAt).ThenBy(m => m.Id)
            .ToList();
        foreach (var m in finished) BookMatch(db, m);
    }

    /// <summary>Verbucht Bilanz + ELO eines beendeten Spiels auf beide Teams.</summary>
    private static void BookMatch(AppDbContext db, Match m)
    {
        if (m.TeamAId is not { } aid || m.TeamBId is not { } bid) return;
        var a = db.Teams.Find(aid);
        var b = db.Teams.Find(bid);
        if (a is null || b is null) return;

        var outcomeA = m.ScoreA > m.ScoreB ? MatchOutcome.Win
            : m.ScoreA < m.ScoreB ? MatchOutcome.Loss : MatchOutcome.Draw;
        var outcomeB = outcomeA switch
        {
            MatchOutcome.Win => MatchOutcome.Loss,
            MatchOutcome.Loss => MatchOutcome.Win,
            _ => MatchOutcome.Draw,
        };
        var (eloA, eloB) = Elo.ApplyMatch(a.Elo, b.Elo, outcomeA);

        Apply(a, outcomeA, m.ScoreA, m.ScoreB, eloA);
        Apply(b, outcomeB, m.ScoreB, m.ScoreA, eloB);

        static void Apply(Team t, MatchOutcome o, int gf, int ga, int elo)
        {
            if (o == MatchOutcome.Win) t.Wins++;
            else if (o == MatchOutcome.Loss) t.Losses++;
            else t.Draws++;
            t.GoalsFor += gf;
            t.GoalsAgainst += ga;
            t.Elo = elo;
            t.UpdatedAt = DateTime.UtcNow;
        }
    }
}
