using Microsoft.EntityFrameworkCore;
using Tischkicker.Core;
using Tischkicker.Core.Domain;
using Tischkicker.Data;

namespace Tischkicker.Services;

public sealed class TournamentSetupException(string message) : Exception(message);

/// <summary>
/// Turnier-Vorbereitung: Erstellen, Kader zuordnen, Spielplan generieren (Matches
/// anlegen + K.o.-Sieger-Wege verdrahten), K.o.-Phase aus Gruppentabellen, Tabellen.
/// Portiert aus services/tournamentSetup.ts + routes/tournaments.ts.
/// </summary>
public sealed class TournamentSetup(IDbContextFactory<AppDbContext> dbf, LiveNotifier notifier)
{
    public static FormatRecommendation Recommend(int teamCount) => FormatAdvisor.Recommend(teamCount);

    public Tournament CreateTournament(string name, TournamentFormat format, int halves, int halfDurationSec,
        bool thirdPlaceMatch = false)
    {
        using var db = dbf.CreateDbContext();
        var t = new Tournament
        {
            Name = name.Trim(),
            Format = format,
            Halves = halves is 1 or 2 ? halves : 1,
            HalfDurationSec = halfDurationSec > 0 ? halfDurationSec : 360,
            ThirdPlaceMatch = thirdPlaceMatch && format != TournamentFormat.RoundRobin,
        };
        db.Tournaments.Add(t);
        db.SaveChanges();
        notifier.NotifyChanged();
        return t;
    }

    /// <summary>Aktualisiert die Stammdaten eines Turniers (Name, Format, Halbzeiten, Dauer).</summary>
    public Tournament UpdateTournament(int tournamentId, string name, TournamentFormat format, int halves, int halfDurationSec,
        bool thirdPlaceMatch = false)
    {
        using var db = dbf.CreateDbContext();
        var t = db.Tournaments.Find(tournamentId)
            ?? throw new TournamentSetupException("Turnier nicht gefunden.");
        t.Name = name.Trim();
        t.Format = format;
        t.Halves = halves is 1 or 2 ? halves : 1;
        t.HalfDurationSec = halfDurationSec > 0 ? halfDurationSec : 360;
        t.ThirdPlaceMatch = thirdPlaceMatch && format != TournamentFormat.RoundRobin;
        db.SaveChanges();
        notifier.NotifyChanged();
        return t;
    }

    /// <summary>
    /// Setzt ein Turnier zurück: alle Ergebnisse gelöscht, Spiele wieder auf „geplant"
    /// (0:0, Uhr/Halbzeit zurück). Bei Gruppen-Turnieren wird eine bereits erzeugte
    /// K.o.-Phase entfernt; in K.o.-Bäumen werden vorgerückte Teams geleert. ELO/Bilanz
    /// anschließend über <see cref="MatchControl.RecomputeStats"/> neu berechnen.
    /// </summary>
    public void ResetResults(int tournamentId)
    {
        using var db = dbf.CreateDbContext();
        var tournament = db.Tournaments.Find(tournamentId)
            ?? throw new TournamentSetupException("Turnier nicht gefunden.");

        var matches = db.Matches.Where(m => m.TournamentId == tournamentId).ToList();

        // Gruppen-Turnier: erzeugte K.o.-Phase (ohne GroupName) wieder entfernen.
        if (tournament.Format == TournamentFormat.Groups)
        {
            var bracket = matches.Where(m => m.GroupName == null).ToList();
            if (bracket.Count > 0)
            {
                db.Matches.RemoveRange(bracket);
                matches = matches.Where(m => m.GroupName != null).ToList();
            }
        }

        // Nur Slots leeren, die erst durchs Vorrücken befüllt werden (aus NextSlot der
        // zubringenden Spiele). Vorbelegte Freilos-Slots bleiben erhalten.
        var fedSlots = new Dictionary<int, HashSet<string>>();
        void MarkFed(int matchId, string slot)
        {
            if (!fedSlots.TryGetValue(matchId, out var slots)) fedSlots[matchId] = slots = [];
            slots.Add(slot);
        }
        foreach (var m in matches.Where(m => m.NextMatchId is not null && m.NextSlot is not null))
            MarkFed(m.NextMatchId!.Value, m.NextSlot!);
        // Auch die Slots des Spiels um Platz 3 (aus den Verlierer-Wegen) werden leergeräumt.
        foreach (var m in matches.Where(m => m.LoserNextMatchId is not null && m.LoserNextSlot is not null))
            MarkFed(m.LoserNextMatchId!.Value, m.LoserNextSlot!);

        foreach (var m in matches)
        {
            m.ScoreA = 0;
            m.ScoreB = 0;
            m.Status = MatchStatus.Scheduled;
            m.CurrentHalf = 1;
            m.ElapsedSec = 0;
            m.TimerRunning = false;
            m.TimerStartedAtMs = null;
            m.StartedAt = null;
            m.FinishedAt = null;
            if (fedSlots.TryGetValue(m.Id, out var clear))
            {
                if (clear.Contains("a")) m.TeamAId = null;
                if (clear.Contains("b")) m.TeamBId = null;
            }
        }

        tournament.Status = TournamentStatus.Running;
        db.SaveChanges();
        notifier.NotifyChanged();
    }

