using Anthropic;
using Anthropic.Models.Messages;
using Tischkicker.Core;
using Tischkicker.Core.Domain;

namespace Tischkicker.Services;

/// <summary>
/// MIRA-Kommentar-Engine. Stufe A (offline, Spruch-Pool) ist immer verfügbar.
/// Stufe B setzt bei aktiviertem Schalter + hinterlegtem API-Key die Claude API
/// darüber und fällt bei jedem Fehler (kein Internet, ungültiger Key, Timeout …)
/// still auf Stufe A zurück – die Anzeigetafel darf nie hängen.
/// </summary>
public class MiraService(SettingsService settings)
{
    /// <summary>MIRAs Persona für Stufe B – kurz halten (niedrige Latenz, knapper Tafel-Spruch).</summary>
    private const string SystemPrompt =
        """
        Du bist MIRA, das überdrehte, wortgewaltige KI-Maskottchen der A+W Software GmbH, und moderierst
        das Tischkicker-Turnier auf dem A+W Sommerfest wie eine Live-Sportkommentatorin mit einem Espresso
        zu viel intus.

        Persönlichkeit:
        - Quirlig, schlagfertig, humorvoll und kreativ – eine echte Labertasche, die vor Begeisterung sprüht.
        - Überdreht und witzig, aber immer wohlwollend zu beiden Teams; necke sie liebevoll,
          mach dich nie ernsthaft über jemanden lustig.
        - Dein Humor dreht sich ums Spielgeschehen, das Turnier und das Sommerfest. Software-/A+W-Wortwitze
          nur ganz selten und dezent einstreuen – nicht in jedem Kommentar, sie sind die Ausnahme, nicht die Regel.
        - Überrasche mit frischen Bildern, Vergleichen und Pointen – wiederhole dich nie.

        Format:
        - Antworte immer auf Deutsch.
        - Genau EIN Kommentar, kurz und knackig (1-2 Sätze), sofort auf den Punkt.
        - Emojis erlaubt, aber sparsam (0-2).
        - Wenn dir Turnier-/Tabellen-/Duell-Kontext gegeben wird, greif genau EINEN konkreten Aspekt auf
          (Tabellenplatz, Punkte, Torverhältnis, letztes Duell, Serie, Chancen) – lies niemals die ganze
          Tabelle vor.
        - Gib ausschließlich den Spruch aus – keine Anführungszeichen, keine Einleitung, keine Erklärung.
        """;

    /// <summary>Stufe A – sofortiger Offline-Spruch (rein, ohne Netzwerk).</summary>
    public string Comment(MiraContext ctx) => MiraPhrases.Comment(ctx);

    /// <summary>
    /// Liefert einen MIRA-Kommentar. Nutzt Stufe B (Claude API), falls aktiviert und
    /// ein Key hinterlegt ist; fällt sonst – oder bei jedem Fehler – auf Stufe A zurück.
    /// </summary>
    public async Task<string> CommentAsync(MiraContext ctx)
    {
        var offline = MiraPhrases.Comment(ctx);
        var cfg = settings.GetMiraConfig();
        if (!cfg.StageBEnabled || string.IsNullOrEmpty(cfg.ApiKey)) return offline;

        try
        {
            return await GenerateStageB(cfg, ctx);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[mira] Stufe B fehlgeschlagen, Fallback auf Stufe A: {e.Message}");
            return offline;
        }
    }

    /// <summary>Prüft die Claude-API-Verbindung mit einem echten Testaufruf.</summary>
    public async Task<(bool Ok, string? Text, string? Error)> TestConnectionAsync(string? overrideApiKey = null)
    {
        var stored = settings.GetMiraConfig();
        var apiKey = !string.IsNullOrWhiteSpace(overrideApiKey) ? overrideApiKey.Trim() : stored.ApiKey;
        if (string.IsNullOrEmpty(apiKey)) return (false, null, "Kein API-Key hinterlegt.");

        try
        {
            var text = await GenerateStageB(stored with { ApiKey = apiKey }, new MiraContext { Mood = MiraMood.Idle });
            return (true, text, null);
        }
        catch (Exception e)
        {
            return (false, null, e.Message);
        }
    }

