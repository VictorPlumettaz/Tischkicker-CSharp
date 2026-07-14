using Tischkicker.Core.Domain;

namespace Tischkicker.Core;

/// <summary>Eine Tabellenzeile (3-1-0-Wertung).</summary>
public sealed class StandingsRow
{
    public int TeamId { get; init; }
    public int Played { get; set; }
    public int Wins { get; set; }
    public int Draws { get; set; }
    public int Losses { get; set; }
    public int GoalsFor { get; set; }
    public int GoalsAgainst { get; set; }
    public int GoalDiff { get; set; }
    public int Points { get; set; }
}

public static class Standings
{
    /// <summary>
    /// Tabelle aus den beendeten Spielen (3 Punkte Sieg, 1 Remis, 0 Niederlage).
    /// Sortierung: Punkte → Tordifferenz → erzielte Tore → Team-ID. Reine Funktion.
    /// Es zählen nur beendete Spiele, bei denen beide Teams zur Menge gehören.
    /// </summary>
    public static List<StandingsRow> Compute(IReadOnlyList<int> teamIds, IEnumerable<Match> matches)
    {
        var rows = teamIds.ToDictionary(id => id, id => new StandingsRow { TeamId = id });

        foreach (var m in matches)
        {
            if (m.Status != MatchStatus.Finished) continue;
            if (m.TeamAId is not { } aid || m.TeamBId is not { } bid) continue;
            if (!rows.TryGetValue(aid, out var a) || !rows.TryGetValue(bid, out var b)) continue;

            a.Played++; b.Played++;
            a.GoalsFor += m.ScoreA; a.GoalsAgainst += m.ScoreB;
            b.GoalsFor += m.ScoreB; b.GoalsAgainst += m.ScoreA;

            if (m.ScoreA > m.ScoreB) { a.Wins++; a.Points += 3; b.Losses++; }
            else if (m.ScoreA < m.ScoreB) { b.Wins++; b.Points += 3; a.Losses++; }
            else { a.Draws++; b.Draws++; a.Points++; b.Points++; }
        }

        var list = rows.Values.ToList();
        foreach (var r in list) r.GoalDiff = r.GoalsFor - r.GoalsAgainst;
        list.Sort((x, y) =>
            y.Points != x.Points ? y.Points - x.Points :
            y.GoalDiff != x.GoalDiff ? y.GoalDiff - x.GoalDiff :
            y.GoalsFor != x.GoalsFor ? y.GoalsFor - x.GoalsFor :
            x.TeamId - y.TeamId);
        return list;
    }
}
