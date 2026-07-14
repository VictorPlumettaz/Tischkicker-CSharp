using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Tischkicker.Data;

/// <summary>Nur für `dotnet ef` (Migrations-Erstellung) – nutzt eine Dummy-SQLite-Datei.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=tischkicker-design.db")
            .Options;
        return new AppDbContext(options);
    }
}
