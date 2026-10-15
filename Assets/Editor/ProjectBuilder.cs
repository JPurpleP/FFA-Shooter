using System;
using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// Generates the game's assets from code: textures, materials, the networked player and pickup prefabs,
/// and the maps (Fluketown and Camp Timberline). Menu: FFA > Rebuild Arena.
/// Batch mode: -executeMethod ProjectBuilder.RebuildAll / ProjectBuilder.BuildWindows
/// </summary>
public static partial class ProjectBuilder
{
    const string GeneratedDir = "Assets/Generated";
    const string ScenePath = "Assets/Scenes/Arena.unity";
    const string PlayerPrefabPath = GeneratedDir + "/Player.prefab";
    const string PickupPrefabPath = GeneratedDir + "/WeaponPickup.prefab";
    const string ProjectilePrefabPath = GeneratedDir + "/Projectile.prefab";

    static readonly Dictionary<string, Material> mats = new();

    [MenuItem("FFA/Rebuild Arena")]
    public static void RebuildAll()
    {
        Directory.CreateDirectory(GeneratedDir);
        Directory.CreateDirectory("Assets/Scenes");
        EnsureLayers();
        GameAssets.InitLayers();

        CreateTextures();
        CreateMaterials();
        var playerPrefab = CreatePlayerPrefab();
        var pickupPrefab = CreatePickupPrefab();
        var projectilePrefab = CreateProjectilePrefab();
        CreateScene(playerPrefab, pickupPrefab, projectilePrefab);

        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("[FFA] Arena rebuilt");
    }

    public static void BuildWindows()
    {
        RebuildAll();
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = "Builds/Windows/FFA-Shooter.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        });
        Debug.Log($"[FFA] Build result: {report.summary.result}, errors={report.summary.totalErrors}");
        if (Application.isBatchMode)
            EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }

    static void EnsureLayers()
    {
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tagManager.FindProperty("layers");
        void Set(int index, string name) => layers.GetArrayElementAtIndex(index).stringValue = name;
        Set(8, "Player");
        Set(9, "ViewModel");
        Set(10, "Debris");
        Set(11, "Hitbox");
        tagManager.ApplyModifiedPropertiesWithoutUndo();
    }

    // ================================================================== textures

    static readonly Dictionary<string, Texture2D> texs = new();

