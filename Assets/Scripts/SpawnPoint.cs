using System.Collections.Generic;
using UnityEngine;

/// <summary>A place players can (re)spawn. The server picks the one farthest from living enemies.</summary>
public class SpawnPoint : MonoBehaviour
{
    static readonly List<SpawnPoint> all = new();

    void OnEnable() => all.Add(this);
    void OnDisable() => all.Remove(this);

    public static void Pick(FpsPlayer forPlayer, out Vector3 position, out float yaw)
    {
        if (all.Count == 0)
        {
            position = Vector3.up;
            yaw = 0f;
            return;
        }

        var players = FindObjectsByType<FpsPlayer>();
        // Score each spawn by its distance to the nearest living enemy, then pick randomly among the best few.
        var scored = new List<(SpawnPoint point, float score)>();
        foreach (var sp in all)
        {
            float nearest = float.MaxValue;
            foreach (var p in players)
            {
                if (p == forPlayer || !p.IsSpawned || p.IsDead)
                    continue;
                nearest = Mathf.Min(nearest, Vector3.Distance(p.transform.position, sp.transform.position));
            }
            scored.Add((sp, nearest));
        }
        scored.Sort((a, b) => b.score.CompareTo(a.score));
        var chosen = scored[Random.Range(0, Mathf.Min(3, scored.Count))].point;
        position = chosen.transform.position;
        yaw = chosen.transform.eulerAngles.y;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position + Vector3.up, 0.5f);
        Gizmos.DrawRay(transform.position + Vector3.up, transform.forward);
    }
}