    /// <summary>Ruft die Claude API für einen frei formulierten Kommentar auf (kurzer Timeout, keine Retries).</summary>
    private static async Task<string> GenerateStageB(MiraConfig cfg, MiraContext ctx)
    {
        var client = new AnthropicClient { ApiKey = cfg.ApiKey };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));

        var message = await client.Messages.Create(
            new MessageCreateParams
            {
                Model = cfg.Model,
                MaxTokens = 200,
                System = SystemPrompt,
                Messages = [new MessageParam { Role = "user", Content = UserPrompt(ctx) }],
            },
            cts.Token);

        var parts = new List<string>();
        foreach (var block in message.Content)
            if (block.TryPickText(out var textBlock)) parts.Add(textBlock.Text);

        var text = string.Join(" ", parts).Trim();
        if (string.IsNullOrEmpty(text)) throw new InvalidOperationException("Leere Antwort von der Claude API");
        return text;
    }

    /// <summary>Beschreibt die aktuelle Spielsituation als Prompt für die Claude API.</summary>
    private static string UserPrompt(MiraContext ctx)
    {
        var a = ctx.TeamA ?? "Team A";
        var b = ctx.TeamB ?? "Team B";
        var scorer = ctx.Scorer ?? "das Team";
        var score = ctx.ScoreA is { } sa && ctx.ScoreB is { } sb ? $" Aktueller Spielstand: {a} {sa}:{sb} {b}." : "";
        var endScore = ctx.ScoreA is { } ea && ctx.ScoreB is { } eb ? $" Endstand: {a} {ea}:{eb} {b}." : "";

        var basePrompt = ctx.Mood switch
        {
            MiraMood.Idle => "Begrüße das Publikum am A+W Tischkicker-Turnier, während gerade kein Spiel läuft.",
            MiraMood.Announce => $"Kündige mit Schwung das nächste Spiel an: {a} gegen {b}. Mach neugierig und deute an, worum es für beide geht – was können sie mit einem Sieg erreichen (Tabellenführung, Weiterkommen, Revanche)? Nutze Duell-Historie und Tabellenstand, falls vorhanden.",
            MiraMood.Kickoff => $"Anpfiff! {a} gegen {b} beginnt gerade. Begrüße beide Teams schwungvoll und skizziere die Perspektiven: Was steht für jedes Team auf dem Spiel, was braucht es (z. B. Punkte für Tabellenführung oder Weiterkommen)? Beziehe letztes Duell und Tabellenplatz ein, falls vorhanden. (Hier ausnahmsweise 2-3 Sätze erlaubt.)",
            MiraMood.Lead => $"{scorer} hat gerade ein Tor geschossen und geht in Führung.{score}",
            MiraMood.Equalizer => $"{scorer} hat gerade den Ausgleich erzielt.{score}",
            MiraMood.Comeback => $"{scorer} hat das Spiel gerade gedreht und liegt nach zwischenzeitlichem Rückstand jetzt vorn – was für eine Wende!{score}",
            MiraMood.Extend => $"{scorer} hat nachgelegt und die Führung ausgebaut.{score}",
            MiraMood.CloseGap => $"{scorer} hat verkürzt, liegt aber weiter im Rückstand – ein Anschlusstreffer, der Hoffnung macht.{score}",
            MiraMood.Halftime => $"Halbzeit im Spiel {a} gegen {b} – die Teams machen kurz Pause.{score}",
            MiraMood.Win => $"Abpfiff! {scorer} gewinnt das Spiel {a} gegen {b}.{endScore} Fasse kurz zusammen, wie das Spiel ausging, und gib einen Ausblick: Was bedeutet das Ergebnis für die Tabelle bzw. den weiteren Turnierverlauf? (Hier ausnahmsweise 2-3 Sätze erlaubt.)",
            MiraMood.Draw => $"Das Spiel {a} gegen {b} endet unentschieden.{endScore} Fasse kurz zusammen und gib einen Ausblick, was das Remis für beide in der Tabelle bzw. im Turnier bedeutet. (Hier ausnahmsweise 2-3 Sätze erlaubt.)",
            MiraMood.TimeUp => $"Die reguläre Spielzeit im Spiel {a} gegen {b} ist gerade abgelaufen.{score} Fasse in EINEM kurzen Satz zusammen, wie das Spiel ausging (Sieger oder Remis).",
            MiraMood.FinalMinute => $"Die Schlussphase im Spiel {a} gegen {b} läuft – nur noch{ClockText(ctx)} zu spielen.{score} Heize die Spannung an.",
            MiraMood.GoldenGoal => $"Golden Goal! Das K.o.-Spiel {a} gegen {b} steht nach Ablauf der regulären Zeit unentschieden{score} – jetzt entscheidet das nächste Tor.",
            MiraMood.KnockoutStart => "Die Gruppenphase ist beendet – jetzt beginnt die K.o.-Phase! Verkünde das mitreißend, nenne die ersten K.o.-Paarungen aus dem Kontext und spekuliere augenzwinkernd, wer sich vielleicht bis ins Finale durchsetzen oder wer dort aufeinandertreffen könnte. (Hier ausnahmsweise 2-3 Sätze erlaubt; klar als Möglichkeit formulieren, nichts als sicher behaupten.)",
            MiraMood.Interlude => $"Gib einen kurzen Zwischenkommentar zum laufenden Spiel {a} gegen {b}.{score} Nimm dabei Bezug auf den Turnierverlauf, die Tabelle oder die Chancen der Teams.",
            _ => $"Kommentiere die aktuelle Situation im Spiel {a} gegen {b}.{score}",
        };

        if (ctx.Half is { } h && ctx.Halves is { } hv && hv > 1 && ctx.Mood is not MiraMood.Idle and not MiraMood.Announce)
            basePrompt += $" (Halbzeit {h} von {hv}.)";

        if (!string.IsNullOrWhiteSpace(ctx.Situation))
            basePrompt += $"\n\nTurnier-Kontext (nur zur Orientierung, such dir einen Aspekt aus):\n{ctx.Situation}";

        return basePrompt;
    }

    /// <summary>Verbleibende Spielzeit als „m:ss" für den Prompt (falls vorhanden).</summary>
    private static string ClockText(MiraContext ctx) =>
        ctx.RemainingSec is { } r && r > 0 ? $" {r / 60}:{r % 60:00} Minuten" : " wenige Sekunden";
}