    static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / (float)uint.MaxValue;
        }
    }

    static float Fbm(float x, float y, float scale, int seed, int octaves = 4)
    {
        float v = 0f, amp = 0.5f, f = scale, norm = 0f;
        for (int o = 0; o < octaves; o++)
        {
            v += amp * Mathf.PerlinNoise(x * f + seed * 17.13f + o * 3.7f, y * f + seed * 9.71f + o * 5.3f);
            norm += amp;
            amp *= 0.5f;
            f *= 2f;
        }
        return v / norm;
    }

    static void CreateTextures()
    {
        Tex("Grid", 256, (x, y) => { bool l = x < 5 || y < 5 || x >= 251 || y >= 251; float v = l ? 0.72f : 1f; return new Color(v, v, v); });

        // World surfaces: patterns are laid out in a 512-unit design space and rendered at 1024 px,
        // with an extra fine-detail octave that only shows at the higher resolution.
        const int Hi = 1024;

        // Lapped wood siding: boards shade darker toward their lower edge where the next board overlaps.
        TexF("Siding", Hi, (x, y) =>
        {
            float b = Mod(y, 64f);
            float lap = b < 4f ? 0.55f : Mathf.Lerp(0.82f, 1f, b / 63f);
            float grain = 0.93f + 0.07f * Fbm(x * 0.15f, y * 1.5f, 0.06f, 1) + 0.04f * (Fbm(x * 0.6f, y * 6f, 0.1f, 31) - 0.5f);
            float v = lap * grain;
            return new Color(v, v, v);
        }, (x, y) => { float b = Mod(y, 64f); return b < 4f ? 0f : 0.3f + 0.7f * (b / 63f) + 0.05f * Fbm(x * 0.6f, y * 6f, 0.1f, 31); }, 3f);

        // Asphalt shingles: staggered tabs with per-tab tint and fine granules.
        TexF("Shingles", Hi, (x, y) =>
        {
            float row = Mathf.Floor(y / 48f), inRow = Mod(y, 48f);
            float tx = x + Mod(row, 2f) * 32f;
            bool gap = inRow < 3f || Mod(tx, 64f) < 3f;
            float tint = 0.8f + 0.2f * Hash((int)Mathf.Floor(tx / 64f), (int)row, 2);
            float granule = 0.84f + 0.16f * HashF(x, y, 3);
            float v = (gap ? 0.45f : tint * granule) * Mathf.Lerp(0.85f, 1f, inRow / 47f);
            return new Color(v, v, v);
        }, (x, y) =>
        {
            float row = Mathf.Floor(y / 48f), inRow = Mod(y, 48f);
            bool gap = inRow < 3f || Mod(x + Mod(row, 2f) * 32f, 64f) < 3f;
            return gap ? 0f : 0.4f + 0.6f * (inRow / 47f) + HashF(x, y, 3) * 0.12f;
        }, 4f);

        // Asphalt: aggregate speckles over blotchy wear, with hairline cracks.
        TexF("Asphalt", Hi, (x, y) =>
        {
            float v = 0.7f + 0.25f * Fbm(x, y, 0.012f, 4);
            float h = HashF(x, y, 5);
            if (h > 0.92f) v += 0.18f;
            else if (h < 0.07f) v -= 0.15f;
            if (Mathf.Abs(Fbm(x, y, 0.008f, 6, 3) - 0.5f) < 0.005f) v *= 0.55f;
            v += 0.05f * (Fbm(x, y, 0.15f, 41, 2) - 0.5f);
            return new Color(v, v, v);
        }, (x, y) => HashF(x, y, 5) * 0.6f + (Mathf.Abs(Fbm(x, y, 0.008f, 6, 3) - 0.5f) < 0.005f ? -0.6f : 0f), 2f);

        // Lawn: patchy color, blade streaks and a fine blade layer.
        TexF("Grass", Hi, (x, y) =>
        {
            float patch = Fbm(x, y, 0.01f, 7);
            float blades = Fbm(x * 0.6f, y * 2.2f, 0.25f, 8, 2);
            float fine = Fbm(x * 0.9f, y * 3.5f, 0.45f, 40, 2);
            float v = 0.65f + 0.25f * patch + 0.13f * (blades - 0.5f) + 0.1f * (fine - 0.5f) + 0.06f * (HashF(x, y, 9) - 0.5f);
            return new Color(v * Mathf.Lerp(0.85f, 1.05f, patch), v, v * 0.8f);
        }, (x, y) => Fbm(x * 0.6f, y * 2.2f, 0.25f, 8, 2) * 0.6f + Fbm(x * 0.9f, y * 3.5f, 0.45f, 40, 2) * 0.4f, 1.6f);

        TexF("Sand", Hi, (x, y) =>
        {
            float ripple = 0.5f + 0.5f * Mathf.Sin(y * 0.09f + Fbm(x, y, 0.01f, 10) * 8f);
            float v = 0.78f + 0.12f * Fbm(x, y, 0.02f, 11) + 0.06f * ripple + 0.05f * (HashF(x, y, 12) - 0.5f);
            return new Color(v, v * 0.97f, v * 0.92f);
        }, (x, y) => Mathf.Sin(y * 0.09f + Fbm(x, y, 0.01f, 10) * 8f) * 0.5f + HashF(x, y, 12) * 0.2f, 1.5f);

        // Concrete slabs with expansion joints, stains, pores and fine aggregate.
        TexF("Concrete", Hi, (x, y) =>
        {
            bool joint = Mod(x, 256f) < 4f || Mod(y, 256f) < 4f;
            float stain = 0.85f + 0.15f * Fbm(x, y, 0.015f, 13);
            float pore = HashF(x, y, 14) > 0.97f ? 0.8f : 1f;
            float v = (joint ? 0.6f : 0.92f) * stain * pore * (0.97f + 0.06f * Fbm(x, y, 0.2f, 42, 2));
            return new Color(v, v, v * 0.98f);
        }, (x, y) => (Mod(x, 256f) < 4f || Mod(y, 256f) < 4f) ? 0f : 0.6f + HashF(x, y, 14) * 0.15f, 3f);

        // Vertical fence planks with grain and knots.
        TexF("Planks", Hi, (x, y) =>
        {
            int board = (int)Mathf.Floor(x / 64f);
            float bx = Mod(x, 64f);
            bool gap = bx < 4f;
            float grain = 0.8f + 0.2f * Fbm(x * 3f, y * 0.15f, 0.05f, 15 + board) + 0.04f * (Fbm(x * 8f, y * 0.4f, 0.1f, 43) - 0.5f);
            float knot = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(bx, Mod(y, 256f)), new Vector2(32f, 40f + Hash(board, 0, 16) * 170f)) / 9f);
            float v = (gap ? 0.35f : grain * (0.9f + 0.1f * Hash(board, 1, 17))) * (1f - knot * 0.45f);
            return new Color(v, v, v);
        }, (x, y) => Mod(x, 64f) < 4f ? 0f : 0.7f + 0.3f * Fbm(x * 3f, y * 0.15f, 0.05f, 15 + (int)Mathf.Floor(x / 64f)), 3f);

        // Hardwood floorboards: horizontal strips with staggered ends.
        TexF("Floorboards", Hi, (x, y) =>
        {
            float row = Mathf.Floor(y / 48f);
            float run = x + row * 97f;
            bool gap = Mod(y, 48f) < 2f || Mod(run, 192f) < 2f;
            float tone = 0.8f + 0.2f * Hash((int)Mathf.Floor(run / 192f), (int)row, 18);
            float grain = 0.88f + 0.12f * Fbm(x * 0.12f, y * 2f, 0.05f, 19) + 0.03f * (Fbm(x * 0.5f, y * 6f, 0.1f, 44) - 0.5f);
            float v = gap ? 0.4f : tone * grain;
            return new Color(v, v, v);
        }, (x, y) => (Mod(y, 48f) < 2f || Mod(x + Mathf.Floor(y / 48f) * 97f, 192f) < 2f) ? 0f : 0.8f, 2.5f);

        // Small-surface detail for weapons and gear (tile once per face).
        TexF("Brushed", 512, (x, y) =>
        {
            float v = 0.95f + 0.05f * Fbm(x * 0.02f, y * 0.6f, 0.25f, 50, 3); // subtle brushed sheen
            return new Color(v, v, v);
        }, (x, y) => Fbm(x * 0.02f, y * 0.6f, 0.25f, 50, 3), 0.35f, design: 512);
        TexF("Stipple", 512, (x, y) =>
        {
            float v = 0.85f + 0.15f * Fbm(x, y, 0.3f, 51, 2);
            return new Color(v, v, v);
        }, (x, y) => Fbm(x, y, 0.3f, 51, 2), 2.5f, design: 512);
        TexF("Fabric", 512, (x, y) =>
        {
            float weave = 0.5f + 0.25f * Mathf.Sin(x * 0.9f) + 0.25f * Mathf.Sin(y * 0.9f + Mathf.Floor(x / 7f) * 1.5f);
            float v = 0.85f + 0.15f * weave;
            return new Color(v, v, v);
        }, (x, y) => 0.5f + 0.25f * Mathf.Sin(x * 0.9f) + 0.25f * Mathf.Sin(y * 0.9f + Mathf.Floor(x / 7f) * 1.5f), 2f, design: 512);

        // "FREEDOM" chest print: bold geometric capitals with wide letter spacing, black on transparent.
        Tex("FreedomPrint", 1024, (x, y) =>
        {
            const int H = 160; // the text band is the bottom 160 rows; the material's UV scale shows only that
            if (y >= H) return new Color(0f, 0f, 0f, 0f);
            float cover = 0f;
            for (int sy = 0; sy < 2; sy++)
                for (int sx = 0; sx < 2; sx++)
                    if (WordGlyphAt(FreedomLetters, 0.62f, x + 0.25f + sx * 0.5f, y + 0.25f + sy * 0.5f)) cover += 0.25f;
            return new Color(0.04f, 0.04f, 0.04f, cover);
        }, alpha: true, repeat: false);

        // "CAMP TIMBERLINE" gate lettering: cream on dark wood, smaller letters to fit the longer name.
        Tex("CampSignText", 1024, (x, y) =>
        {
            if (y >= 160) return new Color(0f, 0f, 0f, 0f);
            float cover = 0f;
            for (int sy = 0; sy < 2; sy++)
                for (int sx = 0; sx < 2; sx++)
                    if (WordGlyphAt(CampLetters, 0.2f, x + 0.25f + sx * 0.5f, y + 0.25f + sy * 0.5f, 70f)) cover += 0.25f;
            return new Color(0.96f, 0.88f, 0.66f, cover);
        }, alpha: true, repeat: false);

        // Forest floor: grass with patches of needles and leaf litter.
        TexF("ForestFloor", Hi, (x, y) =>
        {
            float patch = Fbm(x, y, 0.008f, 60);
            float litter = Fbm(x * 1.3f, y * 0.4f, 0.2f, 61, 2);
            float v = 0.6f + 0.3f * patch + 0.12f * (litter - 0.5f) + 0.06f * (HashF(x, y, 62) - 0.5f);
            float brown = Mathf.Clamp01((Fbm(x, y, 0.02f, 63) - 0.45f) * 3f);
            return Color.Lerp(new Color(v * 0.9f, v, v * 0.75f), new Color(v * 1.1f, v * 0.85f, v * 0.55f), brown * 0.75f);
        }, (x, y) => Fbm(x * 1.3f, y * 0.4f, 0.2f, 61, 2) * 0.6f + HashF(x, y, 62) * 0.2f, 1.5f);
        // Packed dirt with pebbles.
        TexF("Dirt", Hi, (x, y) =>
        {
            float v = 0.72f + 0.2f * Fbm(x, y, 0.015f, 64) + 0.08f * (Fbm(x, y, 0.12f, 65, 2) - 0.5f);
            if (Fbm(x, y, 0.09f, 67, 2) > 0.68f) v += 0.12f;
            if (HashF(x, y, 66) > 0.96f) v -= 0.12f;
            return new Color(v, v * 0.9f, v * 0.78f);
        }, (x, y) => (Fbm(x, y, 0.09f, 67, 2) > 0.68f ? 0.8f : 0.3f) + Fbm(x, y, 0.12f, 65, 2) * 0.3f, 2f);
        // Bark: wavy vertical ridges.
        TexF("Bark", Hi, (x, y) =>
        {
            float ridge = Mathf.Abs(Mathf.Sin((x + Fbm(x * 0.3f, y * 0.05f, 0.05f, 68) * 60f) * 0.09817f));
            float v = 0.5f + 0.45f * ridge + 0.08f * (HashF(x, y, 69) - 0.5f);
            return new Color(v, v, v);
        }, (x, y) => Mathf.Abs(Mathf.Sin((x + Fbm(x * 0.3f, y * 0.05f, 0.05f, 68) * 60f) * 0.09817f)), 4f);
        // Granite with cracks and lichen.
        TexF("Rock", Hi, (x, y) =>
        {
            float v = 0.62f + 0.3f * Fbm(x, y, 0.02f, 70) + 0.1f * (Fbm(x, y, 0.15f, 71, 2) - 0.5f);
            if (Mathf.Abs(Fbm(x, y, 0.01f, 72, 3) - 0.5f) < 0.006f) v *= 0.6f;
            float lichen = Mathf.Clamp01((Fbm(x, y, 0.03f, 73) - 0.62f) * 5f);
            return Color.Lerp(new Color(v, v, v * 0.97f), new Color(0.62f, 0.66f, 0.42f), lichen * 0.6f);
        }, (x, y) => Fbm(x, y, 0.02f, 70) * 0.7f + Fbm(x, y, 0.15f, 71, 2) * 0.3f, 3f);
        // Stacked log walls: rounded logs with dark chinking between them.
        TexF("Logs", Hi, (x, y) =>
        {
            float b = Mod(y, 64f) / 64f;
            float grain = 0.88f + 0.12f * Fbm(x * 0.12f, y * 1.5f, 0.06f, 74);
            float v = (b < 0.06f || b > 0.94f ? 0.35f : 0.55f + 0.45f * Mathf.Sin(b * Mathf.PI)) * grain;
            return new Color(v, v, v);
        }, (x, y) => Mathf.Sin(Mod(y, 64f) / 64f * Mathf.PI), 5f);

        // Downtown: plain brick, building facades (one 3 m x 3.5 m window bay per tile) and floor tiles.
        TexF("Brick", Hi, (x, y) =>
        {
            float row = Mathf.Floor(y / 32f), bx = x + Mod(row, 2f) * 48f;
            if (Mod(y, 32f) < 4f || Mod(bx, 96f) < 4f) return new Color(0.72f, 0.7f, 0.66f);
            float tint = (0.85f + 0.3f * Hash((int)Mathf.Floor(bx / 96f), (int)row, 80)) * (0.92f + 0.08f * Fbm(x, y, 0.2f, 81, 2));
            return new Color(0.62f * tint, 0.3f * tint, 0.22f * tint);
        }, (x, y) => (Mod(y, 32f) < 4f || Mod(x + Mod(Mathf.Floor(y / 32f), 2f) * 48f, 96f) < 4f) ? 0f : 0.7f + 0.2f * Fbm(x, y, 0.2f, 81, 2), 3f);
        TexF("FacadeBrick", Hi, (x, y) =>
        {
            int k = FacadeBrickKind(x, y);
            if (k == 0) { float r = 0.12f + 0.2f * Mathf.Clamp01((y - 110f) / 290f) + 0.06f * Fbm(x, y, 0.01f, 82); return new Color(r * 0.8f, r * 0.95f, r * 1.25f); }
            if (k == 1 || k == 2) return new Color(0.88f, 0.86f, 0.8f);
            if (k == 3) return new Color(0.7f, 0.68f, 0.64f);
            float row = Mathf.Floor(y / 18f), bx = x + Mod(row, 2f) * 32f;
            float tint = 0.85f + 0.3f * Hash((int)Mathf.Floor(bx / 64f), (int)row, 83);
            return new Color(0.6f * tint, 0.29f * tint, 0.21f * tint);
        }, (x, y) => FacadeBrickKind(x, y) switch { 0 => 0f, 1 => 0.3f, 2 => 1f, 3 => 0.4f, _ => 0.6f }, 3f);
        TexF("FacadeConcrete", Hi, (x, y) =>
        {
            bool band = y > 140f && y < 390f && x > 24f && x < 488f;
            if (band)
            {
                if (Mathf.Abs(x - 256f) < 5f || x < 32f || x > 480f || y < 148f || y > 382f) return new Color(0.3f, 0.32f, 0.34f);
                float r = 0.14f + 0.2f * Mathf.Clamp01((y - 140f) / 250f) + 0.05f * Fbm(x, y, 0.01f, 84);
                return new Color(r * 0.85f, r, r * 1.15f);
            }
            float v = 0.78f + 0.1f * Fbm(x, y, 0.02f, 85) + (Mod(x, 256f) < 3f || y < 4f ? -0.2f : 0f);
            return new Color(v, v * 0.97f, v * 0.9f);
        }, (x, y) => y > 140f && y < 390f && x > 24f && x < 488f ? 0f : 0.6f + 0.1f * Fbm(x, y, 0.02f, 85), 3f);
        TexF("FacadeGlass", Hi, (x, y) =>
        {
            if (Mod(x, 128f) < 6f || Mathf.Abs(y - 70f) < 4f) return new Color(0.55f, 0.58f, 0.6f);
            if (y < 70f) return new Color(0.2f, 0.24f, 0.28f);
            float r = 0.2f + 0.25f * Mathf.Clamp01((y - 70f) / 440f) + 0.08f * Fbm(x * 0.5f, y, 0.01f, 86);
            return new Color(r * 0.7f, r * 1.05f, r * 1.25f);
        }, (x, y) => Mod(x, 128f) < 6f || Mathf.Abs(y - 70f) < 4f ? 1f : 0f, 2f);
        TexF("Tiles", Hi, (x, y) =>
        {
            if (Mod(x, 64f) < 3f || Mod(y, 64f) < 3f) return new Color(0.55f, 0.55f, 0.53f);
            float v = 0.85f + 0.1f * Hash((int)Mathf.Floor(x / 64f), (int)Mathf.Floor(y / 64f), 87) + 0.04f * Fbm(x, y, 0.1f, 88, 2);
            return new Color(v, v, v * 0.96f);
        }, (x, y) => Mod(x, 64f) < 3f || Mod(y, 64f) < 3f ? 0f : 0.6f, 2f);

        // Confederate battle flag (chest print): red field, white-edged blue saltire with 13 stars. Aspect 1.6 : 1.
        {
            const float aspect = 1.6f;
            float halfDiag = Mathf.Sqrt(aspect * aspect + 1f) / 2f;
            var stars = new List<Vector2> { new Vector2(aspect / 2f, 0.5f) };
            foreach (var dir in new[] { new Vector2(aspect, 1f).normalized, new Vector2(aspect, -1f).normalized })
                foreach (float sgn in new[] { 1f, -1f })
                    foreach (float t in new[] { 0.24f, 0.48f, 0.72f })
                        stars.Add(new Vector2(aspect / 2f, 0.5f) + dir * sgn * t * halfDiag);
            Tex("RebelFlag", 512, (px, py) =>
            {
                float x = (px + 0.5f) / 512f * aspect, y = (py + 0.5f) / 512f;
                float norm = Mathf.Sqrt(aspect * aspect + 1f);
                float d = Mathf.Min(Mathf.Abs(aspect * y - x), Mathf.Abs(aspect * (1f - y) - x)) / norm;
                foreach (var st in stars)
                {
                    var o = new Vector2(x, y) - st;
                    float ang = Mathf.Atan2(o.y, o.x) + Mathf.PI / 2f;
                    float lobe = 0.022f * (0.45f + 0.55f * Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 2.5f)), 2.5f));
                    if (o.magnitude < lobe) return Color.white;
                }
                if (d < 0.072f) return new Color(0.1f, 0.16f, 0.45f);
                if (d < 0.09f) return new Color(0.96f, 0.96f, 0.94f);
                return new Color(0.75f, 0.1f, 0.13f);
            });
        }

        // Parody armband roundel: white disc, black spider (body, head, eight bent legs), transparent outside the disc.
        {
            float Seg(Vector2 p, Vector2 a, Vector2 b)
            {
                var ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                return (a + ab * t - p).magnitude;
            }
            Tex("SpiderEmblem", 256, (px, py) =>
            {
                var p = new Vector2((px + 0.5f) / 256f, (py + 0.5f) / 256f);
                float r = (p - new Vector2(0.5f, 0.5f)).magnitude;
                if (r > 0.47f) return new Color(0f, 0f, 0f, 0f);
                var black = new Color(0.04f, 0.04f, 0.04f, 1f);
                var abdomen = new Vector2((p.x - 0.5f) / 0.1f, (p.y - 0.4f) / 0.13f);
                if (abdomen.sqrMagnitude < 1f) return black;
                if ((p - new Vector2(0.5f, 0.585f)).magnitude < 0.068f) return black;
                if ((p - new Vector2(0.5f, 0.665f)).magnitude < 0.03f) return black; // head
                foreach (float side in new[] { -1f, 1f })
                    for (int i = 0; i < 4; i++)
                    {
                        // Legs fan out from the front body: up-forward pairs first, back pairs sweeping down.
                        var hip = new Vector2(0.5f + side * 0.045f, 0.62f - i * 0.03f);
                        var knee = new Vector2(0.5f + side * (0.2f + i * 0.015f), 0.72f - i * 0.11f);
                        var foot = new Vector2(0.5f + side * (0.28f + i * 0.02f), 0.8f - i * 0.2f);
                        if (Seg(p, hip, knee) < 0.016f || Seg(p, knee, foot) < 0.013f) return black;
                    }
                return Color.white;
            }, alpha: true, repeat: false);
        }

        // "FLUKETOWN" welcome-sign lettering, same band layout.
        Tex("SignText", 1024, (x, y) =>
        {
            if (y >= 160) return new Color(0f, 0f, 0f, 0f);
            float cover = 0f;
            for (int sy = 0; sy < 2; sy++)
                for (int sx = 0; sx < 2; sx++)
                    if (WordGlyphAt(FluketownLetters, 0.24f, x + 0.25f + sx * 0.5f, y + 0.25f + sy * 0.5f)) cover += 0.25f;
            return new Color(0.1f, 0.22f, 0.14f, cover);
        }, alpha: true, repeat: false);

        // Tracer streak: soft across its width, fading in from the tail (u = 0) to a hot head (u = 1).
        Tex("TracerStreak", 256, (x, y) =>
        {
            float u = x / 255f, v = (y - 15.5f) / 15.5f;
            if (y >= 32) return new Color(0f, 0f, 0f, 0f);
            float across = Mathf.Exp(-v * v * 5f);
            float along = Mathf.Pow(u, 1.6f);
            float core = Mathf.Exp(-v * v * 30f) * Mathf.Pow(u, 4f);
            return new Color(1f, 1f, 1f, Mathf.Clamp01(across * along * 0.8f + core));
        }, alpha: true, repeat: false);
        // Muzzle flash burst: hot core with irregular spikes.
        var flashRng = new System.Random(5);
        var spikes = new float[9];
        for (int i = 0; i < spikes.Length; i++) spikes[i] = 0.5f + (float)flashRng.NextDouble() * 0.5f;
        Tex("FlashBurst", 128, (x, y) =>
        {
            float dx = (x - 63.5f) / 63.5f, dy = (y - 63.5f) / 63.5f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float ang = Mathf.Atan2(dy, dx);
            float k = (ang / (Mathf.PI * 2f) + 0.5f) * spikes.Length;
            float reach = Mathf.Lerp(spikes[(int)k % spikes.Length], spikes[((int)k + 1) % spikes.Length], k - Mathf.Floor(k));
            float spike = Mathf.Pow(Mathf.Max(0f, Mathf.Cos(ang * spikes.Length * 0.5f)), 6f);
            float shape = Mathf.Clamp01(1f - r / (0.35f + 0.6f * reach * (0.4f + 0.6f * spike)));
            float core = Mathf.Clamp01(1f - r * 3.2f);
            return new Color(1f, 1f, 1f, Mathf.Clamp01(shape * shape * 0.9f + core));
        }, alpha: true, repeat: false);

        // Rainbow shirt fabric: six horizontal bands with a fine knit texture.
        var rainbow = new[] { new Color(0.88f, 0.14f, 0.14f), new Color(1f, 0.52f, 0.08f), new Color(1f, 0.88f, 0.12f),
                              new Color(0.18f, 0.7f, 0.25f), new Color(0.16f, 0.42f, 0.92f), new Color(0.52f, 0.22f, 0.78f) };
        Tex("RainbowStripes", 256, (x, y) =>
        {
            var c = rainbow[Mathf.Clamp(5 - y * 6 / 256, 0, 5)];
            float knit = 0.92f + 0.08f * Mathf.Abs(Mathf.Sin(x * 0.8f) * Mathf.Sin(y * 0.8f));
            return new Color(c.r * knit, c.g * knit, c.b * knit);
        });

        // Costume fabrics.
        var dotColors = new[] { new Color(0.9f, 0.15f, 0.15f), new Color(0.15f, 0.35f, 0.9f), new Color(0.15f, 0.7f, 0.25f), new Color(1f, 0.8f, 0.1f) };
        Tex("PolkaDots", 256, (x, y) =>
        {
            int row = y / 43, colIdx = (x + (row % 2) * 21) / 43;
            float cx = colIdx * 43 + 21 - (row % 2) * 21, cy = row * 43 + 21;
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
            return d < 11f ? dotColors[(row * 3 + colIdx) % dotColors.Length] : new Color(0.97f, 0.96f, 0.93f);
        });
        Tex("Plaid", 256, (x, y) =>
        {
            bool bx = x % 64 < 22, by = y % 64 < 22;
            bool line = x % 64 == 40 || y % 64 == 40;
            var c = new Color(0.78f, 0.12f, 0.1f);
            if (bx && by) c = new Color(0.12f, 0.06f, 0.06f);
            else if (bx || by) c = new Color(0.42f, 0.08f, 0.07f);
            if (line) c = Color.Lerp(c, new Color(0.95f, 0.85f, 0.3f), 0.6f);
            return c;
        });
        Tex("SailorStripes", 256, (x, y) => (y % 32) < 14 ? new Color(0.1f, 0.15f, 0.42f) : new Color(0.96f, 0.96f, 0.94f));
        var flowerRng = new System.Random(77);
        var flowers = new List<(Vector2 p, float r, Color c)>();
        for (int i = 0; i < 18; i++)
            flowers.Add((new Vector2((float)flowerRng.NextDouble() * 256f, (float)flowerRng.NextDouble() * 256f), 12f + (float)flowerRng.NextDouble() * 10f,
                flowerRng.NextDouble() < 0.5 ? new Color(0.98f, 0.35f, 0.55f) : new Color(0.98f, 0.97f, 0.9f)));
        Tex("Hawaiian", 256, (x, y) =>
        {
            var c = new Color(0.08f, 0.55f, 0.6f);
            // Leaves
            float leaf = Mathf.PerlinNoise(x * 0.04f, y * 0.04f);
            if (leaf > 0.62f) c = new Color(0.1f, 0.42f, 0.18f);
            foreach (var f in flowers)
            {
                for (int ox = -256; ox <= 256; ox += 256)
                    for (int oy = -256; oy <= 256; oy += 256)
                    {
                        var dv = new Vector2(x + ox, y + oy) - f.p;
                        float ang = Mathf.Atan2(dv.y, dv.x);
                        float petal = f.r * (0.6f + 0.4f * Mathf.Abs(Mathf.Cos(ang * 2.5f)));
                        float dist = dv.magnitude;
                        if (dist < f.r * 0.22f) return new Color(1f, 0.85f, 0.2f);
                        if (dist < petal) c = f.c;
                    }
            }
            return c;
        });

        Tex("SoftDot", 64, (x, y) =>
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 32f;
            return new Color(1f, 1f, 1f, Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d));
        }, alpha: true);

        // Blood splat: a main blob, satellite droplets and streaks, colored dark red with alpha edges.
        var rng = new System.Random(42);
        var blobs = new List<(Vector2 c, float r)> { (new Vector2(0.5f, 0.5f), 0.27f) };
        for (int i = 0; i < 26; i++)
        {
            float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
            float dist = 0.18f + (float)rng.NextDouble() * 0.3f;
            blobs.Add((new Vector2(0.5f, 0.5f) + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * dist, 0.012f + (float)rng.NextDouble() * 0.06f));
        }
        Tex("BloodSplat", 512, (x, y) =>
        {
            var p = new Vector2(x / 511f, y / 511f);
            float field = 0f;
            foreach (var b in blobs) field += b.r * b.r / Mathf.Max(0.0001f, (p - b.c).sqrMagnitude);
            field *= 0.85f + 0.3f * Mathf.PerlinNoise(x * 0.025f, y * 0.025f);
            float a = Mathf.Clamp01((field - 0.9f) * 3f);
            float shade = 0.7f + 0.3f * Fbm(x, y, 0.02f, 20);
            return new Color(0.42f * shade, 0.01f, 0.01f, a);
        }, alpha: true, repeat: false);
        Tex("BulletHole", 128, (x, y) =>
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(63.5f, 63.5f)) / 64f;
            float a = d < 0.3f ? 1f : Mathf.Clamp01((0.9f - d) * 2f) * (0.4f + 0.3f * Hash(x, y, 21));
            float v = d < 0.3f ? 0.03f : 0.22f;
            return new Color(v, v, v, a);
        }, alpha: true, repeat: false);
    }

    // Letter shapes in em units (height 1, y up), stroke width T.
    /// <summary>FacadeBrick layout: 0 glass, 1 mullion, 2 window frame, 3 mortar, 4 brick.</summary>
    static int FacadeBrickKind(float x, float y)
    {
        bool win = x > 150f && x < 362f && y > 110f && y < 400f;
        if (win) return Mathf.Abs(x - 256f) < 4f || Mathf.Abs(y - 300f) < 4f ? 1 : 0;
        if (x > 140f && x < 372f && y > 98f && y < 410f) return 2;
        float row = Mathf.Floor(y / 18f), bx = x + Mod(row, 2f) * 32f;
        return Mod(y, 18f) < 2.5f || Mod(bx, 64f) < 2.5f ? 3 : 4;
    }

    const float GlyphStroke = 0.19f;
    static readonly (char c, float w)[] FreedomLetters = { ('F', 0.66f), ('R', 0.7f), ('E', 0.66f), ('E', 0.66f), ('D', 0.74f), ('O', 0.86f), ('M', 0.86f) };
    static readonly (char c, float w)[] CampLetters =
    {
        ('C', 0.74f), ('A', 0.8f), ('M', 0.86f), ('P', 0.66f), (' ', 0.3f),
        ('T', 0.7f), ('I', 0.3f), ('M', 0.86f), ('B', 0.68f), ('E', 0.66f), ('R', 0.7f), ('L', 0.6f), ('I', 0.3f), ('N', 0.74f), ('E', 0.66f),
    };
    static readonly (char c, float w)[] FluketownLetters = { ('F', 0.66f), ('L', 0.6f), ('U', 0.74f), ('K', 0.7f), ('E', 0.66f), ('T', 0.7f), ('O', 0.86f), ('W', 1.02f), ('N', 0.74f) };

    /// <summary>Is pixel (px, py) of a 1024x160 text band inside the given word (centered, 100px letters)?</summary>
    static bool WordGlyphAt((char c, float w)[] letters, float tracking, float px, float py, float em = 100f)
    {
        float total = -tracking;
        foreach (var l in letters) total += l.w + tracking;
        float x = (px - (1024f - total * em) * 0.5f) / em;
        float y = (py - (160f - em) * 0.5f) / em;
        float pen = 0f;
        foreach (var l in letters)
        {
            if (x >= pen && x <= pen + l.w)
                return Glyph(l.c, x - pen, y, l.w);
            pen += l.w + tracking;
        }
        return false;
    }

    static bool Glyph(char c, float x, float y, float w)
    {
        const float T = GlyphStroke;
        if (y < 0f || y > 1f) return false;
        bool Rect(float x0, float y0, float x1, float y1) => x >= x0 && x <= x1 && y >= y0 && y <= y1;
        bool Seg(float ax, float ay, float bx, float by)
        {
            var p = new Vector2(x, y); var a = new Vector2(ax, ay); var b = new Vector2(bx, by);
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (a + ab * t - p).magnitude <= T * 0.5f;
        }
        bool Ring(float cx, float cy, float rx, float ry, bool rightHalfOnly)
        {
            if (rightHalfOnly && x < cx) return false;
            float dx = (x - cx) / rx, dy = (y - cy) / ry;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            return Mathf.Abs(d - 1f) * Mathf.Min(rx, ry) <= T * 0.5f;
        }
        switch (c)
        {
            case 'F': return Rect(0, 0, T, 1) || Rect(0, 1 - T, w, 1) || Rect(0, 0.45f, w * 0.85f, 0.45f + T);
            case 'E': return Rect(0, 0, T, 1) || Rect(0, 1 - T, w, 1) || Rect(0, 0.42f, w * 0.88f, 0.42f + T) || Rect(0, 0, w, T);
            case 'D':
            {
                float cx = 0.3f;
                return Rect(0, 0, T, 1) || Rect(0, 1 - T, cx, 1) || Rect(0, 0, cx, T) || Ring(cx, 0.5f, w - cx - T * 0.5f, 0.5f - T * 0.5f, true);
            }
            case 'O': return Ring(w * 0.5f, 0.5f, w * 0.5f - T * 0.5f, 0.5f - T * 0.5f, false);
            case 'R':
            {
                float cx = 0.36f, bot = 0.44f;
                float cy = (1f + bot) * 0.5f, ry = (1f - bot) * 0.5f - T * 0.5f;
                return Rect(0, 0, T, 1) || Rect(0, 1 - T, cx, 1) || Rect(0, bot, cx, bot + T) || Ring(cx, cy, w - cx - T * 0.5f, ry, true)
                       || Seg(0.34f, bot + T * 0.3f, w - T * 0.4f, T * 0.4f);
            }
            case 'I': return Rect((w - T) * 0.5f, 0, (w + T) * 0.5f, 1);
            case 'T': return Rect(0, 1 - T, w, 1) || Rect((w - T) * 0.5f, 0, (w + T) * 0.5f, 1);
            case 'K': return Rect(0, 0, T, 1) || Seg(T * 0.6f, 0.45f, w - T * 0.45f, 1 - T * 0.45f) || Seg(0.3f, 0.58f, w - T * 0.45f, T * 0.45f);
            case 'N': return Rect(0, 0, T, 1) || Rect(w - T, 0, w, 1) || Seg(T * 0.5f, 1 - T * 0.5f, w - T * 0.5f, T * 0.5f);
            case 'W': return Seg(T * 0.5f, 1 - T * 0.5f, w * 0.27f, T * 0.5f) || Seg(w * 0.27f, T * 0.5f, w * 0.5f, 0.72f)
                             || Seg(w * 0.5f, 0.72f, w * 0.73f, T * 0.5f) || Seg(w * 0.73f, T * 0.5f, w - T * 0.5f, 1 - T * 0.5f);
            case 'L': return Rect(0, 0, T, 1) || Rect(0, 0, w, T);
            case 'U': return Rect(0, 0.35f, T, 1) || Rect(w - T, 0.35f, w, 1)
                             || y <= 0.35f && Ring(w * 0.5f, 0.35f, w * 0.5f - T * 0.5f, 0.35f - T * 0.5f, false);
            case 'P':
            {
                float cx = 0.3f, bot = 0.42f;
                float cy = (1f + bot) * 0.5f, ry = (1f - bot) * 0.5f - T * 0.5f;
                return Rect(0, 0, T, 1) || Rect(0, 1 - T, cx, 1) || Rect(0, bot, cx, bot + T) || Ring(cx, cy, w - cx - T * 0.5f, ry, true);
            }
            case 'B':
            {
                float cx = 0.3f;
                return Rect(0, 0, T, 1) || Rect(0, 1 - T, cx, 1) || Rect(0, 0.5f - T * 0.5f, cx, 0.5f + T * 0.5f) || Rect(0, 0, cx, T)
                       || Ring(cx, 0.75f, (w - cx - T * 0.5f) * 0.85f, 0.25f - T * 0.25f, true) || Ring(cx, 0.25f, w - cx - T * 0.5f, 0.25f - T * 0.25f, true);
            }
            case 'C': return Ring(w * 0.5f, 0.5f, w * 0.5f - T * 0.5f, 0.5f - T * 0.5f, false) && !(x > w * 0.62f && y > 0.3f && y < 0.7f);
            case 'A': return Seg(T * 0.5f, T * 0.5f, w * 0.5f, 1 - T * 0.5f) || Seg(w * 0.5f, 1 - T * 0.5f, w - T * 0.5f, T * 0.5f) || Rect(w * 0.24f, 0.28f, w * 0.76f, 0.28f + T);
            case 'M': return Rect(0, 0, T, 1) || Rect(w - T, 0, w, 1) || Seg(T * 0.5f, 1 - T * 0.5f, w * 0.5f, 0.32f) || Seg(w - T * 0.5f, 1 - T * 0.5f, w * 0.5f, 0.32f);
        }
        return false;
    }

    static float Mod(float a, float m) => a - m * Mathf.Floor(a / m);

    /// <summary>Per-pixel hash for float design-space coordinates (fine enough for 1024 px renders).</summary>
    static float HashF(float x, float y, int seed) => Hash((int)Mathf.Floor(x * 4f), (int)Mathf.Floor(y * 4f), seed);

    /// <summary>
    /// Texture whose pattern is defined in a `design`-unit space but rendered at `size` px, so raising the
    /// resolution adds detail instead of shrinking the pattern.
    /// </summary>
    static void TexF(string name, int size, Func<float, float, Color> pixel, Func<float, float, float> height, float strength, int design = 512)
    {
        float k = design / (float)size;
        Tex(name, size, (x, y) => pixel(x * k, y * k), alpha: false, repeat: true,
            height: (x, y) => height(x * k, y * k), strength: strength / k);
    }

    /// <summary>Writes a generated texture (and, given a height function, a matching normal map) and imports it.</summary>
    static void Tex(string name, int size, Func<int, int, Color> pixel, bool alpha = false, bool repeat = true,
        Func<int, int, float> height = null, float strength = 2f)
    {
        string path = $"{GeneratedDir}/{name}.png";
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                px[y * size + x] = pixel(x, y);
        tex.SetPixels(px);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        ImportTexture(path, alpha, repeat, normal: false);
        texs[name] = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        if (height == null) return;
        var h = new float[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                h[y * size + x] = height(x, y);
        float H(int x, int y) => h[((y + size) % size) * size + (x + size) % size];
        var n = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var npx = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (H(x + 1, y) - H(x - 1, y)) * strength;
                float dy = (H(x, y + 1) - H(x, y - 1)) * strength;
                var v = new Vector3(-dx, -dy, 1f).normalized;
                npx[y * size + x] = new Color(v.x * 0.5f + 0.5f, v.y * 0.5f + 0.5f, v.z * 0.5f + 0.5f, 1f);
            }
        n.SetPixels(npx);
        string npath = $"{GeneratedDir}/{name}_N.png";
        File.WriteAllBytes(npath, n.EncodeToPNG());
        Object.DestroyImmediate(n);
        ImportTexture(npath, false, repeat, normal: true);
        texs[name + "_N"] = AssetDatabase.LoadAssetAtPath<Texture2D>(npath);
    }

    static void ImportTexture(string path, bool alpha, bool repeat, bool normal)
    {
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 16;
        importer.alphaIsTransparency = alpha;
        importer.mipmapEnabled = true;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.isReadable = false;
        importer.SaveAndReimport();
    }

    // ================================================================== materials

    static void CreateMaterials()
    {
        mats.Clear();
        // World
        Mat("Grass", new Color(0.42f, 0.62f, 0.3f), 0f, 0f, "Grass", new Vector2(24, 18));
        Mat("Sand", new Color(0.82f, 0.72f, 0.55f), 0f, 0f, "Sand", new Vector2(60, 60));
        Mat("Asphalt", new Color(0.3f, 0.3f, 0.32f), 0.05f, 0f, "Asphalt", new Vector2(3, 14));
        Mat("Concrete", new Color(0.78f, 0.77f, 0.74f), 0f, 0f, "Concrete", new Vector2(4, 4));
        Mat("SidingYellow", new Color(0.95f, 0.8f, 0.38f), 0f, 0f, "Siding", new Vector2(3, 1.5f));
        Mat("SidingGreen", new Color(0.52f, 0.74f, 0.58f), 0f, 0f, "Siding", new Vector2(3, 1.5f));
        Mat("SidingBlue", new Color(0.55f, 0.68f, 0.86f), 0f, 0f, "Siding", new Vector2(3, 1.5f));
        Mat("SidingPink", new Color(0.93f, 0.69f, 0.7f), 0f, 0f, "Siding", new Vector2(3, 1.5f));
        Mat("SidingWhite", new Color(0.92f, 0.92f, 0.88f), 0f, 0f, "Siding", new Vector2(3, 1.5f));
        Mat("Trim", new Color(0.95f, 0.95f, 0.93f), 0.1f, 0f, null, Vector2.one);
        Mat("Roof", new Color(0.38f, 0.32f, 0.3f), 0f, 0f, "Shingles", new Vector2(4, 4));
        Mat("RoofRed", new Color(0.6f, 0.28f, 0.22f), 0f, 0f, "Shingles", new Vector2(4, 4));
        Mat("Interior", new Color(0.92f, 0.9f, 0.84f), 0f, 0f, null, Vector2.one);
        Mat("Floorboards", new Color(0.62f, 0.45f, 0.3f), 0.1f, 0f, "Floorboards", new Vector2(3, 3));
        Mat("Fence", new Color(0.58f, 0.42f, 0.28f), 0f, 0f, "Planks", new Vector2(6, 1));
        Mat("FenceWhite", new Color(0.93f, 0.93f, 0.9f), 0f, 0f, "Planks", new Vector2(6, 1));
        Mat("Hedge", new Color(0.2f, 0.42f, 0.18f), 0f, 0f, "Grass", new Vector2(2, 2));
        Mat("Foliage", new Color(0.25f, 0.5f, 0.2f), 0f, 0f, "Grass", new Vector2(2, 2));
        Mat("Trunk", new Color(0.35f, 0.25f, 0.18f), 0f, 0f, "Planks", Vector2.one);
        Mat("Mesa", new Color(0.7f, 0.5f, 0.35f), 0f, 0f, "Sand", new Vector2(4, 4));
        Mat("Window", new Color(0.12f, 0.16f, 0.22f), 0.85f, 0f, null, Vector2.one);
        // Downtown (textures here are mapped in metres, so tiling stays 1)
        Mat("CityBrick", Color.white, 0f, 0f, "Brick", Vector2.one);
        Mat("CityConcrete", new Color(0.8f, 0.79f, 0.76f), 0f, 0f, "Concrete", Vector2.one);
        Mat("CityAsphalt", new Color(0.3f, 0.3f, 0.32f), 0.05f, 0f, "Asphalt", Vector2.one);
        Mat("CityRoof", new Color(0.32f, 0.32f, 0.34f), 0f, 0f, "Asphalt", Vector2.one);
        Mat("CityTiles", new Color(0.9f, 0.88f, 0.84f), 0.3f, 0f, "Tiles", Vector2.one);
        Mat("FacadeBrick", Color.white, 0.15f, 0f, "FacadeBrick", Vector2.one);
        Mat("FacadeConcrete", Color.white, 0.15f, 0f, "FacadeConcrete", Vector2.one);
        Mat("FacadeGlass", Color.white, 0.75f, 0.3f, "FacadeGlass", Vector2.one);
        Mat("ConstructionYellow", new Color(0.95f, 0.72f, 0.08f), 0.3f, 0.1f, null, Vector2.one);
        Mat("Plywood", new Color(0.8f, 0.64f, 0.44f), 0f, 0f, "Planks", new Vector2(3, 1));
        // Camp Timberline
        Mat("ForestFloor", new Color(0.44f, 0.55f, 0.3f), 0f, 0f, "ForestFloor", Vector2.one);
        Mat("Dirt", new Color(0.6f, 0.47f, 0.34f), 0f, 0f, "Dirt", Vector2.one);
        Mat("Bark", new Color(0.4f, 0.3f, 0.22f), 0f, 0f, "Bark", Vector2.one);
        Mat("PineNeedles", new Color(0.17f, 0.33f, 0.18f), 0f, 0f, "Grass", new Vector2(2, 2));
        Mat("Leaves", new Color(0.32f, 0.5f, 0.2f), 0f, 0f, "Grass", new Vector2(2, 2));
        Mat("Rock", new Color(0.6f, 0.6f, 0.58f), 0f, 0f, "Rock", Vector2.one);
        Mat("Water", new Color(0.07f, 0.17f, 0.19f), 0.95f, 0f, null, Vector2.one);
        Mat("CabinLogs", new Color(0.6f, 0.42f, 0.27f), 0f, 0f, "Logs", new Vector2(2, 1.6f));
        Mat("RoofGreen", new Color(0.3f, 0.42f, 0.3f), 0f, 0f, "Shingles", new Vector2(4, 4));
        Mat("TentOrange", new Color(0.95f, 0.5f, 0.12f), 0f, 0f, "Fabric", new Vector2(3, 3));
        Mat("TentBlue", new Color(0.2f, 0.4f, 0.8f), 0f, 0f, "Fabric", new Vector2(3, 3));
        Mat("TentGreen", new Color(0.3f, 0.55f, 0.25f), 0f, 0f, "Fabric", new Vector2(3, 3));
        Mat("Hay", new Color(0.86f, 0.73f, 0.42f), 0f, 0f, "Planks", new Vector2(3, 3));
        Mat("Charred", new Color(0.1f, 0.08f, 0.07f), 0f, 0f, "Bark", Vector2.one);
        var embers = Mat("Embers", new Color(0.3f, 0.08f, 0.02f), 0.2f, 0f, null, Vector2.one);
        embers.EnableKeyword("_EMISSION");
        embers.SetColor("_EmissionColor", new Color(2.4f, 0.75f, 0.15f));
        embers.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        Mat("Furniture", new Color(0.55f, 0.35f, 0.25f), 0.2f, 0f, null, Vector2.one);
        Mat("Fabric", new Color(0.5f, 0.55f, 0.7f), 0f, 0f, null, Vector2.one);
        Mat("Appliance", new Color(0.9f, 0.9f, 0.88f), 0.6f, 0f, null, Vector2.one);
        Mat("BusBlue", new Color(0.2f, 0.4f, 0.7f), 0.4f, 0.1f, null, Vector2.one);
        Mat("BusWhite", new Color(0.92f, 0.92f, 0.9f), 0.4f, 0.1f, null, Vector2.one);
        Mat("TruckRed", new Color(0.7f, 0.15f, 0.12f), 0.5f, 0.2f, null, Vector2.one);
        Mat("CarGreen", new Color(0.3f, 0.55f, 0.45f), 0.6f, 0.2f, null, Vector2.one);
        Mat("CarCream", new Color(0.92f, 0.85f, 0.65f), 0.6f, 0.2f, null, Vector2.one);
        Mat("Tire", new Color(0.08f, 0.08f, 0.08f), 0.1f, 0f, null, Vector2.one);
        Mat("Chrome", new Color(0.8f, 0.8f, 0.82f), 0.85f, 0.9f, "Brushed", Vector2.one);
        Mat("Metal", new Color(0.5f, 0.52f, 0.55f), 0.4f, 0.6f, "Brushed", new Vector2(2, 2));

        // Weapons
        Mat("GunMetal", new Color(0.14f, 0.14f, 0.15f), 0.55f, 0.7f, "Brushed", Vector2.one);
        Mat("GunPolymer", new Color(0.08f, 0.08f, 0.085f), 0.2f, 0f, "Stipple", Vector2.one);
        Mat("GunDark", new Color(0.02f, 0.02f, 0.02f), 0.1f, 0f, null, Vector2.one);
        Mat("GunWood", new Color(0.42f, 0.22f, 0.1f), 0.45f, 0f, "Planks", Vector2.one);
        Mat("GunRubber", new Color(0.06f, 0.06f, 0.06f), 0f, 0f, "Stipple", new Vector2(2, 2));
        Mat("Brass", new Color(0.85f, 0.65f, 0.25f), 0.75f, 1f, null, Vector2.one);
        Mat("Gold", new Color(1f, 0.8f, 0.36f), 0.86f, 1f, "Brushed", Vector2.one);
        Mat("Olive", new Color(0.3f, 0.33f, 0.2f), 0.25f, 0f, "Stipple", Vector2.one);
        Mat("RainbowShirt", Color.white, 0.05f, 0f, "RainbowStripes", Vector2.one);
        Mat("RebelFlagPrint", Color.white, 0.05f, 0f, "RebelFlag", Vector2.one);
        Mat("PolkaShirt", Color.white, 0.05f, 0f, "PolkaDots", Vector2.one);
        Mat("PlaidShirt", Color.white, 0f, 0f, "Plaid", Vector2.one);
        Mat("SailorShirt", Color.white, 0f, 0f, "SailorStripes", Vector2.one);
        Mat("HawaiianShirt", Color.white, 0.05f, 0f, "Hawaiian", Vector2.one);
        Mat("ShellRed", new Color(0.7f, 0.08f, 0.06f), 0.4f, 0f, null, Vector2.one);
        Mat("ScopeLens", new Color(0.05f, 0.1f, 0.2f), 0.95f, 0.3f, null, Vector2.one);
        Mat("SightWhite", new Color(1f, 1f, 1f), 0.2f, 0f, null, Vector2.one);
        Mat("Glove", new Color(0.47f, 0.38f, 0.27f), 0.1f, 0f, "Fabric", Vector2.one); // coyote tan, reads against black guns
        Mat("Sleeve", new Color(0.3f, 0.32f, 0.28f), 0f, 0f, null, Vector2.one);

        // Bodies & gore
        Mat("PlayerBody", Color.white, 0.1f, 0f, null, Vector2.one);
        Mat("Visor", new Color(0.05f, 0.08f, 0.1f), 0.95f, 0.5f, null, Vector2.one);
        Mat("Mannequin", new Color(0.88f, 0.78f, 0.62f), 0.35f, 0f, null, Vector2.one);
        Mat("Flesh", new Color(0.62f, 0.08f, 0.08f), 0.8f, 0f, null, Vector2.one);
        Mat("Gore", new Color(0.28f, 0.0f, 0.01f), 0.9f, 0f, null, Vector2.one);
        Mat("Bone", new Color(0.92f, 0.88f, 0.78f), 0.3f, 0f, null, Vector2.one);

        // Unlit / transparent effects
        Save(new Material(Shader.Find("Sprites/Default")), "Tracer");
        Save(new Material(Shader.Find("Sprites/Default")), "MuzzleFlash");
        // Additive glow for tracers, muzzle flame and sparks.
        var additive = Shader.Find("Legacy Shaders/Particles/Additive");
        var tracerGlow = new Material(additive) { mainTexture = texs["TracerStreak"], mainTextureScale = new Vector2(1f, 32f / 256f) };
        tracerGlow.SetColor("_TintColor", new Color(1f, 0.85f, 0.55f, 0.85f));
        Save(tracerGlow, "TracerGlow");
        var flashGlow = new Material(additive) { mainTexture = texs["FlashBurst"] };
        flashGlow.SetColor("_TintColor", new Color(1f, 0.75f, 0.4f, 0.75f));
        Save(flashGlow, "FlashGlow");
        var sparkGlow = new Material(additive) { mainTexture = texs["SoftDot"] };
        sparkGlow.SetColor("_TintColor", new Color(1f, 0.8f, 0.5f, 0.9f));
        Save(sparkGlow, "SparkGlow");
        var blood = new Material(Shader.Find("Sprites/Default")) { mainTexture = texs["SoftDot"] };
        Save(blood, "BloodParticle");
        var dust = new Material(Shader.Find("Sprites/Default")) { mainTexture = texs["SoftDot"] };
        Save(dust, "Dust");
        var decal = new Material(Shader.Find("Legacy Shaders/Transparent/Diffuse")) { mainTexture = texs["BloodSplat"], color = Color.white };
        Save(decal, "BloodDecal");
        var hole = new Material(Shader.Find("Legacy Shaders/Transparent/Diffuse")) { mainTexture = texs["BulletHole"], color = Color.white };
        Save(hole, "BulletHole");
        // Only the bottom 160/1024 rows of the print texture hold the text band.
        var print = new Material(Shader.Find("Legacy Shaders/Transparent/Cutout/Diffuse"))
        {
            mainTexture = texs["FreedomPrint"],
            color = Color.white,
            mainTextureScale = new Vector2(1f, 160f / 1024f),
        };
        print.SetFloat("_Cutoff", 0.4f);
        Save(print, "FreedomPrint");
        var signText = new Material(Shader.Find("Legacy Shaders/Transparent/Cutout/Diffuse"))
        {
            mainTexture = texs["SignText"],
            color = Color.white,
            mainTextureScale = new Vector2(1f, 160f / 1024f),
        };
        signText.SetFloat("_Cutoff", 0.4f);
        Save(signText, "SignText");
        var campSign = new Material(Shader.Find("Legacy Shaders/Transparent/Cutout/Diffuse"))
        {
            mainTexture = texs["CampSignText"],
            color = Color.white,
            mainTextureScale = new Vector2(1f, 160f / 1024f),
        };
        campSign.SetFloat("_Cutoff", 0.4f);
        Save(campSign, "CampSignText");
        var spider = new Material(Shader.Find("Legacy Shaders/Transparent/Cutout/Diffuse")) { mainTexture = texs["SpiderEmblem"], color = Color.white };
        spider.SetFloat("_Cutoff", 0.5f);
        Save(spider, "SpiderEmblem");
    }

    static Material Mat(string name, Color color, float gloss, float metallic, string tex, Vector2 tiling)
    {
        var mat = new Material(Shader.Find("Standard")) { color = color };
        if (tex != null)
        {
            mat.mainTexture = texs[tex];
            mat.mainTextureScale = tiling;
            if (texs.TryGetValue(tex + "_N", out var normal))
            {
                mat.SetTexture("_BumpMap", normal);
                mat.SetFloat("_BumpScale", 1f);
                mat.EnableKeyword("_NORMALMAP");
            }
        }
        mat.SetFloat("_Glossiness", gloss);
        mat.SetFloat("_Metallic", metallic);
        if (gloss <= 0.05f)
        {
            // Matte: no specular or sky reflections (they wash large surfaces out at grazing angles).
            mat.SetFloat("_SpecularHighlights", 0f);
            mat.SetFloat("_GlossyReflections", 0f);
            mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            mat.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
        }
        mat.enableInstancing = true;
        return Save(mat, name);
    }

    static Material Save(Material mat, string name)
    {
        string path = $"{GeneratedDir}/{name}.mat";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mat, path);
        mats[name] = mat;
        return mat;
    }

    static Material M(string name) => mats[name];

    // ================================================================== prefabs

    static GameObject CreatePlayerPrefab()
    {
        var root = new GameObject("Player") { layer = GameAssets.PlayerLayer };
        var cc = root.AddComponent<CharacterController>();
        cc.center = new Vector3(0f, 1f, 0f);
        cc.height = 2f;
        cc.radius = 0.4f;
        cc.stepOffset = 0.45f;
        cc.skinWidth = 0.04f;
        cc.slopeLimit = 50f;
        root.AddComponent<NetworkObject>();
        var audio = root.AddComponent<AudioSource>();
        audio.playOnAwake = false;
        audio.spatialBlend = 1f;
        audio.minDistance = 6f;
        audio.maxDistance = 120f;
        audio.rolloffMode = AudioRolloffMode.Linear;
        var player = root.AddComponent<FpsPlayer>();

        var head = new GameObject("Head").transform;
        head.SetParent(root.transform, false);
        head.localPosition = new Vector3(0f, 1.6f, 0f);

        var camGo = new GameObject("Camera");
        camGo.transform.SetParent(head, false);
        var cam = camGo.AddComponent<Camera>();
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 1500f;
        cam.fieldOfView = FpsPlayer.BaseFov;
        cam.cullingMask = ~(1 << GameAssets.ViewModelLayer);
        cam.enabled = false;
        var listener = camGo.AddComponent<AudioListener>();
        listener.enabled = false;

        var vmGo = new GameObject("ViewModelCamera");
        vmGo.transform.SetParent(camGo.transform, false);
        var vm = vmGo.AddComponent<Camera>();
        vm.clearFlags = CameraClearFlags.Depth;
        vm.cullingMask = 1 << GameAssets.ViewModelLayer;
        vm.depth = 1;
        vm.fieldOfView = 60f;
        vm.nearClipPlane = 0.01f;
        vm.farClipPlane = 5f;
        vm.enabled = false;

        player.head = head;
        player.playerCamera = cam;
        player.viewModelCamera = vm;
        player.audioListener = listener;
        player.audioSource = audio;

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.ImportAsset(PlayerPrefabPath, ImportAssetOptions.ForceUpdate);
        return prefab;
    }

    static GameObject CreateProjectilePrefab()
    {
        var root = new GameObject("Projectile");
        root.AddComponent<NetworkObject>();
        root.AddComponent<Projectile>();
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, ProjectilePrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.ImportAsset(ProjectilePrefabPath, ImportAssetOptions.ForceUpdate);
        return prefab;
    }

    static GameObject CreatePickupPrefab()
    {
        var root = new GameObject("WeaponPickup");
        root.AddComponent<NetworkObject>();
        root.AddComponent<WeaponPickup>();
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PickupPrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.ImportAsset(PickupPrefabPath, ImportAssetOptions.ForceUpdate);
        return prefab;
    }

    // ================================================================== scene

    static void CreateScene(GameObject playerPrefab, GameObject pickupPrefab, GameObject projectilePrefab)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var sunGo = new GameObject("Sun");
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.15f;
        sun.color = new Color(1f, 0.95f, 0.86f);
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.85f;
        sunGo.transform.rotation = Quaternion.Euler(48f, -40f, 0f);
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.62f, 0.68f, 0.78f);
        RenderSettings.ambientEquatorColor = new Color(0.55f, 0.5f, 0.45f);
        RenderSettings.ambientGroundColor = new Color(0.3f, 0.27f, 0.22f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.78f, 0.8f, 0.82f);
        RenderSettings.fogStartDistance = 130f;
        RenderSettings.fogEndDistance = 600f;

        var menuCamGo = new GameObject("Menu Camera");
        var menuCam = menuCamGo.AddComponent<Camera>();
        menuCam.cullingMask = ~(1 << GameAssets.ViewModelLayer);
        menuCam.farClipPlane = 1500f;
        menuCamGo.AddComponent<AudioListener>();
        var orbit = menuCamGo.AddComponent<MenuCameraOrbit>();
        orbit.radius = 95f;
        orbit.height = 58f;

        // Every map is built into this scene under its own root; MapCatalog switches between them at runtime.
        passages.Clear();
        BuildNuketown();
        PlaceWeaponSpawners("Fluketown", 68);
        PlaceHookSpawners("Fluketown", 8);
        PlaceGrenadeSpawners("Fluketown", 16);
        var fluketown = world.gameObject;
        AddProbe(world, new Vector3(0f, 6f, -2.5f), new Vector3(90f, 30f, 70f));
        ValidateDoorways("Fluketown");
        fluketown.SetActive(false);

        passages.Clear();
        BuildCampground();
        PlaceWeaponSpawners("Timberline", 64, p => p.y < WaterY + 0.3f || PondEllipse(p.x, p.z) < 1.1f);
        PlaceHookSpawners("Timberline", 8, p => p.y < WaterY + 0.3f || PondEllipse(p.x, p.z) < 1.1f);
        PlaceGrenadeSpawners("Timberline", 16, p => p.y < WaterY + 0.3f || PondEllipse(p.x, p.z) < 1.1f);
        var camp = world.gameObject;
        AddProbe(world, new Vector3(0f, 6f, 12f), new Vector3(100f, 30f, 90f));
        ValidateDoorways("Timberline");
        camp.SetActive(false);

        passages.Clear();
        BuildCity();
        PlaceWeaponSpawners("Downtown", 68);
        PlaceHookSpawners("Downtown", 8);
        PlaceGrenadeSpawners("Downtown", 16);
        var city = world.gameObject;
        AddProbe(world, new Vector3(0f, 8f, 10f), new Vector3(140f, 40f, 115f));
        ValidateDoorways("Downtown");
        city.SetActive(false);
        fluketown.SetActive(true);
        BuildDuelArenas();

        // Networking & game systems
        var netGo = new GameObject("NetworkManager");
        var transport = netGo.AddComponent<UnityTransport>();
        var nm = netGo.AddComponent<NetworkManager>();
        nm.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = transport,
            PlayerPrefab = playerPrefab,
            ConnectionApproval = false,
        };
        nm.NetworkConfig.Prefabs.NetworkPrefabsLists = new List<NetworkPrefabsList> { CreatePrefabList(playerPrefab, pickupPrefab, projectilePrefab) };
        var assets = netGo.AddComponent<GameAssets>();
        FillAssets(assets, playerPrefab, pickupPrefab);
        assets.projectilePrefab = projectilePrefab;
        netGo.AddComponent<MatchRules>();
        var ui = netGo.AddComponent<GameUI>();
        ui.menuCamera = menuCam;
        netGo.AddComponent<DevPanel>();
        netGo.AddComponent<BackgroundTestWindow>();
        netGo.AddComponent<DuelManager>();
        var catalog = netGo.AddComponent<MapCatalog>();
        catalog.sun = sun;
        catalog.orbit = orbit;
        catalog.maps = new[]
        {
            new MapCatalog.Entry
            {
                name = "Fluketown", description = "Suburban cul-de-sac and side streets.", root = fluketown,
            },
            new MapCatalog.Entry
            {
                name = "Timberline", description = "Wooded summer camp between two hills.", root = camp,
                fogColor = new Color(0.63f, 0.7f, 0.7f), fogStart = 45f, fogEnd = 290f,
                ambientSky = new Color(0.55f, 0.65f, 0.74f), ambientEquator = new Color(0.45f, 0.5f, 0.42f), ambientGround = new Color(0.22f, 0.24f, 0.18f),
                sunColor = new Color(1f, 0.92f, 0.78f), sunIntensity = 1.1f, sunEuler = new Vector3(40f, -60f, 0f),
                orbitCenter = new Vector3(0f, 3f, 10f), orbitRadius = 104f, orbitHeight = 60f,
            },
            new MapCatalog.Entry
            {
                name = "Downtown", description = "City blocks, rooftops, parking garage.", root = city,
                fogColor = new Color(0.72f, 0.74f, 0.78f), fogStart = 80f, fogEnd = 450f,
                ambientSky = new Color(0.6f, 0.65f, 0.72f), ambientEquator = new Color(0.5f, 0.5f, 0.5f), ambientGround = new Color(0.28f, 0.27f, 0.26f),
                sunColor = new Color(1f, 0.96f, 0.9f), sunIntensity = 1.15f, sunEuler = new Vector3(52f, 28f, 0f),
                orbitCenter = new Vector3(0f, 6f, 10f), orbitRadius = 136f, orbitHeight = 96f,
            },
        };

        EditorSceneManager.SaveScene(scene, ScenePath);
    }

    static void FillAssets(GameAssets a, GameObject playerPrefab, GameObject pickupPrefab)
    {
        a.playerPrefab = playerPrefab;
        a.pickupPrefab = pickupPrefab;
        a.gunMetal = M("GunMetal");
        a.gunPolymer = M("GunPolymer");
        a.gunDark = M("GunDark");
        a.gunWood = M("GunWood");
        a.gunRubber = M("GunRubber");
        a.brass = M("Brass");
        a.gold = M("Gold");
        a.olive = M("Olive");
        a.shellRed = M("ShellRed");
        a.scopeLens = M("ScopeLens");
        a.sightWhite = M("SightWhite");
        a.glove = M("Glove");
        a.sleeve = M("Sleeve");
        a.playerBody = M("PlayerBody");
        a.skin = M("PlayerBody");
        a.visor = M("Visor");
        a.mannequin = M("Mannequin");
        a.freedomPrint = M("FreedomPrint");
        a.rebelFlagPrint = M("RebelFlagPrint");
        a.spiderEmblem = M("SpiderEmblem");
        a.rainbowShirt = M("RainbowShirt");
        a.polkaShirt = M("PolkaShirt");
        a.plaidShirt = M("PlaidShirt");
        a.sailorShirt = M("SailorShirt");
        a.hawaiianShirt = M("HawaiianShirt");
        a.tracer = M("Tracer");
        a.muzzleFlash = M("MuzzleFlash");
        a.tracerGlow = M("TracerGlow");
        a.flashGlow = M("FlashGlow");
        a.sparkGlow = M("SparkGlow");
        a.bloodParticle = M("BloodParticle");
        a.bloodDecal = M("BloodDecal");
        a.bulletHole = M("BulletHole");
        a.flesh = M("Flesh");
        a.gore = M("Gore");
        a.bone = M("Bone");
        a.dust = M("Dust");
        a.bloodSplatTexture = texs["BloodSplat"];
    }

    static NetworkPrefabsList CreatePrefabList(params GameObject[] prefabs)
    {
        const string path = GeneratedDir + "/NetworkPrefabs.asset";
        AssetDatabase.DeleteAsset(path);
        var list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
        foreach (var p in prefabs)
            list.Add(new NetworkPrefab { Prefab = p });
        AssetDatabase.CreateAsset(list, path);
        return list;
    }

    // ================================================================== Nuketown-style map
    //
    // Two two-story houses face each other across a cul-de-sac street (X axis = across, Z = along the street).
    // The yellow house (west) and green house (east) are point-symmetric: the east one is the west one rotated 180°.
    // Playable area: X -38..38, Z -31..26.

    static Transform world;

    // Map bounds (playable): X -90..90, Z -64..84 (180 x 148 m). Grown by a third from the old X -68..68, Z -46..66:
    // each map keeps its original layout in the middle and gains an outer ring.
    public const float MapMinX = -90f, MapMaxX = 90f, MapMinZ = -64f, MapMaxZ = 84f;
    // The old boundary, still used by layouts that sit inside it.
    const float CoreMinX = -68f, CoreMaxX = 68f, CoreMinZ = -46f, CoreMaxZ = 66f;

    static void BuildNuketown()
    {
        world = new GameObject("Nuketown").transform;
        float w = MapMaxX - MapMinX, d = MapMaxZ - MapMinZ, cz = (MapMinZ + MapMaxZ) / 2f;

        // ---------------------------------------------------------------- ground and roads
        Box(world, "Desert", new Vector3(0f, -0.5f, 0f), new Vector3(900f, 1f, 900f), M("Sand"));
        Box(world, "Grass", new Vector3(0f, -0.04f, cz), new Vector3(w, 0.1f, d), M("Grass"));
        // Fluketown street (original), running north into the avenue, with the cul-de-sac at its south end.
        Box(world, "Street", new Vector3(0f, -0.03f, 0f), new Vector3(12f, 0.1f, 62f), M("Asphalt"));
        Cylinder(world, "CulDeSac", new Vector3(0f, -0.03f, -20f), 11f, 0.1f, M("Asphalt"));
        Box(world, "Sidewalk W", new Vector3(-6.75f, 0.06f, 10.5f), new Vector3(1.5f, 0.12f, 41f), M("Concrete"));
        Box(world, "Sidewalk E", new Vector3(6.75f, 0.06f, 10.5f), new Vector3(1.5f, 0.12f, 41f), M("Concrete"));
        for (int i = 0; i < 10; i++)
            Box(world, "LaneDash", new Vector3(0f, 0.025f, 26f - i * 4.5f), new Vector3(0.15f, 0.01f, 2f), M("Trim"), collider: false);

        // North avenue across the whole map.
        Box(world, "Avenue", new Vector3(0f, -0.03f, 36f), new Vector3(w, 0.1f, 10f), M("Asphalt"));
        Box(world, "Avenue Sidewalk N", new Vector3(0f, 0.06f, 41.75f), new Vector3(w, 0.12f, 1.5f), M("Concrete"));
        foreach (var (a, b) in new[] { (MapMinX, -52f), (-44f, -7.5f), (7.5f, 44f), (52f, MapMaxX) })
            Box(world, "Avenue Sidewalk S", new Vector3((a + b) / 2f, 0.06f, 30.25f), new Vector3(b - a, 0.12f, 1.5f), M("Concrete"));
        for (float x = MapMinX + 3f; x < MapMaxX - 2f; x += 4.5f)
            if (Mathf.Abs(x) > 8f && Mathf.Abs(Mathf.Abs(x) - 48f) > 5f)
                Box(world, "LaneDash", new Vector3(x, 0.025f, 36f), new Vector3(2f, 0.01f, 0.15f), M("Trim"), collider: false);

        // Side streets down both flanks, joined by a lane across the south.
        foreach (float sx in new[] { -48f, 48f })
        {
            Box(world, "Side Street", new Vector3(sx, -0.03f, -4.5f), new Vector3(8f, 0.1f, 71f), M("Asphalt"));
            float outer = sx + Mathf.Sign(sx) * 4.75f, inner = sx - Mathf.Sign(sx) * 4.75f;
            Box(world, "Side Sidewalk", new Vector3(outer, 0.06f, -3.75f), new Vector3(1.5f, 0.12f, 66.5f), M("Concrete"));
            Box(world, "Side Sidewalk", new Vector3(inner, 0.06f, -3.75f), new Vector3(1.5f, 0.12f, 66.5f), M("Concrete"));
            for (float z = -36f; z < 29f; z += 4.5f)
                Box(world, "LaneDash", new Vector3(sx, 0.025f, z), new Vector3(0.15f, 0.01f, 2f), M("Trim"), collider: false);
        }
        Box(world, "South Lane", new Vector3(0f, -0.03f, -40f), new Vector3(104f, 0.1f, 6f), M("Asphalt"));

        // ---------------------------------------------------------------- perimeter and distant scenery
        Box(world, "Fence N", new Vector3(0f, 1.5f, MapMaxZ + 0.2f), new Vector3(w + 0.8f, 3f, 0.3f), M("Fence"));
        Box(world, "Fence S", new Vector3(0f, 1.5f, MapMinZ - 0.2f), new Vector3(w + 0.8f, 3f, 0.3f), M("Fence"));
        Box(world, "Fence W", new Vector3(MapMinX - 0.2f, 1.5f, cz), new Vector3(0.3f, 3f, d + 0.4f), M("Fence"));
        Box(world, "Fence E", new Vector3(MapMaxX + 0.2f, 1.5f, cz), new Vector3(0.3f, 3f, d + 0.4f), M("Fence"));
        Box(world, "Invisible Wall", new Vector3(0f, 10f, MapMaxZ + 0.2f), new Vector3(w + 1f, 16f, 0.3f), null);
        Box(world, "Invisible Wall", new Vector3(0f, 10f, MapMinZ - 0.2f), new Vector3(w + 1f, 16f, 0.3f), null);
        Box(world, "Invisible Wall", new Vector3(MapMinX - 0.2f, 10f, cz), new Vector3(0.3f, 16f, d + 1f), null);
        Box(world, "Invisible Wall", new Vector3(MapMaxX + 0.2f, 10f, cz), new Vector3(0.3f, 16f, d + 1f), null);
        var drng = new System.Random(7);
        for (int i = 0; i < 16; i++)
        {
            float ang = i / 16f * Mathf.PI * 2f + (float)drng.NextDouble() * 0.3f;
            float dist = 260f + (float)drng.NextDouble() * 110f;
            var size = new Vector3(40f + (float)drng.NextDouble() * 60f, 15f + (float)drng.NextDouble() * 35f, 40f + (float)drng.NextDouble() * 60f);
            Box(world, "Mesa", new Vector3(Mathf.Cos(ang) * dist, size.y / 2f - 1f, Mathf.Sin(ang) * dist), size, M("Mesa"), yRot: (float)drng.NextDouble() * 90f);
        }
        Box(world, "Tower", new Vector3(-170f, 25f, 230f), new Vector3(2f, 50f, 2f), M("Metal"));
        Box(world, "TowerCab", new Vector3(-170f, 51f, 230f), new Vector3(6f, 4f, 6f), M("Metal"));

        // ---------------------------------------------------------------- houses
        // Fluketown: the original pair facing each other across the street.
        var yellow = BuildHouse("Yellow House", new Vector3(-22.5f, 0f, -1f), 0f, M("SidingYellow"), M("Roof"), M("CarCream"));
        var green = BuildHouse("Green House", new Vector3(22.5f, 0f, 1f), 180f, M("SidingGreen"), M("RoofRed"), M("CarGreen"));
        // North row facing the avenue.
        var north = new[]
        {
            BuildHouse("Blue House", new Vector3(-51f, 0f, 58f), 90f, M("SidingBlue"), M("Roof"), M("BusBlue"), backyard: false),
            BuildHouse("Pink House", new Vector3(-21f, 0f, 58f), 90f, M("SidingPink"), M("RoofRed"), M("CarCream"), backyard: false),
            BuildHouse("White House", new Vector3(9f, 0f, 58f), 90f, M("SidingWhite"), M("Roof"), M("TruckRed"), backyard: false),
            BuildHouse("Yellow House 2", new Vector3(39f, 0f, 58f), 90f, M("SidingYellow"), M("RoofRed"), M("CarGreen"), backyard: false),
        };
        // Compact lots along the side streets.
        var side = new[]
        {
            BuildHouse("Green House 2", new Vector3(61f, 0f, 18f), 180f, M("SidingGreen"), M("Roof"), M("CarCream"), backyard: false, frontYard: false, drivewayLength: 2f),
            BuildHouse("Pink House 2", new Vector3(61f, 0f, -14f), 180f, M("SidingPink"), M("Roof"), M("BusBlue"), backyard: false, frontYard: false, drivewayLength: 2f),
            BuildHouse("Blue House 2", new Vector3(-61f, 0f, -18f), 0f, M("SidingBlue"), M("RoofRed"), M("CarGreen"), backyard: false, frontYard: false, drivewayLength: 2f),
            BuildHouse("White House 2", new Vector3(-61f, 0f, 14f), 0f, M("SidingWhite"), M("RoofRed"), M("TruckRed"), backyard: false, frontYard: false, drivewayLength: 2f),
        };

        // ---------------------------------------------------------------- street props
        BuildBus(new Vector3(0f, 0f, 21f));
        BuildSemi(new Vector3(2.2f, 0f, 0f));
        BuildTownSign(new Vector3(-9.2f, 0f, -11f));
        BuildFluketownOuterRing();
        foreach (var p in new[] { new Vector3(-7.8f, 0f, 14.5f), new Vector3(7.8f, 0f, -12.5f), new Vector3(-8f, 0f, -13f), new Vector3(8f, 0f, 16f),
                                  new Vector3(-30f, 0f, 43f), new Vector3(20f, 0f, 43f), new Vector3(43f, 0f, -20f), new Vector3(-43f, 0f, 20f) })
            Cylinder(world, "TrashCan", p + Vector3.up * 0.5f, 0.32f, 1f, M("Metal"));
        foreach (var p in new[] { new Vector3(-7.4f, 0f, 4f), new Vector3(7.4f, 0f, -2f), new Vector3(-40f, 0f, 42.5f), new Vector3(30f, 0f, 29.8f),
                                  new Vector3(53f, 0f, 0f), new Vector3(-53f, 0f, 2f) })
            Cylinder(world, "Hydrant", p + Vector3.up * 0.4f, 0.15f, 0.8f, M("TruckRed"));

        // Hedge line between the cul-de-sac and the south lane (with gaps to run through).
        for (float x = -34f; x < 34f; x += 8f)
            Box(world, "Hedge", new Vector3(x + 2.5f, 0.6f, -34.5f), new Vector3(5f, 1.2f, 1f), M("Hedge"));
        // Trees along the alleys behind the Fluketown backyards and along the avenue.
        foreach (float z in new[] { -28f, -16f, 8f, 20f })
        {
            Tree(new Vector3(-40.5f, 0f, z), 3.2f);
            Tree(new Vector3(40.5f, 0f, -z), 3.2f);
        }
        foreach (float x in new[] { -60f, -36f, -12f, 24f, 48f })
            Tree(new Vector3(x, 0f, 28.5f), 3f);

        // South parks: playground (west), basketball court and water tower (east).
        BuildPlayground(new Vector3(-22f, 0f, -26f));
        BuildCourt(new Vector3(20f, 0f, -26f));
        BuildWaterTower(new Vector3(36f, 0f, -27f));
        foreach (var p in new[] { new Vector3(-12f, 0f, -20f), new Vector3(-34f, 0f, -18f), new Vector3(12f, 0f, -18f), new Vector3(28f, 0f, -16f) })
            Tree(p, 3.4f);

        // ---------------------------------------------------------------- spawn points
        var spawns = new GameObject("Spawn Points").transform;
        spawns.SetParent(world, false);
        var spawnPositions = new[]
        {
            // Fluketown
            new Vector3(-34.5f, 0.05f, -3f), new Vector3(-31.5f, 0.05f, 9f), new Vector3(34.5f, 0.05f, 3f), new Vector3(31.5f, 0.05f, -9f),
            new Vector3(0f, 0.05f, 24.3f), new Vector3(0f, 0.05f, -28.5f), new Vector3(-14f, 0.05f, -24f), new Vector3(14f, 0.05f, 22f),
            // Avenue and north row
            new Vector3(-62f, 0.05f, 36f), new Vector3(62f, 0.05f, 36f), new Vector3(-34.5f, 0.05f, 50f), new Vector3(-4.5f, 0.05f, 50f), new Vector3(25.5f, 0.05f, 50f),
            // Side streets and alleys
            new Vector3(-48f, 0.05f, -30f), new Vector3(48f, 0.05f, 26f), new Vector3(40.5f, 0.05f, -2f), new Vector3(-40.5f, 0.05f, 2f),
            new Vector3(-56f, 0.05f, -2f), new Vector3(56f, 0.05f, 2f),
            // Parks and the south lane
            new Vector3(-30f, 0.05f, -32f), new Vector3(30f, 0.05f, -34f), new Vector3(-50f, 0.05f, -42f), new Vector3(50f, 0.05f, -42f),
            // Outer ring (bigger map): ring road, south lots, north strip
            new Vector3(-73f, 0.05f, -45f), new Vector3(-73f, 0.05f, 7f), new Vector3(-73f, 0.05f, 52f), new Vector3(73f, 0.05f, -30f),
            new Vector3(73f, 0.05f, 22f), new Vector3(73f, 0.05f, 70f), new Vector3(-40f, 0.05f, -57f), new Vector3(2f, 0.05f, -58f),
            new Vector3(42f, 0.05f, -57f), new Vector3(-50f, 0.05f, 79f), new Vector3(0f, 0.05f, 79f), new Vector3(50f, 0.05f, 79f),
        };
        foreach (var p in spawnPositions)
        {
            var sp = new GameObject("Spawn");
            sp.transform.SetParent(spawns, false);
            sp.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, Mathf.Atan2(-p.x, -p.z) * Mathf.Rad2Deg, 0f));
            sp.AddComponent<SpawnPoint>();
        }
    }

    /// <summary>
    /// The ring the bigger map added around the original neighborhood: a ring road just inside the fence, a row of
    /// houses down each side, parking lots and sheds along the south, and a tree-lined strip along the north.
    /// </summary>
    static void BuildFluketownOuterRing()
    {
        var rng = new System.Random(77);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        // Ring road (8 m), joined to the old avenue (z 36) and the south lane (z -40).
        foreach (float sx in new[] { -73f, 73f })
        {
            Box(world, "Ring Road", new Vector3(sx, -0.03f, 10f), new Vector3(8f, 0.1f, 132f), M("Asphalt"));
            for (float z = -52f; z < 74f; z += 4.5f)
                Box(world, "LaneDash", new Vector3(sx, 0.025f, z), new Vector3(0.15f, 0.01f, 2f), M("Trim"), collider: false);
        }
        Box(world, "Ring Road S", new Vector3(0f, -0.03f, -50f), new Vector3(154f, 0.1f, 6f), M("Asphalt"));
        Box(world, "Ring Road N", new Vector3(0f, -0.03f, 72f), new Vector3(154f, 0.1f, 6f), M("Asphalt"));
        Box(world, "Lane Link W", new Vector3(-62.5f, -0.03f, -40f), new Vector3(21f, 0.1f, 6f), M("Asphalt"));
        Box(world, "Lane Link E", new Vector3(62.5f, -0.03f, -40f), new Vector3(21f, 0.1f, 6f), M("Asphalt"));

        // A row of houses down each side, facing the ring road.
        var sidings = new[] { "SidingYellow", "SidingGreen", "SidingBlue", "SidingPink", "SidingWhite" };
        var roofs = new[] { "Roof", "RoofRed" };
        var cars = new[] { "CarCream", "CarGreen", "BusBlue", "TruckRed" };
        int n = 0;
        foreach (float z in new[] { -46f, -24f, -2f, 20f, 42f, 64f })
        {
            BuildHouse($"Ring House W{n}", new Vector3(-84f, 0f, z), 0f, M(sidings[n % 5]), M(roofs[n % 2]), M(cars[n % 4]),
                backyard: false, frontYard: false, drivewayLength: 2f);
            BuildHouse($"Ring House E{n}", new Vector3(84f, 0f, -z + 20f), 180f, M(sidings[(n + 2) % 5]), M(roofs[(n + 1) % 2]), M(cars[(n + 1) % 4]),
                backyard: false, frontYard: false, drivewayLength: 2f);
            n++;
        }

        // South strip: two parking lots with parked cars, sheds and a field between them.
        foreach (float cx in new[] { -50f, 50f })
        {
            Box(world, "Parking Lot", new Vector3(cx, -0.025f, -58.5f), new Vector3(34f, 0.1f, 10f), M("Asphalt"));
            for (int i = 0; i < 6; i++)
            {
                float x = cx - 14f + i * 5.6f;
                Box(world, "Parking Line", new Vector3(x - 2.8f, 0.03f, -59f), new Vector3(0.12f, 0.01f, 5f), M("Trim"), collider: false);
                if (rng.NextDouble() < 0.6)
                    BuildCar(world, "Parked Car", new Vector3(x, 0f, -59.5f), 90f + R(-6f, 6f), M(cars[rng.Next(cars.Length)]));
            }
        }
        foreach (var (x, z, yaw) in new[] { (-22f, -58f, 0f), (-10f, -60f, 180f), (12f, -58f, 0f), (24f, -60f, 180f) })
            Shed(new Vector3(x, 0f, z), yaw);
        foreach (float x in new[] { -30f, -4f, 18f, 30f })
            Tree(new Vector3(x, 0f, -55.5f + R(-1f, 1f)), 2.8f);

        // North strip: trees, hedges and sheds behind the north row.
        for (float x = -80f; x <= 80f; x += 16f)
            Tree(new Vector3(x + R(-3f, 3f), 0f, 79.5f + R(-1.5f, 1.5f)), R(2.6f, 3.4f));
        foreach (var (x, yaw) in new[] { (-60f, 180f), (-20f, 0f), (28f, 180f), (64f, 0f) })
            Shed(new Vector3(x, 0f, 79f), yaw);
        for (float x = -74f; x < 74f; x += 20f)
            Box(world, "Hedge", new Vector3(x + 6f, 0.6f, 67.2f), new Vector3(7f, 1.2f, 1f), M("Hedge"));

        // Trees and street furniture along the ring road.
        foreach (float z in new[] { -36f, -13f, 9f, 31f, 53f, 75f })
        {
            Tree(new Vector3(-67.5f, 0f, z), 2.8f);
            Tree(new Vector3(67.5f, 0f, z - 6f), 2.8f);
        }
        foreach (var p in new[] { new Vector3(-77.6f, 0f, 30f), new Vector3(77.6f, 0f, -12f), new Vector3(-36f, 0f, -53.5f), new Vector3(36f, 0f, 68.6f) })
            Cylinder(world, "TrashCan", p + Vector3.up * 0.5f, 0.32f, 1f, M("Metal"));
        foreach (var p in new[] { new Vector3(-77.6f, 0f, -20f), new Vector3(77.6f, 0f, 46f) })
            Cylinder(world, "Hydrant", p + Vector3.up * 0.4f, 0.15f, 0.8f, M("TruckRed"));
        BuildCar(world, "Car", new Vector3(-72f, 0f, 30f), 4f, M("CarGreen"));
        BuildCar(world, "Car", new Vector3(74f, 0f, -18f), 182f, M("CarCream"));
        BuildCar(world, "Wrecked Car", new Vector3(20f, 0f, 72.5f), 87f, M("TruckRed"), wrecked: true);
    }

    /// <summary>Small garden shed with an open front: cover you can duck into.</summary>
    static void Shed(Vector3 pos, float yaw)
    {
        var t = new GameObject("Shed").transform;
        t.SetParent(world, false);
        t.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        var wall = M("SidingWhite");
        Box(t, "Floor", new Vector3(0f, 0.05f, 0f), new Vector3(4f, 0.1f, 3f), M("Floorboards"));
        Box(t, "Back", new Vector3(0f, 1.25f, -1.4f), new Vector3(4f, 2.5f, 0.15f), wall);
        Box(t, "Side", new Vector3(-1.93f, 1.25f, 0f), new Vector3(0.15f, 2.5f, 3f), wall);
        Box(t, "Side", new Vector3(1.93f, 1.25f, 0f), new Vector3(0.15f, 2.5f, 3f), wall);
        Box(t, "Front", new Vector3(-1.3f, 1.25f, 1.42f), new Vector3(1.4f, 2.5f, 0.15f), wall);
        Box(t, "Roof", new Vector3(0f, 2.62f, 0f), new Vector3(4.4f, 0.2f, 3.4f), M("Roof"));
    }

    static void Tree(Vector3 pos, float crown)
    {
        Cylinder(world, "Tree Trunk", pos + Vector3.up * 1.6f, 0.25f, 3.2f, M("Trunk"));
        Sphere(world, "Tree Crown", pos + Vector3.up * (2.6f + crown * 0.45f), crown, M("Foliage"));
    }

    static void BuildPlayground(Vector3 pos)
    {
        var t = new GameObject("Playground").transform;
        t.SetParent(world, false);
        t.position = pos;
        Box(t, "Wood Chips", new Vector3(0f, 0.01f, 0f), new Vector3(14f, 0.04f, 10f), M("Sand"), collider: false);
        // Slide tower with ladder and slide.
        Box(t, "Tower Deck", new Vector3(0f, 1.5f, 0f), new Vector3(1.8f, 0.15f, 1.8f), M("Fence"));
        foreach (var c in new[] { new Vector3(-0.85f, 0f, -0.85f), new Vector3(0.85f, 0f, -0.85f), new Vector3(-0.85f, 0f, 0.85f), new Vector3(0.85f, 0f, 0.85f) })
            Box(t, "Tower Post", c + Vector3.up * 1.9f, new Vector3(0.12f, 3.8f, 0.12f), M("TruckRed"));
        Box(t, "Tower Roof", new Vector3(0f, 3.9f, 0f), new Vector3(2.1f, 0.12f, 2.1f), M("BusBlue"));
        Passage(t, "Slide tower deck", new Vector3(0f, 1.58f, 0f), Vector3.right, run: 0.4f);
        for (int i = 0; i < 5; i++)
            Box(t, "Ladder Rung", new Vector3(0f, 0.3f + i * 0.3f, -1.15f), new Vector3(0.7f, 0.06f, 0.3f), M("Metal"));
        var slide = Box(t, "Slide", new Vector3(0f, 0.78f, 2.5f), new Vector3(0.7f, 0.08f, 3.6f), M("TruckRed"));
        slide.transform.localRotation = Quaternion.Euler(24f, 0f, 0f);
        NavHint(t, new Vector3(0f, 0.05f, -1.8f));
        NavHint(t, new Vector3(0f, 1.6f, 0f));
        // Swing set.
        foreach (float x in new[] { 3.5f, 7f })
        {
            Box(t, "Swing Leg", new Vector3(x, 1.2f, -2.6f), new Vector3(0.1f, 2.5f, 0.1f), M("Metal"), zRot: 12f);
            Box(t, "Swing Leg", new Vector3(x, 1.2f, -1.4f), new Vector3(0.1f, 2.5f, 0.1f), M("Metal"), zRot: 12f);
        }
        Box(t, "Swing Bar", new Vector3(5.1f, 2.4f, -2f), new Vector3(4f, 0.1f, 0.1f), M("Metal"));
        foreach (float x in new[] { 4.3f, 5.9f })
        {
            Box(t, "Chain", new Vector3(x - 0.2f, 1.6f, -2f), new Vector3(0.02f, 1.6f, 0.02f), M("Metal"), collider: false);
            Box(t, "Chain", new Vector3(x + 0.2f, 1.6f, -2f), new Vector3(0.02f, 1.6f, 0.02f), M("Metal"), collider: false);
            Box(t, "Seat", new Vector3(x, 0.8f, -2f), new Vector3(0.5f, 0.05f, 0.25f), M("GunRubber"), collider: false);
        }
        // Sandbox, merry-go-round, benches.
        Box(t, "Sandbox Sand", new Vector3(-4.5f, 0.08f, -2f), new Vector3(3.6f, 0.1f, 3.6f), M("Sand"), collider: false);
        foreach (var (p, s) in new[] { (new Vector3(-4.5f, 0.15f, -3.85f), new Vector3(3.9f, 0.3f, 0.15f)), (new Vector3(-4.5f, 0.15f, -0.15f), new Vector3(3.9f, 0.3f, 0.15f)),
                                       (new Vector3(-6.35f, 0.15f, -2f), new Vector3(0.15f, 0.3f, 3.6f)), (new Vector3(-2.65f, 0.15f, -2f), new Vector3(0.15f, 0.3f, 3.6f)) })
            Box(t, "Sandbox Edge", p, s, M("Fence"));
        Cylinder(t, "Merry-Go-Round", new Vector3(-4f, 0.25f, 3f), 1.4f, 0.12f, M("BusBlue"));
        Cylinder(t, "Merry Pole", new Vector3(-4f, 0.7f, 3f), 0.05f, 0.9f, M("Metal"));
        foreach (var p in new[] { new Vector3(5f, 0.25f, 3.5f), new Vector3(-6f, 0.25f, 6.5f) })
            Box(t, "Bench", p, new Vector3(1.8f, 0.5f, 0.5f), M("Fence"));
    }

    static void BuildCourt(Vector3 pos)
    {
        var t = new GameObject("Basketball Court").transform;
        t.SetParent(world, false);
        t.position = pos;
        Box(t, "Court", new Vector3(0f, 0.03f, 0f), new Vector3(15f, 0.06f, 9f), M("Concrete"));
        Box(t, "Center Line", new Vector3(0f, 0.065f, 0f), new Vector3(0.1f, 0.01f, 9f), M("Trim"), collider: false);
        Box(t, "Side Line", new Vector3(0f, 0.065f, 4.4f), new Vector3(15f, 0.01f, 0.1f), M("Trim"), collider: false);
        Box(t, "Side Line", new Vector3(0f, 0.065f, -4.4f), new Vector3(15f, 0.01f, 0.1f), M("Trim"), collider: false);
        foreach (float side in new[] { -1f, 1f })
        {
            Box(t, "Hoop Post", new Vector3(side * 7.9f, 1.6f, 0f), new Vector3(0.15f, 3.2f, 0.15f), M("Metal"));
            Box(t, "Backboard", new Vector3(side * 7.5f, 3.2f, 0f), new Vector3(0.08f, 1.1f, 1.8f), M("BusWhite"));
            Box(t, "Rim", new Vector3(side * 7.15f, 2.9f, 0f), new Vector3(0.5f, 0.04f, 0.5f), M("TruckRed"), collider: false);
            Box(t, "Key", new Vector3(side * 5.9f, 0.065f, 0f), new Vector3(3f, 0.01f, 3.4f), M("TruckRed"), collider: false);
        }
        Box(t, "Bleacher", new Vector3(0f, 0.3f, -5.6f), new Vector3(6f, 0.6f, 1f), M("Metal"));
        Box(t, "Bleacher", new Vector3(0f, 0.75f, -6.3f), new Vector3(6f, 1.5f, 0.6f), M("Metal"));
    }

    static void BuildWaterTower(Vector3 pos)
    {
        var t = new GameObject("Water Tower").transform;
        t.SetParent(world, false);
        t.position = pos;
        const float legTop = 12f;
        foreach (var c in new[] { new Vector3(-2.5f, 0f, -2.5f), new Vector3(2.5f, 0f, -2.5f), new Vector3(-2.5f, 0f, 2.5f), new Vector3(2.5f, 0f, 2.5f) })
            Box(t, "Leg", c * 0.85f + Vector3.up * legTop / 2f, new Vector3(0.35f, legTop, 0.35f), M("Metal"));
        foreach (float y in new[] { 4f, 8f })
        {
            Box(t, "Brace", new Vector3(0f, y, -2.12f), new Vector3(4.3f, 0.15f, 0.15f), M("Metal"), collider: false);
            Box(t, "Brace", new Vector3(0f, y, 2.12f), new Vector3(4.3f, 0.15f, 0.15f), M("Metal"), collider: false);
            Box(t, "Brace", new Vector3(-2.12f, y, 0f), new Vector3(0.15f, 0.15f, 4.3f), M("Metal"), collider: false);
            Box(t, "Brace", new Vector3(2.12f, y, 0f), new Vector3(0.15f, 0.15f, 4.3f), M("Metal"), collider: false);
        }
        for (int i = 0; i < 18; i++)
            Box(t, "Ladder", new Vector3(2.4f, 0.4f + i * 0.65f, 2.4f), new Vector3(0.5f, 0.05f, 0.05f), M("Metal"), collider: false);
        Cylinder(t, "Catwalk", new Vector3(0f, legTop, 0f), 3.9f, 0.15f, M("Metal"));
        Cylinder(t, "Tank", new Vector3(0f, legTop + 2.2f, 0f), 3.4f, 4.2f, M("BusWhite"));
        Cylinder(t, "Tank Band", new Vector3(0f, legTop + 2.2f, 0f), 3.45f, 0.6f, M("TruckRed"));
        Cylinder(t, "Tank Roof", new Vector3(0f, legTop + 4.5f, 0f), 2.6f, 0.5f, M("BusWhite"));
        Cylinder(t, "Tank Cap", new Vector3(0f, legTop + 5f, 0f), 1.2f, 0.5f, M("BusWhite"));
    }

    // ------------------------------------------------------------------ house
    // Local frame: front faces +X (toward the street). Footprint X -5.5..5.5, Z -7..7, garage at Z 7..13.

    static Transform BuildHouse(string name, Vector3 pos, float yRot, Material siding, Material roof, Material carMat,
        bool backyard = true, bool frontYard = true, float drivewayLength = 10f)
    {
        var h = new GameObject(name).transform;
        h.SetParent(world, false);
        h.SetPositionAndRotation(pos, Quaternion.Euler(0f, yRot, 0f));
        var trim = M("Trim");
        var inner = M("Interior");
        const float H = 3f, T = 0.2f;

        // Floors
        Box(h, "Floor", new Vector3(0f, 0.075f, 0f), new Vector3(11f, 0.15f, 14f), M("Floorboards"));
        Box(h, "Foundation", new Vector3(0f, 0.05f, 0f), new Vector3(11.4f, 0.1f, 14.4f), M("Concrete"));

        // Ground floor exterior walls (with doors and windows)
        Wall(h, "Front", WallAxis.AlongZ, 5.4f, -7f, 7f, 0f, H, T, siding,
            (-1.1f, 0.3f, 0f, 2.55f), (2.5f, 5.0f, 1.0f, 2.2f), (-5.5f, -3.0f, 1.0f, 2.2f));
        Wall(h, "Back", WallAxis.AlongZ, -5.4f, -7f, 7f, 0f, H, T, siding,
            (1.9f, 3.3f, 0f, 2.55f), (-4.0f, -1.5f, 1.0f, 2.2f), (4.5f, 6.0f, 1.0f, 2.2f));
        Wall(h, "South", WallAxis.AlongX, -6.9f, -5.5f, 5.5f, 0f, H, T, siding,
            (-2.0f, 0.5f, 1.0f, 2.2f), (2.0f, 4.0f, 1.0f, 2.2f));
        Wall(h, "North", WallAxis.AlongX, 6.9f, -5.5f, 5.5f, 0f, H, T, siding,
            (0.9f, 2.3f, 0f, 2.55f), (-3.5f, -1.5f, 1.0f, 2.2f));
        // Interior wall between living room and kitchen
        Wall(h, "Interior", WallAxis.AlongZ, 0f, -7f, 1f, 0.15f, H - 0.15f, 0.15f, inner, (-4.3f, -2.9f, 0f, 2.4f));

        // Window glass is left open (so you can shoot and jump through); add sills/frames for looks.
        WindowFrame(h, WallAxis.AlongZ, 5.5f, 2.5f, 5.0f, 1.0f, 2.2f, trim);
        WindowFrame(h, WallAxis.AlongZ, 5.5f, -5.5f, -3.0f, 1.0f, 2.2f, trim);
        WindowFrame(h, WallAxis.AlongZ, -5.5f, -4.0f, -1.5f, 1.0f, 2.2f, trim);

        // Stairs up to the bedroom (rise 0.2, run 0.28), climbing toward -Z along the back wall.
        for (int i = 0; i < 16; i++)
        {
            float top = (i + 1) * 0.2f;
            float z = 0.8f - 0.14f - i * 0.28f;
            Box(h, "Step", new Vector3(-4.7f, top / 2f + 0.15f, z), new Vector3(1.1f, top, 0.28f), M("Floorboards"));
            if (i % 2 == 1) NavHint(h, new Vector3(-4.7f, top + 0.15f, z));
        }
        NavHint(h, new Vector3(-4.7f, 0.15f, 1.6f));
        NavHint(h, new Vector3(-4.7f, 3.35f, -4.3f));
        NavHint(h, new Vector3(-3f, 3.35f, -4.3f));

        // Upstairs floor (with a stair opening), railing, walls, bedroom window over the front roof.
        const float up = 3.15f;
        Box(h, "Upstairs Floor A", new Vector3(-1.8f, up + 0.1f, -3f), new Vector3(4.6f, 0.2f, 8f), M("Floorboards"));
        Box(h, "Upstairs Floor B", new Vector3(-4.8f, up + 0.1f, -5.34f), new Vector3(1.4f, 0.2f, 3.32f), M("Floorboards"));
        Box(h, "Railing", new Vector3(-4.1f, up + 0.7f, -1.34f), new Vector3(0.08f, 1f, 4.68f), trim);
        float upBase = up + 0.2f, upH = 2.6f;
        Wall(h, "Up Front", WallAxis.AlongZ, 0.4f, -7f, 1f, upBase, upH, T, siding, (-5.0f, -2.0f, 0.8f, 2.1f));
        Wall(h, "Up Back", WallAxis.AlongZ, -5.4f, -7f, 1f, upBase, upH, T, siding, (-3.0f, -1.0f, 0.9f, 2.0f));
        Wall(h, "Up South", WallAxis.AlongX, -6.9f, -5.5f, 0.5f, upBase, upH, T, siding, (-3.0f, -1.0f, 0.9f, 2.0f));
        Wall(h, "Up North", WallAxis.AlongX, 0.9f, -5.5f, 0.5f, upBase, upH, T, siding, (-3.6f, -2.2f, 0f, 2.4f));
        WindowFrame(h, WallAxis.AlongZ, 0.5f, -5.0f, -2.0f, upBase + 0.8f, upBase + 2.1f, trim);

        // Roofs: flat lower roof over the front room (walkable from the bedroom window), pitched roof upstairs.
        Box(h, "Lower Roof A", new Vector3(3.15f, 3.1f, 0f), new Vector3(5.3f, 0.2f, 14.6f), roof);
        Box(h, "Lower Roof B", new Vector3(-2.65f, 3.1f, 4.15f), new Vector3(6.3f, 0.2f, 6.3f), roof);
        Box(h, "Roof Trim", new Vector3(5.85f, 3.0f, 0f), new Vector3(0.12f, 0.3f, 14.6f), trim);
        float ceilY = upBase + upH;
        Box(h, "Upper Ceiling", new Vector3(-2.5f, ceilY + 0.05f, -3f), new Vector3(6.4f, 0.1f, 8.6f), inner);
        float pitch = Mathf.Atan2(1.6f, 3.3f) * Mathf.Rad2Deg;
        float slab = Mathf.Sqrt(3.3f * 3.3f + 1.6f * 1.6f) + 0.3f;
        Box(h, "Pitched Roof F", new Vector3(-0.85f, ceilY + 0.85f, -3f), new Vector3(slab, 0.15f, 8.8f), roof, zRot: -pitch);
        Box(h, "Pitched Roof B", new Vector3(-4.15f, ceilY + 0.85f, -3f), new Vector3(slab, 0.15f, 8.8f), roof, zRot: pitch);
        Box(h, "Gable S", new Vector3(-2.5f, ceilY + 0.55f, -6.9f), new Vector3(4.2f, 1.1f, 0.2f), siding);
        Box(h, "Gable N", new Vector3(-2.5f, ceilY + 0.55f, 0.9f), new Vector3(4.2f, 1.1f, 0.2f), siding);
        Box(h, "Chimney", new Vector3(-4.6f, 5.2f, -6.2f), new Vector3(0.9f, 4.2f, 0.9f), M("TruckRed"));

        // Front porch with roof on posts, and front steps.
        Box(h, "Porch", new Vector3(6.5f, 0.1f, -0.4f), new Vector3(2.2f, 0.2f, 3.6f), M("Floorboards"));
        Box(h, "Porch Post", new Vector3(7.45f, 1.5f, -2.05f), new Vector3(0.15f, 3f, 0.15f), trim);
        Box(h, "Porch Post", new Vector3(7.45f, 1.5f, 1.25f), new Vector3(0.15f, 3f, 0.15f), trim);
        Box(h, "Porch Roof", new Vector3(6.6f, 3.0f, -0.4f), new Vector3(2.2f, 0.15f, 3.9f), roof);
        NavHint(h, new Vector3(4.4f, 0.15f, -0.4f));
        NavHint(h, new Vector3(6.4f, 0.2f, -0.4f));
        NavHint(h, new Vector3(-4.4f, 0.15f, 2.6f));
        NavHint(h, new Vector3(-6.6f, 0.02f, 2.6f));
        NavHint(h, new Vector3(1.6f, 0.15f, 5.9f));
        NavHint(h, new Vector3(1.6f, 0.15f, 8f));
        NavHint(h, new Vector3(-0.8f, 0.15f, -3.6f));
        NavHint(h, new Vector3(0.8f, 0.15f, -3.6f));

        // Garage (open door facing the street) with a car and a workbench.
        Box(h, "Garage Floor", new Vector3(0.5f, 0.075f, 10f), new Vector3(10f, 0.15f, 6f), M("Concrete"));
        Wall(h, "Garage Back", WallAxis.AlongZ, -4.4f, 7f, 13f, 0f, H, T, siding);
        Wall(h, "Garage Side", WallAxis.AlongX, 12.9f, -4.5f, 5.5f, 0f, H, T, siding, (-2f, 0f, 1.2f, 2.2f));
        Wall(h, "Garage Front", WallAxis.AlongZ, 5.4f, 7f, 13f, 0f, H, T, siding, (7.6f, 12.4f, 0f, 2.5f));
        // Doorways a standing player must fit through (checked by ValidateDoorways after the map is built).
        Passage(h, name + " front door", new Vector3(5.4f, 0.15f, -0.4f), Vector3.right);
        Passage(h, name + " back door", new Vector3(-5.4f, 0.15f, 2.6f), Vector3.right);
        Passage(h, name + " garage door (inside)", new Vector3(1.6f, 0.15f, 6.9f), Vector3.forward);
        Passage(h, name + " kitchen door", new Vector3(0f, 0.15f, -3.6f), Vector3.right);
        Passage(h, name + " bedroom door to roof", new Vector3(-2.9f, upBase, 0.9f), Vector3.forward);
        Passage(h, name + " garage door (outside)", new Vector3(5.4f, 0.15f, 10f), Vector3.right);
        Box(h, "Garage Roof", new Vector3(0.5f, 3.1f, 10f), new Vector3(10.6f, 0.2f, 6.6f), roof);
        Box(h, "Garage Door (open)", new Vector3(4.6f, 2.6f, 10f), new Vector3(1.6f, 0.08f, 4.8f), trim);
        if (drivewayLength >= 4f)
            BuildCar(h, "Driveway Car", new Vector3(5.5f + Mathf.Max(3.5f, drivewayLength * 0.55f), 0.04f, 10f), 90f, carMat);
        else
            BuildCar(h, "Curb Car", new Vector3(5.5f + drivewayLength + 3.6f, 0.04f, 10f), 0f, carMat); // parallel to the street
        Box(h, "Workbench", new Vector3(-3.95f, 0.6f, 10f), new Vector3(0.7f, 0.9f, 2.6f), M("Furniture"));
        Box(h, "Shelves", new Vector3(-2f, 1.1f, 12.55f), new Vector3(2.5f, 1.9f, 0.5f), M("Metal"));

        // Driveway and front path out to the sidewalk.
        Box(h, "Driveway", new Vector3(5.5f + drivewayLength / 2f, 0.015f, 10f), new Vector3(drivewayLength, 0.05f, 5f), M("Concrete"));
        if (drivewayLength > 3f)
            Box(h, "Front Path", new Vector3(7.5f + (drivewayLength - 2f) / 2f, 0.015f, -0.4f), new Vector3(drivewayLength - 2f, 0.05f, 1.4f), M("Concrete"));

        // Furniture (cover)
        Box(h, "Couch", new Vector3(1.4f, 0.55f, -6.3f), new Vector3(2.4f, 0.8f, 0.9f), M("Fabric"));
        Box(h, "Couch Back", new Vector3(1.4f, 1.0f, -6.7f), new Vector3(2.4f, 0.5f, 0.2f), M("Fabric"));
        Box(h, "Coffee Table", new Vector3(2.4f, 0.37f, -4.5f), new Vector3(1.2f, 0.45f, 0.6f), M("Furniture"));
        Box(h, "TV Cabinet", new Vector3(0.45f, 0.65f, -2f), new Vector3(0.5f, 1f, 1.6f), M("Furniture"));
        Box(h, "TV", new Vector3(0.45f, 1.45f, -2f), new Vector3(0.45f, 0.6f, 0.9f), M("Window"));
        Box(h, "Armchair", new Vector3(3.2f, 0.55f, 4.8f), new Vector3(1f, 0.8f, 1f), M("Fabric"));
        Box(h, "Counter", new Vector3(-2.3f, 0.62f, -6.5f), new Vector3(2.8f, 0.95f, 0.7f), M("Appliance"));
        Box(h, "Fridge", new Vector3(-0.5f, 1.1f, -6.45f), new Vector3(0.8f, 1.9f, 0.8f), M("Appliance"));
        Box(h, "Kitchen Island", new Vector3(-2.4f, 0.62f, -1.3f), new Vector3(1.6f, 0.95f, 0.9f), M("Furniture"));
        Box(h, "Dining Table", new Vector3(-2f, 0.52f, 4.2f), new Vector3(1.6f, 0.75f, 1f), M("Furniture"));
        Box(h, "Bookshelf", new Vector3(-5.05f, 1.15f, 5.5f), new Vector3(0.4f, 2f, 1.6f), M("Furniture"));
        Box(h, "Bed", new Vector3(-2.2f, upBase + 0.3f, -6f), new Vector3(2f, 0.6f, 1.4f), M("Fabric"));
        Box(h, "Dresser", new Vector3(-0.3f, upBase + 0.5f, 0.3f), new Vector3(1.1f, 1f, 0.5f), M("Furniture"));

        if (frontYard)
        {
        // Front yard: hedges, mailbox, tree.
        Box(h, "Hedge", new Vector3(13.5f, 0.55f, -5f), new Vector3(1f, 1.1f, 4f), M("Hedge"));
        Box(h, "Hedge", new Vector3(13.5f, 0.55f, 4f), new Vector3(1f, 1.1f, 3f), M("Hedge"));
        Box(h, "Mailbox Post", new Vector3(15f, 0.6f, 1.6f), new Vector3(0.1f, 1.2f, 0.1f), M("Fence"));
        Box(h, "Mailbox", new Vector3(15f, 1.25f, 1.6f), new Vector3(0.5f, 0.25f, 0.25f), M("Metal"));
        Cylinder(h, "Tree Trunk", new Vector3(10.5f, 1.6f, -10f), 0.25f, 3.2f, M("Trunk"));
        Sphere(h, "Tree Crown", new Vector3(10.5f, 4.2f, -10f), 3.4f, M("Foliage"));

        }
        if (!backyard) return h;
        // Backyard: fence with gaps, shed, picnic table, trampoline.
        Wall(h, "Yard Fence Back", WallAxis.AlongZ, -15.5f, -10f, 13f, 0f, 1.8f, 0.12f, M("Fence"), (0f, 2f, 0f, 1.8f));
        Wall(h, "Yard Fence S", WallAxis.AlongX, -10f, -15.5f, -5.5f, 0f, 1.8f, 0.12f, M("Fence"), (-9f, -7f, 0f, 1.8f));
        Wall(h, "Yard Fence N", WallAxis.AlongX, 13f, -15.5f, -4.5f, 0f, 1.8f, 0.12f, M("Fence"), (-8f, -6f, 0f, 1.8f));
        Box(h, "Shed", new Vector3(-12.8f, 1.3f, -7f), new Vector3(3f, 2.6f, 3f), M("FenceWhite"));
        Box(h, "Shed Roof", new Vector3(-12.8f, 2.7f, -7f), new Vector3(3.4f, 0.2f, 3.4f), roof);
        Box(h, "Picnic Table", new Vector3(-8.5f, 0.38f, -3f), new Vector3(1.8f, 0.75f, 0.8f), M("Fence"));
        Cylinder(h, "Trampoline", new Vector3(-10.5f, 0.45f, 6f), 1.6f, 0.1f, M("GunPolymer"));
        foreach (var off in new[] { new Vector3(1.3f, 0f, 0f), new Vector3(-1.3f, 0f, 0f), new Vector3(0f, 0f, 1.3f), new Vector3(0f, 0f, -1.3f) })
            Box(h, "Trampoline Leg", new Vector3(-10.5f, 0.2f, 6f) + off, new Vector3(0.08f, 0.4f, 0.08f), M("Metal"), collider: false);
        return h;
    }

    enum WallAxis { AlongX, AlongZ }

    /// <summary>
    /// A wall running along one local axis from `from` to `to`, at fixed coordinate `at` on the other axis,
    /// with rectangular openings (start, end, bottom, top) measured along the wall / up from its base.
    /// </summary>
    static void Wall(Transform parent, string name, WallAxis axis, float at, float from, float to, float baseY, float height, float thickness, Material mat,
        params (float a, float b, float bottom, float top)[] openings)
    {
        var list = new List<(float a, float b, float bottom, float top)>(openings);
        list.Sort((x, y) => x.a.CompareTo(y.a));
        float cursor = from;
        void Seg(float s, float e, float y0, float y1)
        {
            if (e - s < 0.01f || y1 - y0 < 0.01f) return;
            float mid = (s + e) / 2f, len = e - s, cy = baseY + (y0 + y1) / 2f, hgt = y1 - y0;
            var center = axis == WallAxis.AlongX ? new Vector3(mid, cy, at) : new Vector3(at, cy, mid);
            var size = axis == WallAxis.AlongX ? new Vector3(len, hgt, thickness) : new Vector3(thickness, hgt, len);
            Box(parent, name, center, size, mat);
        }
        foreach (var o in list)
        {
            Seg(cursor, o.a, 0f, height);
            Seg(o.a, o.b, 0f, o.bottom);
            Seg(o.a, o.b, o.top, height);
            cursor = o.b;
        }
        Seg(cursor, to, 0f, height);
    }

    static void WindowFrame(Transform parent, WallAxis axis, float at, float a, float b, float bottom, float top, Material mat)
    {
        float mid = (a + b) / 2f, len = b - a;
        Vector3 C(float along, float y) => axis == WallAxis.AlongX ? new Vector3(along, y, at) : new Vector3(at, y, along);
        Vector3 S(float along, float y, float depth) => axis == WallAxis.AlongX ? new Vector3(along, y, depth) : new Vector3(depth, y, along);
        Box(parent, "Sill", C(mid, bottom - 0.04f), S(len + 0.2f, 0.08f, 0.32f), mat, collider: false);
        Box(parent, "Lintel", C(mid, top + 0.05f), S(len + 0.2f, 0.1f, 0.26f), mat, collider: false);
        Box(parent, "Shutter", C(a - 0.25f, (bottom + top) / 2f), S(0.4f, top - bottom, 0.26f), M("Window"), collider: false);
        Box(parent, "Shutter", C(b + 0.25f, (bottom + top) / 2f), S(0.4f, top - bottom, 0.26f), M("Window"), collider: false);
    }

    // ------------------------------------------------------------------ vehicles & props

    static void BuildBus(Vector3 pos)
    {
        var bus = new GameObject("Bus").transform;
        bus.SetParent(world, false);
        bus.position = pos;
        Box(bus, "Lower Body", new Vector3(0f, 1.25f, 0f), new Vector3(11f, 1.5f, 2.6f), M("BusBlue"));
        Box(bus, "Upper Body", new Vector3(0f, 2.7f, 0f), new Vector3(11f, 1.4f, 2.55f), M("BusWhite"));
        Box(bus, "Windows", new Vector3(0f, 2.65f, 0f), new Vector3(10.4f, 0.7f, 2.6f), M("Window"), collider: false);
        Box(bus, "Roof", new Vector3(0f, 3.45f, 0f), new Vector3(10.8f, 0.15f, 2.4f), M("BusWhite"));
        Box(bus, "Windshield", new Vector3(5.52f, 2.55f, 0f), new Vector3(0.05f, 1.1f, 2.3f), M("Window"), collider: false);
        Box(bus, "Bumper", new Vector3(5.6f, 0.65f, 0f), new Vector3(0.2f, 0.3f, 2.6f), M("Chrome"));
        Box(bus, "Stripe", new Vector3(0f, 1.95f, 0f), new Vector3(11.02f, 0.12f, 2.62f), M("Chrome"), collider: false);
        foreach (float x in new[] { -3.6f, 3.6f })
            foreach (float z in new[] { -1.15f, 1.15f })
                Wheel(bus, new Vector3(x, 0.5f, z), 0.5f, 0.35f);
    }

    /// <summary>
    /// Semi-truck along the street (cab facing +Z). The trailer's sides are open (roof on posts) so you can see,
    /// shoot and walk through it, and a ramp runs from the back of the deck down to the road.
    /// </summary>
    static void BuildSemi(Vector3 pos)
    {
        var t = new GameObject("Semi Truck").transform;
        t.SetParent(world, false);
        t.position = pos;
        const float deckTop = 1.3f, rear = -8f, front = 1f;
        float len = front - rear, mid = (front + rear) / 2f;

        // Trailer: deck, roof on posts, solid front wall, open back. Each side is solid except a gap in the middle.
        Box(t, "Deck", new Vector3(0f, deckTop - 0.1f, mid), new Vector3(2.5f, 0.2f, len), M("Floorboards"));
        Box(t, "Roof", new Vector3(0f, 3.95f, mid), new Vector3(2.6f, 0.1f, len + 0.1f), M("BusWhite"));
        Box(t, "Front Wall", new Vector3(0f, (deckTop + 3.9f) / 2f, front - 0.05f), new Vector3(2.5f, 3.9f - deckTop, 0.1f), M("BusWhite"));
        foreach (float z in new[] { rear + 0.06f, -5f, -2f })
            foreach (float x in new[] { -1.19f, 1.19f })
                Box(t, "Post", new Vector3(x, (deckTop + 3.9f) / 2f, z), new Vector3(0.12f, 3.9f - deckTop, 0.12f), M("Metal"));
        Box(t, "Rear Header", new Vector3(0f, 3.78f, rear + 0.06f), new Vector3(2.5f, 0.25f, 0.12f), M("Metal"));
        const float gapRear = -5f, gapFront = -2f; // see-through opening between the middle posts
        foreach (float x in new[] { -1.22f, 1.22f })
        {
            Box(t, "Side Panel Rear", new Vector3(x, (deckTop + 3.9f) / 2f, (rear + gapRear) / 2f), new Vector3(0.06f, 3.9f - deckTop, gapRear - rear), M("BusWhite"));
            Box(t, "Side Panel Front", new Vector3(x, (deckTop + 3.9f) / 2f, (gapFront + front) / 2f), new Vector3(0.06f, 3.9f - deckTop, front - gapFront), M("BusWhite"));
        }
        Box(t, "Rub Rail L", new Vector3(-1.22f, deckTop - 0.15f, mid), new Vector3(0.08f, 0.22f, len), M("Metal"), collider: false);
        Box(t, "Rub Rail R", new Vector3(1.22f, deckTop - 0.15f, mid), new Vector3(0.08f, 0.22f, len), M("Metal"), collider: false);
        Box(t, "Beam L", new Vector3(-0.5f, 0.95f, mid), new Vector3(0.2f, 0.3f, len), M("GunPolymer"));
        Box(t, "Beam R", new Vector3(0.5f, 0.95f, mid), new Vector3(0.2f, 0.3f, len), M("GunPolymer"));
        foreach (float z in new[] { -6.6f, -5.4f })
            foreach (float x in new[] { -1.0f, 1.0f })
                Wheel(t, new Vector3(x, 0.5f, z), 0.5f, 0.45f, alongX: true);
        foreach (float x in new[] { -0.9f, 0.9f })
            Box(t, "Landing Gear", new Vector3(x, 0.5f, -0.6f), new Vector3(0.12f, 0.9f, 0.12f), M("Metal"));

        // Loading ramp from the back of the deck down to the road (~19 degrees).
        const float run = 3.8f;
        float angle = Mathf.Atan2(deckTop, run) * Mathf.Rad2Deg;
        var ramp = Box(t, "Ramp", new Vector3(0f, deckTop / 2f - 0.07f, rear - run / 2f), new Vector3(2.2f, 0.15f, Mathf.Sqrt(run * run + deckTop * deckTop) + 0.1f), M("Metal"));
        ramp.transform.localRotation = Quaternion.Euler(-angle, 0f, 0f);
        foreach (float x in new[] { -1.12f, 1.12f })
        {
            var edge = Box(t, "Ramp Edge", new Vector3(x, deckTop / 2f + 0.02f, rear - run / 2f), new Vector3(0.06f, 0.12f, Mathf.Sqrt(run * run + deckTop * deckTop)), M("TruckRed"), collider: false);
            edge.transform.localRotation = Quaternion.Euler(-angle, 0f, 0f);
        }
        Passage(t, "Trailer side opening", new Vector3(0f, deckTop, -3.5f), Vector3.right);
        Passage(t, "Trailer rear opening", new Vector3(0f, deckTop, rear + 0.5f), Vector3.forward, run: 0.5f);
        NavHint(t, new Vector3(0f, 0.05f, rear - run - 0.6f));
        NavHint(t, new Vector3(0f, deckTop / 2f + 0.05f, rear - run / 2f));
        NavHint(t, new Vector3(0f, deckTop + 0.02f, rear + 1f));

        // Tractor unit: cab, hood, grille, stacks, tanks, wheels.
        const float cabZ = 2.6f;
        Box(t, "Cab Chassis", new Vector3(0f, 0.75f, cabZ + 0.6f), new Vector3(1.9f, 0.4f, 4.2f), M("GunPolymer"));
        Box(t, "Cab", new Vector3(0f, 2.05f, cabZ), new Vector3(2.4f, 2.2f, 2f), M("TruckRed"));
        Box(t, "Cab Roof Fairing", new Vector3(0f, 3.35f, cabZ - 0.2f), new Vector3(2.3f, 0.4f, 1.6f), M("TruckRed"));
        Box(t, "Hood", new Vector3(0f, 1.5f, cabZ + 1.85f), new Vector3(2.1f, 1.1f, 1.7f), M("TruckRed"));
        Box(t, "Windshield", new Vector3(0f, 2.55f, cabZ + 1.01f), new Vector3(2.1f, 0.8f, 0.04f), M("Window"), collider: false);
        Box(t, "Side Window L", new Vector3(-1.21f, 2.55f, cabZ + 0.3f), new Vector3(0.04f, 0.7f, 0.9f), M("Window"), collider: false);
        Box(t, "Side Window R", new Vector3(1.21f, 2.55f, cabZ + 0.3f), new Vector3(0.04f, 0.7f, 0.9f), M("Window"), collider: false);
        Box(t, "Grille", new Vector3(0f, 1.5f, cabZ + 2.71f), new Vector3(1.6f, 0.9f, 0.04f), M("Chrome"), collider: false);
        Box(t, "Bumper", new Vector3(0f, 0.75f, cabZ + 2.78f), new Vector3(2.5f, 0.3f, 0.18f), M("Chrome"));
        foreach (float x in new[] { -1.3f, 1.3f })
        {
            Cylinder(t, "Exhaust Stack", new Vector3(x, 2.6f, cabZ - 0.95f), 0.08f, 2.8f, M("Chrome"));
            Box(t, "Fuel Tank", new Vector3(x * 0.95f, 0.85f, cabZ + 0.1f), new Vector3(0.4f, 0.5f, 1.1f), M("Chrome"));
        }
        foreach (float z in new[] { cabZ - 1.3f, cabZ + 1.9f })
            foreach (float x in new[] { -1.05f, 1.05f })
                Wheel(t, new Vector3(x, 0.5f, z), 0.5f, 0.4f, alongX: true);
    }

    /// <summary>Roadside welcome sign on two posts, lettered on both faces, facing the street (+X).</summary>
    static void BuildTownSign(Vector3 pos)
    {
        var t = new GameObject("Town Sign").transform;
        t.SetParent(world, false);
        t.position = pos;
        const float boardY = 1.95f, boardW = 2.8f, boardH = 1f;
        Box(t, "Post", new Vector3(0f, 1.2f, -boardW / 2f + 0.2f), new Vector3(0.14f, 2.4f, 0.14f), M("Fence"));
        Box(t, "Post", new Vector3(0f, 1.2f, boardW / 2f - 0.2f), new Vector3(0.14f, 2.4f, 0.14f), M("Fence"));
        Box(t, "Board", new Vector3(0f, boardY, 0f), new Vector3(0.08f, boardH, boardW), M("Trim"));
        Box(t, "Frame Top", new Vector3(0f, boardY + boardH / 2f + 0.03f, 0f), new Vector3(0.11f, 0.07f, boardW + 0.1f), M("TruckRed"), collider: false);
        Box(t, "Frame Bottom", new Vector3(0f, boardY - boardH / 2f - 0.03f, 0f), new Vector3(0.11f, 0.07f, boardW + 0.1f), M("TruckRed"), collider: false);
        Box(t, "Frame End", new Vector3(0f, boardY, -boardW / 2f - 0.03f), new Vector3(0.11f, boardH + 0.13f, 0.07f), M("TruckRed"), collider: false);
        Box(t, "Frame End", new Vector3(0f, boardY, boardW / 2f + 0.03f), new Vector3(0.11f, boardH + 0.13f, 0.07f), M("TruckRed"), collider: false);
        foreach (float side in new[] { 1f, -1f })
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.name = "Sign Text";
            q.isStatic = true;
            q.transform.SetParent(t, false);
            q.transform.localPosition = new Vector3(side * 0.045f, boardY + 0.02f, 0f);
            q.transform.localRotation = Quaternion.Euler(0f, side > 0 ? -90f : 90f, 0f); // readable from each side
            q.transform.localScale = new Vector3(2.5f, 2.5f * 160f / 1024f, 1f);
            q.GetComponent<Renderer>().sharedMaterial = M("SignText");
        }
    }

    static void BuildCar(Transform parent, string name, Vector3 pos, float yRot, Material paint, bool wrecked = false)
    {
        var c = new GameObject(name).transform;
        c.SetParent(parent, false);
        c.localPosition = pos;
        c.localRotation = Quaternion.Euler(0f, yRot, 0f);
        // Long axis = local Z.
        Box(c, "Body", new Vector3(0f, 0.65f, 0f), new Vector3(1.8f, 0.7f, 4.4f), paint);
        Box(c, "Cabin", new Vector3(0f, 1.25f, -0.2f), new Vector3(1.6f, 0.6f, 2.2f), paint);
        Box(c, "Glass", new Vector3(0f, 1.25f, -0.2f), new Vector3(1.62f, 0.45f, 2.1f), M("Window"), collider: false);
        Box(c, "Bumper F", new Vector3(0f, 0.45f, 2.25f), new Vector3(1.85f, 0.2f, 0.12f), M("Chrome"));
        Box(c, "Bumper R", new Vector3(0f, 0.45f, -2.25f), new Vector3(1.85f, 0.2f, 0.12f), M("Chrome"));
        Box(c, "Fin L", new Vector3(-0.8f, 1.05f, -1.9f), new Vector3(0.1f, 0.3f, 0.6f), paint, collider: false);
        Box(c, "Fin R", new Vector3(0.8f, 1.05f, -1.9f), new Vector3(0.1f, 0.3f, 0.6f), paint, collider: false);
        foreach (float z in new[] { -1.4f, 1.4f })
            foreach (float x in new[] { -0.85f, 0.85f })
                Wheel(c, new Vector3(x, 0.35f, z), 0.35f, 0.25f, alongX: true);
        if (wrecked)
        {
            c.localRotation = Quaternion.Euler(0f, yRot, 0f) * Quaternion.Euler(0f, 0f, 175f);
            c.localPosition = pos + Vector3.up * 1.6f;
        }
    }

    static void Wheel(Transform parent, Vector3 pos, float radius, float width, bool alongX = false)
    {
        var w = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        w.name = "Wheel";
        w.isStatic = true;
        w.transform.SetParent(parent, false);
        w.transform.localPosition = pos;
        w.transform.localRotation = alongX ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.Euler(90f, 0f, 0f);
        w.transform.localScale = new Vector3(radius * 2f, width / 2f, radius * 2f);
        w.GetComponent<Renderer>().sharedMaterial = M("Tire");
    }

    static void Mannequin(Vector3 pos, float yaw, bool broken = false)
    {
        var m = new GameObject("Mannequin").transform;
        m.SetParent(world, false);
        m.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        var mat = M("Mannequin");
        Box(m, "Torso", new Vector3(0f, 1.3f, 0f), new Vector3(0.4f, 0.5f, 0.22f), mat);
        Box(m, "Hips", new Vector3(0f, 0.95f, 0f), new Vector3(0.34f, 0.22f, 0.2f), mat);
        if (!broken) Sphere(m, "Head", new Vector3(0f, 1.72f, 0f), 0.26f, mat);
        Cylinder(m, "Neck", new Vector3(0f, 1.58f, 0f), 0.05f, 0.1f, mat);
        foreach (float side in new[] { -1f, 1f })
        {
            Cylinder(m, "Leg", new Vector3(side * 0.1f, 0.43f, 0f), 0.07f, 0.86f, mat);
            if (!broken || side > 0)
            {
                var arm = Cylinder(m, "Arm", new Vector3(side * 0.28f, 1.18f, 0.05f), 0.05f, 0.6f, mat);
                arm.transform.localRotation = Quaternion.Euler(-15f, 0f, side * 8f);
            }
        }
        Cylinder(m, "Stand", new Vector3(0f, 0.02f, 0f), 0.3f, 0.04f, M("Metal"));
        if (broken)
        {
            Sphere(m, "Fallen Head", new Vector3(0.5f, 0.13f, 0.4f), 0.26f, mat);
            var arm = Cylinder(m, "Fallen Arm", new Vector3(-0.6f, 0.06f, -0.3f), 0.05f, 0.6f, mat);
            arm.transform.localRotation = Quaternion.Euler(90f, 30f, 0f);
        }
    }

    // ------------------------------------------------------------------ doorway check

    static readonly List<(string name, Vector3 floor, Vector3 dir, float run)> passages = new();

    /// <summary>Remember a doorway: `floor` is the floor point in its middle, `dir` the way through it.</summary>
    static void Passage(Transform parent, string name, Vector3 localFloor, Vector3 localDir, float run = 1.2f)
        => passages.Add((name, parent.TransformPoint(localFloor), parent.TransformDirection(localDir).normalized, run));

    /// <summary>
    /// Sweep a standing player's capsule (radius 0.4, 2 m tall plus skin) through every recorded doorway in both
    /// directions and fail loudly if anything is in the way, so nobody has to crouch through a door.
    /// </summary>
    static void AddProbe(Transform parent, Vector3 pos, Vector3 size)
    {
        // Reflection probe over the map so metal, chrome, gold, glass and water reflect it.
        var probeGo = new GameObject("Reflection Probe");
        probeGo.transform.SetParent(parent, false);
        probeGo.transform.position = pos;
        var probe = probeGo.AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Realtime;
        probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
        probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
        probe.size = size;
        probe.resolution = 256;
        probe.hdr = true;
        probe.boxProjection = true;
        probe.cullingMask = ~((1 << GameAssets.ViewModelLayer) | (1 << GameAssets.DebrisLayer) | (1 << GameAssets.PlayerLayer));
    }

    static void ValidateDoorways(string map)
    {
        Physics.SyncTransforms();
        const float radius = 0.4f, height = 2.04f, lift = 0.06f;
        int blocked = 0;
        foreach (var (name, floor, dir, run) in passages)
        {
            foreach (float sign in new[] { 1f, -1f })
            {
                Vector3 start = floor - dir * sign * run;
                Vector3 bottom = start + Vector3.up * (lift + radius);
                Vector3 top = start + Vector3.up * (lift + height - radius);
                if (Physics.CapsuleCast(bottom, top, radius * 0.98f, dir * sign, out var hit, run * 2f, 1, QueryTriggerInteraction.Ignore))
                {
                    blocked++;
                    Debug.LogError($"[FFA] Doorway blocked: {name} (by '{hit.collider.name}' at {hit.point})");
                    break;
                }
            }
        }
        Debug.Log($"[FFA] Doorway check ({map}): {passages.Count - blocked}/{passages.Count} passable standing up");
    }

    static void NavHint(Transform parent, Vector3 localPos)
    {
        var go = new GameObject("NavHint");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.AddComponent<NavHint>();
    }

    // ------------------------------------------------------------------ primitive helpers

    static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, float yRot = 0f, float zRot = 0f, bool collider = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.isStatic = true;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(0f, yRot, zRot);
        go.transform.localScale = size;
        if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
        else Object.DestroyImmediate(go.GetComponent<Renderer>()); // invisible blocker
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        if (metricTile != Vector2.zero && mat != null)
        {
            // Texture in metres rather than once per face (city walls, slabs).
            go.GetComponent<MeshFilter>().sharedMesh = MetricCube(size, metricTile);
            go.transform.localScale = Vector3.one;
            if (collider) go.GetComponent<BoxCollider>().size = size;
        }
        return go;
    }

    static GameObject Cylinder(Transform parent, string name, Vector3 pos, float radius, float height, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.isStatic = true;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = new Vector3(radius * 2f, height / 2f, radius * 2f);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        // Cylinder primitives come with a capsule collider; swap for a mesh-accurate box-ish collider.
        Object.DestroyImmediate(go.GetComponent<Collider>());
        var mc = go.AddComponent<MeshCollider>();
        mc.convex = true;
        return go;
    }

    static GameObject Sphere(Transform parent, string name, Vector3 pos, float diameter, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.isStatic = true;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = Vector3.one * diameter;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }
}