    public void DeleteTournament(int tournamentId)
    {
        using var db = dbf.CreateDbContext();
        var t = db.Tournaments.Find(tournamentId)
            ?? throw new TournamentSetupException("Turnier nicht gefunden.");
        db.Matches.RemoveRange(db.Matches.Where(m => m.TournamentId == tournamentId));
        db.TournamentTeams.RemoveRange(db.TournamentTeams.Where(x => x.TournamentId == tournamentId));
        db.Tournaments.Remove(t);
        db.SaveChanges();
        notifier.NotifyChanged();
    }

    /// <summary>Kader setzen (ersetzt den kompletten Kader; Setzung = Reihenfolge).</summary>
    public void SetTeams(int tournamentId, IReadOnlyList<int> teamIds)
    {
        using var db = dbf.CreateDbContext();
        if (db.Tournaments.Find(tournamentId) is null)
            throw new TournamentSetupException("Turnier nicht gefunden.");
        var unique = teamIds.Distinct().ToList();
        foreach (var id in unique)
            if (db.Teams.Find(id) is null)
                throw new TournamentSetupException($"Team {id} nicht gefunden.");

        db.TournamentTeams.RemoveRange(db.TournamentTeams.Where(x => x.TournamentId == tournamentId));
        for (var i = 0; i < unique.Count; i++)
            db.TournamentTeams.Add(new TournamentTeam { TournamentId = tournamentId, TeamId = unique[i], Seed = i });
        db.SaveChanges();
        notifier.NotifyChanged();
    }

    /// <summary>Erzeugt den Spielplan neu aus dem aktuellen Kader.</summary>
    public List<Match> Generate(int tournamentId)
    {
        using var db = dbf.CreateDbContext();
        var tournament = db.Tournaments.Find(tournamentId)
            ?? throw new TournamentSetupException("Turnier nicht gefunden.");
        var ids = RosterTeamIds(db, tournamentId);
        if (ids.Count < 2)
            throw new TournamentSetupException("Für einen Spielplan werden mindestens zwei Teams benötigt.");

        var pairings = Schedule.Generate(tournament.Format, ids, tournament.ThirdPlaceMatch);

        db.Matches.RemoveRange(db.Matches.Where(m => m.TournamentId == tournamentId));
        db.SaveChanges();

        PersistPairings(db, tournamentId, pairings);

        if (tournament.Format == TournamentFormat.Groups)
        {
            var roster = db.TournamentTeams.Where(x => x.TournamentId == tournamentId).ToList();
            foreach (var p in pairings)
            {
                if (p.GroupName is null) continue;
                foreach (var teamId in new[] { p.TeamAId, p.TeamBId })
                    if (teamId is { } tid && roster.FirstOrDefault(r => r.TeamId == tid) is { } rr)
                        rr.GroupName = p.GroupName;
            }
        }

        tournament.Status = TournamentStatus.Running;
        db.SaveChanges();
        notifier.NotifyChanged();
        return db.Matches.Where(m => m.TournamentId == tournamentId).OrderBy(m => m.Id).ToList();
    }

