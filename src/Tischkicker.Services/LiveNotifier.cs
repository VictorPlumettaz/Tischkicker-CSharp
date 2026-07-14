namespace Tischkicker.Services;

/// <summary>
/// Einfacher Pub/Sub-Kanal für Echtzeit-Aktualisierungen (ersetzt die SSE der
/// TS-Version). Steuer-Aktionen rufen <see cref="NotifyChanged"/>; die Live-Tafel
/// abonniert <see cref="Changed"/> und rendert neu. Als Singleton registriert,
/// damit alle Blazor-Circuits (Bediener + TV) benachrichtigt werden.
/// </summary>
public sealed class LiveNotifier
{
    public event Action? Changed;

    public void NotifyChanged() => Changed?.Invoke();
}
