using System.Collections.Generic;
using UnityEngine;

/// <summary>One golden-duel ring, built far away from every map. Each holds one duel at a time.</summary>
public class DuelArena : MonoBehaviour
{
    public int slot;
    public Transform spawnA, spawnB;

    static readonly List<DuelArena> all = new();

    void OnEnable() => all.Add(this);
    void OnDisable() => all.Remove(this);

    public static int Count => all.Count;

    public static DuelArena Get(int slot)
    {
        foreach (var a in all)
            if (a.slot == slot) return a;
        return null;
    }
}
