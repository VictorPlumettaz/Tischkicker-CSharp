namespace Tischkicker.Core.Domain;

/// <summary>Ein Team (Startwert ELO 1000). Career-Statistik über alle Turniere.</summary>
public class Team
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Players { get; set; }
    public int Elo { get; set; } = 1000;
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Draws { get; set; }
    public int GoalsFor { get; set; }
    public int GoalsAgainst { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class Tournament
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public TournamentFormat Format { get; set; }
    public TournamentStatus Status { get; set; } = TournamentStatus.Setup;
    /// <summary>Dauer pro Halbzeit in Sekunden (Standard 360 = 6 Min).</summary>
    public int HalfDurationSec { get; set; } = 360;
    /// <summary>Anzahl Halbzeiten (1 oder 2).</summary>
    public int Halves { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Match
{
    public int Id { get; set; }
    public int TournamentId { get; set; }
    public int? TeamAId { get; set; }
    public int? TeamBId { get; set; }
    public int ScoreA { get; set; }
    public int ScoreB { get; set; }
    public MatchStatus Status { get; set; } = MatchStatus.Scheduled;
    public int CurrentHalf { get; set; } = 1;
    public int? Round { get; set; }
    public string? GroupName { get; set; }
    public string? BracketSlot { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Spieluhr
    public double ElapsedSec { get; set; }
    public bool TimerRunning { get; set; }
    /// <summary>Epoch-ms, seit wann der laufende Abschnitt läuft (null wenn pausiert).</summary>
    public long? TimerStartedAtMs { get; set; }

    // K.o.-Advancement: Sieger rückt in NextMatchId / NextSlot ("a"|"b").
    public int? NextMatchId { get; set; }
    public string? NextSlot { get; set; }
}

/// <summary>Ein Team im Turnier-Kader (mit optionaler Gruppe/Setzung).</summary>
public class TournamentTeam
{
    public int TournamentId { get; set; }
    public int TeamId { get; set; }
    public int? Seed { get; set; }
    public string? GroupName { get; set; }
}

/// <summary>Schlüssel/Wert-Einstellung (u. a. MIRA Stufe B).</summary>
public class Setting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
