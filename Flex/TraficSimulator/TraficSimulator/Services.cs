using Itinero;
using Itinero.Osm.Vehicles;
using System.Collections.Concurrent;

public class TrafficSimulationService
{
    private readonly Router _router;
    public readonly Random Random = new();

    // Настройки
    private string _cityKey = "kostanay";
    private readonly Dictionary<string, (double lat, double lng)> _cities = new()
    {
        { "kostanay", (53.219, 63.635) },
        { "astana",   (51.1694, 71.4491) },
        { "almaty",   (43.238, 76.945) },
        { "moscow",   (55.7558, 37.6173) },
        { "berlin",   (52.52,   13.405) },
    };

    private int _rectSizeKm = 3;
    private int _maxCars = 100;
    private int _spawnEverySec = 1;
    private bool _showRoute = true;

    // Регион (границы)
    private (double south, double west, double north, double east) _bounds;

    // Состояние симуляции
    private int _spawnCounter = 0;
    private readonly ConcurrentDictionary<string, Car> _cars = new();
    private readonly ConcurrentDictionary<Guid, Obstacle> _obstacles = new();

    public TrafficSimulationService(Router router)
    {
        _router = router;
        var c = _cities[_cityKey];
        SetRegionByCenter(c.lat, c.lng, _rectSizeKm);
    }

    public void ApplySettings(int rectSizeKm, int maxCars, int spawnEverySec, bool showRoute)
    {
        _rectSizeKm = Math.Clamp(rectSizeKm, 1, 50);
        _maxCars = Math.Clamp(maxCars, 1, 300);
        _spawnEverySec = Math.Clamp(spawnEverySec, 1, 20);
        _showRoute = showRoute;
        // Регион не меняем, только размер, центр тот же
        var center = GetRegionCenter();
        SetRegionByCenter(center.lat, center.lng, _rectSizeKm);
    }

    public void SetCity(string key)
    {
        if (!_cities.ContainsKey(key)) return;
        _cityKey = key;
        var c = _cities[key];
        SetRegionByCenter(c.lat, c.lng, _rectSizeKm);
        // Очистить машины при смене города
        _cars.Clear();
    }

    public void SetRegion(double south, double west, double north, double east)
    {
        _bounds = (south, west, north, east);
    }

    private (double lat, double lng) GetRegionCenter()
    {
        var (s, w, n, e) = _bounds;
        return ((s + n) / 2.0, (w + e) / 2.0);
    }

    private void SetRegionByCenter(double lat, double lng, int sizeKm)
    {
        double dLat = sizeKm / 110.57 / 2.0;
        double dLng = sizeKm / (111.32 * Math.Cos(lat * Math.PI / 180.0)) / 2.0;
        _bounds = (lat - dLat, lng - dLng, lat + dLat, lng + dLng);
    }

    public object GetStatus() => new
    {
        running = true,
        city = _cities[_cityKey],
        cityKey = _cityKey,
        rectSizeKm = _rectSizeKm,
        maxCars = _maxCars,
        spawnEverySec = _spawnEverySec,
        cars = _cars.Count
    };

    public List<Obstacle> GetObstacles() => _obstacles.Values.ToList();

    public Obstacle AddObstacle(double lat, double lng, string type)
    {
        var o = new Obstacle(lat, lng, type);
        _obstacles[o.Id] = o;
        return o;
    }

    public void RemoveObstacle(Guid id)
    {
        _obstacles.TryRemove(id, out _);
    }

    public List<Car> GetCars() => _cars.Values.ToList();

    public List<Coordinate>? GetPathForCar(string carId)
    {
        if (_cars.TryGetValue(carId, out var car)) return car.Path;
        return null;
    }

    // Основной шаг симуляции
    public void Tick(double dt)
    {
        // Спавним новых машин через _spawnEverySec
        _spawnCounter++;
        if (_spawnCounter % _spawnEverySec == 0 && _cars.Count < _maxCars)
        {
            _ = SpawnOneCar();
        }

        // Двигаем машины
        foreach (var car in _cars.Values.ToList())
        {
            StepCar(car, dt);
        }
    }

    private async Task SpawnOneCar()
    {
        var result = await TryRouteWithRetries(20);
        if (result == null) return;

        var (route, start, end) = result.Value;

        var car = new Car
        {
            Id = RandId(),
            Position = route[0],
            Destination = end,
            Path = route,
            PathIndex = 0,
            SpeedMps = 5 + Random.NextDouble() * 13
        };
        _cars[car.Id] = car;
    }

