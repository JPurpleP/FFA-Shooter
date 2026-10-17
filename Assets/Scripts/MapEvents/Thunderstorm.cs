using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Timberline: every few minutes a thunderstorm rolls over the camp for about a minute and a half. The sky darkens,
/// rain streaks down round the camera, the fog closes in, and lightning lights up the woods, with thunder after it
/// (later and longer for distant strikes). All timed off the shared server clock, lightning included, so every
/// player sees the same flashes.
/// </summary>
public class Thunderstorm : MonoBehaviour
{
    public const float Period = 240f, StormStart = 100f, StormLength = 90f, Ramp = 14f;
    static readonly bool forced = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-ffastorm") >= 0; // tests

    public static float Strength { get; private set; }

    ParticleSystem rain;
    AudioSource rainLoop, thunder;
    AudioClip[] thunderClips;
    Material sky;
    Color skyTint, skyGround;
    float skyExposure, skySun;
    Color ambSky, ambEquator, ambGround;
    bool applied;
    int lastBolt = -1;
    float flash;               // 0..1 brightness of the current lightning flash
    float flashStart = -10f, thunderAt = -1f;
    int thunderIndex;
    float boltDistance;

    void Awake()
    {
        var go = new GameObject("Rain");
        go.transform.SetParent(transform, false);
        rain = go.AddComponent<ParticleSystem>();
        rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = rain.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.02f);
        main.startColor = new Color(0.75f, 0.8f, 0.9f, 0.55f);
        main.maxParticles = 20000;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var shape = rain.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(28f, 1f, 28f);
        var vel = rain.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
        vel.y = new ParticleSystem.MinMaxCurve(-24f, -20f);
        vel.z = new ParticleSystem.MinMaxCurve(0.5f, 1f);
        var em = rain.emission;
        em.rateOverTime = 0f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.035f;
        r.lengthScale = 2f;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (GameAssets.I != null) r.sharedMaterial = GameAssets.I.dust;

