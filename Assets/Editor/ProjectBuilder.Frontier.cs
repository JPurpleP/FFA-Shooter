using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Two outdoor maps on height-mapped ground, the same playable size as the others (X -90..90, Z -64..84):
/// Frostpeak, a logging camp in a snowy mountain valley, and Dust Ridge, a wild-west town among tall mesas.
/// </summary>
public static partial class ProjectBuilder
{
    // ================================================================== shared terrain helpers

    /// <summary>Detailed walkable ground (1 m grid) over the play area plus a margin, with a submesh per material.</summary>
    static void HeightGround(string asset, System.Func<float, float, float> height, Material[] mats, System.Func<float, float, float, int> submesh)
    {
        const float pad = 34f, step = 1f;
        float x0 = MapMinX - pad, x1 = MapMaxX + pad, z0 = MapMinZ - pad, z1 = MapMaxZ + pad;
        int nx = Mathf.RoundToInt((x1 - x0) / step) + 1, nz = Mathf.RoundToInt((z1 - z0) / step) + 1;
        var mb = new MeshBuilder(mats.Length);
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                float x = x0 + i * step, z = z0 + j * step;
                mb.Add(new Vector3(x, height(x, z), z), new Vector2(x, z) / 5f);
            }
        for (int i = 0; i + 1 < nx; i++)
            for (int j = 0; j + 1 < nz; j++)
            {
                int a = i * nz + j, b = a + 1, c = a + nz + 1, dd = a + nz;
                float mx = x0 + (i + 0.5f) * step, mz = z0 + (j + 0.5f) * step;
                int s = Mathf.Clamp(submesh(mx, mz, height(mx, mz)), 0, mats.Length - 1);
                mb.Tri(s, a, b, c);
                mb.Tri(s, a, c, dd);
            }
        var mesh = SaveMesh(mb.Build(asset), asset);
        var go = MeshObject(world, "Ground", mesh, mats, Vector3.zero, Quaternion.identity, Vector3.one);
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    /// <summary>Coarse scenery ground out to the horizon, with a hole where the detailed ground sits.</summary>
    static void HeightFar(string asset, System.Func<float, float, float> height, Material mat)
    {
        const float r = 420f, step = 10f, pad = 34f;
        int n = Mathf.RoundToInt(r * 2f / step) + 1;
        var mb = new MeshBuilder(1);
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                float x = -r + i * step, z = -r + j * step;
                mb.Add(new Vector3(x, height(x, z), z), new Vector2(x, z) / 5f);
            }
        for (int i = 0; i + 1 < n; i++)
            for (int j = 0; j + 1 < n; j++)
            {
                float cx = -r + (i + 0.5f) * step, cz = -r + (j + 0.5f) * step;
                if (cx > MapMinX - pad && cx < MapMaxX + pad && cz > MapMinZ - pad && cz < MapMaxZ + pad) continue;
                int a = i * n + j, b = a + 1, c = a + n + 1, d = a + n;
                mb.Tri(0, a, b, c);
                mb.Tri(0, a, c, d);
            }
        var mesh = SaveMesh(mb.Build(asset), asset);
        MeshObject(world, "Far Ground", mesh, new[] { mat }, Vector3.zero, Quaternion.identity, Vector3.one);
    }

    /// <summary>Extra ground height past the play area: the world climbs away into hills / mountains.</summary>
    static float OutsideRise(float x, float z, float slope, float cap, float noise)
    {
        float ox = Mathf.Max(0f, Mathf.Max(MapMinX - x, x - MapMaxX)), oz = Mathf.Max(0f, Mathf.Max(MapMinZ - z, z - MapMaxZ));
        float o = Mathf.Sqrt(ox * ox + oz * oz);
        if (o <= 0f) return 0f;
        return Mathf.Min(o * slope, cap) + Mathf.PerlinNoise(x * 0.008f + 5f, z * 0.008f + 11f) * Mathf.Min(o * 0.5f, noise);
    }

    static float SegDistance(Vector2 p, Vector2[] line)
    {
        float best = float.MaxValue;
        for (int i = 0; i + 1 < line.Length; i++)
        {
            Vector2 a = line[i], ab = line[i + 1] - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            best = Mathf.Min(best, (a + ab * t - p).magnitude);
        }
        return best;
    }

    static void SpawnPoints(System.Func<float, float, Vector3> ground, Vector2 lookAt, Vector2[] spots)
    {
        var spawns = new GameObject("Spawn Points").transform;
        spawns.SetParent(world, false);
        foreach (var p in spots)
        {
            var sp = new GameObject("Spawn");
            sp.transform.SetParent(spawns, false);
            sp.transform.SetPositionAndRotation(ground(p.x, p.y) + Vector3.up * 0.1f,
                Quaternion.Euler(0f, Mathf.Atan2(lookAt.x - p.x, lookAt.y - p.y) * Mathf.Rad2Deg, 0f));
            sp.AddComponent<SpawnPoint>();
        }
    }

    static void InvisibleWalls(float height)
    {
        float w = MapMaxX - MapMinX, d = MapMaxZ - MapMinZ, cz = (MapMinZ + MapMaxZ) / 2f;
        Box(world, "Invisible Wall", new Vector3(0f, height / 2f - 10f, MapMaxZ + 0.2f), new Vector3(w + 1f, height, 0.3f), null);
        Box(world, "Invisible Wall", new Vector3(0f, height / 2f - 10f, MapMinZ - 0.2f), new Vector3(w + 1f, height, 0.3f), null);
        Box(world, "Invisible Wall", new Vector3(MapMinX - 0.2f, height / 2f - 10f, cz), new Vector3(0.3f, height, d + 1f), null);
        Box(world, "Invisible Wall", new Vector3(MapMaxX + 0.2f, height / 2f - 10f, cz), new Vector3(0.3f, height, d + 1f), null);
    }

    /// <summary>A pile of logs, pyramid stacked along local Z: solid cover.</summary>
    static void LogPile(Vector3 pos, float yaw, float length, int baseCount)
    {
        var t = Building("Log Pile", pos, yaw);
        float r = 0.32f;
        for (int row = 0; row < baseCount; row++)
            for (int i = 0; i < baseCount - row; i++)
            {
                float x = (i - (baseCount - row - 1) / 2f) * r * 2.05f;
                var log = Cylinder(t, "Log", new Vector3(x, r + row * r * 1.75f, 0f), r, length, M("Bark"));
                log.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
        // One box collider for the whole stack (cheap and with no gaps to snag on).
        var col = t.gameObject.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, baseCount * r * 0.9f, 0f);
        col.size = new Vector3(baseCount * r * 2.05f, baseCount * r * 1.75f, length);
    }

    static void Barrel(Vector3 pos) => Cylinder(world, "Barrel", pos + Vector3.up * 0.5f, 0.32f, 1f, M("WesternWood"));

    /// <summary>Wooden wagon (four spoked-less wheels) facing along local Z; optional canvas cover.</summary>
    static void Wagon(Vector3 pos, float yaw, bool covered)
    {
        var t = Building("Wagon", pos, yaw);
        Box(t, "Bed", new Vector3(0f, 1f, 0f), new Vector3(1.6f, 0.5f, 3.4f), M("WesternWood"));
        foreach (float sx in new[] { -0.9f, 0.9f })
            foreach (float sz in new[] { -1.2f, 1.2f })
            {
                var w = Cylinder(t, "Wheel", new Vector3(sx, 0.55f, sz), 0.55f, 0.12f, M("WesternWoodDark"));
                w.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
        if (covered)
            for (int i = 0; i < 4; i++)
            {
                var arch = Cylinder(t, "Cover", new Vector3(0f, 1.5f, -1.2f + i * 0.8f), 0.85f, 0.75f, M("Canvas"));
                arch.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                arch.transform.localScale = new Vector3(1.7f, 0.4f, 1.3f);
            }
    }

    static void Cactus(Vector3 pos, float height, int seed)
    {
        var rng = new System.Random(seed);
        var t = Building("Cactus", pos, (float)rng.NextDouble() * 360f);
        Cylinder(t, "Trunk", new Vector3(0f, height / 2f, 0f), 0.28f, height, M("Cactus"));
        foreach (float side in new[] { -1f, 1f })
        {
            if (rng.NextDouble() < 0.3) continue;
            float y = height * (0.35f + (float)rng.NextDouble() * 0.25f), up = height * 0.35f;
            var arm = Cylinder(t, "Arm", new Vector3(side * 0.45f, y, 0f), 0.18f, 0.7f, M("Cactus"));
            arm.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Cylinder(t, "Arm Up", new Vector3(side * 0.8f, y + up / 2f, 0f), 0.18f, up, M("Cactus"));
        }
    }

    // ================================================================== Frostpeak (snowy logging camp)

    const float LakeX = -44f, LakeZ = -36f, LakeRX = 18f, LakeRZ = 11f, IceY = -0.7f;
    static readonly Vector2[] LoggingRoad = { new(8f, 120f), new(8f, 44f), new(14f, 30f), new(32f, 10f), new(52f, -12f), new(64f, -40f), new(70f, -100f) };
    static readonly List<(Vector2 c, float r)> snowClear = new();

    static float LakeEllipse(float x, float z)
    {
        float ex = (x - LakeX) / LakeRX, ez = (z - LakeZ) / LakeRZ;
        return ex * ex + ez * ez;
    }

    static float SnowHeight(float x, float z)
    {
        // A valley running north-south: the ground climbs toward every edge into snowy ridges.
        float ex = Mathf.Abs(x) / 90f, ez = Mathf.Abs(z - 10f) / 74f;
        float m = Smooth01((Mathf.Max(ex, ez * 0.9f) - 0.42f) / 0.58f);
        float ridge = 0.6f + 0.8f * Mathf.PerlinNoise(x * 0.03f + 11f, z * 0.03f + 5f);
        float n = (Mathf.PerlinNoise(x * 0.05f + 40f, z * 0.05f + 20f) - 0.5f) * 2.4f + (Mathf.PerlinNoise(x * 0.15f, z * 0.15f) - 0.5f) * 0.5f;
        float h = m * m * 15f * ridge + n * Mathf.Lerp(0.4f, 1.1f, m);
        // The camp sits in a flat clearing; the road is graded smooth.
        float camp = RectWeight(x, z, -30f, 34f, -14f, 34f, 10f);
        h = Mathf.Lerp(h, n * 0.08f, camp);
        float road = 1f - Smooth01((SegDistance(new Vector2(x, z), LoggingRoad) - 3f) / 5f);
        h = Mathf.Lerp(h, h * 0.55f + n * 0.05f, road);
        // Frozen lake: a flat sheet of ice.
        float bowl = Smooth01((1.35f - LakeEllipse(x, z)) / 1.35f);
        h = Mathf.Lerp(h, IceY, bowl);
        return h + OutsideRise(x, z, 0.95f, 45f, 75f);
    }

    static Vector3 SnowG(float x, float z, float lift = 0f) => new(x, SnowHeight(x, z) + lift, z);

    static void BuildFrostpeak()
    {
        world = new GameObject("Frostpeak").transform;
        snowClear.Clear();
        var rng = new System.Random(5150);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        void Clear(float x, float z, float r) => snowClear.Add((new Vector2(x, z), r));
        bool IsClear(Vector2 p)
        {
            foreach (var (c, r) in snowClear) if ((p - c).sqrMagnitude < r * r) return true;
            return false;
        }

        // ---------------------------------------------------------------- ground
        HeightGround("SnowGround", SnowHeight, new[] { M("Snow"), M("SnowRoad"), M("Ice"), M("SnowRock") }, (x, z, h) =>
        {
            if (LakeEllipse(x, z) < 0.95f) return 2;
            if (SegDistance(new Vector2(x, z), LoggingRoad) < 3.2f || RectWeight(x, z, -26f, 30f, -10f, 30f, 0.01f) > 0.5f) return 1;
            // Snow doesn't stick to the steep bits: rock shows through.
            float sx = SnowHeight(x + 1f, z) - SnowHeight(x - 1f, z), sz = SnowHeight(x, z + 1f) - SnowHeight(x, z - 1f);
            return Mathf.Sqrt(sx * sx + sz * sz) / 2f > 0.75f ? 3 : 0;
        });
        HeightFar("SnowFar", SnowHeight, M("Snow"));
        InvisibleWalls(80f);

        // ---------------------------------------------------------------- the logging camp
        BuildSawmill(SnowG(2f, 18f), 0f);
        Clear(2f, 18f, 13f);
        var logs = M("CabinLogs");
        var snowRoof = M("SnowRoof");
        BuildCabin("Bunkhouse", SnowG(-20f, 24f), 90f, 7f, 11f, 3f, logs, snowRoof, backDoor: true);
        BuildCabin("Bunkhouse", SnowG(-20f, 6f), 90f, 7f, 11f, 3f, logs, snowRoof, backDoor: true);
        BuildCabin("Cookhouse", SnowG(24f, 22f), 270f, 9f, 13f, 3.2f, logs, snowRoof, backDoor: true, bunks: false);
        BuildCabin("Camp Office", SnowG(24f, -2f), 270f, 6f, 7f, 3f, logs, snowRoof, bunks: false);
        BuildCabin("Tool Shed", SnowG(-4f, -8f), 0f, 5f, 6f, 2.8f, logs, snowRoof, backDoor: true, bunks: false);
        foreach (var (x, z, r) in new[] { (-20f, 24f, 7f), (-20f, 6f, 7f), (24f, 22f, 8f), (24f, -2f, 5f), (-4f, -8f, 4f) }) Clear(x, z, r);

        foreach (var (x, z, yaw, len, n) in new[] { (-6f, 4f, 10f, 7f, 5), (10f, -6f, 80f, 8f, 4), (-28f, -8f, 30f, 7f, 5), (32f, 10f, 0f, 9f, 5),
                                                     (14f, 36f, 95f, 8f, 4), (-12f, 38f, 15f, 6f, 4), (-30f, 34f, 70f, 7f, 3), (40f, -16f, 40f, 8f, 4) })
        {
            LogPile(SnowG(x, z), yaw, len, n);
            Clear(x, z, len * 0.6f);
        }
        // Fuel tanks and a generator shed by the sawmill.
        foreach (var x in new[] { 16f, 19f })
        {
            var tank = Cylinder(world, "Fuel Tank", SnowG(x, 8f, 1.1f), 1.1f, 4f, M("TruckRed"));
            tank.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }
        Clear(17.5f, 8f, 4f);
        // Logging trucks on the road.
        BuildSemi(SnowG(10f, 50f));
        BuildSemi(SnowG(44f, -2f));
        Clear(10f, 50f, 9f);
        Clear(44f, -2f, 9f);
        // Snowmen.
        foreach (var (x, z) in new[] { (-6f, 14f), (12f, -14f), (-40f, -24f) })
        {
            var b = SnowG(x, z);
            Sphere(world, "Snowman", b + Vector3.up * 0.55f, 0.6f, M("Snow"));
            Sphere(world, "Snowman", b + Vector3.up * 1.4f, 0.42f, M("Snow"));
            Sphere(world, "Snowman", b + Vector3.up * 2.0f, 0.28f, M("Snow"));
        }

        // ---------------------------------------------------------------- frozen lake, ridges, clear-cut
        foreach (var (x, z, yaw) in new[] { (-50f, -32f, 30f), (-38f, -40f, 200f), (-44f, -30f, 110f) })
        {
            Shed(new Vector3(x, IceY, z), yaw);
            Clear(x, z, 3f);
        }
        Clear(LakeX, LakeZ, 1f);
        BuildLookout(SnowG(-70f, 32f), 90f);
        Clear(-70f, 32f, 5f);
        BuildDeerStand(SnowG(70f, 52f), 270f);
        Clear(70f, 52f, 4f);
        BuildDeerStand(SnowG(-72f, -46f), 45f);
        Clear(-72f, -46f, 4f);
        BuildCabin("Ranger Hut", SnowG(-42f, 54f), 180f, 6f, 8f, 3f, logs, snowRoof, backDoor: true, bunks: false);
        Clear(-42f, 54f, 6f);
        // Clear-cut field to the south-east: stumps and felled trunks.
        for (int i = 0; i < 26; i++)
        {
            var p = new Vector2(R(36f, 80f), R(-58f, -24f));
            if (SegDistance(p, LoggingRoad) < 5f) continue;
            Cylinder(world, "Stump", SnowG(p.x, p.y, 0.25f), R(0.3f, 0.45f), 0.6f, M("Bark"));
            Clear(p.x, p.y, 1.6f);
        }
        for (int i = 0; i < 8; i++)
        {
            var p = new Vector2(R(38f, 78f), R(-56f, -26f));
            if (SegDistance(p, LoggingRoad) < 6f) continue;
            var log = Cylinder(world, "Felled Log", SnowG(p.x, p.y, 0.4f), 0.4f, R(5f, 8f), M("Bark"));
            log.transform.localRotation = Quaternion.Euler(90f, R(0f, 180f), 0f);
            Clear(p.x, p.y, 4f);
        }

        // ---------------------------------------------------------------- jump pads
        JumpPadTo(SnowG(-12f, -4f), SnowG(-36f, -28f), 6f);   // camp -> the lake
        JumpPadTo(SnowG(30f, 28f), SnowG(60f, 42f), 6f);      // camp -> east ridge
        JumpPadTo(SnowG(-2f, 40f), SnowG(-30f, 62f), 6f);     // north road -> ranger hut
        JumpPadTo(SnowG(50f, -24f), SnowG(74f, -48f), 6f);    // clear-cut -> south-east ridge
        JumpPadTo(SnowG(-48f, 12f), SnowG(-72f, -6f), 6f);    // west slope -> ridge
        JumpPadTo(SnowG(-26f, -48f), SnowG(-4f, -58f), 6f);   // lake -> south slope

        // ---------------------------------------------------------------- spawns
        var spawnXZ = new[]
        {
            new Vector2(-8f, 28f), new Vector2(10f, 30f), new Vector2(-12f, 0f), new Vector2(14f, -10f), new Vector2(32f, 30f), new Vector2(-30f, 16f),
            new Vector2(-52f, -18f), new Vector2(-30f, -46f), new Vector2(-62f, -34f), new Vector2(-70f, 10f), new Vector2(-64f, 50f), new Vector2(-30f, 62f),
            new Vector2(8f, 64f), new Vector2(40f, 66f), new Vector2(70f, 30f), new Vector2(66f, 4f), new Vector2(60f, -30f), new Vector2(40f, -52f),
            new Vector2(10f, -48f), new Vector2(-10f, -30f), new Vector2(44f, 12f), new Vector2(-46f, 40f), new Vector2(80f, 70f), new Vector2(-80f, 74f),
        };
        SpawnPoints((x, z) => SnowG(x, z), new Vector2(0f, 10f), spawnXZ);
        foreach (var p in spawnXZ) Clear(p.x, p.y, 2.5f);

        // ---------------------------------------------------------------- forest (snowy pines) and rocks
        var pines = new[] { PineMesh(0), PineMesh(1) };
        var pineMats = new[] { M("Bark"), M("SnowyNeedles") };
        var trees = new GameObject("Trees").transform;
        trees.SetParent(world, false);
        for (float gx = MapMinX - 34f; gx <= MapMaxX + 34f; gx += 4.6f)
            for (float gz = MapMinZ - 36f; gz <= MapMaxZ + 36f; gz += 4.6f)
            {
                var p = new Vector2(gx + R(-1.8f, 1.8f), gz + R(-1.8f, 1.8f));
                bool inside = InPlayArea(p, 1.5f);
                bool clearing = RectWeight(p.x, p.y, -30f, 34f, -14f, 34f, 0.01f) > 0.5f;
                if (!inside && rng.NextDouble() < 0.25) continue;
                if (inside && rng.NextDouble() > (clearing ? 0.04 : 0.5)) continue;
                if (IsClear(p) || SegDistance(p, LoggingRoad) < 4f || LakeEllipse(p.x, p.y) < 1.3f) continue;
                if (p.x > 34f && p.y < -22f && p.y > -60f && inside) continue; // the clear-cut
                float scale = R(0.9f, 1.5f);
                var go = MeshObject(trees, "Pine", pines[rng.Next(2)], pineMats, new Vector3(p.x, SnowHeight(p.x, p.y) - 0.25f, p.y),
                    Quaternion.Euler(R(-3f, 3f), R(0f, 360f), R(-3f, 3f)), Vector3.one * scale);
                if (inside)
                {
                    var trunk = go.AddComponent<CapsuleCollider>();
                    trunk.center = new Vector3(0f, 2.5f, 0f);
                    trunk.height = 5f;
                    trunk.radius = 0.3f;
                }
            }
        var boulders = new[] { BoulderMesh(0), BoulderMesh(1), BoulderMesh(2) };
        for (int i = 0, tries = 0; i < 40 && tries < 1500; tries++)
        {
            var p = new Vector2(R(MapMinX + 3f, MapMaxX - 3f), R(MapMinZ + 3f, MapMaxZ - 3f));
            if (IsClear(p) || SegDistance(p, LoggingRoad) < 4f || LakeEllipse(p.x, p.y) < 1.3f || RectWeight(p.x, p.y, -30f, 34f, -14f, 34f, 0.01f) > 0.5f) continue;
            float size = R(1.2f, 3.4f);
            var go = MeshObject(world, "Boulder", boulders[rng.Next(3)], new[] { M("SnowRock") },
                new Vector3(p.x, SnowHeight(p.x, p.y) - size * 0.2f, p.y), Quaternion.Euler(0f, R(0f, 360f), 0f), new Vector3(size, size * R(0.5f, 0.85f), size * R(0.8f, 1.2f)));
            go.AddComponent<MeshCollider>().convex = true;
            Clear(p.x, p.y, size * 0.6f);
            i++;
        }

        // ---------------------------------------------------------------- falling snow
        var snowGo = new GameObject("Snowfall");
        snowGo.transform.SetParent(world, false);
        snowGo.transform.position = new Vector3(0f, 32f, 10f);
        var ps = snowGo.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(9f, 13f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
        main.startColor = new Color(1f, 1f, 1f, 0.9f);
        main.gravityModifier = 0.025f;
        main.maxParticles = 4000;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission;
        em.rateOverTime = 380f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(200f, 1f, 170f);
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.35f;
        noise.frequency = 0.2f;
        var pr = snowGo.GetComponent<ParticleSystemRenderer>();
        pr.sharedMaterial = M("Dust");
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        snowGo.AddComponent<Blizzard>().snow = ps; // a storm every few minutes
        Debug.Log("[FFA] Frostpeak built");
    }

    /// <summary>
    /// The freight train that runs the Dust Ridge main line (TrainRunner moves it): a locomotive, two flatcars with
    /// crates to ride behind and a boxcar. Built facing -X; it turns round to run east.
    /// </summary>
    static void BuildTrain(float trackZ)
    {
        var train = new GameObject("Freight Train").transform;
        train.SetParent(world, false);
        train.position = new Vector3(0f, -200f, trackZ);
        var body = train.gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true; // moving colliders want a kinematic body
        train.gameObject.AddComponent<TrainRunner>().trackZ = trackZ;

        Transform Car(string name, float x)
        {
            var car = new GameObject(name).transform;
            car.SetParent(train, false);
            car.localPosition = new Vector3(x, 0f, 0f);
            foreach (float wx in new[] { -3.5f, 3.5f })
                foreach (float wz in new[] { -0.75f, 0.75f })
                {
                    var wheel = Cylinder(car, "Wheel", new Vector3(wx, 0.5f, wz), 0.5f, 0.1f, M("Metal"));
                    wheel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    Object.DestroyImmediate(wheel.GetComponent<Collider>());
                }
            return car;
        }

        var loco = Car("Locomotive", 0f);
        var boiler = Cylinder(loco, "Boiler", new Vector3(-1f, 1.9f, 0f), 1f, 7f, M("GunDark"));
        boiler.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        Box(loco, "Cab", new Vector3(3.6f, 2.2f, 0f), new Vector3(2.6f, 2.8f, 2.6f), M("WesternRed"));
        Box(loco, "Cab Roof", new Vector3(3.6f, 3.7f, 0f), new Vector3(3f, 0.15f, 3f), M("GunDark"));
        Cylinder(loco, "Stack", new Vector3(-3.6f, 3.3f, 0f), 0.35f, 1.4f, M("GunDark"));
        Box(loco, "Footplate", new Vector3(0f, 0.95f, 0f), new Vector3(9.6f, 0.2f, 2.7f), M("GunDark"));
        Box(loco, "Cowcatcher", new Vector3(-5.1f, 0.55f, 0f), new Vector3(0.9f, 0.7f, 2.5f), M("WesternRed"), zRot: 30f);
        Box(loco, "Headlamp", new Vector3(-4.7f, 2.5f, 0f), new Vector3(0.4f, 0.5f, 0.5f), M("Trim"), collider: false);

        foreach (float x in new[] { 11f, 33.6f })
        {
            var flat = Car("Flatcar", x);
            Box(flat, "Deck", new Vector3(0f, 1.25f, 0f), new Vector3(10f, 0.3f, 2.7f), M("WesternWood"));
            Box(flat, "Crate", new Vector3(-2.6f, 2.1f, 0.3f), new Vector3(1.4f, 1.4f, 1.4f), M("WesternWoodDark"));
            Box(flat, "Crate", new Vector3(2.4f, 2.0f, -0.4f), new Vector3(1.2f, 1.2f, 1.2f), M("WesternWoodDark"), yRot: 20f);
            Box(flat, "Crate", new Vector3(2.4f, 3.0f, -0.4f), new Vector3(0.8f, 0.8f, 0.8f), M("WesternWood"), yRot: 35f);
        }
        var box = Car("Boxcar", 22.3f);
        Box(box, "Boxcar", new Vector3(0f, 2.1f, 0f), new Vector3(9.5f, 2.9f, 2.7f), M("WesternGreen"));
        Box(box, "Roof Walk", new Vector3(0f, 3.6f, 0f), new Vector3(9.5f, 0.1f, 0.6f), M("WesternWoodDark"));

        // It moves: nothing on it can be static-batched.
        foreach (var t in train.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = false;
    }

    /// <summary>Open-sided sawmill: roof on posts, a saw bench, a log deck and stacks of sawn boards.</summary>
    static void BuildSawmill(Vector3 pos, float yaw)
    {
        var t = Building("Sawmill", pos, yaw);
        const float w = 22f, d = 12f, h = 5f;
        Box(t, "Floor", new Vector3(0f, 0.1f, 0f), new Vector3(w, 0.2f, d), M("Floorboards"));
        for (float x = -w / 2f + 0.3f; x <= w / 2f; x += w / 4f - 0.15f)
            foreach (float z in new[] { -d / 2f + 0.3f, d / 2f - 0.3f })
                Box(t, "Post", new Vector3(x, h / 2f, z), new Vector3(0.35f, h, 0.35f), M("CabinLogs"));
        Box(t, "Roof", new Vector3(0f, h + 0.2f, 0f), new Vector3(w + 1.2f, 0.3f, d + 1.2f), M("SnowRoof"));
        Box(t, "Back Wall", new Vector3(0f, 1.4f, -d / 2f + 0.15f), new Vector3(w, 2.8f, 0.25f), M("CabinLogs"));
        Box(t, "Saw Bench", new Vector3(-2f, 0.8f, 0f), new Vector3(9f, 1.2f, 1.6f), M("Metal"));
        var blade = Cylinder(t, "Saw Blade", new Vector3(-2f, 1.6f, 0f), 0.9f, 0.05f, M("Chrome"));
        blade.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        var log = Cylinder(t, "Log on Deck", new Vector3(-6.5f, 1.8f, 0f), 0.45f, 5f, M("Bark"));
        log.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        foreach (var (x, z) in new[] { (6f, -3f), (6f, 2.5f), (9f, -0.5f) })
            for (int i = 0; i < 6; i++)
                Box(t, "Boards", new Vector3(x, 0.3f + i * 0.2f, z), new Vector3(2.4f, 0.18f, 2f), M("Floorboards"));
        NavHint(t, new Vector3(0f, 0.25f, 3f));
    }

    // ================================================================== Dust Ridge (wild-west town among mesas)

    static readonly (float x, float z, float rx, float rz, float h)[] Mesas =
    {
        (-62f, 52f, 15f, 11f, 16f), (58f, -36f, 17f, 12f, 19f), (-58f, -40f, 13f, 10f, 14f),
        (64f, 58f, 14f, 11f, 18f), (-6f, 70f, 11f, 8f, 12f), (26f, -42f, 10f, 7f, 11f),
    };
    const float StreetZ0 = 0f, StreetZ1 = 12f, RailZ = -59f;

    static float MesaWeight(float x, float z, int i, out float top)
    {
        var m = Mesas[i];
        float ex = (x - m.x) / m.rx, ez = (z - m.z) / m.rz;
        float d = Mathf.Sqrt(ex * ex + ez * ez) * (1f + (Mathf.PerlinNoise(x * 0.12f + i * 7f, z * 0.12f) - 0.5f) * 0.14f);
        top = m.h + (Mathf.PerlinNoise(x * 0.07f, z * 0.07f + i) - 0.5f) * 0.8f;
        return 1f - Smooth01((d - 0.84f) / 0.16f); // flat top, near-vertical cliffs
    }

    static float DesertHeight(float x, float z)
    {
        float h = (Mathf.PerlinNoise(x * 0.02f + 3f, z * 0.02f + 9f) - 0.5f) * 1.8f + (Mathf.PerlinNoise(x * 0.08f, z * 0.08f) - 0.5f) * 0.4f;
        // The town and the rail line sit on level ground.
        float rail = Mathf.Max(RectWeight(x, z, -260f, 260f, RailZ - 4f, RailZ + 4f, 5f), RectWeight(x, z, -10f, 52f, RailZ - 4f, RailZ + 7.5f, 4f));
        h = Mathf.Lerp(h, 0f, Mathf.Max(RectWeight(x, z, -56f, 56f, -16f, 30f, 8f), rail));
        for (int i = 0; i < Mesas.Length; i++)
        {
            float w = MesaWeight(x, z, i, out float top);
            if (w > 0f) h = Mathf.Lerp(h, Mathf.Max(h, top), w);
        }
        // Beyond the boundary: tall terraced plateaus.
        float rise = OutsideRise(x, z, 1.3f, 30f, 60f) * (1f - RectWeight(x, z, -400f, 400f, RailZ - 4.5f, RailZ + 4.5f, 7f)); // the line runs out through a cutting
        if (rise > 0f) h += Mathf.Round(rise / 9f) * 9f * 0.7f + rise * 0.3f;
        return h;
    }

    static Vector3 DesertG(float x, float z, float lift = 0f) => new(x, DesertHeight(x, z) + lift, z);

    static void BuildDustRidge()
    {
        world = new GameObject("Dust Ridge").transform;
        var rng = new System.Random(1881);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        var occupied = new List<(Vector2 c, float r)>();
        void Occupy(float x, float z, float r) => occupied.Add((new Vector2(x, z), r));
        bool Free(Vector2 p, float pad)
        {
            foreach (var (c, r) in occupied) if ((p - c).sqrMagnitude < (r + pad) * (r + pad)) return false;
            return true;
        }

        // ---------------------------------------------------------------- ground and far scenery
        HeightGround("DesertGround", DesertHeight, new[] { M("DesertSand"), M("TownDirt"), M("RedRock") }, (x, z, h) =>
            h > 2.5f ? 2 : (x > -56f && x < 56f && z > StreetZ0 - 1f && z < StreetZ1 + 1f) || Mathf.Abs(z - RailZ) < 3f ? 1 : 0);
        HeightFar("DesertFar", DesertHeight, M("RedRock"));
        InvisibleWalls(90f);
        var drng = new System.Random(19);
        for (int i = 0; i < 14; i++)
        {
            float ang = i / 14f * Mathf.PI * 2f + (float)drng.NextDouble() * 0.3f, dist = 230f + (float)drng.NextDouble() * 120f;
            var size = new Vector3(50f + (float)drng.NextDouble() * 70f, 40f + (float)drng.NextDouble() * 50f, 50f + (float)drng.NextDouble() * 70f);
            Box(world, "Far Mesa", new Vector3(Mathf.Cos(ang) * dist, size.y / 2f - 1f, Mathf.Sin(ang) * dist), size, M("RedRock"), yRot: (float)drng.NextDouble() * 90f);
        }
        for (int i = 0; i < Mesas.Length; i++) Occupy(Mesas[i].x, Mesas[i].z, Mathf.Max(Mesas[i].rx, Mesas[i].rz) + 2f);

        // ---------------------------------------------------------------- the town: main street along X (z 0..12)
        // North side faces south (-Z), south side faces north (+Z). (name, x, depth, width, floors)
        var north = new[] { ("Saloon", -42f, 12f, 14f, 2), ("Hotel", -24f, 12f, 12f, 2), ("General Store", -8f, 10f, 11f, 1),
                            ("Bank", 8f, 10f, 12f, 1), ("Barber", 23f, 9f, 8f, 1), ("Undertaker", 36f, 9f, 8f, 1) };
        var south = new[] { ("Sheriff", -40f, 10f, 12f, 1), ("Assay Office", -24f, 9f, 9f, 1), ("Telegraph", -10f, 8f, 8f, 1) };
        var paints = new[] { "WesternRed", "WesternBlue", "WesternGreen", "WesternWood" };
        int k = 0;
        foreach (var (name, x, depth, width, floors) in north)
        {
            WesternBuilding(name, new Vector3(x, 0f, StreetZ1 + 3f + depth / 2f), 90f, depth, width, floors, M(paints[k++ % paints.Length]));
            Occupy(x, StreetZ1 + 3f + depth / 2f, Mathf.Max(depth, width) * 0.6f + 2f);
        }
        foreach (var (name, x, depth, width, floors) in south)
        {
            WesternBuilding(name, new Vector3(x, 0f, StreetZ0 - 3f - depth / 2f), -90f, depth, width, floors, M(paints[k++ % paints.Length]));
            Occupy(x, StreetZ0 - 3f - depth / 2f, Mathf.Max(depth, width) * 0.6f + 2f);
        }
        BuildChurch(new Vector3(10f, 0f, -12f));
        Occupy(10f, -12f, 9f);
        BuildLivery(new Vector3(34f, 0f, -10f));
        Occupy(34f, -10f, 10f);
        BuildWaterTower(new Vector3(50f, 0f, 22f));
        Occupy(50f, 22f, 4f);
        // Street furniture: hitching rails, troughs, barrels, wagons.
        foreach (float x in new[] { -46f, -30f, -14f, 2f, 18f, 30f })
            foreach (var (z, yaw) in new[] { (StreetZ1 + 0.6f, 0f), (StreetZ0 - 0.6f, 0f) })
            {
                Box(world, "Hitching Rail", new Vector3(x, 0.9f, z), new Vector3(3f, 0.12f, 0.12f), M("WesternWoodDark"));
                foreach (float px in new[] { -1.4f, 1.4f }) Box(world, "Rail Post", new Vector3(x + px, 0.5f, z), new Vector3(0.14f, 1f, 0.14f), M("WesternWoodDark"));
            }
        foreach (var (x, z) in new[] { (-36f, 13.2f), (-2f, -1.2f), (26f, 13.2f) })
            Box(world, "Water Trough", new Vector3(x, 0.35f, z), new Vector3(2.2f, 0.7f, 0.7f), M("WesternWoodDark"));
        foreach (var (x, z) in new[] { (-48f, -2f), (-47f, -2.8f), (-16f, 14f), (14f, -1.5f), (15f, -2.4f), (42f, 14f), (-30f, 13.5f) })
            Barrel(new Vector3(x, 0f, z));
        Wagon(new Vector3(-20f, 0f, 6f), 80f, true);
        Wagon(new Vector3(16f, 0f, 7f), 100f, false);
        Wagon(new Vector3(48f, 0f, 4f), 10f, true);
        Wagon(new Vector3(-62f, DesertHeight(-62f, 12f), 12f), 40f, false);
        // Corral by the livery.
        float cx0 = 42f, cx1 = 54f, cz0 = -20f, cz1 = -6f;
        for (float x = cx0; x <= cx1; x += 3f)
            foreach (float z in new[] { cz0, cz1 }) Box(world, "Corral Post", new Vector3(x, 0.7f, z), new Vector3(0.15f, 1.4f, 0.15f), M("WesternWoodDark"));
        foreach (float z in new[] { cz0, cz1 })
            foreach (float y in new[] { 0.6f, 1.15f }) Box(world, "Corral Rail", new Vector3((cx0 + cx1) / 2f, y, z), new Vector3(cx1 - cx0, 0.1f, 0.08f), M("WesternWoodDark"));
        foreach (float y in new[] { 0.6f, 1.15f }) Box(world, "Corral Rail", new Vector3(cx1, y, (cz0 + cz1) / 2f), new Vector3(0.08f, 0.1f, cz1 - cz0), M("WesternWoodDark"));
        Occupy(48f, -13f, 8f);

        // ---------------------------------------------------------------- the railroad (south edge)
        // Main line (the train runs it, out past both edges of the map) and a siding where the boxcars are parked.
        Box(world, "Rail Bed", new Vector3(0f, 0.05f, RailZ), new Vector3(400f, 0.1f, 3.2f), M("TownDirt"));
        foreach (float off in new[] { -0.72f, 0.72f })
            Box(world, "Rail", new Vector3(0f, 0.2f, RailZ + off), new Vector3(400f, 0.12f, 0.08f), M("Metal"), collider: false);
        for (float x = -150f; x < 150f; x += 1.2f)
            Box(world, "Tie", new Vector3(x, 0.12f, RailZ), new Vector3(0.25f, 0.1f, 2.4f), M("WesternWoodDark"), collider: false);
        const float SidingZ = RailZ + 4.6f;
        Box(world, "Siding Bed", new Vector3(22f, 0.05f, SidingZ), new Vector3(48f, 0.1f, 3.2f), M("TownDirt"));
        foreach (float off in new[] { -0.72f, 0.72f })
            Box(world, "Rail", new Vector3(22f, 0.2f, SidingZ + off), new Vector3(48f, 0.12f, 0.08f), M("Metal"), collider: false);
        for (float x = -1.5f; x < 46f; x += 1.2f)
            Box(world, "Tie", new Vector3(x, 0.12f, SidingZ), new Vector3(0.25f, 0.1f, 2.4f), M("WesternWoodDark"), collider: false);
        Box(world, "Buffer Stop", new Vector3(46.4f, 0.6f, SidingZ), new Vector3(0.5f, 1.2f, 2.4f), M("WesternRed"));
        Box(world, "Platform", new Vector3(-20f, 0.5f, RailZ + 3.6f), new Vector3(20f, 1f, 3.6f), M("WesternWood"));
        BuildCabin("Depot", new Vector3(-20f, 0f, RailZ + 9.6f), -90f, 6f, 10f, 3.2f, M("WesternWood"), M("Roof"), backDoor: false, bunks: false);
        Occupy(-20f, RailZ + 8f, 8f);
        foreach (var (x, kind) in new[] { (8f, 0), (21f, 1), (34f, 1) })
        {
            var car = Building("Freight Car", new Vector3(x, 0f, SidingZ), 0f);
            foreach (float wx in new[] { -3.5f, 3.5f })
                foreach (float wz in new[] { -0.75f, 0.75f })
                {
                    var wheel = Cylinder(car, "Wheel", new Vector3(wx, 0.5f, wz), 0.5f, 0.1f, M("Metal"));
                    wheel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                }
            Box(car, "Boxcar", new Vector3(0f, 2.1f, 0f), new Vector3(9.5f, 2.9f, 2.7f), M(kind == 0 ? "WesternRed" : "WesternWood"));
            Occupy(x, SidingZ, 6f);
        }
        Occupy(-20f, RailZ, 3f);
        BuildTrain(RailZ);

        // ---------------------------------------------------------------- mesas: pads up, one ramp
        foreach (int i in new[] { 0, 1, 2, 3, 4 })
        {
            var m = Mesas[i];
            Vector2 dir = new Vector2(-m.x, 6f - m.z).normalized; // toward town
            Vector2 basePt = new Vector2(m.x, m.z) + new Vector2(dir.x * (m.rx + 5f), dir.y * (m.rz + 5f));
            JumpPadTo(DesertG(basePt.x, basePt.y), DesertG(m.x, m.z), 4f);
            Occupy(basePt.x, basePt.y, 2f);
        }
        {
            // A plank ramp up the west face of the small south mesa.
            var m = Mesas[5];
            float angle = 22f, run = m.h / Mathf.Tan(angle * Mathf.Deg2Rad), len = m.h / Mathf.Sin(angle * Mathf.Deg2Rad);
            float topX = m.x - m.rx * 0.8f;
            var ramp = Box(world, "Ramp", new Vector3(topX - run / 2f, m.h / 2f, m.z), new Vector3(len, 0.35f, 3f), M("WesternWood"), zRot: angle);
            Occupy(topX - run / 2f, m.z, 3f);
        }
        JumpPadTo(DesertG(-2f, 26f), DesertG(-30f, 44f), 7f); // town -> north flats

        // ---------------------------------------------------------------- desert: cacti, rocks, a few more wagons
        var boulders = new[] { BoulderMesh(0), BoulderMesh(1), BoulderMesh(2) };
        for (int i = 0, tries = 0; i < 70 && tries < 3000; tries++)
        {
            var p = new Vector2(R(MapMinX + 3f, MapMaxX - 3f), R(MapMinZ + 3f, MapMaxZ - 3f));
            if (!Free(p, 1.5f) || RectWeight(p.x, p.y, -56f, 56f, -16f, 30f, 0.01f) > 0.5f || Mathf.Abs(p.y - RailZ) < 4f) continue;
            Cactus(DesertG(p.x, p.y, -0.1f), R(2.2f, 4.2f), i);
            Occupy(p.x, p.y, 1f);
            i++;
        }
        for (int i = 0, tries = 0; i < 34 && tries < 2000; tries++)
        {
            var p = new Vector2(R(MapMinX + 3f, MapMaxX - 3f), R(MapMinZ + 3f, MapMaxZ - 3f));
            if (!Free(p, 2f) || RectWeight(p.x, p.y, -56f, 56f, -16f, 30f, 0.01f) > 0.5f || Mathf.Abs(p.y - RailZ) < 4f) continue;
            float size = R(1.4f, 3.8f);
            var go = MeshObject(world, "Rock", boulders[rng.Next(3)], new[] { M("RedRock") },
                DesertG(p.x, p.y, -size * 0.2f), Quaternion.Euler(0f, R(0f, 360f), 0f), new Vector3(size, size * R(0.5f, 0.9f), size * R(0.8f, 1.2f)));
            go.AddComponent<MeshCollider>().convex = true;
            Occupy(p.x, p.y, size * 0.6f);
            i++;
        }

        // ---------------------------------------------------------------- spawns
        SpawnPoints((x, z) => DesertG(x, z), new Vector2(0f, 6f), new[]
        {
            new Vector2(-50f, 6f), new Vector2(-30f, 6f), new Vector2(0f, 6f), new Vector2(26f, 6f), new Vector2(52f, 8f), new Vector2(-2f, 24f),
            new Vector2(-40f, 34f), new Vector2(-30f, 66f), new Vector2(20f, 60f), new Vector2(40f, 34f), new Vector2(80f, 30f), new Vector2(80f, -10f),
            new Vector2(76f, -51f), new Vector2(54f, -51f), new Vector2(-2f, -51f), new Vector2(-34f, -50f), new Vector2(-78f, -56f), new Vector2(-80f, -12f),
            new Vector2(-80f, 22f), new Vector2(-84f, 76f), new Vector2(30f, 78f), new Vector2(84f, 80f), new Vector2(10f, -30f), new Vector2(-34f, -24f),
        });
        Debug.Log("[FFA] Dust Ridge built");
    }

    /// <summary>
    /// Wild-west storefront: an enterable shop (stairs to the roof) in painted planks, with a tall false front,
    /// a covered boardwalk porch and a sign board. Front faces local +X like BuildShop.
    /// </summary>
    static void WesternBuilding(string name, Vector3 pos, float yaw, float depth, float width, int floors, Material paint)
    {
        var h = BuildShop(name, pos, yaw, depth, width, floors, floors > 1 ? "apartment" : "store", paint, M("WesternWoodDark"));
        float top = floors * ShopFloor;
        float fx = depth / 2f;
        // False front rising above the roof line, and a sign board on it.
        Box(h, "False Front", new Vector3(fx + 0.05f, top + 1.1f, 0f), new Vector3(0.2f, 2.2f, width), paint);
        Box(h, "Cornice", new Vector3(fx + 0.15f, top + 2.25f, 0f), new Vector3(0.4f, 0.2f, width + 0.3f), M("WesternWoodDark"));
        Box(h, "Sign", new Vector3(fx + 0.2f, top + 1.1f, 0f), new Vector3(0.06f, 1f, width * 0.7f), M("SignBoard"));
        // Boardwalk porch with a roof on posts.
        Box(h, "Boardwalk", new Vector3(fx + 1.5f, 0.05f, 0f), new Vector3(3f, 0.1f, width), M("WesternWood")); // flush with the shop floor
        Box(h, "Porch Roof", new Vector3(fx + 1.5f, 3.3f, 0f), new Vector3(3.2f, 0.15f, width + 0.2f), M("WesternWoodDark"));
        foreach (float z in new[] { -width / 2f + 0.2f, 0f, width / 2f - 0.2f })
            Box(h, "Porch Post", new Vector3(fx + 2.9f, 1.65f, z), new Vector3(0.18f, 3.3f, 0.18f), M("WesternWoodDark"));
    }

    static void BuildChurch(Vector3 pos)
    {
        var h = BuildCabin("Church", pos, -90f, 14f, 9f, 4.2f, M("WesternWhite"), M("Roof"), backDoor: true, bunks: false);
        // Bell tower over the front door.
        Box(h, "Tower", new Vector3(6.2f, 6f, 0f), new Vector3(2.4f, 4f, 2.4f), M("WesternWhite"));
        Box(h, "Belfry Roof", new Vector3(6.2f, 8.3f, 0f), new Vector3(2.8f, 0.3f, 2.8f), M("Roof"));
        Box(h, "Spire", new Vector3(6.2f, 9.6f, 0f), new Vector3(0.9f, 2.4f, 0.9f), M("Roof"));
        Box(h, "Cross", new Vector3(6.2f, 11.2f, 0f), new Vector3(0.12f, 1f, 0.12f), M("WesternWoodDark"));
        Box(h, "Cross Arm", new Vector3(6.2f, 11.35f, 0f), new Vector3(0.12f, 0.12f, 0.6f), M("WesternWoodDark"));
        Box(h, "Pew", new Vector3(-1f, 0.45f, -2f), new Vector3(6f, 0.9f, 0.5f), M("WesternWoodDark"));
        Box(h, "Pew", new Vector3(-1f, 0.45f, 2f), new Vector3(6f, 0.9f, 0.5f), M("WesternWoodDark"));
    }

    /// <summary>Livery stable: an open barn on posts with hay bales and stalls.</summary>
    static void BuildLivery(Vector3 pos)
    {
        var t = Building("Livery Stable", pos, 0f);
        const float w = 16f, d = 10f, hgt = 4.5f;
        Box(t, "Floor", new Vector3(0f, 0.08f, 0f), new Vector3(w, 0.16f, d), M("Hay"));
        for (float x = -w / 2f + 0.2f; x <= w / 2f; x += w / 4f - 0.1f)
            foreach (float z in new[] { -d / 2f + 0.2f, d / 2f - 0.2f })
                Box(t, "Post", new Vector3(x, hgt / 2f, z), new Vector3(0.3f, hgt, 0.3f), M("WesternWoodDark"));
        Box(t, "Roof", new Vector3(0f, hgt + 0.15f, 0f), new Vector3(w + 1f, 0.25f, d + 1f), M("RoofRed"));
        Box(t, "Back Wall", new Vector3(0f, 1.6f, -d / 2f + 0.1f), new Vector3(w, 3.2f, 0.2f), M("WesternWood"));
        for (int i = 0; i < 4; i++)
            Box(t, "Stall", new Vector3(-6f + i * 4f, 0.7f, -2.4f), new Vector3(0.15f, 1.4f, 4.6f), M("WesternWoodDark"));
        foreach (var (x, z, n) in new[] { (5f, 2.5f, 3), (-4f, 3f, 2), (7f, -0.5f, 2) })
            for (int i = 0; i < n; i++)
                Box(t, "Hay Bale", new Vector3(x, 0.45f + i * 0.85f, z), new Vector3(1.6f, 0.85f, 1f), M("Hay"));
        NavHint(t, new Vector3(0f, 0.2f, 3f));
    }
}