    // Перестраиваем маршрут при блокировке
    private async Task<bool> RerouteIfBlocked(Car car)
    {
        if (!RouteBlocked(car.Path.Skip(Math.Max(0, car.PathIndex - 1)).ToList()))
            return false;

        Console.WriteLine($"[OBSTACLE DETECTED] Car {car.Id} route blocked, rerouting...");

        //var newRoute = await RouteWithAvoidance(car.Position, car.Destination);
        //if (newRoute != null && newRoute.Count >= 2 && !RouteBlocked(newRoute))
        //{
        //    car.Path = newRoute;
        //    car.PathIndex = 0;
        //    return true;
        //}

        // Если объезд не найден — можно удалить машину или назначить новый заказ
        Console.WriteLine($"[REROUTE FAILED] Car {car.Id} could not find alternative route.");
        _cars.TryRemove(car.Id, out _); // убираем машину из симуляции
        return false;
    }


    private void StepCar(Car car, double dt)
    {
        if (car.PathIndex >= car.Path.Count - 1)
        {
            // Завершил заказ — удалить
            _cars.TryRemove(car.Id, out _);
            return;
        }

        var cur = car.Position;
        var nxt = car.Path[car.PathIndex + 1];
        var dist = Haversine(cur, nxt);
        var move = car.SpeedMps * dt;

        if (move >= dist)
        {
            car.Position = nxt;
            car.PathIndex++;
        }
        else
        {
            var t = move / dist;
            car.Position = new Coordinate(
                cur.Lat + (nxt.Lat - cur.Lat) * t,
                cur.Lng + (nxt.Lng - cur.Lng) * t
            );
        }

        // Проверка препятствий раз в шаг
        _ = RerouteIfBlocked(car);
    }

    // Построение маршрута через Itinero
    public RouteResult BuildRoute(Coordinate start, Coordinate end)
    {
        var profile = Vehicle.Car.Fastest();
        var s = _router.Resolve(profile, (float)start.Lat, (float)start.Lng);
        var e = _router.Resolve(profile, (float)end.Lat, (float)end.Lng);
        var route = _router.Calculate(profile, s, e);
        return new RouteResult
        {
            Coordinates = route.Shape.Select(c => new Coordinate(c.Latitude, c.Longitude)).ToList(),
            Distance = route.TotalDistance,
            Time = route.TotalTime
        };
    }



