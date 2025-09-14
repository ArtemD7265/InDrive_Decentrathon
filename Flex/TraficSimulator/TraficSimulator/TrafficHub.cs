using Microsoft.AspNetCore.SignalR;

public class TrafficHub : Hub
{
    private readonly TrafficSimulationService _sim;

    public TrafficHub(TrafficSimulationService sim)
    {
        _sim = sim;
    }

    public override async Task OnConnectedAsync()
    {
        // Отправим текущее состояние
        await Clients.Caller.SendAsync("ObstaclesReset");
        foreach (var o in _sim.GetObstacles())
            await Clients.Caller.SendAsync("ObstacleAdded", o);

        var info = _sim.GetStatus();
        await Clients.Caller.SendAsync("Status", info);
        await base.OnConnectedAsync();
    }

    public async Task SetCity(string cityKey)
    {
        _sim.SetCity(cityKey);
        await Clients.All.SendAsync("Status", _sim.GetStatus());
    }

    public async Task SetRegion(double south, double west, double north, double east)
    {
        _sim.SetRegion(south, west, north, east);
        await Clients.All.SendAsync("Status", _sim.GetStatus());
    }

    public async Task ApplySettings(int rectSizeKm, int maxCars, int spawnEverySec, bool showRoute)
    {
        _sim.ApplySettings(rectSizeKm, maxCars, spawnEverySec, showRoute);
        await Clients.All.SendAsync("Status", _sim.GetStatus());
    }

    public async Task AddObstacle(double lat, double lng, string type)
    {
        var obs = _sim.AddObstacle(lat, lng, type);
        await Clients.All.SendAsync("ObstacleAdded", obs);
    }

    public async Task RemoveObstacle(Guid id)
    {
        _sim.RemoveObstacle(id);
        await Clients.All.SendAsync("ObstacleRemoved", id);
    }

    public async Task RequestRouteForCar(string carId)
    {
        var path = _sim.GetPathForCar(carId);
        if (path != null && path.Count > 1)
        {
            await Clients.Caller.SendAsync("RouteResult", new { carId, coordinates = path });
        }
    }
}