        rainLoop = gameObject.AddComponent<AudioSource>();
        rainLoop.clip = MakeRain();
        rainLoop.loop = true;
        rainLoop.spatialBlend = 0f;
        rainLoop.playOnAwake = false;
        thunder = gameObject.AddComponent<AudioSource>();
        thunder.spatialBlend = 0f;
        thunder.playOnAwake = false;
        thunderClips = new[] { MakeThunder(1, 3.2f), MakeThunder(2, 4.5f), MakeThunder(3, 2.4f) };
    }

    static double Now
    {
        get
        {
            var net = NetworkManager.Singleton;
            return net != null && net.IsListening ? net.ServerTime.Time : Time.timeAsDouble;
        }
    }

    void OnDisable()
    {
        Strength = 0f;
        Restore();
        if (rain != null) rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (rainLoop != null) rainLoop.Stop();
        applied = false;
        GraphicsQuality.StormVisibility(0f);
    }

    void Update()
    {
        double now = Now;
        int cycle = (int)System.Math.Floor(now / Period);
        float into = forced ? StormLength * 0.5f : (float)(now - cycle * (double)Period) - StormStart;
        float k = into < 0f || into > StormLength ? 0f : Mathf.Clamp01(into / Ramp) * Mathf.Clamp01((StormLength - into) / Ramp);
        k = k * k * (3f - 2f * k);
        Strength = k;
        var map = MapCatalog.I != null && MapCatalog.Current >= 0 ? MapCatalog.I.maps[MapCatalog.Current] : null;
        if (map == null) return;
        if (k <= 0f && !applied) return;
        if (!applied) Capture();
        applied = k > 0f;

        // Lightning: one possible bolt every 6 s slot, decided by a hash of the slot so everyone agrees.
        if (k > 0.6f)
        {
            int slot = (int)System.Math.Floor(now / 6.0);
            uint h = (uint)slot * 2654435761u;
            if (slot != lastBolt && (h % 100) < 70)
            {
                float at = (h >> 8) % 1000 / 1000f * 5f;
                if (now - slot * 6.0 >= at)
                {
                    lastBolt = slot;
                    flashStart = Time.time;
                    boltDistance = 0.3f + ((h >> 16) % 100) / 100f * 0.7f; // 0.3 close .. 1 far
                    thunderAt = Time.time + 0.4f + boltDistance * 3.2f;
                    thunderIndex = (int)((h >> 4) % 3);
                }
            }
        }
        float since = Time.time - flashStart;
        // A double flicker: bright, dip, bright, fade.
        flash = since < 0f || since > 0.5f ? 0f
            : (since < 0.06f ? 1f : since < 0.11f ? 0.25f : since < 0.17f ? 0.85f : Mathf.Exp(-(since - 0.17f) * 9f) * 0.7f) * Mathf.Lerp(1.2f, 0.45f, boltDistance);
        if (thunderAt > 0f && Time.time >= thunderAt)
        {
            thunderAt = -1f;
            thunder.pitch = Mathf.Lerp(1.05f, 0.8f, boltDistance);
            thunder.PlayOneShot(thunderClips[thunderIndex], Mathf.Lerp(1f, 0.55f, boltDistance));
        }

        var dark = new Color(0.52f, 0.53f, 0.55f); // slate grey storm cloud
        RenderSettings.fogColor = Color.Lerp(map.fogColor, new Color(0.42f, 0.46f, 0.5f), k) + Color.white * flash * 0.25f;
        RenderSettings.fogStartDistance = Mathf.Lerp(map.fogStart, 8f, k);
        RenderSettings.fogEndDistance = Mathf.Lerp(map.fogEnd, 110f, k);
        if (MapCatalog.I.sun != null) MapCatalog.I.sun.intensity = Mathf.Lerp(map.sunIntensity, map.sunIntensity * 0.3f, k) + flash * 1.6f;
        RenderSettings.ambientSkyColor = Color.Lerp(ambSky, ambSky * 0.5f, k) + new Color(0.6f, 0.65f, 0.8f) * flash;
        RenderSettings.ambientEquatorColor = Color.Lerp(ambEquator, ambEquator * 0.55f, k) + new Color(0.4f, 0.42f, 0.5f) * flash;
        RenderSettings.ambientGroundColor = Color.Lerp(ambGround, ambGround * 0.6f, k);
        if (sky != null)
        {
            sky.SetColor("_SkyTint", Color.Lerp(skyTint, dark, k));
            sky.SetColor("_GroundColor", Color.Lerp(skyGround, dark * 0.8f, k));
            sky.SetFloat("_Exposure", Mathf.Lerp(skyExposure, 0.75f, k) + flash * 2.5f);
            sky.SetFloat("_SunSize", Mathf.Lerp(skySun, 0f, k));
        }

        var cam = FpsPlayer.Local != null && FpsPlayer.Local.playerCamera != null && FpsPlayer.Local.playerCamera.enabled
            ? FpsPlayer.Local.playerCamera : Camera.main;
        if (cam != null) rain.transform.position = cam.transform.position + Vector3.up * 14f;
        var em = rain.emission;
        em.rateOverTime = 15000f * k * GraphicsQuality.StormParticles;
        var rmain = rain.main;
        rmain.maxParticles = Mathf.RoundToInt(20000 * GraphicsQuality.StormParticles);
        GraphicsQuality.StormVisibility(k);
        if (k > 0f && !rain.isPlaying) rain.Play();
        if (k <= 0f && rain.isPlaying) rain.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (k > 0f && !rainLoop.isPlaying) rainLoop.Play();
        rainLoop.volume = k * 0.5f;
        if (k <= 0f) { rainLoop.Stop(); Restore(); }

        if (into > 0f && into < 2f && FpsPlayer.Local != null && !FpsPlayer.Local.InLobby.Value && announced != cycle)
        {
            announced = cycle;
            GameUI.ShowBanner("THUNDERSTORM", "Rain and lightning rolling over the camp.", new Color(0.7f, 0.8f, 1f));
        }
    }

    int announced = -1;

    void Capture()
    {
        sky = RenderSettings.skybox != null && RenderSettings.skybox.HasProperty("_SkyTint") ? RenderSettings.skybox : null;
        if (sky != null)
        {
            skyTint = sky.GetColor("_SkyTint");
            skyGround = sky.GetColor("_GroundColor");
            skyExposure = sky.GetFloat("_Exposure");
            skySun = sky.GetFloat("_SunSize");
        }
        ambSky = RenderSettings.ambientSkyColor;
        ambEquator = RenderSettings.ambientEquatorColor;
        ambGround = RenderSettings.ambientGroundColor;
    }

    void Restore()
    {
        if (!applied && sky == null) return;
        var map = MapCatalog.I != null && MapCatalog.Current >= 0 ? MapCatalog.I.maps[MapCatalog.Current] : null;
        if (sky != null)
        {
            sky.SetColor("_SkyTint", skyTint);
            sky.SetColor("_GroundColor", skyGround);
            sky.SetFloat("_Exposure", skyExposure);
            sky.SetFloat("_SunSize", skySun);
        }
        sky = null;
        if (map == null || map.root != gameObject) return; // another map is up: it set its own lighting
        RenderSettings.fogColor = map.fogColor;
        RenderSettings.fogStartDistance = map.fogStart;
        RenderSettings.fogEndDistance = map.fogEnd;
        RenderSettings.ambientSkyColor = ambSky;
        RenderSettings.ambientEquatorColor = ambEquator;
        RenderSettings.ambientGroundColor = ambGround;
        if (MapCatalog.I.sun != null) MapCatalog.I.sun.intensity = map.sunIntensity;
    }

    // ------------------------------------------------------------------ sounds (generated)

    static AudioClip MakeRain()
    {
        const int rate = 44100;
        float len = 4f;
        var d = new float[(int)(rate * len)];
        var rng = new System.Random(5);
        float a = 0f, b = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float n = (float)rng.NextDouble() * 2f - 1f;
            a += (n - a) * 0.35f;
            b += (a - b) * 0.2f;
            float drop = rng.NextDouble() < 0.0015 ? ((float)rng.NextDouble() * 2f - 1f) * 0.8f : 0f; // patter
            d[i] = (a - b) * 0.9f + b * 0.5f + drop;
        }
        int fade = rate / 10;
        for (int i = 0; i < fade; i++)
        {
            float f = i / (float)fade;
            d[i] = d[i] * f + d[d.Length - fade + i] * (1f - f);
        }
        var trimmed = new float[d.Length - fade];
        System.Array.Copy(d, trimmed, trimmed.Length);
        var clip = AudioClip.Create("rain", trimmed.Length, 1, rate, false);
        clip.SetData(trimmed, 0);
        return clip;
    }

    static AudioClip MakeThunder(int seed, float len)
    {
        const int rate = 44100;
        var d = new float[(int)(rate * len)];
        var rng = new System.Random(seed * 31);
        float a = 0f, b = 0f, c = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)rate;
            float n = (float)rng.NextDouble() * 2f - 1f;
            a += (n - a) * 0.06f;
            b += (a - b) * 0.04f;
            c += (b - c) * 0.02f;
            // Crack, then a long rolling rumble with a few swells.
            float crack = Mathf.Exp(-t * 18f) * a * 2.2f;
            float roll = (b * 3f + c * 5f) * Mathf.Clamp01(t / 0.25f) * Mathf.Exp(-t * 0.9f) * (0.7f + 0.3f * Mathf.Sin(t * 2.3f + seed));
            d[i] = Mathf.Clamp(crack + roll, -1f, 1f);
        }
        var clip = AudioClip.Create("thunder" + seed, d.Length, 1, rate, false);
        clip.SetData(d, 0);
        return clip;
    }
}