    private async Task<List<Coordinate>?> RouteWithAvoidance(Coordinate start, Coordinate end, int maxIterations = 5)
    {
        try
        {
            var currentStart = start;
            var currentEnd = end;

            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                // Строим маршрут
                var route = BuildRoute(currentStart, currentEnd).Coordinates;
                if (!RouteBlocked(route))
                    return route; // свободный путь найден

                // Находим все препятствия на маршруте
                var hits = AllHits(route);
                if (hits.Count == 0)
                    return route; // препятствий нет, но RouteBlocked вернул true — странно, но едем

                bool rerouted = false;

                foreach (var hit in hits)
                {
                    // Генерируем точки объезда вокруг препятствия
                    var detours = MakeDetoursAround(hit.center, hit.radius + 60, points: 8);
                    foreach (var d in detours)
                    {
                        try
                        {
                            // Строим маршрут через via-точку
                            var a = BuildRoute(currentStart, d).Coordinates;
                            var b = BuildRoute(d, currentEnd).Coordinates;
                            var merged = a.Concat(b.Skip(1)).ToList();

                            if (!RouteBlocked(merged))
                            {
                                // Нашли объезд — продолжаем с новым маршрутом
                                currentStart = merged.First();
                                currentEnd = merged.Last();
                                route = merged;
                                rerouted = true;
                                break;
                            }
                        }
                        catch
                        {
                            // Игнорируем ошибки построения
                        }
                    }
                    if (rerouted) break;
                }

                if (!rerouted)
                {
                    // Не удалось найти объезд ни для одного препятствия
                    return route;
                }
            }

            return null; // Не нашли свободный маршрут за maxIterations
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ROUTE ERROR] {ex.Message}");
            return null;
        }
    }
    private List<(Coordinate center, double radius)> AllHits(List<Coordinate> path)
    {
        var result = new List<(Coordinate, double)>();
        for (int i = 0; i < path.Count - 1; i++)
        {
            foreach (var o in _obstacles.Values)
            {
                if (o.Intersects(path[i], path[i + 1]))
                    result.Add((new Coordinate(o.Lat, o.Lng), o.Radius));
            }
        }
        return result;
    }

    private IEnumerable<Coordinate> MakeDetoursAround(Coordinate c, double safeMeters, int points = 4)
    {
        var (x, y) = LatLngToMeters(c);
        for (int i = 0; i < points; i++)
        {
            double angle = (Math.PI * 2 / points) * i;
            double dx = Math.Cos(angle) * safeMeters;
            double dy = Math.Sin(angle) * safeMeters;
            yield return MetersToLatLng((x + dx, y + dy));
        }
    }


    private (Coordinate center, double radius)? FirstHit(List<Coordinate> path)
    {
        foreach (var seg in path.Zip(path.Skip(1)))
        {
            foreach (var o in _obstacles.Values)
            {
                if (o.Intersects(seg.First, seg.Second))
                    return (new Coordinate(o.Lat, o.Lng), o.Radius + 0);
            }
        }
        return null;
    }

    private IEnumerable<Coordinate> MakeDetoursAround(Coordinate c, double safeMeters)
    {
        // Две точки «влево/вправо» в метрах от центра
        var (x, y) = LatLngToMeters(c);
        var candidates = new (double dx, double dy)[] { (safeMeters, 0), (-safeMeters, 0), (0, safeMeters), (0, -safeMeters) };
        foreach (var (dx, dy) in candidates)
            yield return MetersToLatLng((x + dx, y + dy));
    }

    public async Task<(List<Coordinate> route, Coordinate start, Coordinate end)?> TryRouteWithRetries(int maxAttempts)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            var start = RandomPointInBounds();
            var end = RandomPointInBounds();
            var route = await RouteWithAvoidance(start, end);
            if (route != null && route.Count >= 2)
                return (route, start, end);
        }
        return null;
    }

    private Coordinate RandomPointInBounds()
    {
        var (s, w, n, e) = _bounds;
        var lat = s + Random.NextDouble() * (n - s);
        var lng = w + Random.NextDouble() * (e - w);
        return new Coordinate(lat, lng);
    }

    public bool RouteBlocked(List<Coordinate> path)
    {
        for (int i = 0; i < path.Count - 1; i++)
        {
            foreach (var o in _obstacles.Values)
            {
                if (o.Intersects(path[i], path[i + 1])) return true;
            }
        }
        return false;
    }

    public double EstimateAzimuth(Car c)
    {
        if (c.PathIndex < c.Path.Count - 1)
            return Bearing(c.Position, c.Path[c.PathIndex + 1]);
        if (c.PathIndex > 0)
            return Bearing(c.Path[c.PathIndex - 1], c.Position);
        return 0;
    }

    private static string RandId() => (ulong)Random.Shared.NextInt64(long.MaxValue) + "_" + Random.Shared.Next(1, int.MaxValue);

    // Гео-утилиты
    private static double Haversine(Coordinate a, Coordinate b)
    {
        const double R = 6371000.0;
        double dLat = (b.Lat - a.Lat) * Math.PI / 180.0;
        double dLng = (b.Lng - a.Lng) * Math.PI / 180.0;
        double lat1 = a.Lat * Math.PI / 180.0;
        double lat2 = b.Lat * Math.PI / 180.0;
        double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Sin(dLng / 2) * Math.Sin(dLng / 2) * Math.Cos(lat1) * Math.Cos(lat2);
        return 2 * R * Math.Asin(Math.Min(1.0, Math.Sqrt(h)));
    }

    private static double Bearing(Coordinate a, Coordinate b)
    {
        double φ1 = a.Lat * Math.PI / 180, φ2 = b.Lat * Math.PI / 180;
        double Δλ = (b.Lng - a.Lng) * Math.PI / 180;
        double y = Math.Sin(Δλ) * Math.Cos(φ2);
        double x = Math.Cos(φ1) * Math.Sin(φ2) - Math.Sin(φ1) * Math.Cos(φ2) * Math.Cos(Δλ);
        double θ = Math.Atan2(y, x);
        return (θ * 180 / Math.PI + 360) % 360;
    }

    private static (double x, double y) LatLngToMeters(Coordinate c)
    {
        double x = c.Lng * Math.PI / 180.0 * 6378137.0 * Math.Cos(c.Lat * Math.PI / 180.0);
        double y = c.Lat * Math.PI / 180.0 * 6378137.0;
        return (x, y);
    }

    private static Coordinate MetersToLatLng((double x, double y) m)
    {
        double lat = m.y / 6378137.0;
        double lng = m.x / (6378137.0 * Math.Cos(lat));
        return new Coordinate(lat * 180.0 / Math.PI, lng * 180.0 / Math.PI);
    }
}
