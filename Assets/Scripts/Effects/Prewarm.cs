using System.Collections;
using UnityEngine;

/// <summary>
/// Every sound in the game is synthesised the first time it plays, which stalls that frame (badly in browsers:
/// the first gunshot, explosion or kill hitched for 100-400 ms). Build them all here instead, a few per frame while
/// the player is still in the menu.
/// </summary>
public class Prewarm : MonoBehaviour
{
    public static bool Done { get; private set; }

    /// <summary>
    /// Browsers compile each shader the first time it's drawn, freezing that frame for up to half a second (the first
    /// muzzle flash, blood, corpse, picked-up gun...). Draw every material the game uses once, tiny, in front of the
    /// menu camera (behind the menu), so it's all compiled before a match starts.
    /// </summary>
    IEnumerator WarmShaders()
    {
        float t0 = Time.realtimeSinceStartup;
        var mats = new System.Collections.Generic.HashSet<Material>();
        var a = GameAssets.I;
        if (a != null)
            foreach (var f in typeof(GameAssets).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                var v = f.GetValue(a);
                if (v is Material m) mats.Add(m);
                else if (v is Material[] ms) foreach (var x in ms) mats.Add(x);
                else if (v is GameObject go && go != null)
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true)) foreach (var x in r.sharedMaterials) mats.Add(x);
            }
        // Everything the loaded maps use too (other maps' materials compile while the menu flies over them).
        foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            foreach (var x in r.sharedMaterials) mats.Add(x);
        mats.Remove(null);
        // No GPU instancing in browsers: every instanced shader version (shadow, depth, colour passes...) compiled
        // mid-match the first time it was needed, freezing the game for 300+ ms. Static batching covers the scenery.
        foreach (var m in mats) m.enableInstancing = false;
        Camera cam = null;
        foreach (var c in Camera.allCameras) if (c.isActiveAndEnabled && c.targetTexture == null && (cam == null || c.depth > cam.depth)) cam = c;
        if (cam == null) { Debug.Log("[FFA] Shader warm-up skipped: no camera"); yield break; }
        var rig = new GameObject("Shader Warmup").transform;
        rig.SetParent(cam.transform, false);
        rig.localPosition = new Vector3(0f, 0f, cam.nearClipPlane + 0.5f);
        // Each material three times: a lone sphere (the normal shader) and a pair of cubes (Unity draws matching
        // objects together with the separate *instanced* shader), all casting shadows so the shadow-pass versions
        // compile too. The instanced shadow pass was the big one: 300-450 ms stalls the first time each appeared.
        var cube = Prims.MeshOf(PrimitiveType.Cube);
        var sphere = Prims.MeshOf(PrimitiveType.Sphere);
        int i = 0, batch = 0;
        void Piece(Mesh mesh, Material m, Vector3 at)
        {
            var go = new GameObject("w", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(rig, false);
            go.transform.localPosition = at;
            go.transform.localScale = Vector3.one * 0.0015f;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
        foreach (var m in mats)
        {
            var at = new Vector3((i % 16) * 0.006f - 0.045f, (i / 16) * 0.004f - 0.02f, 0f);
            Piece(sphere, m, at);
            Piece(cube, m, at + new Vector3(0.002f, 0f, 0f));
            Piece(cube, m, at + new Vector3(0.004f, 0f, 0f));
            i++;
            // A dozen materials per frame so the menu stays responsive while they compile.
            if (++batch >= 12) { batch = 0; yield return null; }
        }
        yield return null;
        yield return null;
        Destroy(rig.gameObject);
        Debug.Log($"[FFA] Warmed {mats.Count} materials in {(Time.realtimeSinceStartup - t0) * 1000f:0} ms");
    }

    public static void Begin()
    {
        if (Done || FindAnyObjectByType<Prewarm>() != null) return;
        var go = new GameObject("Prewarm");
        DontDestroyOnLoad(go);
        go.AddComponent<Prewarm>();
    }

    IEnumerator Start()
    {
        float t0 = Time.realtimeSinceStartup;
        int made = 0;
        for (int i = 0; i < WeaponDefs.All.Length; i++)
        {
            var def = WeaponDefs.All[i];
            if (def == null) continue;
            Sfx.Gunshot(def.Id);
            made++;
            yield return null;
        }
        var _ = new[]
        {
            Sfx.Pump, Sfx.Reload, Sfx.ShellInsert, Sfx.DryFire, Sfx.Pickup, Sfx.HitTick, Sfx.Squelch, Sfx.GrappleFire,
            Sfx.Slide, Sfx.Explosion, Sfx.Swing, Sfx.Punch, Sfx.Bell, Sfx.Splatter,
        };
        made += _.Length;
        yield return null;
        foreach (Medal m in System.Enum.GetValues(typeof(Medal))) { Medals.Sting(m); made++; }
        yield return null;
        Debug.Log($"[FFA] Prewarmed {made} sounds in {(Time.realtimeSinceStartup - t0) * 1000f:0} ms");
        if (GraphicsQuality.Web) yield return WarmShaders();
        Done = true;
        Destroy(gameObject);
    }
}
