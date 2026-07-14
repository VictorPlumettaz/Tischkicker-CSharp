namespace Tischkicker.Core;

/// <summary>MIRAs ELO-basierter Tipp für eine Partie.</summary>
public readonly record struct MiraTip(string? Favorite, int ProbabilityA, string Text);

public static class MiraTipEngine
{
    /// <summary>Bei nahezu gleichen Werten (±3 %) bleibt MIRA neutral.</summary>
    public static MiraTip Compute(int eloA, int eloB, string nameA, string nameB)
    {
        var probabilityA = (int)Math.Round(Elo.ExpectedScore(eloA, eloB) * 100, MidpointRounding.AwayFromZero);

        if (Math.Abs(probabilityA - 50) <= 3)
            return new(null, probabilityA,
                $"Kopf-an-Kopf! MIRA sieht {nameA} und {nameB} völlig auf Augenhöhe.");

        var favorite = probabilityA > 50 ? "a" : "b";
        var favoriteName = favorite == "a" ? nameA : nameB;
        var favoritePct = Math.Max(probabilityA, 100 - probabilityA);
        return new(favorite, probabilityA, $"MIRA tippt: {favoriteName} mit {favoritePct} %.");
    }
}
