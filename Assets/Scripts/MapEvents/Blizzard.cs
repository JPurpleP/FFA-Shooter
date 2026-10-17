using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Frostpeak: every few minutes a blizzard rolls through for about a minute. Fog closes in, the snowfall thickens and
/// blows sideways, the sun dims and the wind howls. Timed off the shared server clock, so everyone gets it together.
/// </summary>
public class Blizzard : MonoBehaviour
{
    public const float Period = 210f, StormStart = 130f, StormLength = 70f, Ramp = 12f;
    public ParticleSystem snow;

    /// <summary>How strong the storm is right now, 0..1 (the HUD and bots can read it).</summary>
    public static float Strength { get; private set; }

    AudioSource wind;
    float baseEmission = 380f;
    int announcedCycle = -1;
    bool applied;

    void Awake()
    {
        if (snow != null) baseEmission = snow.emission.rateOverTime.constant;
        wind = gameObject.AddComponent<AudioSource>();
        wind.clip = MakeWind();
        wind.loop = true;
        wind.spatialBlend = 0f;
        wind.volume = 0f;
        wind.playOnAwake = false;
        BuildGusts();
    }

    ParticleSystem gusts; // dense wind-driven snow in a box round the camera (the map-wide snowfall is too sparse up close)
    Material sky;
    Color skyTint, skyGround;
    float skyExposure, skySun;
    static readonly bool forced = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-ffablizzard") >= 0; // tests: storm on

