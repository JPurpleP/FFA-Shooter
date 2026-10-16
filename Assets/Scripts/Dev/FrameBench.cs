using System.Collections.Generic;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

/// <summary>
/// Test runs: -ffabench &lt;seconds&gt; logs average FPS, 1% lows, draw calls and garbage per frame after a 4 s warm-up
/// (vsync and the frame cap are turned off for the run).
/// </summary>
public class FrameBench : MonoBehaviour
{
    float duration, start;
    readonly List<float> frames = new();
    ProfilerRecorder batches, setPass, tris, gc, mainThread, shadowCasters;
    long batchSum, setPassSum, triSum, gcSum;
    int samples;
    bool disabledTypes;
    int spikeLogs;
    // Development builds: every profiler marker, to find what the frame is spent on.
    readonly List<(string name, ProfilerRecorder rec)> markers = new();
    readonly Dictionary<string, double> markerSums = new();
    readonly FrameTiming[] timing = new FrameTiming[1];
    double cpuMain, cpuRender, gpu;
    int timingSamples;

    public static void Begin(float seconds)
    {
        var b = new GameObject("Frame Bench").AddComponent<FrameBench>();
        b.duration = seconds;
        DontDestroyOnLoad(b.gameObject);
    }

    void Start()
    {
        start = Time.unscaledTime + 4f;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
        setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
        tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        shadowCasters = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
        gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
    }

