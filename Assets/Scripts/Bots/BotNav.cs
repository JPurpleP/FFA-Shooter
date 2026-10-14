using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Waypoint graph for bots, generated at runtime by sampling walkable surfaces across the map
/// (plus hand-placed NavHints on stairs and doorways) and linking neighbors that can walk to each other.
/// </summary>
public static class BotNav
{
    static List<Vector3> nodes;
    static List<int>[] links;

    public static Rect Bounds = new(-68f, -46f, 136f, 112f);
    const float Step = 2.5f;
    const float LinkRange = 3.9f;
    const int WorldMask = 1;

    public static int NodeCount => nodes?.Count ?? 0;

    public static void Invalidate() => nodes = null;

    static void EnsureBuilt()
    {
        if (nodes != null) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        nodes = new List<Vector3>();
        var hits = new RaycastHit[16];
        for (float x = Bounds.xMin + 1f; x < Bounds.xMax; x += Step)
            for (float z = Bounds.yMin + 1f; z < Bounds.yMax; z += Step)
            {
                int n = Physics.RaycastNonAlloc(new Vector3(x, 45f, z), Vector3.down, hits, 50f, WorldMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                    TryAdd(hits[i].point, hits[i].normal);
            }
        foreach (var hint in Object.FindObjectsByType<NavHint>())
            nodes.Add(hint.transform.position);

        links = new List<int>[nodes.Count];
        for (int i = 0; i < nodes.Count; i++) links[i] = new List<int>();
        for (int i = 0; i < nodes.Count; i++)
            for (int j = i + 1; j < nodes.Count; j++)
            {
                Vector3 a = nodes[i], b = nodes[j];
                if (Mathf.Abs(a.y - b.y) > 0.95f) continue;
                if ((a - b).sqrMagnitude > LinkRange * LinkRange) continue;
                if (!Walkable(a, b)) continue;
                links[i].Add(j);
                links[j].Add(i);
            }
        Debug.Log($"[FFA] Bot nav graph: {nodes.Count} nodes in {sw.ElapsedMilliseconds} ms");
    }

    static void TryAdd(Vector3 p, Vector3 normal)
    {
        if (normal.y < 0.7f) return;
        if (Physics.CheckCapsule(p + Vector3.up * 0.5f, p + Vector3.up * 1.6f, 0.3f, WorldMask, QueryTriggerInteraction.Ignore)) return;
        nodes.Add(p);
    }

    static bool Walkable(Vector3 a, Vector3 b)
    {
        foreach (float h in new[] { 0.6f, 1.4f })
            if (Physics.Linecast(a + Vector3.up * h, b + Vector3.up * h, WorldMask, QueryTriggerInteraction.Ignore))
                return false;
        // Make sure there is floor along the way (no walking off roofs).
        for (int s = 1; s <= 2; s++)
        {
            Vector3 m = Vector3.Lerp(a, b, s / 3f);
            if (!Physics.Raycast(m + Vector3.up * 1f, Vector3.down, 2.2f, WorldMask, QueryTriggerInteraction.Ignore))
                return false;
        }
        return true;
    }

    public static Vector3 RandomNode()
    {
        EnsureBuilt();
        return nodes.Count > 0 ? nodes[Random.Range(0, nodes.Count)] : Vector3.zero;
    }

    static int Nearest(Vector3 p)
    {
        int best = -1;
        float bestD = float.MaxValue;
        for (int i = 0; i < nodes.Count; i++)
        {
            float d = (nodes[i] - p).sqrMagnitude + Mathf.Abs(nodes[i].y - p.y) * 20f;
            if (d < bestD && d < 100f)
            {
                if (Physics.Linecast(p + Vector3.up * 0.8f, nodes[i] + Vector3.up * 0.8f, WorldMask, QueryTriggerInteraction.Ignore)) continue;
                bestD = d;
                best = i;
            }
        }
        return best;
    }

    /// <summary>A* over the graph. Returns waypoints ending at the destination, or a straight line if unreachable.</summary>
    public static List<Vector3> FindPath(Vector3 from, Vector3 to)
    {
        EnsureBuilt();
        int start = Nearest(from), goal = Nearest(to);
        if (start < 0 || goal < 0)
            return new List<Vector3> { to };

        var open = new SortedSet<(float f, int id)>(Comparer<(float f, int id)>.Create((x, y) => x.f != y.f ? x.f.CompareTo(y.f) : x.id.CompareTo(y.id)));
        var g = new Dictionary<int, float> { [start] = 0f };
        var came = new Dictionary<int, int>();
        open.Add((Vector3.Distance(nodes[start], nodes[goal]), start));
        int iterations = 0;
        while (open.Count > 0 && iterations++ < 4000)
        {
            var cur = open.Min;
            open.Remove(cur);
            if (cur.id == goal)
            {
                var result = new List<Vector3> { to };
                int n = goal;
                while (came.TryGetValue(n, out int prev))
                {
                    result.Add(nodes[n]);
                    n = prev;
                }
                result.Add(nodes[start]);
                result.Reverse();
                return result;
            }
            foreach (int nb in links[cur.id])
            {
                float cost = g[cur.id] + Vector3.Distance(nodes[cur.id], nodes[nb]);
                if (g.TryGetValue(nb, out float old) && old <= cost) continue;
                if (g.ContainsKey(nb)) open.Remove((old + Vector3.Distance(nodes[nb], nodes[goal]), nb));
                g[nb] = cost;
                came[nb] = cur.id;
                open.Add((cost + Vector3.Distance(nodes[nb], nodes[goal]), nb));
            }
        }
        return new List<Vector3> { to };
    }

    /// <summary>Debug drawing of the graph (dev panel toggle).</summary>
    public static void DrawGizmoLines(System.Action<Vector3, Vector3> line)
    {
        if (nodes == null) return;
        for (int i = 0; i < nodes.Count; i++)
            foreach (int j in links[i])
                if (j > i) line(nodes[i] + Vector3.up * 0.1f, nodes[j] + Vector3.up * 0.1f);
    }
}