    void BuildGusts()
    {
        var go = new GameObject("Blizzard Gusts");
        go.transform.SetParent(transform, false);
        gusts = go.AddComponent<ParticleSystem>();
        gusts.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = gusts.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.2f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.075f);
        main.startColor = new Color(1f, 1f, 1f, 0.85f);
        main.maxParticles = 16000;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var shape = gusts.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(26f, 12f, 26f);
        var vel = gusts.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(9f, 15f);
        vel.y = new ParticleSystem.MinMaxCurve(-4f, -2f);
        vel.z = new ParticleSystem.MinMaxCurve(1f, 4f);
        var em = gusts.emission;
        em.rateOverTime = 0f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch; // streaks in the wind
        r.velocityScale = 0.03f;
        r.lengthScale = 1.5f;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (snow != null) r.sharedMaterial = snow.GetComponent<ParticleSystemRenderer>().sharedMaterial;
    }

    /// <summary>The sky whites out too (fog doesn't reach the skybox).</summary>
    void TintSky(float k)
    {
        var current = RenderSettings.skybox;
        if (current != sky)
        {
            RestoreSky();
            sky = current;
            if (sky == null || !sky.HasProperty("_SkyTint")) { sky = null; return; }
            skyTint = sky.GetColor("_SkyTint");
            skyGround = sky.GetColor("_GroundColor");
            skyExposure = sky.GetFloat("_Exposure");
            skySun = sky.GetFloat("_SunSize");
        }
        if (sky == null) return;
        var white = new Color(0.74f, 0.78f, 0.86f); // cool grey: the procedural sky warms whatever it is given
        sky.SetColor("_SkyTint", Color.Lerp(skyTint, white, k));
        sky.SetColor("_GroundColor", Color.Lerp(skyGround, white, k));
        sky.SetFloat("_Exposure", Mathf.Lerp(skyExposure, 2.2f, k));
        sky.SetFloat("_SunSize", Mathf.Lerp(skySun, 0f, k));
    }

    void RestoreSky()
    {
        if (sky == null) return;
        sky.SetColor("_SkyTint", skyTint);
        sky.SetColor("_GroundColor", skyGround);
        sky.SetFloat("_Exposure", skyExposure);
        sky.SetFloat("_SunSize", skySun);
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
        if (wind != null) wind.Stop();
        RestoreSky();
        sky = null;
        GraphicsQuality.StormVisibility(0f);
        if (gusts != null) gusts.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        applied = false; // the next map sets its own fog and light
    }

    void Update()
    {
        double t = Now;
        int cycle = (int)System.Math.Floor(t / Period);
        float into = forced ? StormLength * 0.5f : (float)(t - cycle * (double)Period) - StormStart;
        float k = into < 0f || into > StormLength ? 0f : Mathf.Clamp01(into / Ramp) * Mathf.Clamp01((StormLength - into) / Ramp);
        k = k * k * (3f - 2f * k);
        Strength = k;

        var map = MapCatalog.I != null && MapCatalog.Current >= 0 ? MapCatalog.I.maps[MapCatalog.Current] : null;
        if (map == null) return;
        if (k <= 0f && !applied) return; // calm, and nothing of ours left to undo
        applied = k > 0f;

        RenderSettings.fogColor = Color.Lerp(map.fogColor, new Color(0.88f, 0.9f, 0.94f), k);
        RenderSettings.fogStartDistance = Mathf.Lerp(map.fogStart, 3f, k);
        RenderSettings.fogEndDistance = Mathf.Lerp(map.fogEnd, 48f, k);
        if (MapCatalog.I.sun != null) MapCatalog.I.sun.intensity = Mathf.Lerp(map.sunIntensity, map.sunIntensity * 0.45f, k);

        TintSky(k);
        GraphicsQuality.StormVisibility(k);
        if (gusts != null)
        {
            var cam = FpsPlayer.Local != null && FpsPlayer.Local.playerCamera != null && FpsPlayer.Local.playerCamera.enabled
                ? FpsPlayer.Local.playerCamera : Camera.main;
            if (cam != null) gusts.transform.position = cam.transform.position + Vector3.left * 8f; // a little upwind
            var gem = gusts.emission;
            gem.rateOverTime = 7000f * k * GraphicsQuality.StormParticles;
            var gmain = gusts.main;
            gmain.maxParticles = Mathf.RoundToInt(16000 * GraphicsQuality.StormParticles);
            if (k > 0f && !gusts.isPlaying) gusts.Play();
            if (k <= 0f && gusts.isPlaying) gusts.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
        if (snow != null)
        {
            var em = snow.emission;
            em.rateOverTime = baseEmission;
            var main = snow.main;

            var vel = snow.velocityOverLifetime;
            vel.enabled = k > 0f;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(Mathf.Lerp(0f, 7f, k), Mathf.Lerp(0f, 11f, k));
            vel.y = new ParticleSystem.MinMaxCurve(Mathf.Lerp(0f, -2f, k), Mathf.Lerp(0f, -1f, k));
            vel.z = new ParticleSystem.MinMaxCurve(Mathf.Lerp(0f, 1f, k), Mathf.Lerp(0f, 3f, k));
        }

        if (k > 0f && !wind.isPlaying) wind.Play();
        wind.volume = k * 0.55f;
        if (k <= 0f && wind.isPlaying) wind.Stop();

        if (into > 0f && into < 2f && announcedCycle != cycle)
        {
            announcedCycle = cycle;
            if (FpsPlayer.Local != null && !FpsPlayer.Local.InLobby.Value)
                GameUI.ShowBanner("BLIZZARD", "Visibility is dropping. It blows over in about a minute.", new Color(0.75f, 0.88f, 1f));
        }
    }

    static AudioClip MakeWind()
    {
        const int rate = 44100;
        float len = 6f; // loops: the gusts are periodic over the clip length
        var d = new float[(int)(rate * len)];
        var rng = new System.Random(3);
        float a = 0f, b = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)rate;
            float n = (float)rng.NextDouble() * 2f - 1f;
            float gust = 0.55f + 0.45f * Mathf.Sin(t / len * Mathf.PI * 2f * 2f) * Mathf.Sin(t / len * Mathf.PI * 2f * 3f + 1f);
            float cut = Mathf.Lerp(0.01f, 0.06f, gust); // gusts are brighter
            a += (n - a) * cut;
            b += (a - b) * cut;
            d[i] = b * gust * 3.2f;
        }
        // Fade the seam.
        int fade = rate / 10;
        for (int i = 0; i < fade; i++)
        {
            float f = i / (float)fade;
            d[i] = d[i] * f + d[d.Length - fade + i] * (1f - f);
        }
        var clip = AudioClip.Create("wind", d.Length - fade, 1, rate, false);
        var trimmed = new float[d.Length - fade];
        System.Array.Copy(d, trimmed, trimmed.Length);
        clip.SetData(trimmed, 0);
        return clip;
    }
}
