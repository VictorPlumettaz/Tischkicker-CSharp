using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tischkicker.Core;
using Tischkicker.Core.Domain;
using Tischkicker.Data;
using Tischkicker.Services;
using Xunit;

namespace Tischkicker.Tests;

/// <summary>IDbContextFactory über eine offen gehaltene :memory:-SQLite-Verbindung.</summary>
sealed class TestDbFactory : IDbContextFactory<AppDbContext>, IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _opts;

    public TestDbFactory()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        using var c = CreateDbContext();
        c.Database.EnsureCreated();
    }

    public AppDbContext CreateDbContext() => new(_opts);
    public void Dispose() => _conn.Dispose();
}

public class MatchControlServiceTests : IDisposable
{
    private readonly TestDbFactory _dbf = new();
    private readonly MatchControl _control;
    private const long T0 = 2_000_000;

    public MatchControlServiceTests() => _control = new MatchControl(_dbf, new LiveNotifier());

    private (int a, int b) TwoTeams()
    {
        using var db = _dbf.CreateDbContext();
        var a = new Team { Name = "A" };
        var b = new Team { Name = "B" };
        db.Teams.AddRange(a, b);
        db.SaveChanges();
        return (a.Id, b.Id);
    }

    [Fact]
    public void Finish_BooksWinGoalsAndElo()
    {
        var (a, b) = TwoTeams();
        var m = _control.CreateFriendly(a, b);
        _control.Start(m.Id, T0);
        _control.AdjustScore(m.Id, "a", 3);
        _control.AdjustScore(m.Id, "b", 1);
        _control.Finish(m.Id, T0 + 60_000);

        using var db = _dbf.CreateDbContext();
        var ta = db.Teams.Find(a)!;
        Assert.Equal(1, ta.Wins);
        Assert.Equal(3, ta.GoalsFor);
        Assert.Equal(1016, ta.Elo);
        Assert.Equal(984, db.Teams.Find(b)!.Elo);
    }

    [Fact]
    public void AdjustClock_ForwardBackAndClampAndFinishedThrows()
    {
        var (a, b) = TwoTeams();
        var m = _control.CreateFriendly(a, b);
        Assert.Equal(30, _control.AdjustClock(m.Id, 30, T0).ElapsedSec, 3);
        Assert.Equal(0, _control.AdjustClock(m.Id, -100, T0).ElapsedSec, 3);
        _control.Finish(m.Id, T0);
        Assert.Throws<MatchControlException>(() => _control.AdjustClock(m.Id, 10, T0));
    }

    [Fact]
    public void CorrectResult_FlipsEloAndStandings()
    {
        var (a, b) = TwoTeams();
        var m = _control.CreateFriendly(a, b);
        _control.AdjustScore(m.Id, "a", 3);
        _control.AdjustScore(m.Id, "b", 1);
        _control.Finish(m.Id, T0);           // A gewinnt (1016/984)
        _control.CorrectResult(m.Id, 1, 3);   // jetzt B

        using var db = _dbf.CreateDbContext();
        Assert.Equal(984, db.Teams.Find(a)!.Elo);
        Assert.Equal(1016, db.Teams.Find(b)!.Elo);
        Assert.Equal(1, db.Teams.Find(b)!.Wins);
    }

    public void Dispose() => _dbf.Dispose();
}

public class SettingsServiceTests : IDisposable
{
    private readonly TestDbFactory _dbf = new();
    private readonly SettingsService _settings;

    public SettingsServiceTests() => _settings = new SettingsService(_dbf);

    [Fact]
    public void Defaults_StageBOffNoKeyDefaultModel()
    {
        var cfg = _settings.GetMiraConfig();
        Assert.False(cfg.StageBEnabled);
        Assert.Equal("", cfg.ApiKey);
        Assert.Equal(SettingsService.DefaultModel, cfg.Model);
    }

