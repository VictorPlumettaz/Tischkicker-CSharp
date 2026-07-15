using Tischkicker.Core.Domain;

namespace Tischkicker.Core;

/// <summary>
/// Leitet aus dem Übergang des aktuellen Spiels (vorher → jetzt) die passende
/// MIRA-Stimmung ab. Reine Portierung von client/src/mira.ts. Gibt <c>null</c>
/// zurück, wenn nichts Kommentarwürdiges passiert ist.
/// </summary>
public static class MiraNarrator
{
    /// <param name="scorerAWasBehind">Ob Team A im bisherigen Spielverlauf schon einmal in Rückstand lag.</param>
    /// <param name="scorerBWasBehind">Ob Team B im bisherigen Spielverlauf schon einmal in Rückstand lag.</param>
    public static MiraContext? Derive(Match? prev, Match? current, Match? next, Func<int?, string> teamName,
        bool scorerAWasBehind = false, bool scorerBWasBehind = false)
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
                return new MiraContext { Mood = MiraMood.Draw, TeamA = a, TeamB = b, ScoreA = current.ScoreA, ScoreB = current.ScoreB };
            var winner = current.ScoreA > current.ScoreB ? a : b;
            return new MiraContext { Mood = MiraMood.Win, Scorer = winner, TeamA = a, TeamB = b, ScoreA = current.ScoreA, ScoreB = current.ScoreB };
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
            var scorerWasBehind = scoredA ? scorerAWasBehind : scorerBWasBehind;
            var leadBefore = scoredA ? prev.ScoreA - prev.ScoreB : prev.ScoreB - prev.ScoreA;
            var leadAfter = scoredA ? current.ScoreA - current.ScoreB : current.ScoreB - current.ScoreA;

            // Reihenfolge wichtig: erst der Endstand aus Sicht des Schützen (Ausgleich /
            // weiter im Rückstand), dann die Führungswechsel.
            MiraMood mood;
            if (leadAfter == 0) mood = MiraMood.Equalizer;      // exakter Ausgleich
            else if (leadAfter < 0) mood = MiraMood.CloseGap;    // verkürzt, aber noch hinten
            else if (leadBefore < 0) mood = MiraMood.Comeback;   // war hinten, jetzt vorn (Doppelschlag)
            // Aus dem Gleichstand in Führung: „gedreht", falls der Schütze im Spiel schon zurücklag,
            // sonst die (erste) Führung.
            else if (leadBefore == 0) mood = scorerWasBehind ? MiraMood.Comeback : MiraMood.Lead;
            else mood = MiraMood.Extend;                         // Führung ausgebaut

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
