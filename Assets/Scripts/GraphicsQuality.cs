using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

/// <summary>
/// The Graphics setting: Low (no post-processing, short shadows), Medium (color grading, bloom, vignette),
/// High (plus ambient occlusion and long shadows). Applied to every camera that renders the world.
/// </summary>
public static class GraphicsQuality
{
    public static readonly string[] Names = { "LOW", "MEDIUM", "HIGH" };

    public static void Apply()
    {
        int q = GameSettings.Graphics;
        QualitySettings.shadowDistance = q == 0 ? 60f : q == 1 ? 110f : 170f;
        QualitySettings.shadowCascades = q == 0 ? 2 : 4;
        QualitySettings.shadowResolution = q == 2 ? ShadowResolution.VeryHigh : ShadowResolution.High;
        // Each per-pixel point light draws every object it touches again (forward rendering), so keep few:
        // the extra ones (muzzle flashes, explosions) fall back to cheap per-vertex lighting.
        QualitySettings.pixelLightCount = q == 0 ? 1 : q == 1 ? 2 : 3;
        foreach (var layer in Object.FindObjectsByType<PostProcessLayer>(FindObjectsInactive.Include))
            Configure(layer);
        // Ambient occlusion lives in the map profiles: on only at High.
        foreach (var volume in Object.FindObjectsByType<PostProcessVolume>(FindObjectsInactive.Include))
            if (volume.sharedProfile != null && volume.sharedProfile.TryGetSettings<AmbientOcclusion>(out var ao))
                ao.active = q >= 2;
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
