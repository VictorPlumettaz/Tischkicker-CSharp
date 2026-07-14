using Tischkicker.Core.Domain;

namespace Tischkicker.Core;

/// <summary>
/// Leitet aus dem Übergang des aktuellen Spiels (vorher → jetzt) die passende
/// MIRA-Stimmung ab. Reine Portierung von client/src/mira.ts. Gibt <c>null</c>
/// zurück, wenn nichts Kommentarwürdiges passiert ist.
/// </summary>
public static class MiraNarrator
{
    public static MiraContext? Derive(Match? prev, Match? current, Match? next, Func<int?, string> teamName)
    {
        // Kein laufendes Spiel → nächstes ankündigen bzw. Leerlauf.
        if (current is null)
        {
            if (next is not null && (next.TeamAId is not null || next.TeamBId is not null))
                return new MiraContext
                {
                    Mood = MiraMood.Announce,
                    TeamA = teamName(next.TeamAId),
                    TeamB = teamName(next.TeamBId),
                };
            return new MiraContext { Mood = MiraMood.Idle };
        }

        var a = teamName(current.TeamAId);
        var b = teamName(current.TeamBId);

        if (current.Status == MatchStatus.Finished)
        {
            if (current.ScoreA == current.ScoreB)
                return new MiraContext { Mood = MiraMood.Draw, TeamA = a, TeamB = b };
            var winner = current.ScoreA > current.ScoreB ? a : b;
            return new MiraContext { Mood = MiraMood.Win, Scorer = winner, TeamA = a, TeamB = b };
        }

        // Neues Spiel geworden → Anpfiff.
        if (prev is null || prev.Id != current.Id)
            return new MiraContext { Mood = MiraMood.Kickoff, TeamA = a, TeamB = b };

        // Halbzeitwechsel.
        if (prev.CurrentHalf != current.CurrentHalf)
            return new MiraContext { Mood = MiraMood.Halftime };

        // Tor? Gesamtzahl der Tore gestiegen.
        var scored = current.ScoreA + current.ScoreB - (prev.ScoreA + prev.ScoreB);
        if (scored > 0)
        {
            var scoredA = current.ScoreA > prev.ScoreA;
            var scorer = scoredA ? a : b;
            var other = scoredA ? b : a;
            var leadBefore = scoredA ? prev.ScoreA - prev.ScoreB : prev.ScoreB - prev.ScoreA;
            var leadAfter = scoredA ? current.ScoreA - current.ScoreB : current.ScoreB - current.ScoreA;

            MiraMood mood;
            if (leadAfter == 0) mood = MiraMood.Equalizer;
            else if (leadBefore < 0 && leadAfter > 0) mood = MiraMood.Comeback;
            else if (leadBefore <= 0) mood = MiraMood.Lead;
            else mood = MiraMood.Extend;

            return new MiraContext
            {
                Mood = mood,
                Scorer = scorer,
                Other = other,
                TeamA = a,
                TeamB = b,
                ScoreA = current.ScoreA,
                ScoreB = current.ScoreB,
            };
        }

        return null;
    }
}