    void Update()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        if (Time.unscaledTime < start) return;
        if (Debug.isDebugBuild && markers.Count == 0)
        {
            // After the warm-up, so the per-script markers ("X.Update() [Invoke]") exist.
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            foreach (var h in handles)
            {
                var d = ProfilerRecorderHandle.GetDescription(h);
                if (d.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds || d.Name.StartsWith("Object.") || d.Name.Contains("Job")) continue;
                markers.Add((d.Name, ProfilerRecorder.StartNew(d.Category, d.Name)));
            }
            Debug.Log($"[FFA] Recording {markers.Count} markers");
        }
        if (!disabledTypes)
        {
            // -ffabenchoff Type,Type: switch those scripts off to see what they cost.
            disabledTypes = true;
            var args = System.Environment.GetCommandLineArgs();
            int ai = System.Array.IndexOf(args, "-ffabenchoff");
            if (ai >= 0 && ai + 1 < args.Length)
            {
                var off = new HashSet<string>(args[ai + 1].Split(','));
                int n = 0;
                foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                    if (mb != this && off.Contains(mb.GetType().Name)) { mb.enabled = false; n++; }
                Debug.Log($"[FFA] Bench disabled {n} scripts: {args[ai + 1]}");
            }
            var counts = new Dictionary<string, int>();
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (mb.isActiveAndEnabled) counts[mb.GetType().Name] = counts.TryGetValue(mb.GetType().Name, out int c) ? c + 1 : 1;
            var sb = new System.Text.StringBuilder("[FFA] Scripts:");
            foreach (var kv in counts) sb.Append($" {kv.Key}={kv.Value}");
            Debug.Log(sb.ToString());
        }
        frames.Add(Time.unscaledDeltaTime);
        if (Time.unscaledDeltaTime > 0.025f && spikeLogs < 6 && markers.Count > 0)
        {
            spikeLogs++;
            var hot = new List<(string, long)>();
            foreach (var (name, rec) in markers)
                if (rec.Valid && rec.LastValue > 2_000_000 && rec.LastValue < 1_000_000_000) hot.Add((name, rec.LastValue));
            hot.Sort((x, y) => y.Item2.CompareTo(x.Item2));
            var sb = new System.Text.StringBuilder($"[FFA] Spike {Time.unscaledDeltaTime * 1000f:0}ms:");
            for (int i = 0; i < Mathf.Min(14, hot.Count); i++) sb.Append($" | {hot[i].Item2 / 1e6:0.0} {hot[i].Item1}");
            Debug.Log(sb.ToString());
        }
        batchSum += batches.LastValue;
        setPassSum += setPass.LastValue;
        triSum += tris.LastValue;
        gcSum += gc.LastValue;
        samples++;
        foreach (var (name, rec) in markers)
            if (rec.Valid && rec.LastValue > 0) markerSums[name] = (markerSums.TryGetValue(name, out double v) ? v : 0) + rec.LastValue;
        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, timing) > 0)
        {
            cpuMain += timing[0].cpuMainThreadFrameTime;
            cpuRender += timing[0].cpuRenderThreadFrameTime;
            gpu += timing[0].gpuFrameTime;
            timingSamples++;
        }
        if (Time.unscaledTime < start + duration) return;

        Census();
        frames.Sort();
        float total = 0f;
        foreach (float f in frames) total += f;
        float avg = frames.Count / total;
        int worst = Mathf.Max(1, frames.Count / 100);
        float worstTotal = 0f;
        for (int i = frames.Count - worst; i < frames.Count; i++) worstTotal += frames[i];
        float low = worst / worstTotal;
        Debug.Log($"[FFA] Bench map={MapCatalog.Current} fps={avg:0} low1={low:0} batches={batchSum / samples} setpass={setPassSum / samples} " +
                  $"tris={triSum / samples / 1000}k shadowcasters={shadowCasters.LastValue} gcKB={gcSum / samples / 1024f:0.0} gfx={GameSettings.Graphics}");
        if (timingSamples > 0)
            Debug.Log($"[FFA] Timing cpuMain={cpuMain / timingSamples:0.0}ms cpuRender={cpuRender / timingSamples:0.0}ms gpu={gpu / timingSamples:0.0}ms");
        if (markerSums.Count > 0)
        {
            var top = new List<KeyValuePair<string, double>>(markerSums);
            top.Sort((a, b) => b.Value.CompareTo(a.Value));
            var sb = new System.Text.StringBuilder("[FFA] Markers (ms/frame):");
            for (int i = 0; i < Mathf.Min(45, top.Count); i++) sb.Append($" | {top[i].Value / samples / 1e6:0.00} {top[i].Key}");
            Debug.Log(sb.ToString());
        }
        enabled = false;
    }

    /// <summary>What's in the active scene: renderers, materials, static batching, lights.</summary>
    static void Census()
    {
        var rs = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        int active = 0, batched = 0, casters = 0, instanced = 0;
        var mats = new HashSet<Material>();
        var names = new Dictionary<string, int>();
        foreach (var r in rs)
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            active++;
            if (r.isPartOfStaticBatch) batched++;
            if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) casters++;
            foreach (var m in r.sharedMaterials)
                if (m != null && mats.Add(m) && m.enableInstancing) instanced++;
            string n = r.gameObject.name;
            int cut = n.IndexOfAny(new[] { ' ', '(' });
            if (cut > 0) n = n.Substring(0, cut);
            names[n] = names.TryGetValue(n, out int c) ? c + 1 : 1;
        }
        var top = new List<KeyValuePair<string, int>>(names);
        top.Sort((a, b) => b.Value.CompareTo(a.Value));
        string topText = "";
        for (int i = 0; i < Mathf.Min(12, top.Count); i++) topText += $" {top[i].Key}={top[i].Value}";
        Debug.Log($"[FFA] Census renderers={active} staticBatched={batched} shadowCasting={casters} materials={mats.Count} instancedMats={instanced} top:{topText}");
        string lights = "";
        int pointLights = 0;
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (!l.isActiveAndEnabled) continue;
            if (l.type != LightType.Directional) pointLights++;
            if (lights.Length < 400) lights += $" {l.name}:{l.type}/{l.renderMode}/r{l.range:0}/sh{l.shadows}";
        }
        Debug.Log($"[FFA] Census lights={pointLights} pixelLights={QualitySettings.pixelLightCount} quality={QualitySettings.names[QualitySettings.GetQualityLevel()]}{lights}");
    }

    void OnDestroy()
    {
        batches.Dispose(); setPass.Dispose(); tris.Dispose(); gc.Dispose(); shadowCasters.Dispose();
        foreach (var (_, rec) in markers) rec.Dispose();
    }
}
