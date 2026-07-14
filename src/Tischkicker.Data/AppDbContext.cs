using Microsoft.EntityFrameworkCore;
using Tischkicker.Core.Domain;

namespace Tischkicker.Data;

/// <summary>
/// EF-Core-Kontext (SQLite). Ersetzt das handgeschriebene Migrations-/Repo-System
/// der TypeScript-Version; Schema-Änderungen laufen über EF-Core-Migrationen.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Tournament> Tournaments => Set<Tournament>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<TournamentTeam> TournamentTeams => Set<TournamentTeam>();
    public DbSet<Setting> Settings => Set<Setting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Team>();

        b.Entity<Tournament>(e =>
        {
            e.Property(x => x.Format).HasConversion<string>();
            e.Property(x => x.Status).HasConversion<string>();
        });

        b.Entity<Match>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>();
            e.HasIndex(x => x.TournamentId);
            e.HasIndex(x => x.Status);
        });

        b.Entity<TournamentTeam>(e => e.HasKey(x => new { x.TournamentId, x.TeamId }));

        b.Entity<Setting>(e => e.HasKey(x => x.Key));
    }
}
