using Tischkicker.Core;
using Tischkicker.Core.Domain;
using Xunit;

namespace Tischkicker.Tests;

public class EloTests
{
    [Fact]
    public void ExpectedScore_EqualRatings_IsHalf() =>
        Assert.Equal(0.5, Elo.ExpectedScore(1000, 1000), 10);

    [Fact]
    public void ApplyMatch_EqualRatings_WinnerGains16()
    {
        var (a, b) = Elo.ApplyMatch(1000, 1000, MatchOutcome.Win);
        Assert.Equal(1016, a);
        Assert.Equal(984, b);
    }

    [Fact]
    public void ApplyMatch_Draw_NoChangeWhenEqual()
    {
        var (a, b) = Elo.ApplyMatch(1000, 1000, MatchOutcome.Draw);
        Assert.Equal(1000, a);
        Assert.Equal(1000, b);
    }

    [Fact]
    public void UpdateElo_Underdog_WinGainsMore()
    {
        // 1000 schlägt 1200: großer Gewinn.
        Assert.Equal(1024, Elo.UpdateElo(1000, 1200, MatchOutcome.Win));
    }
}

public class ScheduleTests
{
    [Fact]
    public void RoundRobin_FourTeams_ThreeRoundsSixGames()
    {
        var p = Schedule.RoundRobin([1, 2, 3, 4]);
        Assert.Equal(6, p.Count);
        Assert.Equal(3, p.Select(x => x.Round).Distinct().Count());
    }

    [Fact]
    public void RoundRobin_OddTeams_EveryoneRestsOnce()
    {
        var p = Schedule.RoundRobin([1, 2, 3]); // 3 Teams → 3 Spiele
        Assert.Equal(3, p.Count);
    }

    [Fact]
    public void Knockout_AlwaysNMinusOneGames()
    {
        Assert.Equal(3, Schedule.Knockout([1, 2, 3, 4]).Count);
        Assert.Equal(4, Schedule.Knockout([1, 2, 3, 4, 5]).Count); // mit Freilosen
        Assert.Equal(7, Schedule.Knockout([1, 2, 3, 4, 5, 6, 7, 8]).Count);
    }

    [Fact]
    public void GroupStage_EightTeams_TwoGroups()
    {
        var groups = Schedule.PartitionIntoGroups([1, 2, 3, 4, 5, 6, 7, 8]);
        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Equal(4, g.Count));
    }
}

public class StandingsTests
{
    private static Match Fin(int a, int b, int sa, int sb) => new()
    {
        TeamAId = a, TeamBId = b, ScoreA = sa, ScoreB = sb, Status = MatchStatus.Finished,
    };

    [Fact]
    public void Points_3_1_0_AndTiebreakerByGoalDiff()
    {
        var matches = new[]
        {
            Fin(1, 2, 3, 0), // 1 gewinnt
            Fin(3, 4, 1, 1), // Remis
            Fin(1, 3, 1, 1), // Remis
        };
        var rows = Standings.Compute([1, 2, 3, 4], matches);
        Assert.Equal(1, rows[0].TeamId);       // 4 Punkte, führt
        Assert.Equal(4, rows[0].Points);
        Assert.Equal(3, rows[0].GoalDiff);
    }

    [Fact]
    public void IgnoresUnfinishedMatches()
    {
        var live = new Match { TeamAId = 1, TeamBId = 2, ScoreA = 5, ScoreB = 0, Status = MatchStatus.Live };
        var rows = Standings.Compute([1, 2], [live]);
        Assert.All(rows, r => Assert.Equal(0, r.Played));
    }

    [Fact]
    public void IncludeLive_CountsRunningMatchProvisionally()
    {
        var live = new Match { TeamAId = 1, TeamBId = 2, ScoreA = 5, ScoreB = 0, Status = MatchStatus.Live };
        var rows = Standings.Compute([1, 2], [live], includeLive: true);
        Assert.Equal(1, rows[0].TeamId);   // Team 1 führt provisorisch
        Assert.Equal(3, rows[0].Points);   // Sieg-Zwischenstand → 3 Punkte
        Assert.Equal(5, rows[0].GoalDiff);
        Assert.All(rows, r => Assert.Equal(1, r.Played));
    }

    [Fact]
    public void IncludeLive_StillIgnoresScheduledMatches()
    {
        var sched = new Match { TeamAId = 1, TeamBId = 2, ScoreA = 0, ScoreB = 0, Status = MatchStatus.Scheduled };
        var rows = Standings.Compute([1, 2], [sched], includeLive: true);
        Assert.All(rows, r => Assert.Equal(0, r.Played));
    }
}

public class MatchClockTests
{
    [Fact]
    public void RunningClock_AccumulatesElapsed()
    {
        var s = MatchClock.Start(MatchClock.Reset(), 1000);
        Assert.Equal(12, MatchClock.ElapsedSeconds(s, 13_000), 3);
    }

