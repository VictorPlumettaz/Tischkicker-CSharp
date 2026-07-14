using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
