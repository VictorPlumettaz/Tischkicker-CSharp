using Tischkicker.Core;

namespace Tischkicker.Services;

/// <summary>
/// MIRA-Kommentar-Engine. Stufe A (offline, Spruch-Pool) ist immer verfügbar.
/// Stufe B (Claude API) wird in einem späteren Schritt ergänzt und fällt bei
/// jedem Fehler automatisch auf Stufe A zurück.
/// </summary>
public class MiraService
{
    /// <summary>Stufe A – sofortiger Offline-Spruch.</summary>
    public string Comment(MiraContext ctx) => MiraPhrases.Comment(ctx);
}
