using Tischkicker.Core.Domain;

namespace Tischkicker.Core;

public readonly record struct FormatRecommendation(TournamentFormat Format, string Reason);

/// <summary>MIRAs Formatempfehlung nach Teamzahl (Bediener kann überstimmen).</summary>
public static class FormatAdvisor
{
    public static FormatRecommendation Recommend(int teamCount)
    {
        if (teamCount <= 5)
            return new(TournamentFormat.RoundRobin,
                "Bei bis zu 5 Teams spielt jeder gegen jeden – so gibt es genug Spiele und eine faire Wertung.");
        if (teamCount <= 12)
            return new(TournamentFormat.Groups,
                "Bei 6–12 Teams sind Gruppen + K.o. ausgewogen und sorgen für ein spannendes Finale.");
        return new(TournamentFormat.Knockout,
            "Bei mehr als 12 Teams hält ein K.o.-System das Turnier schnell und übersichtlich.");
    }
}
