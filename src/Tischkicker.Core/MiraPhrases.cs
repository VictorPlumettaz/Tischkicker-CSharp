using Tischkicker.Core.Domain;

namespace Tischkicker.Core;

/// <summary>Kontext für einen MIRA-Spruch.</summary>
public sealed record MiraContext
{
    public required MiraMood Mood { get; init; }
    public string? TeamA { get; init; }
    public string? TeamB { get; init; }
    /// <summary>Das gerade relevante Team (Torschütze / Sieger).</summary>
    public string? Scorer { get; init; }
    public string? Other { get; init; }
    public int? ScoreA { get; init; }
    public int? ScoreB { get; init; }
    /// <summary>Verbleibende Sekunden in der laufenden Halbzeit (für zeitbezogene Kommentare).</summary>
    public int? RemainingSec { get; init; }
    /// <summary>Aktuelle Halbzeit / Gesamtzahl Halbzeiten.</summary>
    public int? Half { get; init; }
    public int? Halves { get; init; }
    /// <summary>
    /// Kompakter Turnier-/Tabellenkontext (Stand, letzte Ergebnisse, offene Spiele).
    /// Nur Stufe B (Claude) wertet ihn aus; Stufe A ignoriert ihn.
    /// </summary>
    public string? Situation { get; init; }
}

/// <summary>
/// MIRA Stufe A – Offline-Sprüche-Engine (rein &amp; testbar). Große, abwechslungs-
/// reiche Pools. Stufe B (MiraService) legt die Claude API darüber, mit Fallback.
/// </summary>
public static class MiraPhrases
{
    private static readonly Dictionary<MiraMood, string[]> Pools = new()
    {
        [MiraMood.Idle] =
        [
            "Willkommen beim A+W Tischkicker-Turnier! 🏆",
            "Schön, dass ihr da seid – gleich geht der Spaß los! ⚽",
            "MIRA ist bereit – wer wird heute Tischkicker-König? 👑",
        ],
        [MiraMood.Announce] =
        [
            "Als Nächstes: {a} gegen {b}. Das wird spannend! ⚽",
            "Gleich am Tisch: {a} vs. {b} – Daumen hoch für beide! ✊",
            "Bereitmachen, {a} und {b} – euer Spiel steht an! 🎉",
            "Jetzt heißt es {a} gegen {b}. Wer schnappt sich die Punkte?",
        ],
        [MiraMood.Kickoff] =
        [
            "Anpfiff! {a} gegen {b} – los geht's! 🔥",
            "Und die Kugel rollt: {a} vs. {b}! ⚽",
            "Es geht los! Viel Erfolg, {a} und {b}! 💪",
        ],
        [MiraMood.Lead] =
        [
            "Tooor für {scorer}! Die Führung ist da! ⚽",
            "{scorer} geht in Führung – stark gemacht! 🔥",
            "Da zappelt's im Netz – {scorer} liegt vorn!",
        ],
        [MiraMood.Equalizer] =
        [
            "Ausgleich durch {scorer}! Alles wieder offen! 😮",
            "{scorer} stellt auf {sa}:{sb} – jetzt ist es wieder spannend!",
            "Der Ausgleich! {scorer} ist zurück im Spiel! ⚖️",
        ],
        [MiraMood.Comeback] =
        [
            "Was für eine Wende! {scorer} dreht das Spiel! 🔄",
            "Comeback! {scorer} liegt jetzt vorn – unglaublich! 🚀",
            "{scorer} dreht auf und übernimmt die Führung! 🔥",
        ],
        [MiraMood.Extend] =
        [
            "Nachgelegt! {scorer} baut die Führung aus! 💥",
            "{scorer} legt nach – {sa}:{sb}! Läuft rund!",
            "Und noch eins von {scorer}! Was für eine Vorstellung! ⚽",
        ],
        [MiraMood.Halftime] =
        [
            "Halbzeit! Kurz durchatmen, dann geht's weiter. ⏸️",
            "Seitenwechsel! Die zweite Halbzeit wartet. 🔁",
            "Pause – gleich geht's mit neuer Energie weiter! 💪",
        ],
        [MiraMood.Win] =
        [
            "Abpfiff! {scorer} gewinnt – herzlichen Glückwunsch! 🏆",
            "Sieg für {scorer}! Stark gespielt! 🎉",
            "{scorer} macht den Deckel drauf – verdienter Sieg! 👏",
        ],
        [MiraMood.Draw] =
        [
            "Unentschieden! Beide Teams haben sich einen Punkt verdient. 🤝",
            "Remis – ein faires Ergebnis für {a} und {b}! ⚖️",
            "Punkteteilung! Das war ein ausgeglichenes Duell. 🤝",
        ],
        [MiraMood.FinalMinute] =
        [
            "Die Schlussphase läuft – jetzt zählt jede Sekunde! ⏱️",
            "Letzte Minute! Wer will den Sieg mehr? 🔥",
            "Gleich ist Schluss – noch ist alles drin! ⚽",
        ],
        [MiraMood.GoldenGoal] =
        [
            "Golden Goal! Das nächste Tor entscheidet – Nervenkitzel pur! ⚡",
            "Gleichstand nach der Zeit – jetzt zählt jeder Schuss! 🥅",
            "Sudden Death! Wer trifft, gewinnt! 🔥",
        ],
        [MiraMood.Interlude] =
        [
            "Spannendes Spiel hier am Tisch – bleibt dran! ⚽",
            "Weiter geht's – die Kugel rollt und rollt! 🔄",
            "MIRA schaut gebannt zu – was für ein Turnier! 🏆",
        ],
    };

    private static string Fill(string template, MiraContext ctx) => template
        .Replace("{a}", ctx.TeamA ?? "Team A")
        .Replace("{b}", ctx.TeamB ?? "Team B")
        .Replace("{scorer}", ctx.Scorer ?? "das Team")
        .Replace("{other}", ctx.Other ?? "das andere Team")
        .Replace("{sa}", (ctx.ScoreA ?? 0).ToString())
        .Replace("{sb}", (ctx.ScoreB ?? 0).ToString());

    /// <summary>Liefert einen MIRA-Spruch zur Situation. <paramref name="pick"/> wählt aus dem Pool.</summary>
    public static string Comment(MiraContext ctx, Func<int, int>? pick = null)
    {
        pick ??= Random.Shared.Next;
        var pool = Pools[ctx.Mood];
        var template = pool[pick(pool.Length)];
        return Fill(template, ctx);
    }
}
