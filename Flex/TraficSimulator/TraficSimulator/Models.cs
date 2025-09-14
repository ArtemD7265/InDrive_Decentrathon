public record Coordinate(double Lat, double Lng);

public class Car
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public Coordinate Position { get; set; } = new(0, 0);
    public Coordinate Destination { get; set; } = new(0, 0);
    public List<Coordinate> Path { get; set; } = new();
    public int PathIndex { get; set; }
    public double SpeedMps { get; set; }
}

public class Obstacle
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public double Lat { get; set; }
    public double Lng { get; set; }
    public string Type { get; set; }
    public double Radius { get; set; }

    public Obstacle(double lat, double lng, string type)
    {
        Lat = lat;
        Lng = lng;
        Type = type;
        Radius = type == "ДТП" ? 30 : 40;
    }

    public bool Intersects(Coordinate a, Coordinate b)
    {
        var p = new Coordinate(Lat, Lng);
        var meters = DistancePointToSegmentMeters(p, a, b);
        return meters <= Radius;
    }

    private static double DistancePointToSegmentMeters(Coordinate p, Coordinate a, Coordinate b)
    {
        static (double x, double y) toMeters(Coordinate c)
        {
            var x = c.Lng * Math.PI / 180.0 * 6378137.0 * Math.Cos(c.Lat * Math.PI / 180.0);
            var y = c.Lat * Math.PI / 180.0 * 6378137.0;
            return (x, y);
        }
        var P = toMeters(p);
        var A = toMeters(a);
        var B = toMeters(b);
        var AP = (x: P.x - A.x, y: P.y - A.y);
        var AB = (x: B.x - A.x, y: B.y - A.y);
        var ab2 = AB.x * AB.x + AB.y * AB.y;
        var t = ab2 > 0 ? Math.Clamp((AP.x * AB.x + AP.y * AB.y) / ab2, 0, 1) : 0;
        var H = (x: A.x + AB.x * t, y: A.y + AB.y * t);
        var dx = P.x - H.x; var dy = P.y - H.y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

public class RouteResult
{
    public List<Coordinate> Coordinates { get; set; } = new();
    public double Distance { get; set; }
    public double Time { get; set; }
}