    /// <summary>K.o.-Phase aus den Gruppentabellen (Standard: beste 2 je Gruppe, über Kreuz).</summary>
    public List<Match> GenerateKnockoutPhase(int tournamentId, int qualifiersPerGroup = 2)
    {
        using var db = dbf.CreateDbContext();
        var tournament = db.Tournaments.Find(tournamentId)
            ?? throw new TournamentSetupException("Turnier nicht gefunden.");
        if (tournament.Format != TournamentFormat.Groups)
            throw new TournamentSetupException("Eine K.o.-Phase gibt es nur bei Gruppen-Turnieren.");

        var all = db.Matches.Where(m => m.TournamentId == tournamentId).ToList();
        var groupMatches = all.Where(m => m.GroupName != null).ToList();
        if (groupMatches.Count == 0)
            throw new TournamentSetupException("Es gibt noch keine Gruppenphase.");
        if (groupMatches.Any(m => m.Status != MatchStatus.Finished))
            throw new TournamentSetupException("Die Gruppenphase ist noch nicht abgeschlossen.");
        if (all.Any(m => m.GroupName == null))
            throw new TournamentSetupException("Die K.o.-Phase wurde bereits erzeugt.");

        var roster = db.TournamentTeams.Where(x => x.TournamentId == tournamentId).ToList();
        var byGroup = roster.Where(r => r.GroupName != null)
            .GroupBy(r => r.GroupName!)
            .ToDictionary(g => g.Key, g => g.Select(r => r.TeamId).ToList());
        var groupNames = byGroup.Keys.OrderBy(x => x).ToList();

        var byRank = new List<int>[qualifiersPerGroup];
        for (var i = 0; i < qualifiersPerGroup; i++) byRank[i] = [];
        foreach (var g in groupNames)
        {
            var standings = Standings.Compute(byGroup[g], groupMatches.Where(m => m.GroupName == g));
            for (var rank = 0; rank < qualifiersPerGroup && rank < standings.Count; rank++)
                byRank[rank].Add(standings[rank].TeamId);
        }
        var seedList = byRank.SelectMany(x => x).ToList();
        if (seedList.Count < 2)
            throw new TournamentSetupException("Zu wenige Qualifikanten für eine K.o.-Phase.");

        var maxGroupRound = groupMatches.Max(m => m.Round ?? 1);
        PersistPairings(db, tournamentId, Schedule.Knockout(seedList, tournament.ThirdPlaceMatch), maxGroupRound);
        notifier.NotifyChanged();
        return db.Matches.Where(m => m.TournamentId == tournamentId).OrderBy(m => m.Id).ToList();
    }

    /// <summary>
    /// Tabellen: Liga → eine, Gruppen → je Gruppe, K.o. → keine. Mit
    /// <paramref name="includeLive"/> zählt ein laufendes Spiel provisorisch mit.
    /// </summary>
    public StandingsResponse GetStandings(int tournamentId, bool includeLive = false)
    {
        using var db = dbf.CreateDbContext();
        var tournament = db.Tournaments.Find(tournamentId)
            ?? throw new TournamentSetupException("Turnier nicht gefunden.");
        var matches = db.Matches.Where(m => m.TournamentId == tournamentId).ToList();
        var roster = db.TournamentTeams.Where(x => x.TournamentId == tournamentId).ToList();

        var groups = new List<StandingsGroup>();
        if (tournament.Format == TournamentFormat.Groups)
        {
            foreach (var name in roster.Where(r => r.GroupName != null).Select(r => r.GroupName!).Distinct().OrderBy(x => x))
            {
                var ids = roster.Where(r => r.GroupName == name).Select(r => r.TeamId).ToList();
                groups.Add(new StandingsGroup(name, Standings.Compute(ids, matches.Where(m => m.GroupName == name), includeLive)));
            }
        }
        else if (tournament.Format == TournamentFormat.RoundRobin)
        {
            var ids = roster.Select(r => r.TeamId).ToList();
            groups.Add(new StandingsGroup(null, Standings.Compute(ids, matches, includeLive)));
        }
        return new StandingsResponse(tournament.Format, groups);
    }

    private static List<int> RosterTeamIds(AppDbContext db, int tournamentId) =>
        db.TournamentTeams.Where(x => x.TournamentId == tournamentId)
            .OrderBy(x => x.Seed).Select(x => x.TeamId).ToList();

    /// <summary>Legt Matches aus Paarungen an und verdrahtet K.o.-Sieger-Wege (2 Durchgänge).</summary>
    private static void PersistPairings(AppDbContext db, int tournamentId, List<Pairing> pairings, int roundOffset = 0)
    {
        var idBySlot = new Dictionary<(int, int), int>();
        foreach (var p in pairings)
        {
            var m = new Match
            {
                TournamentId = tournamentId,
                TeamAId = p.TeamAId,
                TeamBId = p.TeamBId,
                Round = p.Round + roundOffset,
                GroupName = p.GroupName,
                BracketSlot = p.Slot.ToString(),
                IsThirdPlace = p.IsThirdPlace,
            };
            db.Matches.Add(m);
            db.SaveChanges(); // ID materialisieren
            idBySlot[(p.Round, p.Slot)] = m.Id;
        }
        foreach (var p in pairings)
        {
            var m = db.Matches.Find(idBySlot[(p.Round, p.Slot)])!;
            if (p.FeedsInto is { } f)
            {
                m.NextMatchId = idBySlot[(f.Round, f.Slot)];
                m.NextSlot = f.Side;
            }
            if (p.LoserFeedsInto is { } lf)
            {
                m.LoserNextMatchId = idBySlot[(lf.Round, lf.Slot)];
                m.LoserNextSlot = lf.Side;
            }
        }
        db.SaveChanges();
    }
}
