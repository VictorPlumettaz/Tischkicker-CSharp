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
        Du bist MIRA, das freundliche KI-Maskottchen des A+W Sommerfests, und moderierst das Tischkicker-Turnier.
        Regeln:
        - Antworte immer auf Deutsch.
        - Genau EIN kurzer, knackiger Live-Kommentar (maximal 1-2 Sätze).
        - Ton: begeistert, sportlich, humorvoll und wohlwollend zu beiden Teams.
        - Passende Emojis sind erlaubt (sparsam).
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
        var score = ctx.ScoreA is { } sa && ctx.ScoreB is { } sb ? $" Aktueller Spielstand: {sa}:{sb}." : "";

        return ctx.Mood switch
        {
            MiraMood.Idle => "Begrüße das Publikum am A+W Tischkicker-Turnier, während gerade kein Spiel läuft.",
            MiraMood.Announce => $"Kündige das nächste Spiel an: {a} gegen {b}.",
            MiraMood.Kickoff => $"Anpfiff! Das Spiel {a} gegen {b} beginnt gerade.",
            MiraMood.Lead => $"{scorer} hat gerade ein Tor geschossen und geht in Führung.{score}",
            MiraMood.Equalizer => $"{scorer} hat gerade den Ausgleich erzielt.{score}",
            MiraMood.Comeback => $"{scorer} hat gerade das Spiel gedreht und liegt nun vorn.{score}",
            MiraMood.Extend => $"{scorer} hat nachgelegt und die Führung ausgebaut.{score}",
            MiraMood.Halftime => "Halbzeit – die Teams wechseln die Seiten und machen kurz Pause.",
            MiraMood.Win => $"Abpfiff! {scorer} gewinnt das Spiel {a} gegen {b}.{score}",
            MiraMood.Draw => $"Das Spiel {a} gegen {b} endet unentschieden.{score}",
            _ => $"Kommentiere die aktuelle Situation im Spiel {a} gegen {b}.",
        };
    }
}
