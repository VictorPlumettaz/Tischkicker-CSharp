using System.Net;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Tischkicker.Data;
using Tischkicker.Services;
using Tischkicker.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Im LAN erreichbar (Anzeigetafel auf separatem Gerät), fester HTTP-Port.
const int Port = 5088;
builder.WebHost.UseUrls($"http://0.0.0.0:{Port}");

// Lokale SQLite-DB im Benutzerprofil (schreibbar, auch als installierte .exe).
var dataDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tischkicker");
Directory.CreateDirectory(dataDir);
var dbPath = Path.Combine(dataDir, "tischkicker.db");

builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddSingleton<LiveNotifier>();
builder.Services.AddScoped<MatchControl>();
builder.Services.AddScoped<TournamentSetup>();
builder.Services.AddScoped<MiraService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Schema anlegen/aktualisieren.
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.Migrate();
}

// Beispieldaten befüllen (Generalprobe) und beenden.
if (args.Contains("--seed"))
{
    using var scope = app.Services.CreateScope();
    Tischkicker.Web.DemoSeeder.Run(scope.ServiceProvider);
    return;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// LAN-URLs ausgeben (Anzeigetafel).
Console.WriteLine($"[Tischkicker] Bediener-Oberflaeche: http://localhost:{Port}");
foreach (var ip in Dns.GetHostAddresses(Dns.GetHostName()))
    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
        Console.WriteLine($"[Tischkicker] Anzeigetafel (TV/Tablet):  http://{ip}:{Port}/live");

app.Run();
