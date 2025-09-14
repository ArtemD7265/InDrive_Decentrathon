using Itinero;
using Microsoft.AspNetCore.SignalR;
using System.Globalization;
using System.Timers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddSingleton<Router>(sp =>
{
    using var stream = File.OpenRead("Data/Kazakhstan.routerdb"); // путь к твоему routerdb
    var routerDb = RouterDb.Deserialize(stream);
    return new Router(routerDb);
});
builder.Services.AddSingleton<TrafficSimulationService>();

builder.Services.AddControllersWithViews();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHub<TrafficHub>("/trafficHub");
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}"
);

// ‘оновый таймер симул€ции и рассылка в SignalR
var sim = app.Services.GetRequiredService<TrafficSimulationService>();
var hubCtx = app.Services.GetRequiredService<IHubContext<TrafficHub>>();
var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
var basePath = AppDomain.CurrentDomain.BaseDirectory;
var logFilePath = Path.Combine(basePath, "DataSet#new.csv");
var timer = new System.Timers.Timer(1000); // 1 √ц
timer.Elapsed += async (_, __) =>
{
    sim.Tick(1.0);

    var cars = sim.GetCars().Select(c => new
    {
        id = c.Id,
        lat = c.Position.Lat,
        lng = c.Position.Lng
    }).ToList();

    // Ћоги CSV
    var logs = sim.GetCars().Select(c =>
    {
        var azm = sim.EstimateAzimuth(c);
        var alt = 250 + sim.Random.NextDouble() * 200;
        return string.Join(",",
            c.Id,
            c.Position.Lat.ToString("F6", CultureInfo.InvariantCulture),
            c.Position.Lng.ToString("F6", CultureInfo.InvariantCulture),
            alt.ToString("F6", CultureInfo.InvariantCulture),
            c.SpeedMps.ToString("F6", CultureInfo.InvariantCulture),
            azm.ToString("F6", CultureInfo.InvariantCulture)
        );
    }).ToList();

    await hubCtx.Clients.All.SendAsync("UpdateCars", cars);
    if (logs.Count > 0)
        await hubCtx.Clients.All.SendAsync("LogLines", logs);

    try
    {
        File.AppendAllLines(logFilePath, logs);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ќшибка записи лога: {ex.Message}");
    }
};
timer.AutoReset = true;
timer.Start();

app.Run();
