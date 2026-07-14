using Tischkicker.Core.Domain;

namespace Tischkicker.Core;

/// <summary>Tabelle einer Gruppe (bei Liga: <c>GroupName == null</c>).</summary>
public sealed record StandingsGroup(string? GroupName, List<StandingsRow> Rows);

/// <summary>Alle Tabellen eines Turniers.</summary>
public sealed record StandingsResponse(TournamentFormat Format, List<StandingsGroup> Groups);