    [Fact]
    public void Pause_FreezesElapsed()
    {
        var s = MatchClock.Start(MatchClock.Reset(), 1000);
        var paused = MatchClock.Pause(s, 13_000);
        Assert.False(paused.Running);
        Assert.Equal(12, paused.ElapsedSec, 3);
    }

    [Fact]
    public void Remaining_NeverNegative()
    {
        var s = new ClockState(500, false, null);
        Assert.Equal(0, MatchClock.RemainingSeconds(s, 360, 0));
    }
}

public class MiraTests
{
    [Fact]
    public void Comment_FillsTemplate()
    {
        var text = MiraPhrases.Comment(
            new MiraContext { Mood = MiraMood.Lead, Scorer = "Rot" }, _ => 0);
        Assert.Contains("Rot", text);
    }

    private static MiraContext? DeriveGoal(int prevA, int prevB, int curA, int curB,
        bool aWasBehind = false, bool bWasBehind = false)
    {
        var prev = new Match { Id = 1, Status = MatchStatus.Live, ScoreA = prevA, ScoreB = prevB };
        var cur = new Match { Id = 1, Status = MatchStatus.Live, ScoreA = curA, ScoreB = curB };
        return MiraNarrator.Derive(prev, cur, null, id => id?.ToString() ?? "—", aWasBehind, bWasBehind);
    }

    [Fact]
    public void Narrator_ScoringWhileStillTrailing_IsCloseGap()
    {
        // A führt 2:0, B verkürzt auf 2:1 → B liegt weiter hinten, keine Führung.
        var ctx = DeriveGoal(2, 0, 2, 1);
        Assert.Equal(MiraMood.CloseGap, ctx!.Mood);
    }

    [Fact]
    public void Narrator_ScoringToTie_IsEqualizer()
    {
        var ctx = DeriveGoal(1, 0, 1, 1);
        Assert.Equal(MiraMood.Equalizer, ctx!.Mood);
    }

    [Fact]
    public void Narrator_TakingLeadFromTie_IsLead()
    {
        var ctx = DeriveGoal(1, 1, 2, 1);
        Assert.Equal(MiraMood.Lead, ctx!.Mood);
    }

    [Fact]
    public void Narrator_ExtendingLead_IsExtend()
    {
        var ctx = DeriveGoal(1, 0, 2, 0);
        Assert.Equal(MiraMood.Extend, ctx!.Mood);
    }

    [Fact]
    public void Narrator_LeadFromTieAfterTrailing_IsComeback()
    {
        // A lag zurück, glich aus (1:1) und geht jetzt 2:1 in Führung → Spiel gedreht.
        var ctx = DeriveGoal(1, 1, 2, 1, aWasBehind: true);
        Assert.Equal(MiraMood.Comeback, ctx!.Mood);
    }

    [Fact]
    public void Narrator_FirstLeadWithoutTrailing_IsLead()
    {
        // Ohne vorherigen Rückstand ist die Führung aus dem Gleichstand nur „Lead", kein Comeback.
        var ctx = DeriveGoal(1, 1, 2, 1, aWasBehind: false);
        Assert.Equal(MiraMood.Lead, ctx!.Mood);
    }

    [Fact]
    public void Narrator_Finish_CarriesFinalScore()
    {
        var prev = new Match { Id = 1, Status = MatchStatus.Live, ScoreA = 3, ScoreB = 2 };
        var cur = new Match { Id = 1, Status = MatchStatus.Finished, ScoreA = 3, ScoreB = 2 };
        var ctx = MiraNarrator.Derive(prev, cur, null, id => id?.ToString() ?? "—");
        Assert.Equal(MiraMood.Win, ctx!.Mood);
        Assert.Equal(3, ctx.ScoreA);
        Assert.Equal(2, ctx.ScoreB);
    }

    [Fact]
    public void Tip_NeutralWhenClose()
    {
        var tip = MiraTipEngine.Compute(1000, 1000, "A", "B");
        Assert.Null(tip.Favorite);
    }

    [Fact]
    public void Tip_FavoursHigherElo()
    {
        var tip = MiraTipEngine.Compute(1300, 1000, "A", "B");
        Assert.Equal("a", tip.Favorite);
    }

    [Fact]
    public void FormatAdvisor_RecommendsByTeamCount()
    {
        Assert.Equal(TournamentFormat.RoundRobin, FormatAdvisor.Recommend(4).Format);
        Assert.Equal(TournamentFormat.Groups, FormatAdvisor.Recommend(8).Format);
        Assert.Equal(TournamentFormat.Knockout, FormatAdvisor.Recommend(16).Format);
    }
}
