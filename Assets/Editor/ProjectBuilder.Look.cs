using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

/// <summary>
/// The realism pass: per-map post-processing (color grading, AO, bloom, vignette), procedural skies, and ground
/// clutter (grass tufts, pebbles, snow drifts, scrub, puddles, litter) merged into a few big meshes per map.
/// </summary>
public static partial class ProjectBuilder
{
    public const int PostFxLayer = 12;

    static PostProcessResources PostResources =>
        AssetDatabase.LoadAssetAtPath<PostProcessResources>("Packages/com.unity.postprocessing/PostProcessing/PostProcessResources.asset");

    /// <summary>Color grading etc. on a camera; the active map's volume decides the look.</summary>
    static void AddPostLayer(Camera cam)
    {
        var l = cam.gameObject.AddComponent<PostProcessLayer>();
        l.Init(PostResources);
        l.volumeLayer = 1 << PostFxLayer;
        l.volumeTrigger = cam.transform;
        l.antialiasingMode = PostProcessLayer.Antialiasing.None;
    }

    struct Look
    {
        public float Temperature, Tint, Saturation, Contrast, Exposure, Bloom, Vignette, Ao;
        public Color Filter, SkyTint, SkyGround;
        public float Atmosphere, SkyExposure;
    }

    /// <summary>Gives the current map (`world`) a global post-processing volume; returns its sky material.</summary>
    static Material ApplyLook(string map, Look look)
    {
        string path = $"{GeneratedDir}/Post_{map.Replace(' ', '_')}.asset";
        AssetDatabase.DeleteAsset(path);
        var profile = ScriptableObject.CreateInstance<PostProcessProfile>();
        AssetDatabase.CreateAsset(profile, path);
        T Add<T>() where T : PostProcessEffectSettings
        {
            var s = profile.AddSettings<T>();
            s.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(s, profile);
            return s;
        }
        var cg = Add<ColorGrading>();
        cg.enabled.Override(true);
        cg.tonemapper.Override(Tonemapper.ACES);
        cg.temperature.Override(look.Temperature);
        cg.tint.Override(look.Tint);
        cg.saturation.Override(look.Saturation);
        cg.contrast.Override(look.Contrast);
        cg.postExposure.Override(look.Exposure);
        cg.colorFilter.Override(look.Filter);
        var ao = Add<AmbientOcclusion>();
        ao.enabled.Override(true);
        ao.mode.Override(AmbientOcclusionMode.MultiScaleVolumetricObscurance);
        ao.intensity.Override(look.Ao);
        ao.thicknessModifier.Override(1.2f);
        var bloom = Add<Bloom>();
        bloom.enabled.Override(true);
        bloom.intensity.Override(look.Bloom);
        bloom.threshold.Override(1.05f);
        bloom.softKnee.Override(0.5f);
        var vig = Add<Vignette>();
        vig.enabled.Override(true);
        vig.intensity.Override(look.Vignette);
        vig.smoothness.Override(0.45f);
        EditorUtility.SetDirty(profile);

        var go = new GameObject("Post Processing") { layer = PostFxLayer };
        go.transform.SetParent(world, false);
        var volume = go.AddComponent<PostProcessVolume>();
        volume.isGlobal = true;
        volume.priority = 1f;
        volume.sharedProfile = profile;

        var sky = new Material(Shader.Find("Skybox/Procedural"));
        sky.SetFloat("_SunSize", 0.035f);
        sky.SetFloat("_SunSizeConvergence", 6f);
        sky.SetFloat("_AtmosphereThickness", look.Atmosphere);
        sky.SetColor("_SkyTint", look.SkyTint);
        sky.SetColor("_GroundColor", look.SkyGround);
        sky.SetFloat("_Exposure", look.SkyExposure);
        return Save(sky, $"Sky_{map.Replace(' ', '_')}");
    }

    // ------------------------------------------------------------------ clutter: many small things, few draw calls

    /// <summary>Collects small decorative meshes and merges them per material and 45 m tile (no colliders).</summary>
    class Clutter
    {
        readonly Dictionary<(Material mat, int tile, bool shadows), List<CombineInstance>> groups = new();

