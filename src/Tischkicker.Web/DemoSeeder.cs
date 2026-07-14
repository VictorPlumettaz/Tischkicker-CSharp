using Microsoft.EntityFrameworkCore;
using Tischkicker.Core.Domain;
using Tischkicker.Data;
using Tischkicker.Services;

namespace Tischkicker.Web;

/// <summary>
/// Befüllt die DB mit Beispieldaten für eine Generalprobe (Aufruf: <c>--seed</c>):
/// 8 Teams, ein „Gruppen + K.o."-Turnier, Gruppenphase gespielt, ein Spiel live.
/// Idempotent (löscht vorhandene Turniere/Teams zuvor).
/// </summary>
public static class DemoSeeder
{
    public static void Run(IServiceProvider sp)
    {
        var dbf = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var setup = sp.GetRequiredService<TournamentSetup>();
        var control = sp.GetRequiredService<MatchControl>();

        using (var db = dbf.CreateDbContext())
        {
            foreach (var t in db.Tournaments.Select(t => t.Id).ToList()) setup.DeleteTournament(t);
            db.Teams.RemoveRange(db.Teams);
            db.SaveChanges();
        }

        (string Name, string Players)[] names =
        [
            ("Die Tor-Konsulenten", "Anna & Ben"), ("SGWD Kickers", "Clara & David"),
            ("Bug Busters", "Eva & Finn"), ("Die Ballzauberer", "Greta & Hugo"),
            ("Team Rollfeld", "Ida & Jan"), ("Die Flipperkönige", "Klara & Leo"),
            ("Code & Tore", "Mara & Nino"), ("Die Stangenreiter", "Ole & Pia"),
        ];
        var ids = new List<int>();
        using (var db = dbf.CreateDbContext())
        {
            foreach (var (n, p) in names)
            {
                var team = new Team { Name = n, Players = p };
                db.Teams.Add(team);
                db.SaveChanges();
                ids.Add(team.Id);
            }
        }

        var tour = setup.CreateTournament("A+W Sommerfest 2026 – Tischkicker-Cup", TournamentFormat.Groups, 1, 360);
        setup.SetTeams(tour.Id, ids);
        var matches = setup.Generate(tour.Id);

        int[][] targets =
        [
            [2,1],[3,0],[1,1],[0,2],[2,0],[1,3],[4,1],[2,2],[0,1],[3,2],[1,0],[2,3],
        ];
        var group = matches.Where(m => m.GroupName != null).OrderBy(m => m.Id).ToList();
        for (var i = 0; i < group.Count - 1; i++)
        {
            control.Start(group[i].Id, MatchControl.NowMs());
            control.AdjustScore(group[i].Id, "a", targets[i][0]);
            control.AdjustScore(group[i].Id, "b", targets[i][1]);
            control.Finish(group[i].Id, MatchControl.NowMs());
        }
        var live = group[^1];
        control.Start(live.Id, MatchControl.NowMs());
        control.AdjustScore(live.Id, "a", 1);

        Console.WriteLine($"[Seed] Fertig: {ids.Count} Teams, Turnier #{tour.Id}, ein Spiel live (Gruppe {live.GroupName}).");
    }
}
