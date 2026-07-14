using Tischkicker.Core.Domain;

namespace Tischkicker.Core;

/// <summary>
/// Standard-ELO-Berechnung (rein, seiteneffektfrei). Rundung bewusst wie in
/// JavaScript (<c>Math.round</c> = <c>floor(x + 0.5)</c>), damit die Werte 1:1
/// zur TypeScript-Version passen.
/// </summary>
public static class Elo
{
    public const int DefaultElo = 1000;
    public const int DefaultK = 32;

    /// <summary>Erwartungswert für Team A gegen Team B (0..1).</summary>
    public static double ExpectedScore(double ratingA, double ratingB) =>
        1.0 / (1.0 + Math.Pow(10, (ratingB - ratingA) / 400.0));

    /// <summary>Sieg 1, Remis 0.5, Niederlage 0.</summary>
    public static double ScoreValue(MatchOutcome outcome) => outcome switch
    {
        MatchOutcome.Win => 1.0,
        MatchOutcome.Draw => 0.5,
        _ => 0.0,
    };

    private static int JsRound(double x) => (int)Math.Floor(x + 0.5);

    public static int UpdateElo(double rating, double opponentRating, MatchOutcome outcome, int k = DefaultK)
    {
        var expected = ExpectedScore(rating, opponentRating);
        var actual = ScoreValue(outcome);
        return JsRound(rating + k * (actual - expected));
    }

    /// <summary>Beide neuen ELO-Werte für ein beendetes Spiel (outcome aus Sicht A).</summary>
    public static (int RatingA, int RatingB) ApplyMatch(
        double ratingA, double ratingB, MatchOutcome outcomeA, int k = DefaultK)
    {
        var outcomeB = outcomeA switch
        {
            MatchOutcome.Win => MatchOutcome.Loss,
            MatchOutcome.Loss => MatchOutcome.Win,
            _ => MatchOutcome.Draw,
        };
        return (UpdateElo(ratingA, ratingB, outcomeA, k), UpdateElo(ratingB, ratingA, outcomeB, k));
    }
}
