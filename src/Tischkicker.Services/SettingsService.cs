using Microsoft.EntityFrameworkCore;
using Tischkicker.Core.Domain;
using Tischkicker.Data;

namespace Tischkicker.Services;

/// <summary>Vollständige MIRA-Stufe-B-Konfiguration inkl. API-Key (nur serverintern!).</summary>
public sealed record MiraConfig
{
    public bool StageBEnabled { get; init; }
    public string ApiKey { get; init; } = "";
    public string Model { get; init; } = SettingsService.DefaultModel;
    /// <summary>Abstand periodischer Zwischenkommentare der Live-Tafel in Sekunden.</summary>
    public int InterludeSec { get; init; } = SettingsService.DefaultInterludeSec;
}

/// <summary>Nach außen sichtbare Konfiguration: der API-Key wird nie geliefert, nur <see cref="HasApiKey"/>.</summary>
public sealed record MiraConfigPublic(bool StageBEnabled, string Model, bool HasApiKey, int InterludeSec);

/// <summary>
/// Schlüssel/Wert-Zugriff auf die <c>Settings</c>-Tabelle mit getippten Helfern für
/// die MIRA-Konfiguration. Der API-Key verlässt die Anwendung nie (nur <c>HasApiKey</c>).
/// </summary>
public class SettingsService(IDbContextFactory<AppDbContext> dbf)
{
    /// <summary>Auswählbare Claude-Modelle für MIRA Stufe B.</summary>
    public static readonly string[] MiraModels = ["claude-haiku-4-5", "claude-sonnet-5", "claude-opus-4-8"];

    /// <summary>Standard: schnelles, günstiges Modell – passend für kurze Live-Sprüche.</summary>
    public const string DefaultModel = "claude-haiku-4-5";

    /// <summary>Standard-Abstand periodischer Zwischenkommentare (Sekunden).</summary>
    public const int DefaultInterludeSec = 30;
    /// <summary>Erlaubte Unter-/Obergrenze für den Zwischenkommentar-Abstand (Sekunden).</summary>
    public const int MinInterludeSec = 10;
    public const int MaxInterludeSec = 600;

    private const string KeyStageB = "mira.stageBEnabled";
    private const string KeyApiKey = "mira.apiKey";
    private const string KeyModel = "mira.model";
    private const string KeyInterlude = "mira.interludeSec";

    /// <summary>Vollständige Config inkl. API-Key – nur serverintern verwenden.</summary>
    public MiraConfig GetMiraConfig()
    {
        using var db = dbf.CreateDbContext();
        var map = db.Settings.ToDictionary(s => s.Key, s => s.Value);
        var model = map.GetValueOrDefault(KeyModel);
        return new MiraConfig
        {
            StageBEnabled = map.GetValueOrDefault(KeyStageB) == "true",
            ApiKey = map.GetValueOrDefault(KeyApiKey) ?? "",
            Model = model is not null && MiraModels.Contains(model) ? model : DefaultModel,
            InterludeSec = ClampInterlude(
                int.TryParse(map.GetValueOrDefault(KeyInterlude), out var iv) ? iv : DefaultInterludeSec),
        };
    }

    /// <summary>Begrenzt den Zwischenkommentar-Abstand auf den erlaubten Bereich.</summary>
    private static int ClampInterlude(int sec) => Math.Clamp(sec, MinInterludeSec, MaxInterludeSec);

    public MiraConfigPublic GetMiraConfigPublic()
    {
        var c = GetMiraConfig();
        return new MiraConfigPublic(c.StageBEnabled, c.Model, c.ApiKey.Length > 0, c.InterludeSec);
    }

    /// <summary>
    /// Aktualisiert die Konfiguration. <paramref name="apiKey"/> wird nur gesetzt, wenn
    /// nicht-leer; <paramref name="clearApiKey"/> entfernt den gespeicherten Key.
    /// </summary>
    public MiraConfigPublic SetMiraConfig(
        bool? stageBEnabled = null, string? model = null, string? apiKey = null, bool clearApiKey = false,
        int? interludeSec = null)
    {
        using var db = dbf.CreateDbContext();

        void Set(string key, string value)
        {
            var e = db.Settings.Find(key);
            if (e is null) db.Settings.Add(new Setting { Key = key, Value = value, UpdatedAt = DateTime.UtcNow });
            else { e.Value = value; e.UpdatedAt = DateTime.UtcNow; }
        }

        if (stageBEnabled is { } sb) Set(KeyStageB, sb ? "true" : "false");
        if (interludeSec is { } iv)
        {
            if (iv < MinInterludeSec || iv > MaxInterludeSec)
                throw new ArgumentException($"Zwischenkommentar-Abstand muss zwischen {MinInterludeSec} und {MaxInterludeSec} Sekunden liegen.");
            Set(KeyInterlude, iv.ToString());
        }
        if (model is not null)
        {
            if (!MiraModels.Contains(model))
                throw new ArgumentException($"Ungültiges Modell. Erlaubt: {string.Join(", ", MiraModels)}");
            Set(KeyModel, model);
        }
        if (clearApiKey)
        {
            var e = db.Settings.Find(KeyApiKey);
            if (e is not null) db.Settings.Remove(e);
        }
        else if (!string.IsNullOrWhiteSpace(apiKey))
        {
            Set(KeyApiKey, apiKey.Trim());
        }

        db.SaveChanges();
        return GetMiraConfigPublic();
    }
}
