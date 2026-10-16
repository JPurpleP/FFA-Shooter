using UnityEngine;

/// <summary>
/// Scene-wide references to generated materials/prefabs used by runtime-built models and effects.
/// Filled in by ProjectBuilder; lives on the NetworkManager object.
/// </summary>
public class GameAssets : MonoBehaviour
{
    public static GameAssets I { get; private set; }

    [Header("Prefabs")]
    public GameObject pickupPrefab;
    public GameObject playerPrefab;
    public GameObject projectilePrefab;

    [Header("Weapons")]
    public Material gunMetal;
    public Material gunPolymer;
    public Material gunDark;
    public Material gunWood;
    public Material gunRubber;
    public Material brass;
    public Material gold;
    public Material olive;
    public Material shellRed;
    public Material scopeLens;
    public Material sightWhite;
    public Material glove;
    public Material sleeve;

    [Header("Bodies")]
    public Material playerBody;
    public Material skin;
    public Material visor;
    public Material mannequin;
    public Material freedomPrint;
    public Material rebelFlagPrint;
    public Material spiderEmblem; // parody armband roundel: white disc with a black spider
    public Material rainbowShirt;
    public Material polkaShirt;
    public Material plaidShirt;
    public Material sailorShirt;
    public Material hawaiianShirt;

    public Material ShirtPatternMaterial(ShirtPattern p) => p switch
    {
        ShirtPattern.Rainbow => rainbowShirt,
        ShirtPattern.Polka => polkaShirt,
        ShirtPattern.Plaid => plaidShirt,
        ShirtPattern.Sailor => sailorShirt,
        ShirtPattern.Hawaiian => hawaiianShirt,
        _ => null,
    };

    [Header("Effects")]
    public Material tracer;
    public Material tracerGlow;
    public Material flashGlow;
    public Material sparkGlow;
    public Material muzzleFlash;
    public Material bloodParticle;
    public Material bloodDecal;
    public Material bulletHole;
    public Material flesh;
    public Material gore;
    public Material bone;
    public Material dust;
    public Texture2D bloodSplatTexture;

    public static int PlayerLayer { get; private set; }
    public static int ViewModelLayer { get; private set; }
    public static int DebrisLayer { get; private set; }
    public static int HitboxLayer { get; private set; }
    public static int PostFxLayer { get; private set; }
    /// <summary>Shaders and lookup textures the post-processing stack needs (assigned by the builder).</summary>
    public UnityEngine.Rendering.PostProcessing.PostProcessResources postResources;
    /// <summary>What bullets can hit: world geometry and live players (not ragdolls, gibs or view models).</summary>
    public static int ShotMask { get; private set; }

    void Awake()
    {
        I = this;
        InitLayers();
        ApplyQuality();
        foreach (var probe in FindObjectsByType<ReflectionProbe>())
            probe.RenderProbe();
    }

    /// <summary>Crisper textures and shadows than the default quality preset.</summary>
    static void ApplyQuality()
    {
        QualitySettings.globalTextureMipmapLimit = 0;
        QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
        QualitySettings.antiAliasing = 4;
        QualitySettings.shadows = ShadowQuality.All;
        QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
        QualitySettings.shadowDistance = 110f;
        QualitySettings.shadowCascades = 4;
        QualitySettings.pixelLightCount = 6;
        QualitySettings.softParticles = true;
        QualitySettings.lodBias = 2f;
        QualitySettings.realtimeReflectionProbes = true;
    }

    public static void InitLayers()
    {
        PlayerLayer = LayerMask.NameToLayer("Player");
        ViewModelLayer = LayerMask.NameToLayer("ViewModel");
        DebrisLayer = LayerMask.NameToLayer("Debris");
        HitboxLayer = LayerMask.NameToLayer("Hitbox");
        PostFxLayer = Mathf.Max(0, LayerMask.NameToLayer("PostFX"));
        if (HitboxLayer < 0) HitboxLayer = 0;
        if (PlayerLayer < 0) PlayerLayer = 0;
        if (ViewModelLayer < 0) ViewModelLayer = 0;
        if (DebrisLayer < 0) DebrisLayer = 0;
        ShotMask = (1 << 0) | (1 << PlayerLayer) | (1 << HitboxLayer);
        if (HitboxLayer != 0)
            for (int i = 0; i < 32; i++)
                Physics.IgnoreLayerCollision(HitboxLayer, i, true); // only raycasts see hitboxes
        if (DebrisLayer != 0)
        {
            Physics.IgnoreLayerCollision(DebrisLayer, PlayerLayer, true);
            Physics.IgnoreLayerCollision(DebrisLayer, ViewModelLayer, true);
        }
    }

    public static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }
}
