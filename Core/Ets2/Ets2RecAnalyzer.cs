using System.Globalization;

namespace DualSenseHub.Core.Ets2;

/// <summary>Compact summary of a recording made by <see cref="Ets2Recorder"/>: per speed bucket percentiles and event counts.</summary>
public static class Ets2RecAnalyzer
{
    private static readonly (double Lo, double Hi, string Name)[] Buckets =
        [(0, 5, "<5"), (5, 30, "5-30"), (30, 50, "30-50"), (50, 70, "50-70"), (70, 90, "70-90"), (90, 1e9, "90+")];

    public static int Run(string path)
    {
        if (!File.Exists(path)) { Console.Error.WriteLine("File not found: " + path); return 1; }
        var lines = File.ReadAllLines(path);
        if (lines.Length < 2) { Console.Error.WriteLine("No rows."); return 1; }
        var col = lines[0].Split(',').Select((n, i) => (n, i)).ToDictionary(x => x.n, x => x.i);
        int W = 0;
        var rows = new List<(double T, double Speed, int Wheels, double[] Vel, double[] Exc, string Active)>();
        foreach (var l in lines.Skip(1))
        {
            var c = l.Split(',');
            if (c.Length < col.Count) continue;
            double D(string k) => double.Parse(c[col[k]], CultureInfo.InvariantCulture);
            int wheels = (int)D("wheels");
            W = Math.Max(W, wheels);
            var vel = new double[8]; var exc = new double[8];
            for (int i = 0; i < 8; i++) { vel[i] = D("vel" + i); exc[i] = D("exc" + i); }
            rows.Add((D("t_ms"), D("speed_kmh"), wheels, vel, exc, c[col["active"]]));
        }
        if (rows.Count == 0) { Console.Error.WriteLine("No valid rows."); return 1; }
        double dur = (rows[^1].T - rows[0].T) / 1000.0;
        Console.WriteLine($"{Path.GetFileName(path)}: {rows.Count} rows, {dur:F1} s, ~{rows.Count / Math.Max(dur, 0.001):F0} pkt/s, {W} wheels");

        foreach (var (lo, hi, name) in Buckets)
        {
            var b = rows.Where(r => r.Speed >= lo && r.Speed < hi).ToList();
            if (b.Count == 0) continue;
            double roadPct = 100.0 * b.Count(r => Has(r.Active, "road")) / b.Count;
            Console.WriteLine($"\nspeed {name} km/h: n={b.Count} ({100.0 * b.Count / rows.Count:F0}%), road rumble active {roadPct:F0}% of time");
            Console.WriteLine("  wheel   vel p50/p90/p99/max          exc p50/p90/p99/max");
            for (int i = 0; i < W; i++)
            {
                var v = b.Where(r => r.Vel[i] != 0).Select(r => r.Vel[i]).OrderBy(x => x).ToArray();
                var e = b.Where(r => r.Vel[i] != 0).Select(r => r.Exc[i]).OrderBy(x => x).ToArray();
                if (v.Length == 0) continue;
                Console.WriteLine($"  {i,5}   {Pct(v)}   {Pct(e)}");
            }
        }

        int road = Edges(rows.Select(r => r.Active), "road"), gear = Edges(rows.Select(r => r.Active), "gear"), crash = Edges(rows.Select(r => r.Active), "crash");
        double all = 100.0 * rows.Count(r => Has(r.Active, "road")) / rows.Count;
        Console.WriteLine($"\nEvents (rising edges): road={road}, gear={gear}, crash={crash}; road rumble active {all:F0}% of all rows");
        return 0;
    }

    private static bool Has(string active, string f) => active.Split(';').Contains(f);

    private static int Edges(IEnumerable<string> active, string f)
    {
        int n = 0; bool prev = false;
        foreach (var a in active) { bool now = Has(a, f); if (now && !prev) n++; prev = now; }
        return n;
    }

    private static string Pct(double[] s)
    {
        double P(double q) => s[Math.Min(s.Length - 1, (int)(q * s.Length))];
        return $"{P(0.5),6:F2} {P(0.9),6:F2} {P(0.99),6:F2} {s[^1],6:F2}";
    }
}
