using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static partial class ProjectBuilder
{
    // ================================================================== Camp Timberline (forest map)
    //
    // Same playable footprint as Fluketown (X -68..68, Z -46..66). A summer camp sits in a flat clearing in the middle
    // (mess hall, cabins, campfire), a pond with a dock lies to the south, the parking lot and gate to the north, and
    // wooded hills climb along the west and east edges: a fire lookout tower on the west ridge, a ranger station and
    // deer stand on the east one. Trees everywhere outside the clearing.

    const float PondX = 0f, PondZ = -28f, PondRX = 14f, PondRZ = 9f, WaterY = -0.45f;
    static readonly Vector2 CampCenter = new(0f, 10f);

    static readonly List<(Vector2 c, float r, float fall, float h)> campPads = new();
    static readonly List<(Vector2[] pts, float width)> campPaths = new();
    static readonly List<(Vector2 c, float r)> campClear = new();

    static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    /// <summary>0..1 weight that is 1 inside the rectangle and fades out over `fall` metres.</summary>
    static float RectWeight(float x, float z, float x0, float x1, float z0, float z1, float fall)
    {
        float dx = Mathf.Max(0f, Mathf.Max(x0 - x, x - x1)), dz = Mathf.Max(0f, Mathf.Max(z0 - z, z - z1));
        return 1f - Smooth01(Mathf.Sqrt(dx * dx + dz * dz) / fall);
    }

    /// <summary>How high the side hills are at this point (0 in the valley, 1 on the ridges).</summary>
    static float HillAmount(float x) => Smooth01((Mathf.Abs(x) - 22f) / 40f);

    static float PondEllipse(float x, float z)
    {
        float ex = (x - PondX) / PondRX, ez = (z - PondZ) / PondRZ;
        return ex * ex + ez * ez;
    }

    /// <summary>Ground height before building pads are flattened in.</summary>
    static float CampBase(float x, float z)
    {
        float s = HillAmount(x);
        float ridge = 0.7f + 0.6f * Mathf.PerlinNoise(x * 0.025f + 31.7f, z * 0.03f + 7.3f);
        float h = s * 12f * ridge;
        float n = (Mathf.PerlinNoise(x * 0.045f + 100f, z * 0.045f + 50f) - 0.5f) * 2.2f
                + (Mathf.PerlinNoise(x * 0.14f + 3f, z * 0.14f + 9f) - 0.5f) * 0.5f;
        h += n * Mathf.Lerp(0.35f, 1f, s);
        // The camp clearing and the road in from the north stay almost flat.
        float core = Mathf.Max(RectWeight(x, z, -24f, 24f, -14f, 40f, 8f), RectWeight(x, z, -12f, 12f, 40f, 72f, 6f));
        h = Mathf.Lerp(h, n * 0.12f, core);
        // Pond bowl.
        float bowl = Smooth01((1.25f - PondEllipse(x, z)) / 1.25f);
        h = Mathf.Lerp(h, -1.6f, bowl);
        // Beyond the playable area the ground climbs into the surrounding hills.
        float ox = Mathf.Max(0f, Mathf.Abs(x) - MapMaxX), oz = Mathf.Max(0f, Mathf.Max(MapMinZ - z, z - MapMaxZ));
        float o = Mathf.Sqrt(ox * ox + oz * oz);
        if (o > 0f)
            h += Mathf.Min(o * 0.55f, 20f) + Mathf.PerlinNoise(x * 0.008f + 5f, z * 0.008f + 11f) * Mathf.Min(o * 0.4f, 60f);
        return h;
    }

    static float CampHeight(float x, float z)
    {
        float h = CampBase(x, z);
        foreach (var p in campPads)
        {
            float d = Vector2.Distance(new Vector2(x, z), p.c);
            float w = 1f - Smooth01((d - p.r) / p.fall);
            if (w > 0f) h = Mathf.Lerp(h, p.h, w);
        }
        return h;
    }

    static Vector3 CampG(float x, float z, float lift = 0f) => new(x, CampHeight(x, z) + lift, z);

    static void CampPad(float x, float z, float r, float fall) => campPads.Add((new Vector2(x, z), r, fall, CampBase(x, z)));

    static void CampPath(float width, params float[] xz)
    {
        var pts = new Vector2[xz.Length / 2];
        for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2(xz[i * 2], xz[i * 2 + 1]);
        campPaths.Add((pts, width));
    }

    /// <summary>Signed distance to the nearest trail edge (negative = on a trail).</summary>
    static float PathDistance(Vector2 p)
    {
        float best = float.MaxValue;
        foreach (var (pts, width) in campPaths)
            for (int i = 0; i + 1 < pts.Length; i++)
            {
                Vector2 a = pts[i], ab = pts[i + 1] - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t) - width * 0.5f);
            }
        return best;
    }

    static void Clear(float x, float z, float r) => campClear.Add((new Vector2(x, z), r));

    static bool IsClear(Vector2 p)
    {
        foreach (var (c, r) in campClear)
            if ((p - c).sqrMagnitude < r * r) return true;
        return false;
    }

    static bool InPlayArea(Vector2 p, float margin = 0f) =>
        p.x > MapMinX - margin && p.x < MapMaxX + margin && p.y > MapMinZ - margin && p.y < MapMaxZ + margin;

    static bool InClearing(Vector2 p) => (p.x > -24f && p.x < 24f && p.y > -14f && p.y < 40f) || (p.x > -12f && p.x < 12f && p.y > 40f);

    // ------------------------------------------------------------------ the map

    static void BuildCampground()
    {
        world = new GameObject("Camp Timberline").transform;
        campPads.Clear();
        campPaths.Clear();
        campClear.Clear();

        // ---------------------------------------------------------------- layout: building pads and trails
        CampPad(-52f, 14f, 6.5f, 6f);   // fire lookout
        CampPad(50f, -6f, 7f, 6f);      // ranger station
        CampPad(54f, 34f, 5f, 5f);      // deer stand
        CampPad(-46f, -26f, 4f, 4f);    // lean-to
        CampPad(0f, 31f, 11f, 4f);      // mess hall
        foreach (var c in new[] { new Vector2(-17f, 22f), new Vector2(-17f, 2f), new Vector2(17f, 22f), new Vector2(17f, 2f) })
            CampPad(c.x, c.y, 5f, 3f);

        CampPath(5f, 0f, 75f, 0f, 56f, 0f, 38f);                                   // road from the gate
        CampPath(2.6f, 0f, 26f, 0f, 15f);                                          // mess hall to campfire
        CampPath(2.6f, 0f, 5f, 0f, -4f, 2f, -12f, 4f, -17f);                       // campfire to the dock
        var ring = new float[26];
        for (int i = 0; i <= 12; i++)
        {
            float a = i / 12f * Mathf.PI * 2f;
            ring[i * 2] = CampCenter.x + Mathf.Cos(a) * 5.8f;
            ring[i * 2 + 1] = CampCenter.y + Mathf.Sin(a) * 5.8f;
        }
        CampPath(2f, ring);                                                         // around the campfire
        CampPath(2f, -13.5f, 22f, -4.5f, 13f);
        CampPath(2f, -13.5f, 2f, -4.5f, 7f);
        CampPath(2f, 13.5f, 22f, 4.5f, 13f);
        CampPath(2f, 13.5f, 2f, 4.5f, 7f);
        CampPath(2.2f, -5.8f, 10f, -20f, 11f, -32f, 16f, -42f, 12f, -48f, 14f);     // west trail up to the lookout
        CampPath(2.2f, 5.8f, 10f, 20f, 9f, 32f, 4f, 40f, -2f, 45f, -6f);            // east trail to the ranger station
        CampPath(2f, 40f, -2f, 44f, 14f, 50f, 26f, 52f, 31f);                      // on to the deer stand
        var pond = new float[34];
        for (int i = 0; i <= 16; i++)
        {
            float a = i / 16f * Mathf.PI * 2f;
            pond[i * 2] = PondX + Mathf.Cos(a) * 17.5f;
            pond[i * 2 + 1] = PondZ + Mathf.Sin(a) * 12f;
        }
        CampPath(2f, pond);                                                         // around the pond
        CampPath(2f, -15f, -34f, -28f, -40f, -42f, -34f, -46f, -29f);              // south-west trail to the lean-to
        CampPath(2f, -6f, 44f, -20f, 50f, -38f, 54f, -50f, 40f, -52f, 21f);         // north-west loop to the lookout
        CampPath(2f, 6f, 44f, 22f, 52f, 40f, 50f, 50f, 38f);                       // north-east trail

        // ---------------------------------------------------------------- ground
        BuildCampGround();
        BuildCampFarHills();
        Box(world, "Water", new Vector3(PondX, WaterY, PondZ), new Vector3(PondRX * 2.2f, 0.02f, PondRZ * 2.4f), M("Water"), collider: false);
        Clear(PondX, PondZ, PondRZ + 2.5f);
        Clear(PondX - 8f, PondZ, PondRZ + 2f);
        Clear(PondX + 8f, PondZ, PondRZ + 2f);

        // ---------------------------------------------------------------- camp buildings
        var messHall = BuildMessHall(CampG(0f, 31f), 90f);
        Clear(0f, 31f, 13f);
        var cabins = new[]
        {
            BuildCabin("Cabin Bear", CampG(-17f, 22f), 0f, 6f, 7f, 2.9f, M("CabinLogs"), M("RoofGreen")),
            BuildCabin("Cabin Wolf", CampG(-17f, 2f), 0f, 6f, 7f, 2.9f, M("CabinLogs"), M("RoofRed")),
            BuildCabin("Cabin Elk", CampG(17f, 22f), 180f, 6f, 7f, 2.9f, M("CabinLogs"), M("RoofRed")),
            BuildCabin("Cabin Hawk", CampG(17f, 2f), 180f, 6f, 7f, 2.9f, M("CabinLogs"), M("RoofGreen")),
        };
        foreach (var c in cabins) Clear(c.position.x, c.position.z, 6.5f);
        var ranger = BuildCabin("Ranger Station", CampG(50f, -6f), 180f, 7f, 9f, 3f, M("SidingGreen"), M("Roof"), backDoor: true, bunks: false);
        Box(ranger, "Radio Desk", new Vector3(-2.6f, 0.55f, 2.8f), new Vector3(1.2f, 0.8f, 2.4f), M("Furniture"));
        Box(ranger, "Radio", new Vector3(-2.7f, 1.12f, 2.6f), new Vector3(0.5f, 0.35f, 0.7f), M("GunPolymer"));
        Box(ranger, "Map Table", new Vector3(0.5f, 0.5f, -2.4f), new Vector3(1.6f, 0.75f, 1.2f), M("Furniture"));
        Box(ranger, "Gun Rack", new Vector3(-3.2f, 1.2f, -3f), new Vector3(0.3f, 1.8f, 1.4f), M("GunWood"));
        Clear(50f, -6f, 8f);
        var lookout = BuildLookout(CampG(-52f, 14f), 0f);
        Clear(-52f, 14f, 7.5f);
        var stand = BuildDeerStand(CampG(54f, 34f), 180f);
        Clear(54f, 34f, 6f);
        BuildLeanTo(CampG(-46f, -26f), 30f);
        Clear(-46f, -26f, 4.5f);

        // ---------------------------------------------------------------- camp props
        BuildCampfire(CampG(CampCenter.x, CampCenter.y));
        Clear(CampCenter.x, CampCenter.y, 7f);
        BuildFlagpole(CampG(0f, -1f));
        Clear(0f, -1f, 2f);
        foreach (var (x, z, yaw, mat) in new[] { (-12f, -7f, 20f, "TentOrange"), (-5.5f, -9f, -5f, "TentBlue"), (7f, -8.5f, 10f, "TentGreen"), (13f, -6f, -25f, "TentOrange") })
        {
            BuildTent(CampG(x, z), yaw, M(mat));
            Clear(x, z, 3.5f);
        }
        foreach (var (x, z, yaw) in new[] { (-7f, 19f, 0f), (7f, 19f, 0f), (-9f, 3f, 90f), (9f, 3f, 90f), (-6f, -16f, 30f) })
        {
            BuildPicnicTable(CampG(x, z), yaw);
            Clear(x, z, 2f);
        }
        BuildArchery(CampG(16f, 34f));
        Clear(16f, 34f, 5f);
        foreach (var (x, z, yaw) in new[] { (-21.5f, 32f, 90f), (-21.5f, 35f, 90f), (22f, -12f, -90f) })
        {
            BuildOuthouse(CampG(x, z), yaw);
            Clear(x, z, 1.8f);
        }
        BuildWoodpile(CampG(-11f, 34f), 0f);
        BuildWoodpile(CampG(46f, -14f), 90f);
        Clear(-11f, 34f, 2.5f);
        Clear(46f, -14f, 2.5f);
        BuildCanoeRack(CampG(-11f, -17.5f), 15f);
        Clear(-11f, -17.5f, 3f);
        var dockEnd = BuildDock(new Vector3(4f, 0f, -16f));
        Clear(4f, -24f, 4f);
        BuildRowboat(new Vector3(-7f, WaterY, -31f), 35f);

        // Parking lot, camp shuttle and the gate.
        BuildBus(CampG(-5f, 52f));
        BuildCar(world, "Parked Car", CampG(5f, 47.5f), 0f, M("CarGreen"));
        BuildCar(world, "Parked Truck", CampG(8.2f, 55f), 180f, M("TruckRed"));
        Clear(0f, 51f, 11f);
        BuildCampGate(CampG(0f, 63.5f));
        Clear(0f, 63.5f, 6f);

        // ---------------------------------------------------------------- spawn points
        var spawns = new GameObject("Spawn Points").transform;
        spawns.SetParent(world, false);
        var spawnXZ = new[]
        {
            // Camp
            new Vector2(-8f, 14f), new Vector2(8f, 6f), new Vector2(-21f, 12f), new Vector2(21f, 12f), new Vector2(-10f, 40f), new Vector2(10f, 40f),
            new Vector2(-7f, 47f), new Vector2(9f, 60f), new Vector2(-17f, -12f), new Vector2(18f, -12f),
            // Pond
            new Vector2(-21f, -30f), new Vector2(21f, -33f), new Vector2(0f, -43f),
            // West hill
            new Vector2(-45f, 0f), new Vector2(-44f, 30f), new Vector2(-58f, 52f), new Vector2(-57f, -38f), new Vector2(-34f, -12f), new Vector2(-36f, 44f),
            // East hill
            new Vector2(44f, 10f), new Vector2(45f, 46f), new Vector2(58f, -30f), new Vector2(37f, -22f), new Vector2(35f, 30f), new Vector2(60f, 58f),
        };
        foreach (var p in spawnXZ)
        {
            var sp = new GameObject("Spawn");
            sp.transform.SetParent(spawns, false);
            sp.transform.SetPositionAndRotation(CampG(p.x, p.y, 0.1f), Quaternion.Euler(0f, Mathf.Atan2(CampCenter.x - p.x, CampCenter.y - p.y) * Mathf.Rad2Deg, 0f));
            sp.AddComponent<SpawnPoint>();
            Clear(p.x, p.y, 2.5f);
        }

        // ---------------------------------------------------------------- forest
        var rng = new System.Random(4242);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        var boulderMeshes = new[] { BoulderMesh(0), BoulderMesh(1), BoulderMesh(2) };
        var rockMat = new[] { M("Rock") };

        // A big rock on the east ridge, outcrops along both ridges, and loose boulders on the slopes.
        Boulder(new Vector3(58f, CampHeight(58f, 14f) - 1.2f, 14f), 7f, 3.6f, 20f, 0);
        Clear(58f, 14f, 5f);
        for (int i = 0, tries = 0; i < 42 && tries < 1000; tries++)
        {
            var p = new Vector2(R(-66f, 66f), R(-44f, 64f));
            if (HillAmount(p.x) < 0.2f || IsClear(p) || PathDistance(p) < 1.5f) continue;
            float size = R(1.2f, 3.2f);
            Boulder(new Vector3(p.x, CampHeight(p.x, p.y) - size * 0.2f, p.y), size, size * R(0.5f, 0.85f), R(0f, 360f), rng.Next(3));
            i++;
        }
        void Boulder(Vector3 pos, float size, float height, float yaw, int kind)
        {
            var go = MeshObject(world, "Boulder", boulderMeshes[kind], rockMat, pos, Quaternion.Euler(0f, yaw, 0f), new Vector3(size, height, size * R(0.8f, 1.2f)));
            go.AddComponent<MeshCollider>().convex = true;
        }

        // Fallen logs for cover in the woods.
        for (int i = 0, tries = 0; i < 18 && tries < 500; tries++)
        {
            var p = new Vector2(R(-64f, 64f), R(-42f, 62f));
            if (InClearing(p) || IsClear(p) || PathDistance(p) < 3f) continue;
            float len = R(4f, 7.5f), r = R(0.3f, 0.45f), yaw = R(0f, 180f);
            var log = Cylinder(world, "Fallen Log", CampG(p.x, p.y, r * 0.8f), r, len, M("Bark"));
            log.transform.localRotation = Quaternion.Euler(0f, yaw, 90f);
            Clear(p.x, p.y, len * 0.5f);
            i++;
        }

        // Trees: pines dominate on the hills, leafy trees mix in down in the valley. A dense, collider-free band
        // outside the boundary hides the edge of the world.
        var pines = new[] { PineMesh(0), PineMesh(1) };
        var leafy = new[] { LeafyMesh(0), LeafyMesh(1) };
        var pineMats = new[] { M("Bark"), M("PineNeedles") };
        var leafMats = new[] { M("Bark"), M("Leaves") };
        var trees = new GameObject("Trees").transform;
        trees.SetParent(world, false);
        int treeCount = 0;
        for (float gx = -104f; gx <= 104f; gx += 4.3f)
            for (float gz = -84f; gz <= 104f; gz += 4.3f)
            {
                var p = new Vector2(gx + R(-1.8f, 1.8f), gz + R(-1.8f, 1.8f));
                bool inside = InPlayArea(p, 1.5f);
                if (!inside && rng.NextDouble() < 0.2) continue;
                if (inside && rng.NextDouble() > (InClearing(p) ? 0.07 : 0.6)) continue;
                if (IsClear(p) || PathDistance(p) < 1.4f || PondEllipse(p.x, p.y) < 1.5f) continue;
                if (inside && InPlayArea(p, -1.5f) == false && rng.NextDouble() < 0.5) continue; // thin out right on the wall line
                bool pine = rng.NextDouble() < Mathf.Lerp(0.55f, 0.9f, HillAmount(p.x));
                float scale = pine ? R(0.85f, 1.35f) : R(0.85f, 1.2f);
                var go = MeshObject(trees, pine ? "Pine" : "Tree", pine ? pines[rng.Next(2)] : leafy[rng.Next(2)], pine ? pineMats : leafMats,
                    new Vector3(p.x, CampHeight(p.x, p.y) - 0.25f, p.y), Quaternion.Euler(R(-3f, 3f), R(0f, 360f), R(-3f, 3f)), Vector3.one * scale);
                if (inside)
                {
                    var trunk = go.AddComponent<CapsuleCollider>();
                    trunk.center = new Vector3(0f, 2.5f, 0f);
                    trunk.height = 5f;
                    trunk.radius = pine ? 0.28f : 0.34f;
                }
                treeCount++;
            }
        // Bushes: concealment you can push through (no colliders).
        for (float gx = -66f; gx <= 66f; gx += 6.5f)
            for (float gz = -44f; gz <= 64f; gz += 6.5f)
            {
                var p = new Vector2(gx + R(-2.5f, 2.5f), gz + R(-2.5f, 2.5f));
                if (rng.NextDouble() > (InClearing(p) ? 0.12 : 0.5)) continue;
                if (PathDistance(p) < 0.8f || PondEllipse(p.x, p.y) < 1.1f) continue;
                bool nearThing = false;
                foreach (var (c, r) in campClear)
                    if ((p - c).sqrMagnitude < (r - 1f) * (r - 1f)) { nearThing = true; break; }
                if (nearThing) continue;
                var b = Sphere(trees, "Bush", CampG(p.x, p.y, 0.25f), 1f, M("Hedge"));
                b.transform.localScale = new Vector3(R(1.6f, 2.6f), R(1.1f, 1.6f), R(1.6f, 2.6f));
                Object.DestroyImmediate(b.GetComponent<Collider>());
            }
        Debug.Log($"[FFA] Timberline: {treeCount} trees");

        // ---------------------------------------------------------------- boundary
        float w = MapMaxX - MapMinX, d = MapMaxZ - MapMinZ, cz = (MapMinZ + MapMaxZ) / 2f;
        Box(world, "Invisible Wall", new Vector3(0f, 20f, MapMaxZ + 0.2f), new Vector3(w + 1f, 70f, 0.3f), null);
        Box(world, "Invisible Wall", new Vector3(0f, 20f, MapMinZ - 0.2f), new Vector3(w + 1f, 70f, 0.3f), null);
        Box(world, "Invisible Wall", new Vector3(MapMinX - 0.2f, 20f, cz), new Vector3(0.3f, 70f, d + 1f), null);
        Box(world, "Invisible Wall", new Vector3(MapMaxX + 0.2f, 20f, cz), new Vector3(0.3f, 70f, d + 1f), null);
    }

    // ------------------------------------------------------------------ ground meshes

    /// <summary>Walkable ground: 1 m grid over the play area and a margin, grass and dirt (trails, pond bed) submeshes.</summary>
    static void BuildCampGround()
    {
        const float x0 = -100f, x1 = 100f, z0 = -80f, z1 = 100f, step = 1f;
        int nx = Mathf.RoundToInt((x1 - x0) / step) + 1, nz = Mathf.RoundToInt((z1 - z0) / step) + 1;
        var mb = new MeshBuilder(2);
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                float x = x0 + i * step, z = z0 + j * step;
                mb.Add(new Vector3(x, CampHeight(x, z), z), new Vector2(x, z) / 5f);
            }
        for (int i = 0; i + 1 < nx; i++)
            for (int j = 0; j + 1 < nz; j++)
            {
                int a = i * nz + j, b = a + 1, c = a + nz + 1, dd = a + nz;
                var mid = new Vector2(x0 + (i + 0.5f) * step, z0 + (j + 0.5f) * step);
                bool dirt = PathDistance(mid) < 0f || PondEllipse(mid.x, mid.y) < 0.95f
                            || (mid - CampCenter).sqrMagnitude < 4.6f * 4.6f
                            || (mid.x > -12f && mid.x < 12f && mid.y > 43f && mid.y < 60f);
                int s = dirt ? 1 : 0;
                mb.Tri(s, a, b, c);
                mb.Tri(s, a, c, dd);
            }
        var mesh = SaveMesh(mb.Build("CampGround"), "CampGround");
        var go = MeshObject(world, "Ground", mesh, new[] { M("ForestFloor"), M("Dirt") }, Vector3.zero, Quaternion.identity, Vector3.one);
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    /// <summary>Coarse scenery ground out to the horizon, with a hole where the detailed ground sits.</summary>
    static void BuildCampFarHills()
    {
        const float r = 420f, step = 10f;
        int n = Mathf.RoundToInt(r * 2f / step) + 1;
        var mb = new MeshBuilder(1);
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                float x = -r + i * step, z = -r + j * step;
                mb.Add(new Vector3(x, CampHeight(x, z), z), new Vector2(x, z) / 5f);
            }
        for (int i = 0; i + 1 < n; i++)
            for (int j = 0; j + 1 < n; j++)
            {
                float cx = -r + (i + 0.5f) * step, cz = -r + (j + 0.5f) * step;
                if (cx > -100f && cx < 100f && cz > -80f && cz < 100f) continue;
                int a = i * n + j, b = a + 1, c = a + n + 1, d = a + n;
                mb.Tri(0, a, b, c);
                mb.Tri(0, a, c, d);
            }
        var mesh = SaveMesh(mb.Build("CampFarHills"), "CampFarHills");
        MeshObject(world, "Far Hills", mesh, new[] { M("ForestFloor") }, Vector3.zero, Quaternion.identity, Vector3.one);
    }

    // ------------------------------------------------------------------ buildings

    static Transform Building(string name, Vector3 pos, float yRot)
    {
        var h = new GameObject(name).transform;
        h.SetParent(world, false);
        h.SetPositionAndRotation(pos, Quaternion.Euler(0f, yRot, 0f));
        return h;
    }

    /// <summary>Pitched roof with its ridge along local Z, plus triangular gables at both ends.</summary>
    static void GableRoof(Transform h, float depth, float width, float wallH, float rise, Material roof, Material gable)
    {
        float hx = depth / 2f, over = 0.45f, run = hx + over;
        float pitch = Mathf.Atan2(rise, hx);
        float slab = run / Mathf.Cos(pitch);
        foreach (float side in new[] { 1f, -1f })
            Box(h, "Roof", new Vector3(side * run / 2f, wallH + rise - run * Mathf.Tan(pitch) / 2f + 0.08f, 0f),
                new Vector3(slab, 0.16f, width + over * 2f), roof, zRot: -side * pitch * Mathf.Rad2Deg);
        var mesh = GableMesh();
        foreach (float side in new[] { 1f, -1f })
        {
            var g = MeshObject(h, "Gable", mesh, new[] { gable }, new Vector3(0f, wallH, side * (width / 2f - 0.125f)), Quaternion.identity, new Vector3(depth, rise, 0.25f));
            g.AddComponent<MeshCollider>().convex = true;
        }
    }

    /// <summary>Log cabin (front door on +X). Optional back door and bunk beds along the side walls.</summary>
    static Transform BuildCabin(string name, Vector3 pos, float yRot, float depth, float width, float wallH, Material walls, Material roof,
        bool backDoor = false, bool bunks = true)
    {
        var h = Building(name, pos, yRot);
        float hx = depth / 2f, hz = width / 2f;
        const float T = 0.25f;
        Box(h, "Foundation", new Vector3(0f, -0.6f, 0f), new Vector3(depth + 0.3f, 1.2f, width + 0.3f), M("Rock"));
        Box(h, "Floor", new Vector3(0f, 0.075f, 0f), new Vector3(depth, 0.15f, width), M("Floorboards"));
        float win = Mathf.Min(1.4f, (hz - 1.3f) * 0.8f);
        Wall(h, "Front", WallAxis.AlongZ, hx - T / 2f, -hz, hz, 0f, wallH, T, walls,
            (-0.65f, 0.65f, 0f, 2.5f), (-hz + 0.7f, -hz + 0.7f + win, 1.0f, 2.0f), (hz - 0.7f - win, hz - 0.7f, 1.0f, 2.0f));
        if (backDoor)
            Wall(h, "Back", WallAxis.AlongZ, -hx + T / 2f, -hz, hz, 0f, wallH, T, walls, (-0.65f, 0.65f, 0f, 2.5f), (hz - 0.7f - win, hz - 0.7f, 1.0f, 2.0f));
        else
            Wall(h, "Back", WallAxis.AlongZ, -hx + T / 2f, -hz, hz, 0f, wallH, T, walls, (-win / 2f, win / 2f, 1.0f, 2.0f));
        Wall(h, "Side", WallAxis.AlongX, -hz + T / 2f, -hx + T, hx - T, 0f, wallH, T, walls, (-0.7f, 0.7f, 1.0f, 2.0f));
        Wall(h, "Side", WallAxis.AlongX, hz - T / 2f, -hx + T, hx - T, 0f, wallH, T, walls, (-0.7f, 0.7f, 1.0f, 2.0f));
        GableRoof(h, depth, width, wallH, depth * 0.3f, roof, walls);
        Box(h, "Porch", new Vector3(hx + 0.9f, 0.075f, 0f), new Vector3(1.8f, 0.15f, 3f), M("Floorboards"));
        Box(h, "Porch Post", new Vector3(hx + 1.7f, 1.4f, -1.4f), new Vector3(0.15f, 2.8f, 0.15f), M("Trunk"), collider: false);
        Box(h, "Porch Post", new Vector3(hx + 1.7f, 1.4f, 1.4f), new Vector3(0.15f, 2.8f, 0.15f), M("Trunk"), collider: false);
        Box(h, "Porch Roof", new Vector3(hx + 0.95f, 2.85f, 0f), new Vector3(1.9f, 0.12f, 3.3f), roof, collider: false);
        Passage(h, name + " door", new Vector3(hx - T / 2f, 0.15f, 0f), Vector3.right);
        NavHint(h, new Vector3(hx + 1.2f, 0.15f, 0f));
        NavHint(h, new Vector3(hx - 1.2f, 0.15f, 0f));
        if (backDoor)
        {
            Passage(h, name + " back door", new Vector3(-hx + T / 2f, 0.15f, 0f), Vector3.right);
            Box(h, "Back Step", new Vector3(-hx - 0.6f, 0.075f, 0f), new Vector3(1.2f, 0.15f, 2f), M("Floorboards"));
            NavHint(h, new Vector3(-hx + 1.2f, 0.15f, 0f));
            NavHint(h, new Vector3(-hx - 0.9f, 0.15f, 0f));
        }
        if (bunks)
            foreach (float side in new[] { -1f, 1f })
            {
                float z = side * (hz - T - 0.5f), x = -hx + T + 1.05f;
                Box(h, "Bunk Low", new Vector3(x, 0.4f, z), new Vector3(2f, 0.35f, 0.95f), M("Fabric"));
                Box(h, "Bunk High", new Vector3(x, 1.55f, z), new Vector3(2f, 0.25f, 0.95f), M("Fabric"));
                foreach (float px in new[] { x - 1f, x + 1f })
                    foreach (float pz in new[] { z - 0.45f, z + 0.45f })
                        Box(h, "Bunk Post", new Vector3(px, 1f, pz), new Vector3(0.08f, 2f, 0.08f), M("Furniture"), collider: false);
            }
        else
            Box(h, "Bench", new Vector3(hx - 1f, 0.4f, -hz + 0.6f), new Vector3(1.4f, 0.5f, 0.6f), M("Furniture"));
        float stoveZ = backDoor ? -(hz - T - 0.5f) : 0f; // keep it out of the back doorway
        Box(h, "Stove", new Vector3(-hx + T + 0.45f, 0.6f, stoveZ), new Vector3(0.7f, 0.9f, 0.7f), M("GunDark"));
        Cylinder(h, "Stove Pipe", new Vector3(-hx + T + 0.45f, wallH, stoveZ), 0.1f, wallH + 1.2f, M("GunDark"));
        return h;
    }

    /// <summary>Big log mess hall: double front doors, side doors and a kitchen door, long tables inside.</summary>
    static Transform BuildMessHall(Vector3 pos, float yRot)
    {
        var h = Building("Mess Hall", pos, yRot);
        const float depth = 11f, width = 18f, wallH = 3.4f, T = 0.25f;
        float hx = depth / 2f, hz = width / 2f;
        var walls = M("CabinLogs");
        Box(h, "Foundation", new Vector3(0f, -0.6f, 0f), new Vector3(depth + 0.3f, 1.2f, width + 0.3f), M("Rock"));
        Box(h, "Floor", new Vector3(0f, 0.075f, 0f), new Vector3(depth, 0.15f, width), M("Floorboards"));
        Wall(h, "Front", WallAxis.AlongZ, hx - T / 2f, -hz, hz, 0f, wallH, T, walls,
            (-1.3f, 1.3f, 0f, 2.7f), (-7.6f, -5.6f, 1.1f, 2.4f), (-4.4f, -2.4f, 1.1f, 2.4f), (2.4f, 4.4f, 1.1f, 2.4f), (5.6f, 7.6f, 1.1f, 2.4f));
        Wall(h, "Back", WallAxis.AlongZ, -hx + T / 2f, -hz, hz, 0f, wallH, T, walls,
            (-7f, -4.6f, 1.1f, 2.4f), (-2f, 0.4f, 1.1f, 2.4f), (4.6f, 5.9f, 0f, 2.6f));
        Wall(h, "Side", WallAxis.AlongX, -hz + T / 2f, -hx + T, hx - T, 0f, wallH, T, walls, (-4.2f, -2.4f, 1.1f, 2.4f), (-0.65f, 0.65f, 0f, 2.6f), (2.4f, 4.2f, 1.1f, 2.4f));
        Wall(h, "Side", WallAxis.AlongX, hz - T / 2f, -hx + T, hx - T, 0f, wallH, T, walls, (-4.2f, -2.4f, 1.1f, 2.4f), (-0.65f, 0.65f, 0f, 2.6f), (2.4f, 4.2f, 1.1f, 2.4f));
        GableRoof(h, depth, width, wallH, 3f, M("RoofGreen"), walls);
        Box(h, "Chimney", new Vector3(-2.5f, 4.2f, -hz - 0.3f), new Vector3(1.2f, 8.4f, 0.8f), M("Rock"));
        // Front deck and steps.
        Box(h, "Deck", new Vector3(hx + 1.25f, 0.075f, 0f), new Vector3(2.5f, 0.15f, 7f), M("Floorboards"));
        Box(h, "Deck Roof", new Vector3(hx + 1.3f, 3.1f, 0f), new Vector3(2.6f, 0.12f, 7.4f), M("RoofGreen"), collider: false);
        foreach (float z in new[] { -3.3f, 3.3f })
            Box(h, "Deck Post", new Vector3(hx + 2.4f, 1.55f, z), new Vector3(0.18f, 3.1f, 0.18f), M("Trunk"));
        // Long tables with benches.
        foreach (float x in new[] { 2.4f, -1f })
        {
            Box(h, "Long Table", new Vector3(x, 0.55f, -0.5f), new Vector3(0.9f, 0.08f, 11f), M("Furniture"));
            Box(h, "Table Base", new Vector3(x, 0.3f, -0.5f), new Vector3(0.3f, 0.45f, 10f), M("Furniture"));
            foreach (float s in new[] { -0.75f, 0.75f })
                Box(h, "Bench", new Vector3(x + s, 0.3f, -0.5f), new Vector3(0.32f, 0.45f, 11f), M("Fence"));
        }
        // Kitchen along the back wall.
        Box(h, "Counter", new Vector3(-hx + T + 0.4f, 0.5f, -3.8f), new Vector3(0.8f, 0.85f, 8f), M("Appliance"));
        Box(h, "Range", new Vector3(-hx + T + 0.45f, 0.55f, 1.4f), new Vector3(0.9f, 0.95f, 1.6f), M("GunDark"));
        Box(h, "Fridge", new Vector3(-hx + T + 0.45f, 1.05f, 3.2f), new Vector3(0.9f, 1.9f, 0.9f), M("Appliance"));
        Box(h, "Serving Counter", new Vector3(-2.9f, 0.55f, -3.8f), new Vector3(0.6f, 0.95f, 5f), M("Furniture"));
        Passage(h, "Mess hall front doors", new Vector3(hx - T / 2f, 0.15f, 0f), Vector3.right);
        Passage(h, "Mess hall kitchen door", new Vector3(-hx + T / 2f, 0.15f, 5.25f), Vector3.right);
        Passage(h, "Mess hall side door S", new Vector3(0f, 0.15f, -hz + T / 2f), Vector3.forward);
        Passage(h, "Mess hall side door N", new Vector3(0f, 0.15f, hz - T / 2f), Vector3.forward);
        foreach (var p in new[] { new Vector3(hx + 1.2f, 0.15f, 0f), new Vector3(hx - 1.4f, 0.15f, 0f), new Vector3(0.7f, 0.15f, -6.8f), new Vector3(0.7f, 0.15f, 6.8f),
                                  new Vector3(0f, 0.15f, -hz - 1f), new Vector3(0f, 0.15f, hz + 1f), new Vector3(-hx + 1.3f, 0.15f, 5.25f), new Vector3(-hx - 1f, 0.05f, 5.25f) })
            NavHint(h, p);
        return h;
    }

    /// <summary>Straight flight of open stair treads from `start` (bottom floor point) along an axis-aligned `dir`.</summary>
    static void Flight(Transform parent, Vector3 start, Vector3 dir, int steps, float rise, float run, float width)
    {
        bool alongX = Mathf.Abs(dir.x) > 0.5f;
        for (int i = 0; i < steps; i++)
        {
            Vector3 c = start + dir * (run * (i + 0.5f));
            float top = start.y + (i + 1) * rise;
            Box(parent, "Step", new Vector3(c.x, top - 0.06f, c.z), alongX ? new Vector3(run + 0.02f, 0.12f, width) : new Vector3(width, 0.12f, run + 0.02f), M("Floorboards"));
            if (i % 3 == 1) NavHint(parent, new Vector3(c.x, top, c.z));
        }
        // Stringers along both sides (looks only).
        float len = steps * run, totalRise = steps * rise, angle = Mathf.Atan2(totalRise, len) * Mathf.Rad2Deg;
        Vector3 mid = start + dir * (len / 2f) + Vector3.up * (totalRise / 2f - 0.1f);
        Vector3 side = alongX ? Vector3.forward : Vector3.right;
        foreach (float s in new[] { -1f, 1f })
        {
            var st = Box(parent, "Stringer", mid + side * s * (width / 2f + 0.04f), alongX ? new Vector3(Mathf.Sqrt(len * len + totalRise * totalRise), 0.22f, 0.06f)
                                                                                            : new Vector3(0.06f, 0.22f, Mathf.Sqrt(len * len + totalRise * totalRise)), M("Trunk"), collider: false);
            st.transform.localRotation = alongX ? Quaternion.Euler(0f, 0f, angle * Mathf.Sign(dir.x)) : Quaternion.Euler(-angle * Mathf.Sign(dir.z), 0f, 0f);
        }
    }

    /// <summary>Fire lookout: a 6 m deck with a half-walled cab and roof, reached by a switchback stair on its +X side.</summary>
    static Transform BuildLookout(Vector3 pos, float yRot)
    {
        var t = Building("Fire Lookout", pos, yRot);
        const float top = 6f;
        foreach (float x in new[] { -2.8f, 2.8f })
            foreach (float z in new[] { -2.8f, 2.8f })
                Box(t, "Leg", new Vector3(x, (top - 3f) / 2f, z), new Vector3(0.35f, top + 3f, 0.35f), M("Trunk"));
        foreach (float y in new[] { 1.5f, 4f })
        {
            Box(t, "Brace", new Vector3(0f, y, -2.8f), new Vector3(5.6f, 0.15f, 0.15f), M("Trunk"), collider: false);
            Box(t, "Brace", new Vector3(0f, y, 2.8f), new Vector3(5.6f, 0.15f, 0.15f), M("Trunk"), collider: false);
            Box(t, "Brace", new Vector3(-2.8f, y, 0f), new Vector3(0.15f, 0.15f, 5.6f), M("Trunk"), collider: false);
            Box(t, "Brace", new Vector3(2.8f, y, 0f), new Vector3(0.15f, 0.15f, 5.6f), M("Trunk"), collider: false);
        }
        Box(t, "Deck", new Vector3(0f, top - 0.1f, 0f), new Vector3(6.2f, 0.2f, 6.2f), M("Floorboards"));
        // Half walls (open above so snipers can see out), with the entry gap at the stair landing.
        Box(t, "Half Wall", new Vector3(0f, top + 0.55f, -3.05f), new Vector3(6.2f, 1.1f, 0.12f), M("CabinLogs"));
        Box(t, "Half Wall", new Vector3(0f, top + 0.55f, 3.05f), new Vector3(6.2f, 1.1f, 0.12f), M("CabinLogs"));
        Box(t, "Half Wall", new Vector3(-3.05f, top + 0.55f, 0f), new Vector3(0.12f, 1.1f, 6.2f), M("CabinLogs"));
        Box(t, "Half Wall", new Vector3(3.05f, top + 0.55f, -0.75f), new Vector3(0.12f, 1.1f, 4.7f), M("CabinLogs"));
        foreach (float x in new[] { -3f, 3f })
            foreach (float z in new[] { -3f, 3f })
                Box(t, "Cab Post", new Vector3(x, top + 1.3f, z), new Vector3(0.15f, 2.6f, 0.15f), M("Trunk"));
        Box(t, "Cab Roof", new Vector3(0f, top + 2.7f, 0f), new Vector3(7.4f, 0.2f, 7.4f), M("RoofRed"));
        Box(t, "Fire Finder", new Vector3(0f, top + 0.5f, 0f), new Vector3(0.9f, 1f, 0.9f), M("Furniture"));
        // Switchback stair: up along -Z, landing, back up along +Z, top landing into the cab.
        Flight(t, new Vector3(3.65f, 0f, 1.6f), Vector3.back, 12, 0.25f, 0.3f, 1.1f);
        Box(t, "Mid Landing", new Vector3(4.25f, 2.9f, -2.6f), new Vector3(2.3f, 0.2f, 1.2f), M("Floorboards"));
        Flight(t, new Vector3(4.85f, 3f, -2f), Vector3.forward, 12, 0.25f, 0.3f, 1.1f);
        Box(t, "Top Landing", new Vector3(4.25f, top - 0.1f, 2.35f), new Vector3(2.3f, 0.2f, 1.5f), M("Floorboards"));
        Box(t, "Rail", new Vector3(5.45f, top + 0.5f, 2.35f), new Vector3(0.08f, 1f, 1.5f), M("Trunk"));
        Box(t, "Rail", new Vector3(4.25f, top + 0.5f, 3.1f), new Vector3(2.3f, 1f, 0.08f), M("Trunk"));
        Box(t, "Rail", new Vector3(5.45f, 3.5f, -2.6f), new Vector3(0.08f, 1f, 1.2f), M("Trunk"));
        foreach (var (x, z) in new[] { (3.1f, -3.2f), (5.4f, -3.2f), (5.4f, 3.1f) })
            Box(t, "Stair Post", new Vector3(x, (top + 2f) / 2f - 1.5f, z), new Vector3(0.18f, top + 1f, 0.18f), M("Trunk"));
        Passage(t, "Lookout entry", new Vector3(3.0f, top, 2.35f), Vector3.right, run: 0.8f);
        NavHint(t, new Vector3(3.65f, 0.05f, 2.4f));
        NavHint(t, new Vector3(4.25f, 3f, -2.6f));
        NavHint(t, new Vector3(4.25f, top, 2.35f));
        NavHint(t, new Vector3(1.5f, top, 1.5f));
        return t;
    }

    /// <summary>Hunter's deer stand: a roofed platform on posts with a stair up its -X side.</summary>
    static Transform BuildDeerStand(Vector3 pos, float yRot)
    {
        var t = Building("Deer Stand", pos, yRot);
        const float top = 3.25f;
        foreach (float x in new[] { -1.2f, 1.2f })
            foreach (float z in new[] { -1.2f, 1.2f })
                Box(t, "Post", new Vector3(x, (top + 2.4f - 1.5f) / 2f, z), new Vector3(0.22f, top + 2.4f + 1.5f, 0.22f), M("Trunk"));
        Box(t, "Platform", new Vector3(0f, top - 0.1f, 0f), new Vector3(2.8f, 0.2f, 2.8f), M("Floorboards"));
        Box(t, "Rail", new Vector3(1.35f, top + 0.5f, 0f), new Vector3(0.1f, 1f, 2.8f), M("Fence"));
        Box(t, "Rail", new Vector3(0f, top + 0.5f, -1.35f), new Vector3(2.8f, 1f, 0.1f), M("Fence"));
        Box(t, "Rail", new Vector3(0f, top + 0.5f, 1.35f), new Vector3(2.8f, 1f, 0.1f), M("Fence"));
        var roof = Box(t, "Roof", new Vector3(0f, top + 2.45f, 0f), new Vector3(3.3f, 0.12f, 3.3f), M("Roof"));
        roof.transform.localRotation = Quaternion.Euler(0f, 0f, 8f);
        Flight(t, new Vector3(-1.4f - 13 * 0.3f, 0f, 0f), Vector3.right, 13, 0.25f, 0.3f, 1f);
        NavHint(t, new Vector3(-1.4f - 13 * 0.3f - 0.5f, 0.05f, 0f));
        NavHint(t, new Vector3(0f, top, 0f));
        return t;
    }

    static void BuildLeanTo(Vector3 pos, float yRot)
    {
        var t = Building("Lean-to", pos, yRot);
        foreach (float z in new[] { -1.6f, 1.6f })
        {
            Box(t, "Front Post", new Vector3(1.2f, 1.25f, z), new Vector3(0.2f, 2.5f, 0.2f), M("Trunk"));
            Box(t, "Back Post", new Vector3(-1.2f, 0.6f, z), new Vector3(0.2f, 1.2f, 0.2f), M("Trunk"));
        }
        Box(t, "Roof", new Vector3(0f, 1.95f, 0f), new Vector3(2.9f, 0.15f, 3.8f), M("Bark"), zRot: Mathf.Atan2(1.3f, 2.4f) * Mathf.Rad2Deg);
        Box(t, "Back Wall", new Vector3(-1.25f, 0.6f, 0f), new Vector3(0.15f, 1.2f, 3.4f), M("Fence"));
        var log = Cylinder(t, "Log Seat", new Vector3(2.4f, 0.3f, 0f), 0.3f, 2.6f, M("Bark"));
        log.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
    }

    // ------------------------------------------------------------------ camp props

    static void BuildCampfire(Vector3 pos)
    {
        var t = Building("Campfire", pos, 0f);
        var stone = BoulderMesh(1);
        for (int i = 0; i < 11; i++)
        {
            float a = i / 11f * Mathf.PI * 2f;
            var s = MeshObject(t, "Fire Stone", stone, new[] { M("Rock") }, new Vector3(Mathf.Cos(a) * 1.25f, 0.05f, Mathf.Sin(a) * 1.25f),
                Quaternion.Euler(0f, i * 47f, 0f), new Vector3(0.45f, 0.35f, 0.4f));
            s.AddComponent<MeshCollider>().convex = true;
        }
        for (int i = 0; i < 3; i++)
        {
            var l = Cylinder(t, "Charred Log", new Vector3(0f, 0.25f, 0f), 0.12f, 1.5f, M("Charred"));
            l.transform.localRotation = Quaternion.Euler(70f, i * 120f, 0f);
            Object.DestroyImmediate(l.GetComponent<Collider>());
        }
        foreach (var p in new[] { new Vector3(0.15f, 0.12f, 0.1f), new Vector3(-0.2f, 0.1f, -0.05f), new Vector3(0.05f, 0.14f, -0.25f) })
        {
            var e = Sphere(t, "Embers", p, 0.3f, M("Embers"));
            Object.DestroyImmediate(e.GetComponent<Collider>());
        }
        var lightGo = new GameObject("Fire Light");
        lightGo.transform.SetParent(t, false);
        lightGo.transform.localPosition = new Vector3(0f, 0.8f, 0f);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.55f, 0.2f);
        light.intensity = 1.8f;
        light.range = 9f;
        light.shadows = LightShadows.None;
        // Log benches around the fire.
        for (int i = 0; i < 4; i++)
        {
            float a = (i + 0.5f) / 4f * Mathf.PI * 2f;
            var bench = Cylinder(t, "Log Bench", new Vector3(Mathf.Cos(a) * 3.6f, 0.3f, Mathf.Sin(a) * 3.6f), 0.3f, 2.6f, M("Bark"));
            bench.transform.localRotation = Quaternion.Euler(0f, 90f - a * Mathf.Rad2Deg, 0f) * Quaternion.Euler(0f, 0f, 90f);
        }
    }

    static void BuildFlagpole(Vector3 pos)
    {
        var t = Building("Flagpole", pos, 0f);
        Cylinder(t, "Pole", new Vector3(0f, 4.5f, 0f), 0.07f, 9f, M("Chrome"));
        Box(t, "Flag", new Vector3(0f, 8.2f, 0.9f), new Vector3(0.03f, 1.1f, 1.7f), M("TruckRed"), collider: false);
        Box(t, "Flag Stripe", new Vector3(0f, 8.2f, 0.9f), new Vector3(0.035f, 0.3f, 1.72f), M("Trim"), collider: false);
        Box(t, "Base", new Vector3(0f, 0.15f, 0f), new Vector3(1.2f, 0.3f, 1.2f), M("Concrete"));
    }

    static void BuildTent(Vector3 pos, float yaw, Material cloth)
    {
        var t = Building("Tent", pos, yaw);
        const float halfW = 2f, ridge = 2.6f, len = 3.6f;
        float slope = Mathf.Sqrt(halfW * halfW + ridge * ridge) + 0.1f, ang = Mathf.Atan2(ridge, halfW) * Mathf.Rad2Deg;
        foreach (float side in new[] { -1f, 1f })
            Box(t, "Tent Side", new Vector3(side * halfW / 2f, ridge / 2f, 0f), new Vector3(slope, 0.05f, len), cloth, zRot: -side * ang);
        var pole = Cylinder(t, "Ridge Pole", new Vector3(0f, ridge, 0f), 0.04f, len + 0.4f, M("Metal"));
        pole.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        Object.DestroyImmediate(pole.GetComponent<Collider>());
        Box(t, "Sleeping Bag", new Vector3(-0.6f, 0.08f, 0f), new Vector3(0.75f, 0.16f, 2.1f), M("TentBlue"), collider: false);
        Box(t, "Sleeping Bag", new Vector3(0.6f, 0.08f, 0f), new Vector3(0.75f, 0.16f, 2.1f), M("TentGreen"), collider: false);
    }

    static void BuildPicnicTable(Vector3 pos, float yaw)
    {
        var t = Building("Picnic Table", pos, yaw);
        Box(t, "Top", new Vector3(0f, 0.75f, 0f), new Vector3(1.9f, 0.07f, 0.85f), M("Fence"));
        foreach (float z in new[] { -0.7f, 0.7f })
            Box(t, "Seat", new Vector3(0f, 0.45f, z), new Vector3(1.9f, 0.06f, 0.3f), M("Fence"));
        foreach (float x in new[] { -0.75f, 0.75f })
            Box(t, "Legs", new Vector3(x, 0.37f, 0f), new Vector3(0.08f, 0.75f, 1.6f), M("Fence"), collider: false);
    }

    static void BuildArchery(Vector3 pos)
    {
        var t = Building("Archery Range", pos, 0f);
        for (int i = -1; i <= 1; i++)
        {
            Box(t, "Hay Bale", new Vector3(i * 3f, 0.55f, 2f), new Vector3(1.4f, 1.1f, 0.8f), M("Hay"));
            var face = Cylinder(t, "Target", new Vector3(i * 3f, 1.3f, 1.62f), 0.55f, 0.04f, M("Trim"));
            face.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var eye = Cylinder(t, "Bullseye", new Vector3(i * 3f, 1.3f, 1.59f), 0.22f, 0.04f, M("TruckRed"));
            eye.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Object.DestroyImmediate(eye.GetComponent<Collider>());
        }
        Box(t, "Shooting Line", new Vector3(0f, 0.02f, -6f), new Vector3(9f, 0.03f, 0.12f), M("Trim"), collider: false);
    }

    static void BuildOuthouse(Vector3 pos, float yaw)
    {
        var t = Building("Outhouse", pos, yaw);
        Box(t, "Booth", new Vector3(0f, 1.2f, 0f), new Vector3(1.4f, 2.4f, 1.4f), M("Fence"));
        Box(t, "Door", new Vector3(0.72f, 1.05f, 0f), new Vector3(0.05f, 2f, 0.8f), M("Furniture"), collider: false);
        Box(t, "Roof", new Vector3(0f, 2.5f, 0f), new Vector3(1.8f, 0.1f, 1.8f), M("Roof"), zRot: 10f);
    }

    static void BuildWoodpile(Vector3 pos, float yaw)
    {
        var t = Building("Woodpile", pos, yaw);
        for (int row = 0; row < 3; row++)
            for (int i = 0; i < 4 - row; i++)
            {
                var l = Cylinder(t, "Firewood", new Vector3(-0.9f + i * 0.6f + row * 0.3f, 0.27f + row * 0.48f, 0f), 0.27f, 1.6f, M("Bark"));
                l.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
    }

    static void BuildCanoeRack(Vector3 pos, float yaw)
    {
        var t = Building("Canoe Rack", pos, yaw);
        foreach (float z in new[] { -1.4f, 1.4f })
        {
            Box(t, "Rack Post", new Vector3(-0.9f, 0.6f, z), new Vector3(0.12f, 1.2f, 0.12f), M("Trunk"));
            Box(t, "Rack Post", new Vector3(0.9f, 0.6f, z), new Vector3(0.12f, 1.2f, 0.12f), M("Trunk"));
            Box(t, "Rack Bar", new Vector3(0f, 1.15f, z), new Vector3(2f, 0.1f, 0.1f), M("Trunk"));
        }
        foreach (var (x, mat) in new[] { (-0.5f, "TruckRed"), (0.5f, "CarGreen") })
        {
            var canoe = Sphere(t, "Canoe", new Vector3(x, 1.38f, 0f), 1f, M(mat));
            canoe.transform.localScale = new Vector3(0.8f, 0.4f, 4.6f);
            Object.DestroyImmediate(canoe.GetComponent<Collider>());
        }
    }

    /// <summary>Wooden dock from the north shore out over the pond. Returns where the golden Deagle sits.</summary>
    static Vector3 BuildDock(Vector3 shore)
    {
        var t = Building("Dock", shore, 0f);
        const float deckY = 0.3f, length = 15f;
        Box(t, "Deck", new Vector3(0f, deckY - 0.08f, -length / 2f), new Vector3(2.4f, 0.16f, length), M("Floorboards"));
        Box(t, "End Deck", new Vector3(0f, deckY - 0.08f, -length - 1.5f), new Vector3(7f, 0.16f, 3f), M("Floorboards"));
        for (float z = -2f; z > -length - 2.5f; z -= 2.5f)
            foreach (float x in new[] { -1.1f, 1.1f })
                Cylinder(t, "Piling", new Vector3(x, -0.9f, z), 0.12f, 2.6f, M("Trunk"));
        foreach (float x in new[] { -3.3f, 3.3f })
            Cylinder(t, "Piling", new Vector3(x, -0.6f, -length - 2.8f), 0.14f, 2.2f, M("Trunk"));
        Box(t, "Bench", new Vector3(0f, deckY + 0.25f, -length - 2.7f), new Vector3(2.6f, 0.08f, 0.4f), M("Fence"));
        Box(t, "Life Ring", new Vector3(3.3f, deckY + 0.55f, -length - 2.8f), new Vector3(0.6f, 0.6f, 0.1f), M("TruckRed"), collider: false);
        for (int i = 0; i < 4; i++) NavHint(t, new Vector3(0f, deckY, -2f - i * 4f));
        return t.TransformPoint(new Vector3(-2f, deckY + 0.02f, -length - 1.5f));
    }

    static void BuildRowboat(Vector3 pos, float yaw)
    {
        var t = Building("Rowboat", pos, yaw);
        Box(t, "Hull", new Vector3(0f, 0.05f, 0f), new Vector3(1.5f, 0.55f, 3.6f), M("CarCream"));
        Box(t, "Hull Floor", new Vector3(0f, 0.2f, 0f), new Vector3(1.2f, 0.1f, 3.2f), M("Floorboards"), collider: false);
        Box(t, "Seat", new Vector3(0f, 0.35f, 0.3f), new Vector3(1.3f, 0.06f, 0.3f), M("Furniture"), collider: false);
        Box(t, "Bow", new Vector3(0f, 0.05f, 2f), new Vector3(1f, 0.55f, 0.6f), M("CarCream"), yRot: 45f);
    }

    static void BuildCampGate(Vector3 pos)
    {
        var t = Building("Camp Gate", pos, 0f);
        foreach (float x in new[] { -4.2f, 4.2f })
            Cylinder(t, "Gate Post", new Vector3(x, 2.5f, 0f), 0.3f, 6f, M("Bark"));
        var beam = Cylinder(t, "Gate Beam", new Vector3(0f, 5.2f, 0f), 0.25f, 10f, M("Bark"));
        beam.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        Box(t, "Sign Board", new Vector3(0f, 4.35f, 0f), new Vector3(6.4f, 1.1f, 0.12f), M("GunWood"));
        foreach (float x in new[] { -2.4f, 2.4f })
            Box(t, "Chain", new Vector3(x, 4.95f, 0f), new Vector3(0.05f, 0.4f, 0.05f), M("Metal"), collider: false);
        foreach (float side in new[] { -1f, 1f })
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.name = "Sign Text";
            q.isStatic = true;
            q.transform.SetParent(t, false);
            q.transform.localPosition = new Vector3(0f, 4.37f, side * 0.065f);
            q.transform.localRotation = Quaternion.Euler(0f, side > 0 ? 180f : 0f, 0f); // readable from each side
            q.transform.localScale = new Vector3(6.1f, 6.1f * 160f / 1024f, 1f);
            q.GetComponent<Renderer>().sharedMaterial = M("CampSignText");
        }
    }

    // ------------------------------------------------------------------ generated meshes

    static GameObject MeshObject(Transform parent, string name, Mesh mesh, Material[] materials, Vector3 pos, Quaternion rot, Vector3 scale)
    {
        var go = new GameObject(name) { isStatic = true };
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = materials;
        return go;
    }

    static readonly Dictionary<string, Mesh> meshCache = new();

    static Mesh SaveMesh(Mesh mesh, string name)
    {
        string path = $"{GeneratedDir}/{name}.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        meshCache[name] = mesh;
        return mesh;
    }

    static Mesh Cached(string name, System.Func<Mesh> make)
    {
        if (meshCache.TryGetValue(name, out var m) && m != null) return m;
        return SaveMesh(make(), name);
    }

    /// <summary>Triangular prism (gable end): base along X from -0.5 to 0.5, apex at y = 1, depth along Z.</summary>
    static Mesh GableMesh() => Cached("Gable", () =>
    {
        var mb = new MeshBuilder(1);
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i0 = mb.Add(a, new Vector2(a.x + a.z, a.y)), i1 = mb.Add(b, new Vector2(b.x + b.z, b.y)), i2 = mb.Add(c, new Vector2(c.x + c.z, c.y)), i3 = mb.Add(d, new Vector2(d.x + d.z, d.y));
            mb.Tri(0, i0, i1, i2);
            mb.Tri(0, i0, i2, i3);
        }
        int f0 = mb.Add(new Vector3(-0.5f, 0f, -0.5f), new Vector2(0f, 0f)), f1 = mb.Add(new Vector3(0f, 1f, -0.5f), new Vector2(0.5f, 1f)), f2 = mb.Add(new Vector3(0.5f, 0f, -0.5f), new Vector2(1f, 0f));
        mb.Tri(0, f0, f1, f2);
        int b0 = mb.Add(new Vector3(-0.5f, 0f, 0.5f), new Vector2(1f, 0f)), b1 = mb.Add(new Vector3(0.5f, 0f, 0.5f), new Vector2(0f, 0f)), b2 = mb.Add(new Vector3(0f, 1f, 0.5f), new Vector2(0.5f, 1f));
        mb.Tri(0, b0, b1, b2);
        Quad(new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0f, 1f, 0.5f), new Vector3(0f, 1f, -0.5f));
        Quad(new Vector3(0.5f, 0f, -0.5f), new Vector3(0f, 1f, -0.5f), new Vector3(0f, 1f, 0.5f), new Vector3(0.5f, 0f, 0.5f));
        return mb.Build("Gable");
    });

    /// <summary>Pine: bark trunk (submesh 0) and three or four stacked needle cones (submesh 1).</summary>
    static Mesh PineMesh(int variant) => Cached("Pine" + variant, () =>
    {
        var rng = new System.Random(100 + variant);
        var mb = new MeshBuilder(2);
        float height = variant == 0 ? 10f : 12.5f;
        mb.Tube(0, Vector3.zero, 0.3f, 0.12f, height * 0.8f, 8);
        int tiers = variant == 0 ? 3 : 4;
        for (int i = 0; i < tiers; i++)
        {
            float f = i / (float)tiers;
            float baseY = Mathf.Lerp(2.4f, height * 0.62f, f);
            float r = Mathf.Lerp(2.3f, 1.0f, f) * (0.9f + 0.2f * (float)rng.NextDouble());
            float h = Mathf.Lerp(4.2f, height - baseY, i == tiers - 1 ? 1f : 0f);
            if (i < tiers - 1) h = Mathf.Lerp(4.4f, 3.4f, f);
            mb.Cone(1, new Vector3(0f, baseY, 0f), r, h, 9, (float)rng.NextDouble() * 40f);
        }
        return mb.Build("Pine" + variant);
    });

    /// <summary>Leafy tree: trunk with a couple of branches and a lumpy crown of noisy spheres.</summary>
    static Mesh LeafyMesh(int variant) => Cached("Leafy" + variant, () =>
    {
        var mb = new MeshBuilder(2);
        float trunkH = variant == 0 ? 5f : 6f;
        mb.Tube(0, Vector3.zero, 0.36f, 0.2f, trunkH, 8);
        var crown = new Vector3(0f, trunkH + 0.8f, 0f);
        mb.Blob(1, crown, new Vector3(2.6f, 2.2f, 2.6f), 7, 10, 0.18f, 11 + variant);
        mb.Blob(1, crown + new Vector3(1.6f, -0.6f, 0.4f), new Vector3(1.7f, 1.5f, 1.7f), 6, 9, 0.2f, 21 + variant);
        mb.Blob(1, crown + new Vector3(-1.3f, -0.4f, -1f), new Vector3(1.8f, 1.6f, 1.8f), 6, 9, 0.2f, 31 + variant);
        if (variant == 1) mb.Blob(1, crown + new Vector3(0.2f, 1.4f, -0.3f), new Vector3(1.6f, 1.4f, 1.6f), 6, 9, 0.2f, 41);
        return mb.Build("Leafy" + variant);
    });

    /// <summary>Lumpy rock about 1 unit across with a flattened base.</summary>
    static Mesh BoulderMesh(int variant) => Cached("Boulder" + variant, () =>
    {
        var mb = new MeshBuilder(1);
        mb.Blob(0, Vector3.zero, new Vector3(0.5f, 0.5f, 0.5f), 8, 12, 0.32f, 77 + variant * 13, flattenBelow: -0.25f);
        return mb.Build("Boulder" + variant);
    });

    /// <summary>Small helper for building meshes with several submeshes (one per material).</summary>
    class MeshBuilder
    {
        readonly List<Vector3> v = new();
        readonly List<Vector2> uv = new();
        readonly List<int>[] sub;

        public MeshBuilder(int submeshes)
        {
            sub = new List<int>[submeshes];
            for (int i = 0; i < submeshes; i++) sub[i] = new List<int>();
        }

        public int Add(Vector3 p, Vector2 t)
        {
            v.Add(p);
            uv.Add(t);
            return v.Count - 1;
        }

        public void Tri(int s, int a, int b, int c)
        {
            sub[s].Add(a);
            sub[s].Add(b);
            sub[s].Add(c);
        }

        /// <summary>Open tube from `basePos` straight up, tapering from r0 to r1.</summary>
        public void Tube(int s, Vector3 basePos, float r0, float r1, float h, int seg)
        {
            int start = v.Count;
            for (int k = 0; k <= seg; k++)
            {
                float a = k / (float)seg * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Add(basePos + dir * r0, new Vector2(k / (float)seg * 2f, 0f));
                Add(basePos + dir * r1 + Vector3.up * h, new Vector2(k / (float)seg * 2f, h / 2f));
            }
            for (int k = 0; k < seg; k++)
            {
                int b0 = start + k * 2, t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
                Tri(s, b0, t0, b1);
                Tri(s, b1, t0, t1);
            }
        }

        /// <summary>Cone with a closed base, its rim slightly ragged.</summary>
        public void Cone(int s, Vector3 basePos, float r, float h, int seg, float phase)
        {
            var ring = new Vector3[seg + 1];
            for (int k = 0; k <= seg; k++)
            {
                float a = (k % seg) / (float)seg * Mathf.PI * 2f + phase * Mathf.Deg2Rad;
                float rr = r * (0.85f + 0.3f * Mathf.PerlinNoise(k % seg * 0.9f + phase, phase));
                float droop = (k % 2 == 0 ? -0.25f : 0f);
                ring[k] = basePos + new Vector3(Mathf.Cos(a) * rr, droop, Mathf.Sin(a) * rr);
            }
            var apex = basePos + Vector3.up * h;
            for (int k = 0; k < seg; k++)
            {
                int i0 = Add(ring[k], new Vector2(k / (float)seg * 3f, 0f));
                int i1 = Add(apex, new Vector2((k + 0.5f) / seg * 3f, h / 2f));
                int i2 = Add(ring[k + 1], new Vector2((k + 1f) / seg * 3f, 0f));
                Tri(s, i0, i1, i2);
            }
            int c = Add(basePos + Vector3.up * 0.15f, new Vector2(0.5f, 0.5f));
            for (int k = 0; k < seg; k++)
            {
                int i0 = Add(ring[k], new Vector2(ring[k].x - basePos.x, ring[k].z - basePos.z) * 0.3f);
                int i1 = Add(ring[k + 1], new Vector2(ring[k + 1].x - basePos.x, ring[k + 1].z - basePos.z) * 0.3f);
                Tri(s, c, i0, i1);
            }
        }

        /// <summary>UV sphere with radii `radii`, pushed in and out by smooth noise.</summary>
        public void Blob(int s, Vector3 center, Vector3 radii, int lat, int lon, float noise, int seed, float flattenBelow = -999f)
        {
            int start = v.Count;
            for (int i = 0; i <= lat; i++)
            {
                float th = Mathf.PI * i / lat;
                for (int j = 0; j <= lon; j++)
                {
                    float ph = Mathf.PI * 2f * (j % lon) / lon;
                    var dir = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    float n = 1f + noise * (Mathf.PerlinNoise(dir.x * 1.7f + seed, dir.y * 1.7f + dir.z * 1.3f + seed * 0.37f) - 0.5f) * 2f;
                    var p = Vector3.Scale(dir * n, radii);
                    if (p.y < flattenBelow) p.y = flattenBelow;
                    Add(center + p, new Vector2(j / (float)lon * 2f, i / (float)lat));
                }
            }
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    int a = start + i * (lon + 1) + j, b = a + 1, c = a + lon + 1, d = c + 1;
                    Tri(s, a, b, c);
                    Tri(s, b, d, c);
                }
        }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(v);
            m.SetUVs(0, uv);
            m.subMeshCount = sub.Length;
            for (int i = 0; i < sub.Length; i++) m.SetTriangles(sub[i], i);
            m.RecalculateNormals();
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }
    }
}
