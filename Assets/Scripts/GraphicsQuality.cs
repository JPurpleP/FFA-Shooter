using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

/// <summary>
/// The Graphics setting: Low (no post-processing, short shadows), Medium (color grading, bloom, vignette),
/// High (plus ambient occlusion and long shadows). Applied to every camera that renders the world.
/// </summary>
public static class GraphicsQuality
{
    public static readonly string[] Names = { "LOW", "MEDIUM", "HIGH" };

    /// <summary>Running in a browser: every draw call and pixel costs more, so the settings lean lighter.</summary>
    public static bool Web => Application.platform == RuntimePlatform.WebGLPlayer;

    public static void Apply()
    {
        int q = GameSettings.Graphics;
        QualitySettings.shadowDistance = ShadowDistance;
        QualitySettings.shadows = Web && q == 0 ? ShadowQuality.Disable : ShadowQuality.All;
        QualitySettings.shadowCascades = Web ? (q == 2 ? 2 : 1) : q == 0 ? 2 : 4;
        QualitySettings.shadowResolution = q == 2 && !Web ? ShadowResolution.VeryHigh : Web && q < 2 ? ShadowResolution.Medium : ShadowResolution.High;
        // Each per-pixel point light draws every object it touches again (forward rendering), so keep few:
        // the extra ones (muzzle flashes, explosions) fall back to cheap per-vertex lighting.
        QualitySettings.pixelLightCount = Web ? (q == 2 ? 1 : 0) : q == 0 ? 1 : q == 1 ? 2 : 3;
        if (Web) QualitySettings.antiAliasing = 0; // MSAA is expensive in WebGL; the low-poly look holds up without it
        foreach (var layer in Object.FindObjectsByType<PostProcessLayer>(FindObjectsInactive.Include))
            Configure(layer);
        foreach (var volume in Object.FindObjectsByType<PostProcessVolume>(FindObjectsInactive.Include))
        {
            if (volume.sharedProfile == null) continue;
            // Ambient occlusion: High only (and never in browsers: it needs compute shaders).
            if (volume.sharedProfile.TryGetSettings<AmbientOcclusion>(out var ao)) ao.active = q >= 2 && SystemInfo.supportsComputeShaders;
            // Bloom is several full-screen passes: browsers keep it for High only.
            if (volume.sharedProfile.TryGetSettings<Bloom>(out var bloom)) bloom.active = !Web || q >= 2;
        }
        // Browsers: don't draw much past where the fog has eaten everything anyway.
        if (Web)
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
                if (cam.farClipPlane > WebFarClip) cam.farClipPlane = WebFarClip;
    }

    const float WebFarClip = 320f;

    public static float ShadowDistance => Web
        ? (GameSettings.Graphics == 0 ? 0f : GameSettings.Graphics == 1 ? 45f : 80f)
        : GameSettings.Graphics == 0 ? 60f : GameSettings.Graphics == 1 ? 110f : 170f;
    /// <summary>How much rain / snow the storms spawn: lighter on Low and Medium.</summary>
    public static float StormParticles => GameSettings.Graphics == 0 ? 0.35f : GameSettings.Graphics == 1 ? 0.65f : 1f;

    static Camera clippedCam;
    static float clippedFar = -1f;

    /// <summary>
    /// During a storm the fog hides everything past its end, so stop drawing (and shadowing) what can't be seen:
    /// the shadow distance and the local camera's far plane come in to just past the fog. strength 0 puts them back.
    /// </summary>
    public static void StormVisibility(float strength)
    {
        float fogEnd = RenderSettings.fogEndDistance;
        QualitySettings.shadowDistance = strength > 0f ? Mathf.Min(ShadowDistance, fogEnd + 5f) : ShadowDistance;
        var cam = FpsPlayer.Local != null ? FpsPlayer.Local.playerCamera : null;
        if (cam != clippedCam)
        {
            if (clippedCam != null && clippedFar > 0f) clippedCam.farClipPlane = clippedFar;
            clippedCam = cam;
            clippedFar = cam != null ? cam.farClipPlane : -1f;
        }
        if (cam != null) cam.farClipPlane = strength > 0f ? Mathf.Min(clippedFar, fogEnd * 1.1f + 10f) : clippedFar;
    }

    /// <summary>Give a camera the shared post-processing setup (killcam cameras are made at runtime).</summary>
    public static void AddTo(Camera cam)
    {
        if (cam == null || cam.GetComponent<PostProcessLayer>() != null || GameAssets.I == null || GameAssets.I.postResources == null) return;
        var layer = cam.gameObject.AddComponent<PostProcessLayer>();
        layer.Init(GameAssets.I.postResources);
        layer.volumeLayer = 1 << GameAssets.PostFxLayer;
        layer.volumeTrigger = cam.transform;
        Configure(layer);
    }

    static void Configure(PostProcessLayer layer)
    {
        layer.enabled = GameSettings.Graphics > 0;
        layer.antialiasingMode = PostProcessLayer.Antialiasing.None; // MSAA already smooths edges
    }
}
