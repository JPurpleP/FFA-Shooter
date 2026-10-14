using System.Collections.Generic;
using UnityEngine;

/// <summary>Turns some blood particle collisions into decals on walls and floors.</summary>
public class BloodDrops : MonoBehaviour
{
    public int decalEvery = 8;
    ParticleSystem ps;
    readonly List<ParticleCollisionEvent> events = new();
    int counter, made;
    const int MaxPerSystem = 12;

    void Awake() => ps = GetComponent<ParticleSystem>();

    void OnParticleCollision(GameObject other)
    {
        if (made >= MaxPerSystem) return;
        int n = ps.GetCollisionEvents(other, events);
        for (int i = 0; i < n && made < MaxPerSystem; i++)
        {
            if (++counter % decalEvery != 0) continue;
            Gore.Decal(events[i].intersection, events[i].normal, Random.Range(0.12f, 0.45f), false);
            made++;
        }
    }
}
