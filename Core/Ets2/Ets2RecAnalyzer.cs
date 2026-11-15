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
        var rows = new List<(double T, double Speed, int Wheels, double[] Vel, double[] Exc, string Active, string[] Subst)>();
        foreach (var l in lines.Skip(1))
        {
            var c = l.Split(',');
            if (c.Length < col.Count) continue;
            double D(string k) => double.Parse(c[col[k]], CultureInfo.InvariantCulture);
            int wheels = (int)D("wheels");
            W = Math.Max(W, wheels);
            var vel = new double[8]; var exc = new double[8];
            for (int i = 0; i < 8; i++) { vel[i] = D("vel" + i); exc[i] = D("exc" + i); }
            rows.Add((D("t_ms"), D("speed_kmh"), wheels, vel, exc, c[col["active"]],
                col.TryGetValue("subst", out var si) && si < c.Length ? c[si].Split('|', StringSplitOptions.RemoveEmptyEntries) : []));
        }
        if (rows.Count == 0) { Console.Error.WriteLine("No valid rows."); return 1; }
        double dur = (rows[^1].T - rows[0].T) / 1000.0;
        Console.WriteLine($"{Path.GetFileName(path)}: {rows.Count} rows, {dur:F1} s, ~{rows.Count / Math.Max(dur, 0.001):F0} pkt/s, {W} wheels");

        foreach (var (lo, hi, name) in Buckets)
        {
            var b = rows.Where(r => r.Speed >= lo && r.Speed < hi).ToList();
            if (b.Count == 0) continue;
            double roadPct = 100.0 * b.Count(r => Has(r.Active, "tex") || Has(r.Active, "hit")) / b.Count;
            Console.WriteLine($"\nspeed {name} km/h: n={b.Count} ({100.0 * b.Count / rows.Count:F0}%), road feel (texture/hit) active {roadPct:F0}% of time");
            Console.WriteLine("  wheel   vel p50/p90/p99/max          exc p50/p90/p99/max");
            for (int i = 0; i < W; i++)
            {
                var v = b.Where(r => r.Vel[i] != 0).Select(r => r.Vel[i]).OrderBy(x => x).ToArray();
                var e = b.Where(r => r.Vel[i] != 0).Select(r => r.Exc[i]).OrderBy(x => x).ToArray();
                if (v.Length == 0) continue;
                Console.WriteLine($"  {i,5}   {Pct(v)}   {Pct(e)}");
            }
        }

        int gear = Edges(rows.Select(r => r.Active), "gear"), crash = Edges(rows.Select(r => r.Active), "crash");
        double all = 100.0 * rows.Count(r => Has(r.Active, "tex") || Has(r.Active, "hit")) / rows.Count;
        Console.WriteLine($"\nEvents (rising edges): texture={Edges(rows.Select(r => r.Active), "tex")}, hit={Edges(rows.Select(r => r.Active), "hit")}, gear={gear}, crash={crash}; road feel active {all:F0}% of all rows");

        if (rows.Any(r => r.Subst.Length > 0))
        {
            // Time is attributed to every distinct substance under the wheels; events to the primary one (first non-road, else first).
            var time = new SortedDictionary<string, double>();
            var tex = new Dictionary<string, int>(); var hit = new Dictionary<string, int>();
            string prevPrimary = ""; bool pTex = false, pHit = false;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                double dt = i == 0 ? 0 : Math.Min(rows[i].T - rows[i - 1].T, 250) / 1000.0;
                foreach (var n in r.Subst.Distinct()) time[n] = time.GetValueOrDefault(n) + dt;
                string primary = r.Subst.FirstOrDefault(n => Ets2Haptics.Classify(n) is { Rumble: true } or { Rough: true }) ?? r.Subst.FirstOrDefault() ?? "";
                bool t = Has(r.Active, "tex"), h = Has(r.Active, "hit");
                if (t && (!pTex || primary != prevPrimary)) tex[primary] = tex.GetValueOrDefault(primary) + 1;
                if (h && !pHit) hit[primary] = hit.GetValueOrDefault(primary) + 1;
                prevPrimary = primary; pTex = t; pHit = h;
            }
            Console.WriteLine("\nSubstances (time under wheels, texture events, hit events):");
            foreach (var (n, t) in time)
            {
                var (known, rumble, rough) = Ets2Haptics.Classify(n);
                string kind = rumble ? "rumble" : rough ? "rough" : known ? "road" : "UNKNOWN->road";
                Console.WriteLine($"  {n,-16} {t,7:F1} s  {kind,-14} texture={tex.GetValueOrDefault(n)} hit={hit.GetValueOrDefault(n)}");
            }
        }
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
