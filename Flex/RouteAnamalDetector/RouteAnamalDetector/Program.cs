using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

class Program
{
    const int MIN_FREQ = 20;
    const int DIFF_RATIO = 3;

    static void Main()
    {
        string basePath = AppDomain.CurrentDomain.BaseDirectory;

        string file1 = Path.Combine(basePath, "Heatmap#1.csv");
        string file2 = Path.Combine(basePath, "Heatmap#2.csv");
        string outputFile = Path.Combine(basePath, "traffic_drop_filtered.csv");

        var dict1 = LoadCsv(file1);
        var dict2 = LoadCsv(file2);

        var anomalies = new List<(int x, int y, int f1, int f2)>();

        // Сравнение первого со вторым
        foreach (var kv in dict1)
        {
            var key = kv.Key;
            int freq1 = kv.Value;
            int freq2 = dict2.ContainsKey(key) ? dict2[key] : 0;

            if (freq1 < MIN_FREQ && freq2 < MIN_FREQ)
                continue;

            if (freq2 == 0 || freq1 == 0 ||
                freq1 / Math.Max(freq2, 1) >= DIFF_RATIO ||
                freq2 / Math.Max(freq1, 1) >= DIFF_RATIO)
            {
                anomalies.Add((key.Item1, key.Item2, freq1, freq2));
            }
        }

        // Те, что есть только во втором
        foreach (var kv in dict2)
        {
            var key = kv.Key;
            int freq2 = kv.Value;
            if (dict1.ContainsKey(key)) continue;

            int freq1 = 0;
            if (freq1 < MIN_FREQ && freq2 < MIN_FREQ)
                continue;

            anomalies.Add((key.Item1, key.Item2, freq1, freq2));
        }

        // Переводим в формат cell_x,cell_y,frequency = берем freq2
        var rows = anomalies
            .Select(a => (a.x, a.y, Math.Max(a.f1, a.f2)))
            .Where(r => r.Item3 >= 100) // фильтр < 100
            .ToList();

        // Теперь ищем компоненты
        var cells = rows.Select(r => (r.x, r.y)).ToHashSet();
        var components = FindConnectedComponents(cells, dist: 2);

        // Берем самую большую компоненту
        var road = components.OrderByDescending(c => c.Count).First();

        var filtered = rows.Where(r => road.Contains((r.x, r.y))).ToList();

        // Сохраняем CSV
        using (var sw = new StreamWriter(outputFile))
        {
            sw.WriteLine("cell_x,cell_y,frequency");
            foreach (var r in filtered)
                sw.WriteLine($"{r.x},{r.y},{r.Item3}");
        }

        Console.WriteLine($"Найдено компонентов: {components.Count}");
        Console.WriteLine($"Дорога найдена, клеток: {road.Count}");
        foreach (var (x, y) in road.OrderBy(r => r.Item1).ThenBy(r => r.Item2))
            Console.WriteLine($"Квадрат: ({x}, {y})");

        Console.WriteLine($"Результат сохранен: {outputFile}");
    }

    static Dictionary<(int, int), int> LoadCsv(string path)
    {
        var dict = new Dictionary<(int, int), int>();
        foreach (var line in File.ReadLines(path).Skip(1)) // skip header
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(',');
            int x = int.Parse(parts[0]);
            int y = int.Parse(parts[1]);
            int freq = int.Parse(parts[2]);
            dict[(x, y)] = freq;
        }
        return dict;
    }

    static IEnumerable<(int, int)> GetNeighbors(int x, int y, int dist)
    {
        for (int dx = -dist; dx <= dist; dx++)
        {
            for (int dy = -dist; dy <= dist; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                yield return (x + dx, y + dy);
            }
        }
    }

    static List<HashSet<(int, int)>> FindConnectedComponents(HashSet<(int, int)> cells, int dist)
    {
        var visited = new HashSet<(int, int)>();
        var components = new List<HashSet<(int, int)>>();

        foreach (var cell in cells)
        {
            if (visited.Contains(cell)) continue;

            var comp = new HashSet<(int, int)>();
            var queue = new Queue<(int, int)>();
            queue.Enqueue(cell);
            visited.Add(cell);

            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();
                comp.Add((cx, cy));

                foreach (var neighbor in GetNeighbors(cx, cy, dist))
                {
                    if (cells.Contains(neighbor) && !visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }

            components.Add(comp);
        }

        return components;
    }
}
