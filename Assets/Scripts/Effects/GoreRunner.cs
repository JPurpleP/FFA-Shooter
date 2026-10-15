using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Runs deferred gore work on later frames, so a big burst (obliteration) is spread out instead of hitching.</summary>
public class GoreRunner : MonoBehaviour
{
    static GoreRunner instance;
    readonly List<Action> pending = new(), running = new();

    public static void NextFrame(Action action)
    {
        if (instance == null)
        {
            var go = new GameObject("GoreRunner") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            instance = go.AddComponent<GoreRunner>();
        }
        instance.pending.Add(action);
    }

    void Update()
    {
        if (pending.Count == 0) return;
        running.AddRange(pending);
        pending.Clear();
        foreach (var a in running)
        {
            try { a(); }
            catch (Exception e) { Debug.LogException(e); }
        }
        running.Clear();
    }
}