    [Fact]
    public void SetMiraConfig_StoresAndHidesKey()
    {
        _settings.SetMiraConfig(stageBEnabled: true, model: "claude-sonnet-5", apiKey: "sk-ant-secret");

        var cfg = _settings.GetMiraConfig();
        Assert.True(cfg.StageBEnabled);
        Assert.Equal("claude-sonnet-5", cfg.Model);
        Assert.Equal("sk-ant-secret", cfg.ApiKey);

        // Nach außen ist der Key nie sichtbar, nur HasApiKey.
        var pub = _settings.GetMiraConfigPublic();
        Assert.True(pub.HasApiKey);
        Assert.Equal("claude-sonnet-5", pub.Model);
    }

    [Fact]
    public void SetMiraConfig_EmptyKeyKeepsExisting_ClearRemoves()
    {
        _settings.SetMiraConfig(apiKey: "sk-ant-keep");
        _settings.SetMiraConfig(apiKey: "   ");           // leer → behalten
        Assert.Equal("sk-ant-keep", _settings.GetMiraConfig().ApiKey);

        _settings.SetMiraConfig(clearApiKey: true);       // entfernen
        Assert.Equal("", _settings.GetMiraConfig().ApiKey);
        Assert.False(_settings.GetMiraConfigPublic().HasApiKey);
    }

    [Fact]
    public void SetMiraConfig_RejectsUnknownModel()
        => Assert.Throws<ArgumentException>(() => _settings.SetMiraConfig(model: "gpt-4"));

    public void Dispose() => _dbf.Dispose();
}

public class TournamentSetupServiceTests : IDisposable
{
    private readonly TestDbFactory _dbf = new();
    private readonly TournamentSetup _setup;
    private readonly MatchControl _control;

    public TournamentSetupServiceTests()
    {
        var notifier = new LiveNotifier();
        _setup = new TournamentSetup(_dbf, notifier);
        _control = new MatchControl(_dbf, notifier);
    }

    private List<int> Teams(int n)
    {
        using var db = _dbf.CreateDbContext();
        var ids = new List<int>();
        for (var i = 0; i < n; i++)
        {
            var t = new Team { Name = $"T{i}" };
            db.Teams.Add(t);
            db.SaveChanges();
            ids.Add(t.Id);
        }
        return ids;
    }

    [Fact]
    public void Generate_Groups_ProducesGroupMatches()
    {
        var ids = Teams(8);
        var t = _setup.CreateTournament("Cup", TournamentFormat.Groups, 1, 360);
        _setup.SetTeams(t.Id, ids);
        var matches = _setup.Generate(t.Id);
        Assert.Equal(12, matches.Count); // 2 Gruppen à 4 → je 6 Spiele
        Assert.All(matches, m => Assert.NotNull(m.GroupName));
    }

    [Fact]
    public void UpdateTournament_ChangesNameFormatAndClock()
    {
        var t = _setup.CreateTournament("Alt", TournamentFormat.RoundRobin, 1, 360);
        _setup.UpdateTournament(t.Id, "Neu", TournamentFormat.Groups, 2, 300);

        using var db = _dbf.CreateDbContext();
        var updated = db.Tournaments.Find(t.Id)!;
        Assert.Equal("Neu", updated.Name);
        Assert.Equal(TournamentFormat.Groups, updated.Format);
        Assert.Equal(2, updated.Halves);
        Assert.Equal(300, updated.HalfDurationSec);
    }

    [Fact]
    public void ResetResults_ClearsMatchesAndRevertsElo()
    {
        var ids = Teams(4);
        var t = _setup.CreateTournament("Liga", TournamentFormat.RoundRobin, 1, 360);
        _setup.SetTeams(t.Id, ids);
        var matches = _setup.Generate(t.Id);
        foreach (var m in matches)
        {
            _control.AdjustScore(m.Id, "a", 2);
            _control.Finish(m.Id, MatchControl.NowMs());
        }
        // Nach dem Spielen: ELO hat sich verschoben.
        using (var db = _dbf.CreateDbContext())
            Assert.Contains(db.Teams, x => x.Elo != Elo.DefaultElo);

        _setup.ResetResults(t.Id);
        _control.RecomputeStats();

        using var db2 = _dbf.CreateDbContext();
        var reset = db2.Matches.Where(m => m.TournamentId == t.Id).ToList();
        Assert.All(reset, m => Assert.Equal(MatchStatus.Scheduled, m.Status));
        Assert.All(reset, m => Assert.Equal(0, m.ScoreA + m.ScoreB));
        // ELO/Bilanz wieder auf Startwert (nur dieses Turnier existierte).
        Assert.All(db2.Teams, x => Assert.Equal(Elo.DefaultElo, x.Elo));
        Assert.All(db2.Teams, x => Assert.Equal(0, x.Wins + x.Losses + x.Draws));
    }

