using System;
using UnityEngine;

/// <summary>
/// Every map lives in the one scene under its own root; only the selected root is active. The host picks the
/// map before starting, and clients switch to it when the first player object arrives (see FpsPlayer.MapId).
/// </summary>
public class MapCatalog : MonoBehaviour
{
    [Serializable]
    public class Entry
    {
        public string name;
        public string description;
        public GameObject root;
        public Rect navBounds = new(-90f, -64f, 180f, 148f);
        public Color fogColor = new(0.78f, 0.8f, 0.82f);
        public float fogStart = 130f, fogEnd = 600f;
        public Color ambientSky = new(0.62f, 0.68f, 0.78f);
        public Color ambientEquator = new(0.55f, 0.5f, 0.45f);
        public Color ambientGround = new(0.3f, 0.27f, 0.22f);
        public Color sunColor = new(1f, 0.95f, 0.86f);
        public float sunIntensity = 1.15f;
        public Vector3 sunEuler = new(48f, -40f, 0f);
        public Vector3 orbitCenter = new(0f, 2f, 10f);
        public float orbitRadius = 124f, orbitHeight = 74f;
        public Material skybox; // procedural sky tinted for the map
    }

    public Entry[] maps;
    public Light sun;
    public MenuCameraOrbit orbit;

    public static MapCatalog I { get; private set; }
    public static int Current { get; private set; } = -1;
    /// <summary>The active map as a network value (0 before anything is applied).</summary>
    public static byte CurrentByte => (byte)Mathf.Max(0, Current);
    public static int Count => I != null && I.maps != null ? I.maps.Length : 1;
    public static string NameOf(int id) => I != null && I.maps != null && id >= 0 && id < I.maps.Length ? I.maps[id].name : "Fluketown";
    public static string DescriptionOf(int id) => I != null && I.maps != null && id >= 0 && id < I.maps.Length ? I.maps[id].description : "";

    void Awake() => I = this;

    void Start()
    {
        if (Current < 0) Apply(GameSettings.Map);
    }

    void OnDestroy()
    {
        if (I == this) { I = null; Current = -1; }
    }

    /// <summary>Activate one map and its lighting, and drop the bot graph so it is rebuilt for that map.</summary>
    static readonly System.Collections.Generic.HashSet<GameObject> tidied = new();

    /// <summary>
    /// Once per map: matte objects skip reflection-probe lookups (the reflection wouldn't show anyway) and very
    /// small props stop casting shadows (nobody sees a pebble's shadow). Both cost real time per frame on maps with
    /// thousands of renderers.
    /// </summary>
    static void Tidy(GameObject root)
    {
        if (root == null || !tidied.Add(root) || System.Array.IndexOf(CommandLine.Args, "-ffanotidy") >= 0) return;
        int probesOff = 0, shadowsOff = 0;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            bool shiny = false;
            foreach (var m in r.sharedMaterials)
                if (m != null && ((m.HasProperty("_Glossiness") && m.GetFloat("_Glossiness") > 0.4f) || (m.HasProperty("_Metallic") && m.GetFloat("_Metallic") > 0.3f)))
                    shiny = true;
            if (!shiny && r.reflectionProbeUsage != UnityEngine.Rendering.ReflectionProbeUsage.Off)
            {
                r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                probesOff++;
            }
            if (r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.On && r.bounds.size.magnitude < 0.45f)
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                shadowsOff++;
            }
        }
        Debug.Log($"[FFA] Tidied {root.name}: reflection probes off on {probesOff}, shadows off on {shadowsOff}");
    }

    public static void Apply(int id)
    {
        if (I == null || I.maps == null || I.maps.Length == 0) return;
        id = Mathf.Clamp(id, 0, I.maps.Length - 1);
        if (id == Current) return;
        Current = id;
        for (int i = 0; i < I.maps.Length; i++)
            if (I.maps[i].root != null) I.maps[i].root.SetActive(i == id);

        var e = I.maps[id];
        Tidy(e.root);
        RenderSettings.fogColor = e.fogColor;
        RenderSettings.fogStartDistance = e.fogStart;
        RenderSettings.fogEndDistance = e.fogEnd;
        RenderSettings.ambientSkyColor = e.ambientSky;
        RenderSettings.ambientEquatorColor = e.ambientEquator;
        RenderSettings.ambientGroundColor = e.ambientGround;
        if (I.sun != null)
        {
            I.sun.color = e.sunColor;
            I.sun.intensity = e.sunIntensity;
            I.sun.transform.rotation = Quaternion.Euler(e.sunEuler);
        }
        if (I.orbit != null)
        {
            I.orbit.center = e.orbitCenter;
            I.orbit.radius = e.orbitRadius;
            I.orbit.height = e.orbitHeight;
        }
        if (e.skybox != null)
        {
            RenderSettings.skybox = e.skybox;
            DynamicGI.UpdateEnvironment();
        }
        BotNav.Bounds = e.navBounds;
        BotNav.Invalidate();
        Physics.SyncTransforms();
        if (e.root != null)
            foreach (var probe in e.root.GetComponentsInChildren<ReflectionProbe>())
                probe.RenderProbe();
        Debug.Log($"[FFA] Map: {e.name}");
    }
}
