using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class ProjectBuilder
{
    // ================================================================== Downtown (city map)
    //
    // Same playable footprint as the other maps (X -90..90, Z -64..84; the original grid sits inside X -68..68, Z -46..66,
    // ringed by outer avenues at x = +-73 and outer streets at z = -51 and 70 with a band of blocks beyond them). Three east-west streets (1st St z=-22,
    // Main St z=10, 3rd St z=44) cross three avenues (West Ave x=-38, Center Ave x=0, East Ave x=38), leaving a
    // 4x4 grid of blocks. Blocks hold enterable two-storey shops and apartments with stairs to the roof, tower
    // lobbies, a parking garage with ramps, a construction site, a warehouse, a gas station, a plaza and a court.
    // A skyline of towers rings the playable area.

    /// <summary>When set, Box() maps textures in metres (one tile = this many metres) instead of stretching them per face.</summary>
    static Vector2 metricTile;

    static readonly float[] AvenueX = { -38f, 0f, 38f };
    static readonly float[] StreetZ = { -22f, 10f, 44f };
    static readonly float[] AvenueW = { 10f, 12f, 10f };
    static readonly float[] StreetW = { 10f, 12f, 10f };

    // Block bands between roads (including sidewalks).
    static readonly (float a, float b)[] BlockX = { (-90f, -78f), (-68f, -43f), (-33f, -6f), (6f, 33f), (43f, 68f), (78f, 90f) };
    static readonly (float a, float b)[] BlockZ = { (-64f, -56f), (-46f, -27f), (-17f, 4f), (16f, 39f), (49f, 66f), (74f, 84f) };
    static readonly float[] OuterAvenueX = { -73f, 73f };
    static readonly float[] OuterStreetZ = { -51f, 70f };
    const float Curb = 0.15f;

    static void BuildCity()
    {
        world = new GameObject("Downtown").transform;
        Directory.CreateDirectory(GeneratedDir + "/City");
        var rng = new System.Random(909);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        // ---------------------------------------------------------------- ground, blocks and road markings
        metricTile = new Vector2(6f, 6f);
        Box(world, "Street Ground", new Vector3(0f, -0.5f, 10f), new Vector3(900f, 1f, 900f), M("CityAsphalt"));
        metricTile = new Vector2(3f, 3f);
        foreach (var bx in BlockX)
            foreach (var bz in BlockZ)
                Box(world, "Block", new Vector3((bx.a + bx.b) / 2f, Curb / 2f, (bz.a + bz.b) / 2f), new Vector3(bx.b - bx.a, Curb, bz.b - bz.a), M("CityConcrete"));
        metricTile = default;
        bool NearAvenue(float x) { foreach (var a in AvenueX) if (Mathf.Abs(x - a) < 7.5f) return true; return false; }
        bool NearStreet(float z) { foreach (var s in StreetZ) if (Mathf.Abs(z - s) < 7.5f) return true; return false; }
        foreach (float z in StreetZ)
            for (float x = MapMinX + 2f; x < MapMaxX - 1f; x += 4.5f)
                if (!NearAvenue(x)) Box(world, "LaneDash", new Vector3(x, 0.012f, z), new Vector3(2f, 0.01f, 0.15f), M("Trim"), collider: false);
        foreach (float x in AvenueX)
            for (float z = MapMinZ + 2f; z < MapMaxZ - 1f; z += 4.5f)
                if (!NearStreet(z)) Box(world, "LaneDash", new Vector3(x, 0.012f, z), new Vector3(0.15f, 0.01f, 2f), M("Trim"), collider: false);
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                Crosswalks(AvenueX[i], StreetZ[j], AvenueW[i] / 2f, StreetW[j] / 2f);
        // The outer ring roads.
        foreach (float x in OuterAvenueX)
            for (float z = MapMinZ + 4f; z < MapMaxZ - 4f; z += 4.5f)
                if (!NearStreet(z) && Mathf.Abs(z - OuterStreetZ[0]) > 6f && Mathf.Abs(z - OuterStreetZ[1]) > 6f)
                    Box(world, "LaneDash", new Vector3(x, 0.012f, z), new Vector3(0.15f, 0.01f, 2f), M("Trim"), collider: false);
        foreach (float z in OuterStreetZ)
            for (float x = MapMinX + 4f; x < MapMaxX - 4f; x += 4.5f)
                if (!NearAvenue(x) && Mathf.Abs(Mathf.Abs(x) - 73f) > 6f)
                    Box(world, "LaneDash", new Vector3(x, 0.012f, z), new Vector3(2f, 0.01f, 0.15f), M("Trim"), collider: false);
        BuildCityOuterRing(rng);
        // Jump pads: from the streets up onto shop roofs (the roofs have stairs back down), plus one across the ring.
        JumpPadTo(new Vector3(-25f, 0f, -24.5f), new Vector3(-25f, Curb + 7.3f, -37f), 4f);   // 1st St -> Pawn Shop roof
        JumpPadTo(new Vector3(-14.5f, 0f, 7f), new Vector3(-14.5f, Curb + 3.7f, -6.5f), 4f);  // Main St -> Diner roof
        JumpPadTo(new Vector3(14.5f, 0f, 13f), new Vector3(14.5f, Curb + 7.3f, -6.5f), 4f);   // Main St -> Corner Store roof
        JumpPadTo(new Vector3(-25f, 0f, 46.5f), new Vector3(-25f, Curb + 7.3f, 57.5f), 4f);   // 3rd St -> Record Store roof
        JumpPadTo(new Vector3(-73f, 0f, -12f), new Vector3(-50f, 0f, 10f), 9f);               // west ring -> Main St
        JumpPadTo(new Vector3(73f, 0f, 30f), new Vector3(50f, 0f, 10f), 9f);                  // east ring -> Main St

        // ---------------------------------------------------------------- blocks
        // Row 1 (south, along 1st St)
        var warehouse = BuildWarehouse(new Vector3(-56.5f, Curb, -37.5f), 0f);
        var shopB1a = BuildShop("Pawn Shop", new Vector3(-25f, Curb, -37f), -90f, 13f, 10f, 2, "store", M("CityBrick"), M("TruckRed"));
        var shopB1b = BuildShop("Laundromat", new Vector3(-13f, Curb, -37f), -90f, 13f, 10f, 2, "store", M("CityBrick"), M("TentBlue"));
        Dumpster(new Vector3(-19f, Curb, -43f), 90f);
        var aptC1 = BuildShop("Apartments", new Vector3(15f, Curb, -37f), -90f, 13f, 12f, 2, "apartment", M("CityBrick"), M("TentGreen"));
        BuildTower("Tower C1", new Vector3(27f, Curb, -38f), 0f, 7f, 15f, 28f, M("FacadeConcrete"), false);
        Dumpster(new Vector3(22f, Curb, -44f), 0f);
        BuildParkingLot(new Vector3(56.5f, Curb, -37.5f));
        // Row 2 (between 1st St and Main St)
        var site = BuildConstruction(new Vector3(-56.5f, Curb, -6.5f));
        var diner = BuildShop("Diner", new Vector3(-14.5f, Curb, -6.5f), 0f, 12f, 15f, 1, "diner", M("CityBrick"), M("TruckRed"));
        BuildTower("Tower B2", new Vector3(-27f, Curb, -6.5f), 0f, 8f, 16f, 20f, M("FacadeBrick"), false);
        var store = BuildShop("Corner Store", new Vector3(14.5f, Curb, -6.5f), 180f, 12f, 15f, 2, "store", M("CityBrick"), M("TentOrange"));
        BuildTower("Tower C2", new Vector3(27f, Curb, -6.5f), 0f, 8f, 16f, 24f, M("FacadeBrick"), false);
        var garage = BuildGarage(new Vector3(56.5f, Curb, -6.5f));
        // Row 3 (between Main St and 3rd St)
        var aptA3 = BuildShop("Walk-up", new Vector3(-51.5f, Curb, 27.5f), 0f, 12f, 16f, 2, "apartment", M("CityBrick"), M("TentGreen"));
        foreach (var (x, z) in new[] { (-62f, 20f), (-62f, 35f) }) Dumpster(new Vector3(x, Curb, z), 90f);
        BuildPicnicTable(new Vector3(-63f, Curb, 27.5f), 0f);
        var office = BuildTower("Office Tower", new Vector3(-19.5f, Curb, 27.5f), 0f, 18f, 14f, 46f, M("FacadeGlass"), true);
        var plaza = BuildPlaza(new Vector3(19.5f, Curb, 27.5f));
        var hotel = BuildTower("Hotel", new Vector3(56.5f, Curb, 27.5f), 180f, 14f, 16f, 34f, M("FacadeConcrete"), true);
        // Row 4 (north, along 3rd St)
        var gas = BuildGasStation(new Vector3(-56.5f, Curb, 58.5f));
        var shopB4a = BuildShop("Record Store", new Vector3(-25f, Curb, 57.5f), 90f, 12f, 10f, 2, "store", M("CityBrick"), M("TentOrange"));
        var shopB4b = BuildShop("Pizzeria", new Vector3(-13f, Curb, 57.5f), 90f, 12f, 10f, 1, "diner", M("CityBrick"), M("TruckRed"));
        Dumpster(new Vector3(-19f, Curb, 64f), 90f);
        BuildTower("Tower C4", new Vector3(19.5f, Curb, 58.5f), 0f, 22f, 14f, 40f, M("FacadeGlass"), false);
        BuildCourt(new Vector3(56.5f, Curb, 59.5f));

        // ---------------------------------------------------------------- street props
        BuildBus(new Vector3(-20f, 0f, 13f));
        BuildSemi(new Vector3(-36f, 0f, -4f));
        var cars = new (float x, float z, float yaw, string mat, bool wreck)[]
        {
            (-58f, 6.2f, 90f, "CarGreen", false), (-50f, 6.2f, 90f, "CarCream", false), (24f, 6.2f, 90f, "TruckRed", false), (50f, 13.8f, 90f, "CarCream", false),
            (14f, 13.8f, 90f, "BusBlue", false), (-4f, 30f, 0f, "CarGreen", false), (4f, -12f, 180f, "CarCream", false), (-4f, 56f, 0f, "TruckRed", false),
            (-35f, 26f, 0f, "CarCream", false), (35.5f, -10f, 180f, "CarGreen", false), (41f, 52f, 180f, "BusBlue", false), (-41f, -40f, 0f, "TruckRed", false),
            (-28f, -19.5f, 90f, "CarGreen", false), (26f, -24.5f, 90f, "CarCream", false), (-58f, 46.5f, 90f, "BusBlue", false), (58f, 41.8f, 90f, "CarGreen", false),
            // Abandoned / wrecked in the roads (cover)
            (1.5f, 4f, 30f, "CarCream", false), (-38f, 18f, 75f, "TruckRed", true), (38f, -20f, 120f, "CarGreen", true), (-2f, 40f, 160f, "BusBlue", false),
            (20f, 9f, 10f, "CarGreen", true), (-52f, -23f, 100f, "CarCream", false),
        };
        foreach (var c in cars)
            BuildCar(world, c.wreck ? "Wrecked Car" : "Car", new Vector3(c.x, 0.02f, c.z), c.yaw, M(c.mat), c.wreck);
        // Police cars and barriers where the roads leave the map.
        foreach (var (x, z, yaw) in new[] { (-64f, 10f, 0f), (64f, 10f, 0f), (-64f, -22f, 0f), (64f, -22f, 0f), (-64f, 44f, 0f), (64f, 44f, 0f),
                                            (0f, -42.5f, 90f), (0f, 62.5f, 90f), (-38f, -42.5f, 90f), (38f, -42.5f, 90f), (-38f, 62.5f, 90f), (38f, 62.5f, 90f) })
        {
            var p = new Vector3(x, 0f, z);
            var side = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            JerseyBarrier(p + side * 2.2f, yaw + 90f);
            JerseyBarrier(p - side * 2.2f, yaw + 90f);
        }
        BuildCar(world, "Police Car", new Vector3(-61f, 0.02f, 7f), 20f, M("BusWhite"));
        BuildCar(world, "Police Car", new Vector3(3f, 0.02f, -39f), 70f, M("BusWhite"));
        BuildCar(world, "Police Car", new Vector3(60f, 0.02f, 47f), -30f, M("BusWhite"));
        // Jersey barriers in the open roads for cover.
        foreach (var (x, z, yaw) in new[] { (-20f, -21f, 10f), (18f, -23f, -5f), (-2f, 22f, 80f), (2f, -2f, 95f), (38f, 25f, 85f), (-38f, 33f, 95f),
                                            (-16f, 45f, 0f), (24f, 43f, 15f), (52f, 10f, 5f), (-46f, 11f, -10f) })
            JerseyBarrier(new Vector3(x, 0f, z), yaw);
        BusStop(new Vector3(-9f, Curb, 17.6f), 0f);
        BusStop(new Vector3(9f, Curb, 2.4f), 180f);
        // Street lights along the sidewalks, traffic lights at the corners.
        foreach (var bx in BlockX)
            foreach (var bz in BlockZ)
            {
                foreach (float x in new[] { bx.a + 1f, bx.b - 1f })
                    foreach (float z in new[] { bz.a + 1f, bz.b - 1f })
                    {
                        bool atMapEdge = x < MapMinX + 2f || x > MapMaxX - 2f || z < MapMinZ + 2f || z > MapMaxZ - 2f;
                        if (atMapEdge) continue;
                        if (rng.NextDouble() < 0.55) TrafficLight(new Vector3(x, Curb, z), Mathf.Atan2(-x, 10f - z) * Mathf.Rad2Deg);
                        else StreetLight(new Vector3(x, Curb, z), Mathf.Atan2(-x, 10f - z) * Mathf.Rad2Deg);
                    }
                for (float x = bx.a + 8f; x < bx.b - 6f; x += 11f)
                    foreach (float z in new[] { bz.a + 0.7f, bz.b - 0.7f })
                        if (z > MapMinZ + 2f && z < MapMaxZ - 2f && bz.a >= CoreMinZ && bz.b <= CoreMaxZ)
                            StreetLight(new Vector3(x, Curb, z), z < (bz.a + bz.b) / 2f ? 180f : 0f);
            }
        foreach (var (x, z) in new[] { (-7f, -15f), (7f, 36f), (-31f, 6f), (32f, 14f), (-44f, 41f), (44f, -25f), (-7f, 50f), (31f, -16f) })
            Cylinder(world, "Hydrant", new Vector3(x, Curb + 0.4f, z), 0.15f, 0.8f, M("TruckRed"));
        foreach (var (x, z) in new[] { (-6.8f, 5f), (6.8f, 15f), (-32f, 15f), (32f, 5f), (-6.8f, -26f), (6.8f, 48f), (-43f, -18f), (43f, 38f) })
            Cylinder(world, "TrashCan", new Vector3(x, Curb + 0.5f, z), 0.32f, 1f, M("Metal"));

        // ---------------------------------------------------------------- skyline (outside the play area)
        var skyline = new GameObject("Skyline").transform;
        skyline.SetParent(world, false);
        var facades = new[] { M("FacadeGlass"), M("FacadeConcrete"), M("FacadeBrick") };
        for (float x = -170f; x <= 170f; x += 20f)
            for (float z = -150f; z <= 180f; z += 20f)
            {
                if (x > MapMinX - 14f && x < MapMaxX + 14f && z > MapMinZ - 14f && z < MapMaxZ + 14f) continue;
                bool road = false;
                foreach (float s in StreetZ) if (Mathf.Abs(z - s) < 12f && (x < MapMinX || x > MapMaxX)) road = true;
                foreach (float a in AvenueX) if (Mathf.Abs(x - a) < 12f && (z < MapMinZ || z > MapMaxZ)) road = true;
                if (road) continue;
                float sx = 12f + 2f * rng.Next(3), sz = 12f + 2f * rng.Next(3), hgt = 3.5f * rng.Next(6, 22);
                var mat = facades[rng.Next(3)];
                Facade(skyline, "Skyline Tower", new Vector3(x + R(-2f, 2f), hgt / 2f, z + R(-2f, 2f)), new Vector3(sx, hgt, sz), mat);
                Box(skyline, "Skyline Roof", new Vector3(x, hgt + 0.15f, z), new Vector3(sx, 0.3f, sz), M("CityRoof"), collider: false);
            }

        // ---------------------------------------------------------------- spawn points
        var spawns = new GameObject("Spawn Points").transform;
        spawns.SetParent(world, false);
        var spawnPos = new[]
        {
            new Vector3(-20f, 0.05f, 8f), new Vector3(26f, 0.05f, 12f), new Vector3(0f, 0.05f, -8f), new Vector3(0f, 0.05f, 30f),
            new Vector3(-38f, 0.05f, -35f), new Vector3(-38f, 0.05f, 30f), new Vector3(38f, 0.05f, -35f), new Vector3(38f, 0.05f, 30f),
            new Vector3(-58f, 0.05f, -22f), new Vector3(55f, 0.05f, -22f), new Vector3(-24f, 0.05f, -22f), new Vector3(22f, 0.05f, -22f),
            new Vector3(-55f, 0.05f, 44f), new Vector3(55f, 0.05f, 44f), new Vector3(-20f, 0.05f, 44f), new Vector3(20f, 0.05f, 44f),
            new Vector3(21.5f, Curb + 0.05f, 19.5f), new Vector3(-19f, Curb + 0.05f, -29f), new Vector3(56.5f, Curb + 0.05f, -37.5f), new Vector3(-49f, Curb + 0.05f, 52f),
            new Vector3(62f, Curb + 0.05f, 54f), new Vector3(-64f, Curb + 0.05f, -2f), new Vector3(52f, Curb + 0.05f, -1f), new Vector3(-62f, Curb + 0.05f, 31f),
            // Outer ring roads (bigger map)
            new Vector3(-73f, 0.05f, -38f), new Vector3(-73f, 0.05f, 28f), new Vector3(73f, 0.05f, -8f), new Vector3(73f, 0.05f, 58f),
            new Vector3(-28f, 0.05f, -51f), new Vector3(30f, 0.05f, -51f), new Vector3(-20f, 0.05f, 70f), new Vector3(44f, 0.05f, 70f),
            new Vector3(-73f, 0.05f, 70f), new Vector3(73f, 0.05f, -51f),
        };
        foreach (var p in spawnPos)
        {
            var sp = new GameObject("Spawn");
            sp.transform.SetParent(spawns, false);
            sp.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, Mathf.Atan2(-p.x, 10f - p.z) * Mathf.Rad2Deg, 0f));
            sp.AddComponent<SpawnPoint>();
        }

        // ---------------------------------------------------------------- boundary
        float w = MapMaxX - MapMinX, d = MapMaxZ - MapMinZ, cz = (MapMinZ + MapMaxZ) / 2f;
        Box(world, "Invisible Wall", new Vector3(0f, 40f, MapMaxZ + 0.2f), new Vector3(w + 1f, 100f, 0.3f), null);
        Box(world, "Invisible Wall", new Vector3(0f, 40f, MapMinZ - 0.2f), new Vector3(w + 1f, 100f, 0.3f), null);
        Box(world, "Invisible Wall", new Vector3(MapMinX - 0.2f, 40f, cz), new Vector3(0.3f, 100f, d + 1f), null);
        Box(world, "Invisible Wall", new Vector3(MapMaxX + 0.2f, 40f, cz), new Vector3(0.3f, 100f, d + 1f), null);
    }

    // ------------------------------------------------------------------ outer ring (the bigger map)

    /// <summary>
    /// Fills the band of blocks outside the original grid: enterable shops and walk-ups (stairs to the roof) mixed with
    /// solid buildings, all facing in toward the ring roads, plus parked cars and barriers on those roads.
    /// </summary>
    static void BuildCityOuterRing(System.Random rng)
    {
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        var names = new[] { "Hardware Store", "Bakery", "Barber", "Tailor", "Bookshop", "Deli", "Florist", "Arcade", "Pharmacy", "Tattoo Parlor", "Pet Shop", "Thrift Store" };
        var awnings = new[] { "TruckRed", "TentBlue", "TentGreen", "TentOrange" };
        var facades = new[] { M("FacadeBrick"), M("FacadeConcrete"), M("FacadeGlass") };
        int k = 0;
        foreach (var bx in BlockX)
            foreach (var bz in BlockZ)
            {
                bool outerX = bx.a < CoreMinX || bx.b > CoreMaxX, outerZ = bz.a < CoreMinZ || bz.b > CoreMaxZ;
                if (!outerX && !outerZ) continue; // the original grid is built by hand above
                float cx = (bx.a + bx.b) / 2f, cz = (bz.a + bz.b) / 2f, w = bx.b - bx.a, d = bz.b - bz.a;
                // Face the ring road: side bands face in along X, the south/north bands along Z.
                bool alongZ = outerX; // the long side of a side-band block runs along Z
                float yaw = outerX ? (cx < 0f ? 0f : 180f) : (cz < 0f ? -90f : 90f);
                float depth = (alongZ ? w : d) - 2f, length = (alongZ ? d : w) - 2f;
                // Shops keep a 2 m alley behind them (their back doors open onto it), so they sit forward a little.
                float shopDepth = depth - 1f;
                Vector3 front = outerX ? new Vector3(cx < 0f ? 1f : -1f, 0f, 0f) : new Vector3(0f, 0f, cz < 0f ? 1f : -1f);
                if (depth < 5f || length < 5f) continue;
                // Split the block's length into buildings 9-14 m wide with 2 m gaps.
                float start = -length / 2f;
                while (start < length / 2f - 6f)
                {
                    float bw = Mathf.Min(R(9f, 14f), length / 2f - start);
                    if (bw < 6f) break;
                    float along = start + bw / 2f;
                    var pos = alongZ ? new Vector3(cx, Curb, cz + along) : new Vector3(cx + along, Curb, cz);
                    if (k % 3 == 2 || shopDepth < 8.5f)
                    {
                        // Solid building: cover and a landmark, no interior.
                        float hgt = R(7f, 16f);
                        var size = alongZ ? new Vector3(depth, hgt, bw) : new Vector3(bw, hgt, depth);
                        Facade(world, "Outer Building", pos + Vector3.up * hgt / 2f, size, facades[rng.Next(facades.Length)]);
                        Box(world, "Outer Roof", pos + Vector3.up * (hgt + 0.15f), size + new Vector3(0f, 0.3f - hgt, 0f), M("CityRoof"), collider: false);
                    }
                    else
                        BuildShop(names[k % names.Length], pos + front * 0.5f, yaw, shopDepth, bw, 1 + rng.Next(2), k % 2 == 0 ? "store" : "apartment",
                            M("CityBrick"), M(awnings[rng.Next(awnings.Length)]));
                    k++;
                    start += bw + 2f;
                }
            }

        // Parked cars, a wreck and barriers on the ring roads.
        var mats = new[] { "CarCream", "CarGreen", "BusBlue", "TruckRed", "BusWhite" };
        foreach (var (x, z, yaw) in new[] { (-70.5f, -20f, 0f), (-75.5f, 44f, 180f), (70.5f, 30f, 0f), (75.5f, -36f, 180f),
                                            (-50f, -48.5f, 90f), (14f, -53.5f, -90f), (-36f, 72.5f, 90f), (52f, 67.5f, -90f) })
            BuildCar(world, "Parked Car", new Vector3(x, 0.02f, z), yaw + R(-4f, 4f), M(mats[rng.Next(mats.Length)]));
        BuildCar(world, "Wrecked Car", new Vector3(73f, 0.02f, 20f), 40f, M("TruckRed"), true);
        foreach (var (x, z, yaw) in new[] { (-73f, 6f, 5f), (73f, 44f, -8f), (0f, -51f, 85f), (-50f, 70f, 95f), (40f, -51f, 80f) })
            JerseyBarrier(new Vector3(x, 0f, z), yaw);
    }

    // ------------------------------------------------------------------ road details

    static void Crosswalks(float x, float z, float hwAve, float hwSt)
    {
        // Zebra stripes on each leg of the intersection.
        foreach (float side in new[] { -1f, 1f })
        {
            float cx = x + side * (hwAve + 1.8f);
            for (float zz = z - hwSt + 0.8f; zz < z + hwSt - 0.4f; zz += 1.1f)
                Box(world, "Crosswalk", new Vector3(cx, 0.013f, zz), new Vector3(2.8f, 0.01f, 0.55f), M("Trim"), collider: false);
            float cz = z + side * (hwSt + 1.8f);
            for (float xx = x - hwAve + 0.8f; xx < x + hwAve - 0.4f; xx += 1.1f)
                Box(world, "Crosswalk", new Vector3(xx, 0.013f, cz), new Vector3(0.55f, 0.01f, 2.8f), M("Trim"), collider: false);
        }
    }

    static void StreetLight(Vector3 pos, float yaw)
    {
        var t = Building("Street Light", pos, yaw);
        Cylinder(t, "Pole", new Vector3(0f, 3f, 0f), 0.09f, 6f, M("GunDark"));
        Box(t, "Arm", new Vector3(0f, 5.9f, 0.7f), new Vector3(0.08f, 0.08f, 1.5f), M("GunDark"), collider: false);
        Box(t, "Lamp", new Vector3(0f, 5.8f, 1.35f), new Vector3(0.35f, 0.15f, 0.6f), M("Appliance"), collider: false);
    }

    static void TrafficLight(Vector3 pos, float yaw)
    {
        var t = Building("Traffic Light", pos, yaw);
        Cylinder(t, "Pole", new Vector3(0f, 2.6f, 0f), 0.1f, 5.2f, M("GunDark"));
        Box(t, "Mast", new Vector3(0f, 5f, 2.2f), new Vector3(0.1f, 0.1f, 4.4f), M("GunDark"), collider: false);
        Box(t, "Signal", new Vector3(0f, 4.55f, 3.6f), new Vector3(0.35f, 0.95f, 0.3f), M("ConstructionYellow"), collider: false);
        Box(t, "Red", new Vector3(0f, 4.85f, 3.6f), new Vector3(0.37f, 0.2f, 0.2f), M("TruckRed"), collider: false);
        Box(t, "Green", new Vector3(0f, 4.25f, 3.6f), new Vector3(0.37f, 0.2f, 0.2f), M("CarGreen"), collider: false);
    }

    static void JerseyBarrier(Vector3 pos, float yaw)
    {
        var t = Building("Jersey Barrier", pos, yaw);
        Box(t, "Base", new Vector3(0f, 0.25f, 0f), new Vector3(3f, 0.5f, 0.65f), M("CityConcrete"));
        Box(t, "Top", new Vector3(0f, 0.7f, 0f), new Vector3(3f, 0.4f, 0.3f), M("CityConcrete"));
    }

    static void Dumpster(Vector3 pos, float yaw)
    {
        var t = Building("Dumpster", pos, yaw);
        Box(t, "Bin", new Vector3(0f, 0.65f, 0f), new Vector3(1.9f, 1.3f, 1.2f), M("CarGreen"));
        Box(t, "Lid", new Vector3(0f, 1.35f, -0.05f), new Vector3(1.95f, 0.08f, 1.3f), M("GunPolymer"), zRot: 0f, collider: false);
    }

    static void BusStop(Vector3 pos, float yaw)
    {
        var t = Building("Bus Stop", pos, yaw);
        Box(t, "Back Panel", new Vector3(0f, 1.3f, 0.6f), new Vector3(3.2f, 2.2f, 0.06f), M("Window"));
        Box(t, "Roof", new Vector3(0f, 2.45f, 0.2f), new Vector3(3.4f, 0.1f, 1.3f), M("GunDark"));
        Box(t, "Bench", new Vector3(0f, 0.45f, 0.35f), new Vector3(2.6f, 0.08f, 0.4f), M("Metal"));
        foreach (float x in new[] { -1.6f, 1.6f })
            Box(t, "Post", new Vector3(x, 1.2f, 0.6f), new Vector3(0.08f, 2.4f, 0.08f), M("GunDark"), collider: false);
    }

    // ------------------------------------------------------------------ buildings

    /// <summary>A box with metre-scaled UVs (windows keep their size on any building) and a box collider.</summary>
    static GameObject Facade(Transform parent, string name, Vector3 center, Vector3 size, Material mat)
    {
        var go = MeshObject(parent, name, MetricCube(size, new Vector2(3f, 3.5f)), new[] { mat }, center, Quaternion.identity, Vector3.one);
        go.AddComponent<BoxCollider>().size = size;
        return go;
    }

    /// <summary>Cube mesh of the given size whose UVs run in metres / tile, measured from its min corner.</summary>
    static Mesh MetricCube(Vector3 size, Vector2 tile)
    {
        string key = $"City/Cube_{size.x:0.##}x{size.y:0.##}x{size.z:0.##}_{tile.x:0.##}x{tile.y:0.##}";
        return Cached(key, () =>
        {
            var mb = new MeshBuilder(1);
            Vector3 h = size / 2f;
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int axis)
            {
                Vector2 UV(Vector3 p) => axis switch
                {
                    0 => new Vector2((p.z + h.z) / tile.x, (p.y + h.y) / tile.y),
                    1 => new Vector2((p.x + h.x) / tile.x, (p.z + h.z) / tile.y),
                    _ => new Vector2((p.x + h.x) / tile.x, (p.y + h.y) / tile.y),
                };
                int i0 = mb.Add(a, UV(a)), i1 = mb.Add(b, UV(b)), i2 = mb.Add(c, UV(c)), i3 = mb.Add(d, UV(d));
                mb.Tri(0, i0, i1, i2);
                mb.Tri(0, i0, i2, i3);
            }
            float x = h.x, y = h.y, z = h.z;
            Quad(new(x, -y, -z), new(x, y, -z), new(x, y, z), new(x, -y, z), 0);
            Quad(new(-x, -y, z), new(-x, y, z), new(-x, y, -z), new(-x, -y, -z), 0);
            Quad(new(x, -y, z), new(x, y, z), new(-x, y, z), new(-x, -y, z), 2);
            Quad(new(-x, -y, -z), new(-x, y, -z), new(x, y, -z), new(x, -y, -z), 2);
            Quad(new(-x, y, -z), new(-x, y, z), new(x, y, z), new(x, y, -z), 1);
            Quad(new(-x, -y, z), new(-x, -y, -z), new(x, -y, -z), new(x, -y, z), 1);
            return mb.Build(key);
        });
    }

    /// <summary>Evenly spaced window openings along a wall, leaving room for an optional door.</summary>
    static (float a, float b, float bottom, float top)[] Openings(float from, float to, float winW, float spacing, float bottom, float top,
        float doorAt = float.NaN, float doorW = 1.4f, float doorTop = 2.6f)
    {
        var list = new List<(float a, float b, float bottom, float top)>();
        bool door = !float.IsNaN(doorAt);
        if (door) list.Add((doorAt - doorW / 2f, doorAt + doorW / 2f, 0f, doorTop));
        int n = Mathf.FloorToInt((to - from - 0.6f) / spacing);
        float start = (from + to) / 2f - (n - 1) * spacing / 2f;
        for (int i = 0; i < n; i++)
        {
            float c = start + i * spacing;
            if (door && Mathf.Abs(c - doorAt) < (doorW + winW) / 2f + 0.3f) continue;
            list.Add((c - winW / 2f, c + winW / 2f, bottom, top));
        }
        return list.ToArray();
    }

    /// <summary>Horizontal slab with an optional rectangular hole (stair well), as up to four boxes.</summary>
    static void Slab(Transform parent, string name, float topY, float x0, float x1, float z0, float z1, Material mat,
        float hx0 = 0f, float hx1 = 0f, float hz0 = 0f, float hz1 = 0f)
    {
        const float T = 0.2f;
        void Part(float a0, float a1, float b0, float b1)
        {
            if (a1 - a0 < 0.05f || b1 - b0 < 0.05f) return;
            Box(parent, name, new Vector3((a0 + a1) / 2f, topY - T / 2f, (b0 + b1) / 2f), new Vector3(a1 - a0, T, b1 - b0), mat);
        }
        if (hx1 <= hx0) { Part(x0, x1, z0, z1); return; }
        Part(x0, x1, z0, hz0);
        Part(x0, x1, hz1, z1);
        Part(x0, hx0, hz0, hz1);
        Part(hx1, x1, hz0, hz1);
    }

    const float ShopFloor = 3.6f; // storey height of the walk-in buildings

    /// <summary>
    /// Walk-in brick building, front door on local +X, back door on -X. One or two storeys; two-storey ones have a
    /// stacked stair along the -Z wall from the ground floor to the upper floor and on up to the roof.
    /// </summary>
    static Transform BuildShop(string name, Vector3 pos, float yRot, float depth, float width, int floors, string kind, Material walls, Material awning)
    {
        var h = Building(name, pos, yRot);
        float hx = depth / 2f, hz = width / 2f;
        const float T = 0.25f, F = ShopFloor, run = 0.28f;
        const int steps = 15;
        float rise = F / steps, L = steps * run;
        float top = floors * F;
        float sx0 = -hx + T + 1.2f, sz0 = -hz + T, sz1 = -hz + T + 1.3f, szc = (sz0 + sz1) / 2f;
        metricTile = new Vector2(2.5f, 2.5f);
        Box(h, "Floor", new Vector3(0f, 0.05f, 0f), new Vector3(depth, 0.1f, width), M("CityTiles"));
        for (int f = 0; f < floors; f++)
        {
            float b = f * F;
            Wall(h, "Front", WallAxis.AlongZ, hx - T / 2f, -hz, hz, b, F, T, walls,
                f == 0 ? Openings(-hz, hz, 2.2f, 3.2f, 0.9f, 2.7f, doorAt: 0f) : Openings(-hz, hz, 1.4f, 2.6f, 1.0f, 2.6f));
            Wall(h, "Back", WallAxis.AlongZ, -hx + T / 2f, -hz, hz, b, F, T, walls,
                f == 0 ? Openings(-hz + 1.6f, hz, 1.4f, 2.8f, 1.1f, 2.4f, doorAt: hz * 0.45f) : Openings(-hz, hz, 1.4f, 2.8f, 1.1f, 2.4f));
            Wall(h, "Side", WallAxis.AlongX, -hz + T / 2f, -hx + T, hx - T, b, F, T, walls, Openings(-hx + T, hx - T, 1.4f, 3f, 1.1f, 2.4f));
            Wall(h, "Side", WallAxis.AlongX, hz - T / 2f, -hx + T, hx - T, b, F, T, walls, Openings(-hx + T, hx - T, 1.4f, 3f, 1.1f, 2.4f));
            if (f > 0)
            {
                Slab(h, "Upper Floor", b, -hx + T, hx - T, -hz + T, hz - T, M("CityTiles"), sx0, sx0 + L, sz0, sz1);
                Box(h, "Stair Rail", new Vector3(sx0 + L / 2f, b + 0.5f, sz1 + 0.04f), new Vector3(L, 1f, 0.06f), M("Metal"));
            }
        }
        // Roof with parapet (and a stair hole on two-storey buildings).
        if (floors >= 2)
        {
            Slab(h, "Roof", top + 0.2f, -hx, hx, -hz, hz, M("CityRoof"), sx0, sx0 + L, sz0, sz1);
            Box(h, "Roof Rail", new Vector3(sx0 + L / 2f, top + 0.7f, sz1 + 0.04f), new Vector3(L, 1f, 0.06f), M("Metal"));
            Box(h, "Roof Rail", new Vector3(sx0 - 0.04f, top + 0.7f, szc), new Vector3(0.06f, 1f, sz1 - sz0), M("Metal"));
        }
        else
            Slab(h, "Roof", top + 0.2f, -hx, hx, -hz, hz, M("CityRoof"));
        Box(h, "Parapet", new Vector3(0f, top + 0.7f, hz - 0.12f), new Vector3(depth, 1f, 0.24f), walls);
        Box(h, "Parapet", new Vector3(0f, top + 0.7f, -hz + 0.12f), new Vector3(depth, 1f, 0.24f), walls);
        Box(h, "Parapet", new Vector3(hx - 0.12f, top + 0.7f, 0f), new Vector3(0.24f, 1f, width - 0.48f), walls);
        Box(h, "Parapet", new Vector3(-hx + 0.12f, top + 0.7f, 0f), new Vector3(0.24f, 1f, width - 0.48f), walls);
        metricTile = default;
        for (int f = 0; f < floors && floors >= 2; f++)
            Flight(h, new Vector3(sx0, f * F + (f == 0 ? 0.1f : 0f), szc), Vector3.right, steps, f == 0 ? (F - 0.1f) / steps : rise, run, 1.2f);
        if (floors >= 2)
        {
            NavHint(h, new Vector3(sx0 - 0.6f, 0.1f, szc));
            NavHint(h, new Vector3(sx0 + L + 0.7f, F, szc));
            NavHint(h, new Vector3(sx0 - 0.6f, F, szc));
            NavHint(h, new Vector3(sx0 + L + 0.7f, top + 0.2f, szc));
        }

        // Awning and sign over the front door.
        Box(h, "Awning", new Vector3(hx + 0.6f, 3.0f, 0f), new Vector3(1.3f, 0.08f, Mathf.Min(width - 1f, 6f)), awning, zRot: -15f, collider: false);
        Box(h, "Sign", new Vector3(hx + 0.08f, 3.35f, 0f), new Vector3(0.1f, 0.5f, Mathf.Min(width - 2f, 4.5f)), M("GunDark"), collider: false);

        // Furniture by kind.
        switch (kind)
        {
            case "store":
                Box(h, "Counter", new Vector3(hx - 2.2f, 0.55f, -hz + 3.2f), new Vector3(0.8f, 0.9f, 2.4f), M("Furniture"));
                foreach (float z in new[] { 1.2f, hz - 1.0f })
                    Box(h, "Shelf", new Vector3(0.4f, 0.9f, z), new Vector3(Mathf.Min(5f, depth - 6f), 1.6f, 0.6f), M("Metal"));
                break;
            case "diner":
                Box(h, "Counter", new Vector3(-hx + 2.6f, 0.6f, hz * 0.3f), new Vector3(0.8f, 1f, hz * 1.1f), M("Appliance"));
                for (float z = -hz + 1.6f; z < hz - 1f; z += 2.4f)
                {
                    if ((floors >= 2 && z < sz1 + 0.6f) || Mathf.Abs(z) < 1.8f) continue; // keep the stairs and front door clear
                    Box(h, "Booth Table", new Vector3(hx - 1.3f, 0.7f, z), new Vector3(1f, 0.08f, 0.8f), M("Furniture"));
                    Box(h, "Booth Seat", new Vector3(hx - 1.3f, 0.45f, z - 0.75f), new Vector3(1.4f, 0.5f, 0.45f), M("TruckRed"));
                    Box(h, "Booth Seat", new Vector3(hx - 1.3f, 0.45f, z + 0.75f), new Vector3(1.4f, 0.5f, 0.45f), M("TruckRed"));
                }
                break;
            default: // apartment
                Box(h, "Couch", new Vector3(1.5f, 0.45f, hz - 1f), new Vector3(2.4f, 0.7f, 0.9f), M("Fabric"));
                Box(h, "Table", new Vector3(1.5f, 0.42f, hz - 2.6f), new Vector3(1.2f, 0.5f, 0.6f), M("Furniture"));
                Box(h, "Cabinet", new Vector3(hx - 0.6f, 0.9f, -hz + 3f), new Vector3(0.6f, 1.6f, 1.6f), M("Furniture"));
                break;
        }
        if (floors >= 2)
        {
            Box(h, "Desk", new Vector3(hx - 2f, F + 0.4f, hz - 1.5f), new Vector3(1.4f, 0.75f, 0.8f), M("Furniture"));
            Box(h, "Desk", new Vector3(hx - 2f, F + 0.4f, -hz + 3f), new Vector3(1.4f, 0.75f, 0.8f), M("Furniture"));
            Box(h, "Bed", new Vector3(-hx + 1.4f, F + 0.3f, hz - 1.4f), new Vector3(2f, 0.6f, 1.4f), M("Fabric"));
        }

        Passage(h, name + " front door", new Vector3(hx - T / 2f, 0.1f, 0f), Vector3.right);
        Passage(h, name + " back door", new Vector3(-hx + T / 2f, 0.1f, hz * 0.45f), Vector3.right);
        NavHint(h, new Vector3(hx + 1f, 0f, 0f));
        NavHint(h, new Vector3(hx - 1.2f, 0.1f, 0f));
        NavHint(h, new Vector3(-hx + 1.2f, 0.1f, hz * 0.45f));
        NavHint(h, new Vector3(-hx - 1f, 0f, hz * 0.45f));
        return h;
    }

    /// <summary>Tower block; with `lobby`, the ground floor is a walk-in lobby with doors on all four sides.</summary>
    static Transform BuildTower(string name, Vector3 pos, float yRot, float sx, float sz, float height, Material facade, bool lobby)
    {
        var t = Building(name, pos, yRot);
        const float LobbyH = 4.6f, T = 0.3f;
        float baseY = lobby ? LobbyH : 0f;
        float hx = sx / 2f, hz = sz / 2f;
        if (lobby)
        {
            metricTile = new Vector2(2.5f, 2.5f);
            Box(t, "Lobby Floor", new Vector3(0f, 0.05f, 0f), new Vector3(sx, 0.1f, sz), M("CityTiles"));
            Wall(t, "Lobby Front", WallAxis.AlongZ, hx - T / 2f, -hz, hz, 0f, LobbyH, T, M("CityConcrete"), Openings(-hz, hz, 2.4f, 3.6f, 0.8f, 3.6f, doorAt: 0f, doorW: 2.6f, doorTop: 3.2f));
            Wall(t, "Lobby Back", WallAxis.AlongZ, -hx + T / 2f, -hz, hz, 0f, LobbyH, T, M("CityConcrete"), Openings(-hz, hz, 2.4f, 3.6f, 0.8f, 3.6f, doorAt: 0f, doorW: 2.6f, doorTop: 3.2f));
            Wall(t, "Lobby Side", WallAxis.AlongX, -hz + T / 2f, -hx + T, hx - T, 0f, LobbyH, T, M("CityConcrete"), Openings(-hx + T, hx - T, 2.4f, 3.6f, 0.8f, 3.6f, doorAt: 0f, doorW: 2f, doorTop: 3.2f));
            Wall(t, "Lobby Side", WallAxis.AlongX, hz - T / 2f, -hx + T, hx - T, 0f, LobbyH, T, M("CityConcrete"), Openings(-hx + T, hx - T, 2.4f, 3.6f, 0.8f, 3.6f, doorAt: 0f, doorW: 2f, doorTop: 3.2f));
            metricTile = default;
            foreach (float x in new[] { -hx * 0.5f, hx * 0.5f })
                foreach (float z in new[] { -hz * 0.5f, hz * 0.5f })
                    Box(t, "Column", new Vector3(x, LobbyH / 2f, z), new Vector3(0.7f, LobbyH, 0.7f), M("CityConcrete"));
            float coreX = -hx + T + 3f; // keep 2 m clear inside the back door, new Vector3(1.6f, LobbyH, Mathf.Min(5f, sz - 6f)), M("CityConcrete"));
            Box(t, "Elevator Doors", new Vector3(coreX + 0.82f, 1.2f, 0f), new Vector3(0.05f, 2.3f, 1.6f), M("Chrome"), collider: false);
            Box(t, "Reception Desk", new Vector3(hx * 0.5f - 1.8f, 0.6f, 0f), new Vector3(0.9f, 1.1f, 3.6f), M("Furniture"));
            foreach (float z in new[] { -hz + 1.2f, hz - 1.2f })
            {
                Cylinder(t, "Planter", new Vector3(hx - 1.4f, 0.4f, z), 0.45f, 0.8f, M("CityConcrete"));
                var plant = Sphere(t, "Plant", new Vector3(hx - 1.4f, 1.3f, z), 1.1f, M("Hedge"));
                Object.DestroyImmediate(plant.GetComponent<Collider>());
            }
            Box(t, "Bench", new Vector3(0f, 0.3f, -hz * 0.5f - 1f), new Vector3(3f, 0.45f, 0.6f), M("Furniture"));
            Box(t, "Bench", new Vector3(0f, 0.3f, hz * 0.5f + 1f), new Vector3(3f, 0.45f, 0.6f), M("Furniture"));
            Passage(t, name + " lobby front", new Vector3(hx - T / 2f, 0.1f, 0f), Vector3.right);
            Passage(t, name + " lobby back", new Vector3(-hx + T / 2f, 0.1f, 0f), Vector3.right);
            Passage(t, name + " lobby side S", new Vector3(0f, 0.1f, -hz + T / 2f), Vector3.forward);
            Passage(t, name + " lobby side N", new Vector3(0f, 0.1f, hz - T / 2f), Vector3.forward);
            foreach (var p in new[] { new Vector3(hx + 1f, 0f, 0f), new Vector3(-hx - 1f, 0f, 0f), new Vector3(0f, 0f, -hz - 1f), new Vector3(0f, 0f, hz + 1f),
                                      new Vector3(hx - 1.5f, 0.1f, 0f), new Vector3(0f, 0.1f, -hz + 1.5f), new Vector3(0f, 0.1f, hz - 1.5f), new Vector3(-hx + 1.2f, 0.1f, 0f) })
                NavHint(t, p);
        }
        Facade(t, "Facade", new Vector3(0f, baseY + (height - baseY) / 2f, 0f), new Vector3(sx, height - baseY, sz), facade);
        metricTile = new Vector2(4f, 4f);
        Box(t, "Roof", new Vector3(0f, height + 0.1f, 0f), new Vector3(sx + 0.2f, 0.2f, sz + 0.2f), M("CityRoof"));
        Box(t, "Parapet", new Vector3(0f, height + 0.6f, hz), new Vector3(sx + 0.2f, 1f, 0.3f), M("CityConcrete"));
        Box(t, "Parapet", new Vector3(0f, height + 0.6f, -hz), new Vector3(sx + 0.2f, 1f, 0.3f), M("CityConcrete"));
        Box(t, "Parapet", new Vector3(hx, height + 0.6f, 0f), new Vector3(0.3f, 1f, sz), M("CityConcrete"));
        Box(t, "Parapet", new Vector3(-hx, height + 0.6f, 0f), new Vector3(0.3f, 1f, sz), M("CityConcrete"));
        metricTile = default;
        Box(t, "AC Unit", new Vector3(hx * 0.4f, height + 0.8f, hz * 0.3f), new Vector3(2.2f, 1.4f, 1.6f), M("Metal"));
        Box(t, "Stair Hut", new Vector3(-hx * 0.4f, height + 1.3f, -hz * 0.3f), new Vector3(2.6f, 2.4f, 2.6f), M("CityConcrete"));
        if (sx > 10f) Cylinder(t, "Water Tank", new Vector3(-hx * 0.4f, height + 3.8f, hz * 0.35f), 1.3f, 2.6f, M("Fence"));
        return t;
    }

    /// <summary>Brick warehouse with big doors at both ends, crates, racks and a mezzanine reached by stairs.</summary>
    static Transform BuildWarehouse(Vector3 pos, float yRot)
    {
        var h = Building("Warehouse", pos, yRot);
        const float depth = 20f, width = 15f, H = 7f, T = 0.3f;
        float hx = depth / 2f, hz = width / 2f;
        metricTile = new Vector2(2.5f, 2.5f);
        Box(h, "Floor", new Vector3(0f, 0.05f, 0f), new Vector3(depth, 0.1f, width), M("CityConcrete"));
        Wall(h, "Front", WallAxis.AlongZ, hx - T / 2f, -hz, hz, 0f, H, T, M("CityBrick"), (-2.5f, 2.5f, 0f, 4.4f), (4f, 5.2f, 0f, 2.5f), (-6f, -4f, 4.6f, 6f));
        Wall(h, "Back", WallAxis.AlongZ, -hx + T / 2f, -hz, hz, 0f, H, T, M("CityBrick"), (-1.8f, 1.8f, 0f, 4f), (3.5f, 6f, 4.6f, 6f));
        Wall(h, "Side", WallAxis.AlongX, -hz + T / 2f, -hx + T, hx - T, 0f, H, T, M("CityBrick"), Openings(-hx + T, hx - T, 2f, 4f, 4.6f, 6f));
        Wall(h, "Side", WallAxis.AlongX, hz - T / 2f, -hx + T, hx - T, 0f, H, T, M("CityBrick"), Openings(-hx + T, hx - T, 2f, 4f, 4.6f, 6f, doorAt: 3f, doorW: 1.4f));
        Slab(h, "Roof", H + 0.2f, -hx, hx, -hz, hz, M("CityRoof"));
        // Mezzanine along the back wall.
        const float mz = 3.4f, mDepth = 3.4f;
        float mEdge = -hx + T + mDepth;
        Box(h, "Mezzanine", new Vector3((-hx + T + mEdge) / 2f, mz - 0.1f, 0f), new Vector3(mDepth, 0.2f, width - 2f * T), M("Floorboards"));
        metricTile = default;
        float sz0 = -hz + T + 0.05f, szc = sz0 + 0.6f;
        Flight(h, new Vector3(mEdge + 15 * 0.28f, 0.1f, szc), Vector3.left, 15, (mz - 0.1f) / 15f, 0.28f, 1.2f);
        Box(h, "Mezzanine Rail", new Vector3(mEdge + 0.03f, mz + 0.5f, (sz0 + 1.3f + hz - T) / 2f), new Vector3(0.06f, 1f, hz - T - sz0 - 1.3f), M("Metal"));
        foreach (float z in new[] { -hz + 3.5f, 0f, hz - 2f })
            Box(h, "Mezzanine Post", new Vector3(mEdge - 0.1f, mz / 2f, z), new Vector3(0.2f, mz - 0.2f, 0.2f), M("Metal"));
        // Racks and crates.
        foreach (float z in new[] { -3.5f, 2.5f })
            Box(h, "Rack", new Vector3(1.5f, 1.6f, z), new Vector3(7f, 3.2f, 1.1f), M("Metal"));
        foreach (var (x, z, s) in new[] { (6f, -5.5f, 1.2f), (7f, -4.2f, 1f), (6.2f, 5.6f, 1.3f), (-2f, 6f, 1.1f), (3f, 6.2f, 1.2f), (6.5f, 5.6f, 1f) })
            Box(h, "Crate", new Vector3(x, s / 2f + 0.1f, z), Vector3.one * s, M("Fence"));
        Box(h, "Forklift", new Vector3(4.5f, 0.9f, -0.5f), new Vector3(2.2f, 1.6f, 1.2f), M("ConstructionYellow"));
        Passage(h, "Warehouse front doors", new Vector3(hx - T / 2f, 0.1f, 0f), Vector3.right);
        Passage(h, "Warehouse back doors", new Vector3(-hx + T / 2f, 0.1f, 0f), Vector3.right);
        foreach (var p in new[] { new Vector3(hx + 1f, 0f, 0f), new Vector3(hx - 1.5f, 0.1f, 0f), new Vector3(-hx + 1.5f + mDepth, 0.1f, 0f), new Vector3(-hx - 1f, 0f, 0f),
                                  new Vector3(mEdge + 4.8f, 0.1f, szc), new Vector3(mEdge - 0.8f, mz, szc), new Vector3(mEdge - 1.5f, mz, 2f) })
            NavHint(h, p);
        return h;
    }

    /// <summary>Two-level open parking garage (ground, deck at 3.5 m, roof deck at 7 m) with ramps along both long sides.</summary>
    static Transform BuildGarage(Vector3 pos)
    {
        var g = Building("Parking Garage", pos, 0f);
        const float hw = 11f, hd = 8f, d1 = 3.5f, d2 = 7f, rampHalf = 7f, stripIn = 4.5f;
        metricTile = new Vector2(3f, 3f);
        Box(g, "Garage Floor", new Vector3(0f, 0.05f, 0f), new Vector3(hw * 2f, 0.1f, hd * 2f), M("CityConcrete"));
        // Deck 1: everything except the hole over ramp 1 (south strip, x -7..7).
        Slab(g, "Deck", d1, -hw, hw, -hd, hd, M("CityConcrete"), -rampHalf, rampHalf, -hd - 0.01f, -stripIn);
        // Roof: everything except the hole over ramp 2 (north strip, x -7..7).
        Slab(g, "Roof Deck", d2, -hw, hw, -hd, hd, M("CityConcrete"), -rampHalf, rampHalf, stripIn, hd + 0.01f);
        metricTile = default;
        float run = rampHalf * 2f, len = Mathf.Sqrt(run * run + d1 * d1), ang = Mathf.Atan2(d1, run) * Mathf.Rad2Deg;
        var r1 = Box(g, "Ramp", new Vector3(0f, d1 / 2f - 0.12f, (-hd - stripIn) / 2f), new Vector3(len, 0.25f, hd - stripIn), M("CityConcrete"));
        r1.transform.localRotation = Quaternion.Euler(0f, 0f, ang);       // climbs toward +X
        var r2 = Box(g, "Ramp", new Vector3(0f, d1 + d1 / 2f - 0.12f, (hd + stripIn) / 2f), new Vector3(len, 0.25f, hd - stripIn), M("CityConcrete"));
        r2.transform.localRotation = Quaternion.Euler(0f, 0f, -ang);      // climbs toward -X
        // Rails beside the ramp holes, parapets around the decks (gaps where the ramps land).
        Box(g, "Rail", new Vector3(0f, d1 + 0.5f, -stripIn + 0.05f), new Vector3(rampHalf * 2f, 1f, 0.1f), M("Metal"));
        Box(g, "Rail", new Vector3(0f, d2 + 0.5f, stripIn - 0.05f), new Vector3(rampHalf * 2f, 1f, 0.1f), M("Metal"));
        foreach (float y in new[] { d1, d2 })
        {
            Box(g, "Parapet", new Vector3(0f, y + 0.5f, -hd + 0.1f), new Vector3(hw * 2f, 1f, 0.2f), M("CityConcrete"));
            Box(g, "Parapet", new Vector3(0f, y + 0.5f, hd - 0.1f), new Vector3(hw * 2f, 1f, 0.2f), M("CityConcrete"));
            Box(g, "Parapet", new Vector3(-hw + 0.1f, y + 0.5f, 0f), new Vector3(0.2f, 1f, stripIn * 2f - 0.2f), M("CityConcrete"));
            Box(g, "Parapet", new Vector3(hw - 0.1f, y + 0.5f, 0f), new Vector3(0.2f, 1f, stripIn * 2f - 0.2f), M("CityConcrete"));
        }
        foreach (var (x, z) in new[] { (-10.6f, 0f), (10.6f, 0f), (0f, 0f), (-5.3f, 0f), (5.3f, 0f), (-10.6f, -7.6f), (10.6f, -7.6f), (-10.6f, 7.6f), (10.6f, 7.6f) })
            Box(g, "Column", new Vector3(x, d2 / 2f, z), new Vector3(0.5f, d2, 0.5f), M("CityConcrete"));
        foreach (var (x, y, z, yaw, mat) in new[] { (-7.5f, 0.12f, 2f, 0f, "CarGreen"), (2.5f, 0.12f, 2.2f, 0f, "TruckRed"), (-2.5f, d1 + 0.02f, -1.8f, 180f, "CarCream"),
                                                    (7.5f, d1 + 0.02f, 1.8f, 0f, "BusBlue"), (4f, d2 + 0.02f, -2f, 180f, "CarGreen") })
            BuildCar(g, "Parked Car", new Vector3(x, y, z), yaw, M(mat));
        foreach (var p in new[] { new Vector3(-9f, 0.1f, -6f), new Vector3(-rampHalf + 1f, 0.35f, -6.2f), new Vector3(0f, d1 / 2f, -6.2f), new Vector3(rampHalf - 1f, d1 - 0.3f, -6.2f),
                                  new Vector3(9f, d1, -6f), new Vector3(9f, d1, 6f), new Vector3(rampHalf - 1f, d1 + 0.35f, 6.2f), new Vector3(0f, d1 * 1.5f, 6.2f),
                                  new Vector3(-rampHalf + 1f, d2 - 0.3f, 6.2f), new Vector3(-9f, d2, 6f), new Vector3(0f, d2, -2f), new Vector3(0f, d1, 0f) })
            NavHint(g, p);
        return g;
    }

    /// <summary>Steel-frame building site: two partial floors reached by scaffold stairs, a tower crane and materials.</summary>
    static Transform BuildConstruction(Vector3 pos)
    {
        var s = Building("Construction Site", pos, 0f);
        Box(s, "Site Dirt", new Vector3(0f, 0.02f, 0f), new Vector3(22f, 0.04f, 16f), M("Dirt"), collider: false);
        // Hoarding along the street sides, with gaps.
        Wall(s, "Hoarding", WallAxis.AlongZ, 11f, -8f, 8f, 0f, 2.4f, 0.1f, M("Plywood"), (-2f, 2f, 0f, 2.4f));
        Wall(s, "Hoarding", WallAxis.AlongX, 8f, -11f, 11f, 0f, 2.4f, 0.1f, M("Plywood"), (-8f, -5f, 0f, 2.4f), (3f, 6f, 0f, 2.4f));
        Wall(s, "Hoarding", WallAxis.AlongX, -8f, -11f, 11f, 0f, 2.4f, 0.1f, M("Plywood"), (2f, 5f, 0f, 2.4f));
        // Frame: columns on a grid, perimeter beams at each level.
        for (float x = -7f; x <= 7.01f; x += 3.5f)
            for (float z = -5f; z <= 5.01f; z += 5f)
                Box(s, "Column", new Vector3(x, 4.5f, z), new Vector3(0.3f, 9f, 0.3f), M("Metal"));
        foreach (float y in new[] { 3.5f, 7f, 9f })
        {
            Box(s, "Beam", new Vector3(0f, y - 0.15f, -5f), new Vector3(14.3f, 0.3f, 0.25f), M("TruckRed"));
            Box(s, "Beam", new Vector3(0f, y - 0.15f, 5f), new Vector3(14.3f, 0.3f, 0.25f), M("TruckRed"));
            Box(s, "Beam", new Vector3(-7f, y - 0.15f, 0f), new Vector3(0.25f, 0.3f, 10.3f), M("TruckRed"));
            Box(s, "Beam", new Vector3(7f, y - 0.15f, 0f), new Vector3(0.25f, 0.3f, 10.3f), M("TruckRed"));
        }
        metricTile = new Vector2(3f, 3f);
        Slab(s, "Level 1", 3.5f, -7f, 7f, -5f, 2f, M("CityConcrete"));
        Slab(s, "Level 2", 7f, -1f, 7f, -5f, 1f, M("CityConcrete"));
        metricTile = default;
        // Scaffold stair outside the -Z side: two stacked flights with landings.
        const float x0 = -3f, zc = -5.8f;
        Flight(s, new Vector3(x0, 0f, zc), Vector3.right, 14, 0.25f, 0.3f, 1.2f);
        Flight(s, new Vector3(x0, 3.5f, zc), Vector3.right, 14, 0.25f, 0.3f, 1.2f);
        Box(s, "Landing", new Vector3(x0 - 0.6f, 3.4f, -5.7f), new Vector3(1.2f, 0.2f, 1.4f), M("Floorboards"));
        Box(s, "Landing", new Vector3(1.8f, 3.4f, -5.7f), new Vector3(1.2f, 0.2f, 1.4f), M("Floorboards"));
        Box(s, "Landing", new Vector3(1.8f, 6.9f, -5.7f), new Vector3(1.2f, 0.2f, 1.4f), M("Floorboards"));
        foreach (float x in new[] { x0 - 1.2f, x0, 1.2f, 2.4f })
            Box(s, "Scaffold Pole", new Vector3(x, 3.5f, -6.45f), new Vector3(0.08f, 7f, 0.08f), M("Metal"), collider: false);
        foreach (var p in new[] { new Vector3(x0 - 0.6f, 0f, zc), new Vector3(1.8f, 3.5f, zc), new Vector3(x0 - 0.6f, 3.5f, zc), new Vector3(1.8f, 7f, zc),
                                  new Vector3(-4f, 3.5f, 0f), new Vector3(3f, 7f, -2f) })
            NavHint(s, p);
        // Tower crane in the corner.
        var c = new Vector3(9f, 0f, 5.5f);
        foreach (float dx in new[] { -0.6f, 0.6f })
            foreach (float dz in new[] { -0.6f, 0.6f })
                Box(s, "Crane Leg", c + new Vector3(dx, 14f, dz), new Vector3(0.15f, 28f, 0.15f), M("ConstructionYellow"));
        for (float y = 2f; y < 28f; y += 2.5f)
            Box(s, "Crane Brace", c + new Vector3(0f, y, -0.6f), new Vector3(1.3f, 0.1f, 0.1f), M("ConstructionYellow"), collider: false);
        Box(s, "Crane Jib", c + new Vector3(-11f, 28.5f, 0f), new Vector3(24f, 1f, 1f), M("ConstructionYellow"));
        Box(s, "Crane Counter-jib", c + new Vector3(4f, 28.5f, 0f), new Vector3(6f, 0.8f, 1f), M("ConstructionYellow"));
        Box(s, "Counterweight", c + new Vector3(6.2f, 27.6f, 0f), new Vector3(1.6f, 1.6f, 1.6f), M("CityConcrete"));
        Box(s, "Crane Cab", c + new Vector3(0f, 27.4f, 0.9f), new Vector3(1.4f, 1.4f, 1.2f), M("Window"));
        Box(s, "Cable", c + new Vector3(-16f, 22f, 0f), new Vector3(0.05f, 12f, 0.05f), M("GunDark"), collider: false);
        Box(s, "Hanging Beam", c + new Vector3(-16f, 15.8f, 0f), new Vector3(0.4f, 0.4f, 6f), M("TruckRed"));
        // Materials lying around (cover).
        for (int i = 0; i < 4; i++) Box(s, "Steel Beam", new Vector3(-8.5f, 0.15f + i * 0.3f, -2f + (i % 2) * 0.1f), new Vector3(0.3f, 0.3f, 7f), M("TruckRed"));
        Box(s, "Pallet Stack", new Vector3(3f, 0.6f, 6.4f), new Vector3(1.4f, 1.2f, 1.4f), M("Fence"));
        Box(s, "Pallet Stack", new Vector3(4.6f, 0.4f, 6.4f), new Vector3(1.4f, 0.8f, 1.4f), M("Fence"));
        var mixer = Cylinder(s, "Cement Mixer", new Vector3(-9f, 1f, 5.5f), 0.8f, 1.6f, M("TentOrange"));
        mixer.transform.localRotation = Quaternion.Euler(60f, 0f, 0f);
        foreach (float x in new[] { 9.5f, 8.2f })
            Box(s, "Porta-potty", new Vector3(x, 1.2f, -6.6f), new Vector3(1.2f, 2.4f, 1.2f), M("TentBlue"));
        return s;
    }

    static Transform BuildGasStation(Vector3 pos)
    {
        var g = Building("Gas Station", pos, 0f);
        metricTile = new Vector2(3f, 3f);
        Box(g, "Forecourt", new Vector3(3f, 0.02f, 0f), new Vector3(14f, 0.04f, 13f), M("CityConcrete"), collider: false);
        metricTile = default;
        foreach (float x in new[] { -1.5f, 7.5f })
            foreach (float z in new[] { -3.5f, 3.5f })
                Box(g, "Canopy Column", new Vector3(x, 2.3f, z), new Vector3(0.4f, 4.6f, 0.4f), M("Trim"));
        Box(g, "Canopy", new Vector3(3f, 4.8f, 0f), new Vector3(12f, 0.5f, 10f), M("Trim"));
        Box(g, "Canopy Band", new Vector3(3f, 4.8f, 0f), new Vector3(12.05f, 0.3f, 10.05f), M("TruckRed"), collider: false);
        foreach (float z in new[] { -3.5f, 0f, 3.5f })
        {
            Box(g, "Pump Island", new Vector3(3f, 0.1f, z), new Vector3(4f, 0.2f, 0.9f), M("CityConcrete"));
            Box(g, "Pump", new Vector3(3f, 0.95f, z), new Vector3(0.7f, 1.5f, 0.6f), M("BusWhite"));
        }
        BuildCar(g, "Car at Pump", new Vector3(3f, 0.02f, -1.75f), 90f, M("CarGreen"));
        var mart = BuildShop("Mini-mart", g.TransformPoint(new Vector3(-5.5f, 0f, 0f)), 0f, 7f, 12f, 1, "store", M("CityBrick"), M("TruckRed"));
        mart.SetParent(g, true);
        return g;
    }

    static Transform BuildPlaza(Vector3 pos)
    {
        var p = Building("Plaza", pos, 0f);
        // Fountain with the golden Deagle on the centre pedestal.
        Cylinder(p, "Fountain Basin", new Vector3(0f, 0.3f, 0f), 4f, 0.6f, M("CityConcrete"));
        var water = Cylinder(p, "Fountain Water", new Vector3(0f, 0.58f, 0f), 3.7f, 0.05f, M("Water"));
        Object.DestroyImmediate(water.GetComponent<Collider>());
        Cylinder(p, "Pedestal", new Vector3(0f, 0.8f, 0f), 0.6f, 1.6f, M("CityConcrete"));
        Cylinder(p, "Pedestal Top", new Vector3(0f, 1.62f, 0f), 0.9f, 0.08f, M("CityConcrete"));
        // Planters with trees, benches, a kiosk, a food truck and an abstract sculpture.
        foreach (var (x, z) in new[] { (-8f, -6f), (8f, -6f), (-8f, 6f), (8f, 6f) })
        {
            Box(p, "Planter", new Vector3(x, 0.35f, z), new Vector3(3f, 0.7f, 3f), M("CityConcrete"));
            Cylinder(p, "Tree Trunk", new Vector3(x, 2f, z), 0.22f, 2.6f, M("Trunk"));
            Sphere(p, "Tree Crown", new Vector3(x, 4f, z), 3.2f, M("Foliage"));
        }
        foreach (var (x, z, yaw) in new[] { (-4.5f, -6.5f, 0f), (4.5f, -6.5f, 0f), (-4.5f, 6.5f, 180f), (4.5f, 6.5f, 180f), (-9.5f, 0f, 90f) })
        {
            var b = Building("Bench", p.TransformPoint(new Vector3(x, 0f, z)), yaw);
            b.SetParent(p, true);
            Box(b, "Seat", new Vector3(0f, 0.45f, 0f), new Vector3(2f, 0.08f, 0.5f), M("Furniture"));
            Box(b, "Back", new Vector3(0f, 0.75f, 0.25f), new Vector3(2f, 0.5f, 0.06f), M("Furniture"));
        }
        Box(p, "Kiosk", new Vector3(-6f, 1.2f, 0f), new Vector3(2f, 2.4f, 2.4f), M("CarGreen"));
        Box(p, "Kiosk Roof", new Vector3(-6f, 2.5f, 0f), new Vector3(2.6f, 0.15f, 3f), M("TruckRed"), collider: false);
        Box(p, "Food Truck", new Vector3(8.5f, 1.4f, 0f), new Vector3(2.4f, 2.5f, 5.5f), M("ConstructionYellow"));
        Box(p, "Food Truck Window", new Vector3(7.28f, 1.7f, 0f), new Vector3(0.05f, 0.9f, 2.6f), M("Window"), collider: false);
        foreach (float z in new[] { -1.8f, 1.8f }) Wheel(p, new Vector3(8.5f, 0.4f, z), 0.4f, 2.5f, alongX: true);
        Box(p, "Sculpture Base", new Vector3(0f, 0.5f, 9f), new Vector3(2f, 1f, 2f), M("CityConcrete"));
        var s1 = Box(p, "Sculpture", new Vector3(0f, 2f, 9f), new Vector3(1.2f, 2f, 1.2f), M("Chrome"));
        s1.transform.localRotation = Quaternion.Euler(30f, 45f, 20f);
        var s2 = Box(p, "Sculpture", new Vector3(0f, 3.2f, 9f), new Vector3(0.9f, 0.9f, 0.9f), M("Gold"));
        s2.transform.localRotation = Quaternion.Euler(45f, 10f, 45f);
        NavHint(p, new Vector3(0f, 0f, -5f));
        NavHint(p, new Vector3(0f, 0f, 5f));
        return p;
    }

    static void BuildParkingLot(Vector3 pos)
    {
        var l = Building("Parking Lot", pos, 0f);
        Box(l, "Lot", new Vector3(0f, 0.01f, 0f), new Vector3(20f, 0.03f, 15f), M("Asphalt"), collider: false);
        for (float x = -8f; x <= 8.1f; x += 2.8f)
            foreach (float z in new[] { -4f, 4f })
                Box(l, "Bay Line", new Vector3(x, 0.03f, z), new Vector3(0.1f, 0.01f, 4.6f), M("Trim"), collider: false);
        foreach (var (x, z, yaw, mat) in new[] { (-6.6f, -4f, 0f, "CarCream"), (-1f, -4f, 180f, "BusBlue"), (4.6f, -4f, 0f, "CarGreen"), (-3.8f, 4f, 180f, "TruckRed"), (7.4f, 4f, 0f, "CarCream") })
            BuildCar(l, "Parked Car", new Vector3(x, 0.02f, z), yaw, M(mat));
        Box(l, "Booth", new Vector3(-9f, 1.2f, 6.5f), new Vector3(1.6f, 2.4f, 1.6f), M("BusWhite"));
        Box(l, "Gate Arm", new Vector3(-7f, 1f, 6.5f), new Vector3(3f, 0.1f, 0.1f), M("TruckRed"), collider: false);
    }
}