    [Fact]
    public void ResetResults_Knockout_PreservesByeTeams()
    {
        // 3 Teams → K.o. mit einem Freilos: ein Team ist im Finale vorbelegt.
        var ids = Teams(3);
        var t = _setup.CreateTournament("KO", TournamentFormat.Knockout, 1, 360);
        _setup.SetTeams(t.Id, ids);
        var matches = _setup.Generate(t.Id);

        var final = matches.Single(m => m.NextMatchId == null);
        var r1 = matches.Single(m => m.NextMatchId != null);
        var byeSlot = final.TeamAId is not null ? "a" : "b";
        var byeTeam = final.TeamAId ?? final.TeamBId;
        Assert.NotNull(byeTeam);

        // Runde 1 spielen → Sieger rückt ins Finale (in den anderen Slot).
        _control.AdjustScore(r1.Id, "a", 1);
        _control.Finish(r1.Id, MatchControl.NowMs());

        _setup.ResetResults(t.Id);

        using var db = _dbf.CreateDbContext();
        var f = db.Matches.Find(final.Id)!;
        var stillBye = byeSlot == "a" ? f.TeamAId : f.TeamBId;
        var fed = byeSlot == "a" ? f.TeamBId : f.TeamAId;
        Assert.Equal(byeTeam, stillBye); // Freilos-Team bleibt erhalten
        Assert.Null(fed);                // nur der durchs Vorrücken befüllte Slot ist leer
    }

    [Fact]
    public void Finish_KnockoutDraw_ThrowsUntilWinner()
    {
        var ids = Teams(2);
        var t = _setup.CreateTournament("KO", TournamentFormat.Knockout, 1, 360);
        _setup.SetTeams(t.Id, ids);
        var final = _setup.Generate(t.Id).Single();

        _control.Start(final.Id, MatchControl.NowMs());
        // 0:0 → im K.o. nicht beendbar (Golden Goal).
        Assert.Throws<MatchControlException>(() => _control.Finish(final.Id, MatchControl.NowMs()));

        // Nach einem Tor lässt sich das Spiel beenden.
        _control.AdjustScore(final.Id, "a", 1);
        Assert.Equal(MatchStatus.Finished, _control.Finish(final.Id, MatchControl.NowMs()).Status);
    }

    [Fact]
    public void Finish_LeagueDraw_Allowed()
    {
        var ids = Teams(2);
        var t = _setup.CreateTournament("Liga", TournamentFormat.RoundRobin, 1, 360);
        _setup.SetTeams(t.Id, ids);
        var m = _setup.Generate(t.Id).First();

        _control.Start(m.Id, MatchControl.NowMs());
        // 0:0 in der Liga ist ein gültiges Remis.
        Assert.Equal(MatchStatus.Finished, _control.Finish(m.Id, MatchControl.NowMs()).Status);
    }

    [Fact]
    public void KnockoutPhase_FromFinishedGroups_WiresFinal()
    {
        var ids = Teams(8);
        var t = _setup.CreateTournament("Cup", TournamentFormat.Groups, 1, 360);
        _setup.SetTeams(t.Id, ids);
        foreach (var m in _setup.Generate(t.Id))
        {
            _control.AdjustScore(m.Id, "a", 1); // A gewinnt jeweils 1:0
            _control.Finish(m.Id, MatchControl.NowMs());
        }
        var ko = _setup.GenerateKnockoutPhase(t.Id);
        var bracket = ko.Where(m => m.GroupName == null).ToList();
        Assert.Equal(3, bracket.Count);                       // 2 Halbfinals + Finale
        Assert.Equal(2, bracket.Count(m => m.NextMatchId != null)); // beide HF speisen ins Finale
    }

    public void Dispose() => _dbf.Dispose();
}