        public void Add(Mesh mesh, Material mat, Vector3 pos, Quaternion rot, Vector3 scale, bool shadows = false)
        {
            int tile = Mathf.FloorToInt((pos.x + 300f) / 45f) * 100 + Mathf.FloorToInt((pos.z + 300f) / 45f);
            var key = (mat, tile, shadows);
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<CombineInstance>();
            list.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.TRS(pos, rot, scale) });
        }

        public void Build(string map)
        {
            var parent = new GameObject("Clutter").transform;
            parent.SetParent(world, false);
            int n = 0;
            foreach (var kv in groups)
            {
                var all = kv.Value;
                for (int start = 0; start < all.Count;)
                {
                    // Stay under the 16-bit vertex limit per chunk.
                    int verts = 0, end = start;
                    while (end < all.Count && verts + all[end].mesh.vertexCount < 64000) verts += all[end++].mesh.vertexCount;
                    var mesh = new Mesh { name = $"Clutter_{map}_{n}" };
                    mesh.CombineMeshes(all.GetRange(start, end - start).ToArray(), true, true);
                    mesh.RecalculateBounds();
                    SaveMesh(mesh, $"Clutter_{map.Replace(' ', '_')}_{n}");
                    var go = MeshObject(parent, "Clutter", mesh, new[] { kv.Key.mat }, Vector3.zero, Quaternion.identity, Vector3.one);
                    var r = go.GetComponent<MeshRenderer>();
                    r.shadowCastingMode = kv.Key.shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                    n++;
                    start = end;
                }
            }
        }
    }

    static Mesh builtinSphere, builtinCylinder;

    static Mesh BuiltinMesh(PrimitiveType type)
    {
        var tmp = GameObject.CreatePrimitive(type);
        var mesh = tmp.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(tmp);
        return mesh;
    }

    static Mesh SphereMesh => builtinSphere != null ? builtinSphere : builtinSphere = BuiltinMesh(PrimitiveType.Sphere);
    static Mesh DiscMesh => builtinCylinder != null ? builtinCylinder : builtinCylinder = BuiltinMesh(PrimitiveType.Cylinder);

    /// <summary>A tuft of grass: thin blades fanning out from one spot, each with a front and a back face.</summary>
    static Mesh TuftMesh(int variant) => Cached("Tuft" + variant, () =>
    {
        var rng = new System.Random(300 + variant);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        var mb = new MeshBuilder(1);
        int blades = 7 + variant * 2;
        for (int i = 0; i < blades; i++)
        {
            float ang = R(0f, Mathf.PI * 2f), lean = R(0.1f, 0.35f), h = R(0.3f, 0.6f), w = R(0.03f, 0.05f);
            Vector3 dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)), side = new Vector3(-dir.z, 0f, dir.x);
            Vector3 root = dir * R(0f, 0.08f), tip = root + dir * lean + Vector3.up * h;
            Vector3 a = root - side * w, b = root + side * w;
            int a0 = mb.Add(a, new Vector2(0f, 0f)), b0 = mb.Add(b, new Vector2(1f, 0f)), t0 = mb.Add(tip, new Vector2(0.5f, 1f));
            mb.Tri(0, a0, t0, b0);
            int a1 = mb.Add(a, new Vector2(0f, 0f)), b1 = mb.Add(b, new Vector2(1f, 0f)), t1 = mb.Add(tip, new Vector2(0.5f, 1f));
            mb.Tri(0, a1, b1, t1);
        }
        return mb.Build("Tuft" + variant);
    });

    // ------------------------------------------------------------------ per-map dressing

    static bool GroundHit(float x, float z, out RaycastHit hit) =>
        Physics.Raycast(new Vector3(x, 120f, z), Vector3.down, out hit, 250f, 1, QueryTriggerInteraction.Ignore);

    static void DressFluketown()
    {
        Physics.SyncTransforms();
        var rng = new System.Random(71);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        var c = new Clutter();
        var tufts = new[] { TuftMesh(0), TuftMesh(1), TuftMesh(2) };
        var pebbles = new[] { BoulderMesh(0), BoulderMesh(1), BoulderMesh(2) };
        for (int i = 0; i < 9000; i++)
        {
            float x = R(MapMinX + 1f, MapMaxX - 1f), z = R(MapMinZ + 1f, MapMaxZ - 1f);
            if (!GroundHit(x, z, out var hit) || hit.collider.name != "Grass") continue;
            float s = R(0.7f, 1.4f);
            c.Add(tufts[rng.Next(3)], M(rng.NextDouble() < 0.7 ? "GrassTuft" : "GrassTuftDry"), hit.point, Quaternion.Euler(0f, R(0f, 360f), 0f), new Vector3(s, s * R(0.8f, 1.2f), s));
            if (rng.NextDouble() < 0.04)
                c.Add(pebbles[rng.Next(3)], M("Rock"), hit.point + Vector3.down * 0.05f, Quaternion.Euler(0f, R(0f, 360f), 0f), Vector3.one * R(0.15f, 0.4f), true);
        }
        c.Build("Fluketown");
        // Street lights along the avenue and the ring road.
        for (float x = -80f; x <= 80f; x += 22f)
            if (Mathf.Abs(x) > 9f && Mathf.Abs(Mathf.Abs(x) - 48f) > 6f && Mathf.Abs(Mathf.Abs(x) - 73f) > 6f)
                StreetLight(new Vector3(x, 0.12f, 42.2f), 180f);
        foreach (float sx in new[] { -1f, 1f })
            for (float z = -40f; z <= 64f; z += 26f)
                if (Mathf.Abs(z - 36f) > 8f)
                    StreetLight(new Vector3(sx * 67.6f, 0f, z), sx < 0 ? 90f : -90f);
    }

    static void DressTimberline()
    {
        var rng = new System.Random(72);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        var c = new Clutter();
        var tufts = new[] { TuftMesh(0), TuftMesh(1), TuftMesh(2) };
        var pebbles = new[] { BoulderMesh(0), BoulderMesh(1), BoulderMesh(2) };
        for (int i = 0; i < 11000; i++)
        {
            var p = new Vector2(R(MapMinX + 1f, MapMaxX - 1f), R(MapMinZ + 1f, MapMaxZ - 1f));
            if (PathDistance(p) < 1.2f || PondEllipse(p.x, p.y) < 1.15f || IsClear(p)) continue;
            Vector3 pos = new Vector3(p.x, CampHeight(p.x, p.y) - 0.02f, p.y);
            float s = R(0.7f, 1.5f);
            string mat = HillAmount(p.x) > 0.5f ? (rng.NextDouble() < 0.5 ? "GrassTuftDry" : "Fern") : rng.NextDouble() < 0.35 ? "Fern" : "GrassTuft";
            c.Add(tufts[rng.Next(3)], M(mat), pos, Quaternion.Euler(0f, R(0f, 360f), 0f), new Vector3(s, s * R(0.8f, 1.3f), s));
            if (rng.NextDouble() < 0.05)
                c.Add(pebbles[rng.Next(3)], M("Rock"), pos + Vector3.down * 0.05f, Quaternion.Euler(0f, R(0f, 360f), 0f), Vector3.one * R(0.15f, 0.45f), true);
        }
        c.Build("Timberline");
    }

    static void DressDowntown()
    {
        Physics.SyncTransforms();
        var rng = new System.Random(73);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        var c = new Clutter();
        bool OnRoad(float x, float z, out RaycastHit hit) => GroundHit(x, z, out hit) && hit.collider.name == "Street Ground";
        // Puddles and manhole covers on the roads.
        for (int i = 0, tries = 0; i < 60 && tries < 4000; tries++)
        {
            float x = R(MapMinX + 2f, MapMaxX - 2f), z = R(MapMinZ + 2f, MapMaxZ - 2f);
            if (!OnRoad(x, z, out _)) continue;
            c.Add(DiscMesh, M("Puddle"), new Vector3(x, 0.012f, z), Quaternion.Euler(0f, R(0f, 180f), 0f), new Vector3(R(1.2f, 3.6f), 0.002f, R(0.9f, 2.4f)));
            i++;
        }
        foreach (float a in AvenueX)
            for (float z = MapMinZ + 8f; z < MapMaxZ - 6f; z += R(16f, 26f))
                if (OnRoad(a + 2f, z, out _)) c.Add(DiscMesh, M("Manhole"), new Vector3(a + 2f, 0.014f, z), Quaternion.identity, new Vector3(0.9f, 0.003f, 0.9f));
        foreach (float s in StreetZ)
            for (float x = MapMinX + 8f; x < MapMaxX - 6f; x += R(16f, 26f))
                if (OnRoad(x, s - 2f, out _)) c.Add(DiscMesh, M("Manhole"), new Vector3(x, 0.014f, s - 2f), Quaternion.identity, new Vector3(0.9f, 0.003f, 0.9f));
        // Trash bags at the kerbs, weeds in the cracks.
        for (int i = 0, tries = 0; i < 70 && tries < 4000; tries++)
        {
            float x = R(MapMinX + 2f, MapMaxX - 2f), z = R(MapMinZ + 2f, MapMaxZ - 2f);
            if (!GroundHit(x, z, out var hit) || hit.collider.name != "Block" || hit.point.y > Curb + 0.05f) continue;
            // Only right at the kerb edge (a road within 1.5 m).
            if (!OnRoad(x + 1.5f, z, out _) && !OnRoad(x - 1.5f, z, out _) && !OnRoad(x, z + 1.5f, out _) && !OnRoad(x, z - 1.5f, out _)) continue;
            for (int b = 0; b < 1 + rng.Next(3); b++)
                c.Add(SphereMesh, M("TrashBag"), hit.point + new Vector3(R(-0.5f, 0.5f), 0.2f, R(-0.5f, 0.5f)), Quaternion.Euler(R(0f, 30f), R(0f, 360f), 0f), new Vector3(R(0.45f, 0.6f), R(0.38f, 0.5f), R(0.45f, 0.6f)), true);
            i++;
        }
        var tufts = new[] { TuftMesh(0), TuftMesh(1) };
        for (int i = 0, tries = 0; i < 260 && tries < 6000; tries++)
        {
            float x = R(MapMinX + 1f, MapMaxX - 1f), z = R(MapMinZ + 1f, MapMaxZ - 1f);
            if (!GroundHit(x, z, out var hit) || (hit.collider.name != "Block" && hit.collider.name != "Street Ground")) continue;
            c.Add(tufts[rng.Next(2)], M("GrassTuftDry"), hit.point, Quaternion.Euler(0f, R(0f, 360f), 0f), Vector3.one * R(0.5f, 0.9f));
            i++;
        }
        c.Build("Downtown");
    }

    static void DressFrostpeak()
    {
        var rng = new System.Random(74);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        var c = new Clutter();
        var tufts = new[] { TuftMesh(0), TuftMesh(1), TuftMesh(2) };
        var pebbles = new[] { BoulderMesh(0), BoulderMesh(1), BoulderMesh(2) };
        bool Open(Vector2 p) => SegDistance(p, LoggingRoad) > 4f && LakeEllipse(p.x, p.y) > 1.2f && RectWeight(p.x, p.y, -30f, 34f, -14f, 34f, 0.01f) < 0.5f;
        for (int i = 0; i < 700; i++)
        {
            var p = new Vector2(R(MapMinX + 2f, MapMaxX - 2f), R(MapMinZ + 2f, MapMaxZ - 2f));
            if (!Open(p)) continue;
            float s = R(1.5f, 4.5f);
            c.Add(SphereMesh, M("Snow"), SnowG(p.x, p.y, -s * 0.12f), Quaternion.Euler(0f, R(0f, 360f), 0f), new Vector3(s, s * R(0.18f, 0.32f), s * R(0.6f, 1f)), true);
        }
        for (int i = 0; i < 3500; i++)
        {
            var p = new Vector2(R(MapMinX + 1f, MapMaxX - 1f), R(MapMinZ + 1f, MapMaxZ - 1f));
            if (LakeEllipse(p.x, p.y) < 1.1f || SegDistance(p, LoggingRoad) < 3f) continue;
            float s = R(0.6f, 1.2f);
            c.Add(tufts[rng.Next(3)], M("GrassTuftDry"), SnowG(p.x, p.y, -0.08f), Quaternion.Euler(0f, R(0f, 360f), 0f), new Vector3(s, s * R(0.6f, 1f), s));
            if (rng.NextDouble() < 0.08)
                c.Add(pebbles[rng.Next(3)], M("SnowRock"), SnowG(p.x, p.y, -0.05f), Quaternion.Euler(0f, R(0f, 360f), 0f), Vector3.one * R(0.2f, 0.5f), true);
        }
        c.Build("Frostpeak");
    }

    static void DressDustRidge()
    {
        var rng = new System.Random(75);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        var c = new Clutter();
        var tufts = new[] { TuftMesh(0), TuftMesh(1), TuftMesh(2) };
        var pebbles = new[] { BoulderMesh(0), BoulderMesh(1), BoulderMesh(2) };
        bool Town(Vector2 p) => RectWeight(p.x, p.y, -56f, 56f, -16f, 30f, 0.01f) > 0.5f || Mathf.Abs(p.y - RailZ) < 3f;
        for (int i = 0; i < 5000; i++)
        {
            var p = new Vector2(R(MapMinX + 1f, MapMaxX - 1f), R(MapMinZ + 1f, MapMaxZ - 1f));
            if (Town(p) && rng.NextDouble() < 0.85) continue;
            float h = DesertHeight(p.x, p.y);
            bool scrub = rng.NextDouble() < 0.35;
            float s = scrub ? R(1.4f, 2.6f) : R(0.6f, 1.2f);
            c.Add(tufts[rng.Next(3)], M(scrub ? "Scrub" : "GrassTuftDry"), new Vector3(p.x, h - 0.05f, p.y), Quaternion.Euler(0f, R(0f, 360f), 0f), new Vector3(s, s * R(0.5f, 0.9f), s), scrub);
            if (rng.NextDouble() < 0.12)
                c.Add(pebbles[rng.Next(3)], M("RedRock"), new Vector3(p.x + R(-1f, 1f), h - 0.05f, p.y + R(-1f, 1f)), Quaternion.Euler(0f, R(0f, 360f), 0f), Vector3.one * R(0.15f, 0.5f), true);
        }
        for (int i = 0; i < 30; i++)
        {
            var p = new Vector2(R(MapMinX + 3f, MapMaxX - 3f), R(MapMinZ + 3f, MapMaxZ - 3f));
            float s = R(0.6f, 1f);
            c.Add(SphereMesh, M("Tumbleweed"), DesertG(p.x, p.y, s * 0.4f), Quaternion.Euler(R(0f, 360f), R(0f, 360f), 0f), new Vector3(s, s * 0.85f, s), true);
        }
        c.Build("Dust Ridge");
    }
}
