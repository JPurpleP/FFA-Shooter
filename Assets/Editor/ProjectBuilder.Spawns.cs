using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static partial class ProjectBuilder
{
    // ================================================================== random weapon spawns

    /// <summary>
    /// Spread `count` random-weapon spawners evenly over the current map (`world`): sample the walkable ground
    /// level on a 1.5 m grid (street, floors of buildings you can walk into, not roofs or tops of things), keep
    /// spots with room to stand, then pick them farthest-point first so they cover the whole map.
    /// </summary>
    static void PlaceWeaponSpawners(string map, int count, System.Func<Vector3, bool> reject = null)
        => PlaceSpawners(map, count, reject, WeaponId.None, "Weapon Spawns", 30f);

    /// <summary>Grappling hook pickups on their own spots, filling the gaps between the weapon spawns.</summary>
    static void PlaceHookSpawners(string map, int count, System.Func<Vector3, bool> reject = null)
        => PlaceSpawners(map, count, reject, WeaponId.GrappleHook, "Grapple Hook Spawns", 45f);

    /// <summary>Grenade pickups (a random throwable each time) on their own spots, between the weapon and hook spawns.</summary>
    static void PlaceGrenadeSpawners(string map, int count, System.Func<Vector3, bool> reject = null)
        => PlaceSpawners(map, count, reject, WeaponId.FragGrenade, "Grenade Spawns", 40f);

    static void PlaceSpawners(string map, int count, System.Func<Vector3, bool> reject, WeaponId fixedItem, string groupName, float delay)
    {
        Physics.SyncTransforms();
        var hits = new RaycastHit[32];
        var spawnPoints = world.GetComponentsInChildren<SpawnPoint>(true).Select(s => s.transform.position).ToList();
        var existing = world.GetComponentsInChildren<WeaponSpawner>(true).Select(s => s.transform.position).ToList();
        var candidates = new List<Vector3>();
        for (float x = MapMinX + 3f; x <= MapMaxX - 3f; x += 1.5f)
            for (float z = MapMinZ + 3f; z <= MapMaxZ - 3f; z += 1.5f)
            {
                int n = Physics.RaycastNonAlloc(new Vector3(x, 150f, z), Vector3.down, hits, 250f, 1, QueryTriggerInteraction.Ignore);
                int lowest = -1;
                for (int i = 0; i < n; i++)
                    if (lowest < 0 || hits[i].point.y < hits[lowest].point.y) lowest = i;
                if (lowest < 0 || hits[lowest].normal.y < 0.85f) continue;
                Vector3 p = hits[lowest].point;
                // Room to stand (and nothing overhead within reach), away from player spawns.
                if (Physics.CheckCapsule(p + Vector3.up * 0.55f, p + Vector3.up * 1.8f, 0.45f, 1, QueryTriggerInteraction.Ignore)) continue;
                if (spawnPoints.Any(s => (s - p).sqrMagnitude < 2.5f * 2.5f)) continue;
                if (reject != null && reject(p)) continue;
                candidates.Add(p);
            }

        // Farthest-point sampling (on the ground plane) from the middle of the map outward.
        var chosen = new List<Vector3>();
        var minDist = new float[candidates.Count];
        for (int i = 0; i < minDist.Length; i++)
        {
            minDist[i] = float.MaxValue;
            foreach (var e in existing) minDist[i] = Mathf.Min(minDist[i], Flat(candidates[i] - e)); // keep clear of other spawners
        }
        var center = new Vector3((MapMinX + MapMaxX) / 2f, 0f, (MapMinZ + MapMaxZ) / 2f);
        int next = -1;
        float bestCenter = float.MaxValue, bestGap = 0f;
        for (int i = 0; i < candidates.Count; i++)
        {
            if (existing.Count > 0)
            {
                if (minDist[i] > bestGap) { bestGap = minDist[i]; next = i; } // start in the biggest gap
                continue;
            }
            float d = Flat(candidates[i] - center);
            if (d < bestCenter) { bestCenter = d; next = i; }
        }
        while (next >= 0 && chosen.Count < count)
        {
            var c = candidates[next];
            chosen.Add(c);
            next = -1;
            float far = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                minDist[i] = Mathf.Min(minDist[i], Flat(candidates[i] - c));
                if (minDist[i] > far) { far = minDist[i]; next = i; }
            }
        }

        var parent = new GameObject(groupName).transform;
        parent.SetParent(world, false);
        // Gun spawners wander: every respawn picks a new spot from (a thinned-out set of) the walkable candidates.
        PickupSpotPool pool = null;
        if (fixedItem == WeaponId.None && candidates.Count > 0)
        {
            pool = parent.gameObject.AddComponent<PickupSpotPool>();
            int step = Mathf.Max(1, candidates.Count / 900);
            pool.spots = candidates.Where((_, i) => i % step == 0).ToArray();
        }
        foreach (var p in chosen)
        {
            bool grenades = fixedItem == WeaponId.FragGrenade;
            var go = new GameObject(fixedItem == WeaponId.None ? "Random Weapon Spawner" : grenades ? "Random Grenade Spawner" : $"{fixedItem} Spawner");
            go.transform.SetParent(parent, false);
            go.transform.position = p + Vector3.up * 0.03f;
            var s = go.AddComponent<WeaponSpawner>();
            s.randomWeapon = fixedItem == WeaponId.None;
            s.randomThrowable = grenades;
            s.weapon = fixedItem == WeaponId.None ? WeaponId.M4A1 : fixedItem;
            s.respawnDelay = delay;
            s.pool = pool;
        }
        Debug.Log($"[FFA] {groupName} ({map}): {chosen.Count} placed from {candidates.Count} candidates");

        static float Flat(Vector3 v) => v.x * v.x + v.z * v.z;
    }

    // ================================================================== golden duel rings

    /// <summary>Four boxing rings in an underground fight club, far away from every map (always active).</summary>
    static void BuildDuelArenas()
    {
        var keep = world;
        world = new GameObject("Duel Arenas").transform;
        for (int i = 0; i < 4; i++)
            BuildDuelArena(i, new Vector3(5000f + i * 80f, 0f, 5000f));
        world = keep;
    }

    static void BuildDuelArena(int slot, Vector3 pos)
    {
        var a = new GameObject($"Duel Ring {slot + 1}").transform;
        a.SetParent(world, false);
        a.position = pos;
        const float half = 9f, H = 6.5f, ring = 3.6f, deck = 0.45f;

        // Concrete room, brick walls, low ceiling.
        metricTile = new Vector2(3f, 3f);
        Box(a, "Floor", new Vector3(0f, -0.25f, 0f), new Vector3(half * 2f, 0.5f, half * 2f), M("CityConcrete"));
        Box(a, "Ceiling", new Vector3(0f, H + 0.25f, 0f), new Vector3(half * 2f + 1f, 0.5f, half * 2f + 1f), M("CityRoof"));
        Box(a, "Wall", new Vector3(0f, H / 2f, half + 0.25f), new Vector3(half * 2f + 1f, H, 0.5f), M("CityBrick"));
        Box(a, "Wall", new Vector3(0f, H / 2f, -half - 0.25f), new Vector3(half * 2f + 1f, H, 0.5f), M("CityBrick"));
        Box(a, "Wall", new Vector3(half + 0.25f, H / 2f, 0f), new Vector3(0.5f, H, half * 2f), M("CityBrick"));
        Box(a, "Wall", new Vector3(-half - 0.25f, H / 2f, 0f), new Vector3(0.5f, H, half * 2f), M("CityBrick"));
        metricTile = default;
        foreach (float side in new[] { -1f, 1f })
        {
            Box(a, "Gold Stripe", new Vector3(0f, 2.6f, side * (half - 0.01f)), new Vector3(half * 2f, 0.3f, 0.04f), M("Gold"), collider: false);
            Box(a, "Gold Stripe", new Vector3(side * (half - 0.01f), 2.6f, 0f), new Vector3(0.04f, 0.3f, half * 2f), M("Gold"), collider: false);
        }

        // The ring: canvas deck on a blue skirt, corner posts, three ropes, and an invisible cage so nobody leaves.
        Box(a, "Ring Skirt", new Vector3(0f, (deck - 0.05f) / 2f, 0f), new Vector3(ring * 2f + 0.5f, deck - 0.05f, ring * 2f + 0.5f), M("TentBlue"));
        Box(a, "Ring Canvas", new Vector3(0f, deck / 2f, 0f), new Vector3(ring * 2f, deck, ring * 2f), M("Trim"));
        Box(a, "Center Logo", new Vector3(0f, deck + 0.005f, 0f), new Vector3(2.2f, 0.01f, 2.2f), M("Gold"), yRot: 45f, collider: false);
        var postMats = new[] { M("TruckRed"), M("Trim"), M("TentBlue"), M("Trim") };
        int pi = 0;
        foreach (float x in new[] { -ring, ring })
            foreach (float z in new[] { -ring, ring })
                Cylinder(a, "Corner Post", new Vector3(x, deck + 0.75f, z), 0.09f, 1.5f, postMats[pi++]);
        foreach (float y in new[] { 0.45f, 0.85f, 1.25f })
        {
            var rope = y > 1f ? M("TruckRed") : M("Trim");
            Box(a, "Rope", new Vector3(0f, deck + y, ring), new Vector3(ring * 2f, 0.04f, 0.04f), rope);
            Box(a, "Rope", new Vector3(0f, deck + y, -ring), new Vector3(ring * 2f, 0.04f, 0.04f), rope);
            Box(a, "Rope", new Vector3(ring, deck + y, 0f), new Vector3(0.04f, 0.04f, ring * 2f), rope);
            Box(a, "Rope", new Vector3(-ring, deck + y, 0f), new Vector3(0.04f, 0.04f, ring * 2f), rope);
        }
        Box(a, "Cage", new Vector3(0f, deck + 2f, ring + 0.05f), new Vector3(ring * 2f + 0.2f, 4f, 0.1f), null);
        Box(a, "Cage", new Vector3(0f, deck + 2f, -ring - 0.05f), new Vector3(ring * 2f + 0.2f, 4f, 0.1f), null);
        Box(a, "Cage", new Vector3(ring + 0.05f, deck + 2f, 0f), new Vector3(0.1f, 4f, ring * 2f), null);
        Box(a, "Cage", new Vector3(-ring - 0.05f, deck + 2f, 0f), new Vector3(0.1f, 4f, ring * 2f), null);

        // A silent crowd of mannequins and bleachers along the walls.
        for (int i = 0; i < 14; i++)
        {
            float ang = (i + 0.5f) / 14f * Mathf.PI * 2f;
            var p = new Vector3(Mathf.Cos(ang) * 6.3f, 0f, Mathf.Sin(ang) * 6.3f);
            Mannequin(a.TransformPoint(p), Mathf.Atan2(-p.x, -p.z) * Mathf.Rad2Deg, i % 5 == 3);
        }
        foreach (float side in new[] { -1f, 1f })
        {
            Box(a, "Bleacher", new Vector3(side * (half - 0.6f), 0.25f, 0f), new Vector3(1.2f, 0.5f, 12f), M("Metal"));
            Box(a, "Bleacher", new Vector3(side * (half - 0.3f), 0.75f, 0f), new Vector3(0.6f, 1.5f, 12f), M("Metal"));
        }

        // Lights: a hard spotlight over the ring and two dim house lights.
        var spot = new GameObject("Ring Spotlight").AddComponent<Light>();
        spot.transform.SetParent(a, false);
        spot.transform.localPosition = new Vector3(0f, H - 0.4f, 0f);
        spot.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        spot.type = LightType.Spot;
        spot.spotAngle = 95f;
        spot.range = 12f;
        spot.intensity = 3.2f;
        spot.color = new Color(1f, 0.95f, 0.85f);
        spot.shadows = LightShadows.Soft;
        Box(a, "Lamp Hood", new Vector3(0f, H - 0.25f, 0f), new Vector3(1.2f, 0.3f, 1.2f), M("GunDark"), collider: false);
        foreach (float x in new[] { -5.5f, 5.5f })
        {
            var house = new GameObject("House Light").AddComponent<Light>();
            house.transform.SetParent(a, false);
            house.transform.localPosition = new Vector3(x, H - 1f, 0f);
            house.type = LightType.Point;
            house.range = 14f;
            house.intensity = 1.1f;
            house.color = new Color(1f, 0.75f, 0.45f);
            house.shadows = LightShadows.None;
        }

        var arena = a.gameObject.AddComponent<DuelArena>();
        arena.slot = slot;
        arena.spawnA = new GameObject("Corner A").transform;
        arena.spawnA.SetParent(a, false);
        arena.spawnA.localPosition = new Vector3(-2.2f, deck + 0.05f, 0f);
        arena.spawnA.localRotation = Quaternion.Euler(0f, 90f, 0f);
        arena.spawnB = new GameObject("Corner B").transform;
        arena.spawnB.SetParent(a, false);
        arena.spawnB.localPosition = new Vector3(2.2f, deck + 0.05f, 0f);
        arena.spawnB.localRotation = Quaternion.Euler(0f, -90f, 0f);
    }
}
