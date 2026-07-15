namespace Tischkicker.Core.Domain;

public enum TournamentFormat
{
    Knockout,
    RoundRobin,
    Groups,
}

public enum TournamentStatus
{
    Setup,
    Running,
    Finished,
}

public enum MatchStatus
{
    Scheduled,
    Live,
    Finished,
}

/// <summary>Ergebnis eines Spiels aus Sicht eines Teams.</summary>
public enum MatchOutcome
{
    Win,
    Loss,
    Draw,
}

/// <summary>Stimmung/Anlass eines MIRA-Kommentars.</summary>
public enum MiraMood
{
    Idle,
    Announce,
    Kickoff,
    Lead,
    Equalizer,
    Comeback,
    Extend,
    Halftime,
    Win,
    Draw,
    /// <summary>Schlussphase – letzte Minute einer Halbzeit.</summary>
    FinalMinute,
    /// <summary>K.o.-Spiel: reguläre Zeit abgelaufen, aber Gleichstand (Golden Goal).</summary>
    GoldenGoal,
    /// <summary>Periodischer Zwischenkommentar ohne konkretes Ereignis (mit Turnier-/Tabellenbezug).</summary>
    Interlude,
}
