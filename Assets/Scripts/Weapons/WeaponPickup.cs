using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>A floating, spinning weapon on the ground. Spawned by WeaponSpawners or dropped by dead players.</summary>
public class WeaponPickup : NetworkBehaviour
{
    public static readonly List<WeaponPickup> All = new();

    public readonly NetworkVariable<int> Weapon = new(-1);
    public readonly NetworkVariable<int> Mag = new();
    public readonly NetworkVariable<int> Reserve = new();

    public WeaponId Id => (WeaponId)Weapon.Value;
    public WeaponDef Def => WeaponDefs.Get(Weapon.Value);
    public bool Available => IsSpawned && !claimed && Weapon.Value >= 0;

    // Server-side bookkeeping
    WeaponId initId;
    int initMag, initReserve;
    float despawnAt = -1f;
    bool claimed;
    public WeaponSpawner Spawner { get; private set; }

    Transform visual;
    Light glow;
    int builtFor = -2;
    float bobOffset;

    public static WeaponPickup Spawn(WeaponId id, int mag, int reserve, Vector3 position, WeaponSpawner spawner, float lifetime)
    {
        var go = Instantiate(GameAssets.I.pickupPrefab, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        var p = go.GetComponent<WeaponPickup>();
        p.initId = id;
        p.initMag = mag;
        p.initReserve = reserve;
        p.Spawner = spawner;
        p.despawnAt = lifetime > 0f ? Time.time + lifetime : -1f;
        go.GetComponent<NetworkObject>().Spawn(true);
        return p;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            Weapon.Value = (int)initId;
            Mag.Value = initMag;
            Reserve.Value = initReserve;
        }
        All.Add(this);
        bobOffset = Random.Range(0f, 6f);
        Weapon.OnValueChanged += (_, _) => Rebuild();
        Rebuild();
    }

    public override void OnNetworkDespawn() => All.Remove(this);

    public override void OnDestroy()
    {
        All.Remove(this);
        base.OnDestroy();
    }

    void Rebuild()
    {
        if (builtFor == Weapon.Value) return;
        builtFor = Weapon.Value;
        if (visual != null) Destroy(visual.gameObject);
        var def = Def;
        if (def == null) return;

        visual = new GameObject("Visual").transform;
        visual.SetParent(transform, false);
        var model = WeaponModel.Build(def.Id, visual);
        // Center the model on its own bounds so it spins around its middle.
        var bounds = new Bounds(model.transform.position, Vector3.zero);
        foreach (var r in model.GetComponentsInChildren<Renderer>())
        {
            bounds.Encapsulate(r.bounds);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
        model.transform.localPosition = -visual.InverseTransformPoint(bounds.center);
        float scale = def.Id == WeaponId.FragGrenade ? 3.2f : def.Id == WeaponId.Glock17 || def.Id == WeaponId.GoldenDeagle || def.Id == WeaponId.GrappleHook ? 2f : 1.25f; // small things are hard to spot
        visual.localScale = Vector3.one * scale;

        glow = new GameObject("Glow").AddComponent<Light>();
        glow.transform.SetParent(transform, false);
        glow.transform.localPosition = new Vector3(0f, 0.9f, 0f);
        glow.type = LightType.Point;
        glow.range = 3f;
        glow.intensity = 2f;
        glow.color = def.PickupGlow;
        glow.shadows = LightShadows.None;

        // Glowing ring on the floor beneath it.
        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        DestroyImmediate(ring.GetComponent<Collider>());
        ring.name = "Ring";
        ring.transform.SetParent(visual.parent, false);
        ring.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        ring.transform.localScale = new Vector3(0.8f, 0.005f, 0.8f);
        var rr = ring.GetComponent<Renderer>();
        rr.sharedMaterial = GameAssets.I.muzzleFlash;
        var block = new MaterialPropertyBlock();
        block.SetColor("_Color", new Color(def.PickupGlow.r * 0.8f, def.PickupGlow.g * 0.8f, def.PickupGlow.b * 0.8f, 0.3f));
        rr.SetPropertyBlock(block);
        rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    void Update()
    {
        if (visual != null)
        {
            float t = Time.time + bobOffset;
            visual.localPosition = new Vector3(0f, 0.9f + Mathf.Sin(t * 2f) * 0.1f, 0f);
            visual.localRotation = Quaternion.Euler(0f, t * 70f, 0f);
            if (glow != null) glow.intensity = 1.6f + Mathf.Sin(t * 4f) * 0.5f;
        }
        if (IsServer && despawnAt > 0f && Time.time > despawnAt && IsSpawned)
            Despawn();
    }

    /// <summary>Server: remove from the world (taken or expired).</summary>
    public void Despawn()
    {
        if (!IsServer || claimed) return;
        claimed = true;
        Spawner?.OnPickupGone();
        NetworkObject.Despawn(true);
    }

    public static WeaponPickup Nearest(Vector3 pos, float maxDistance)
    {
        WeaponPickup best = null;
        float bestSq = maxDistance * maxDistance;
        foreach (var p in All)
        {
            if (!p.Available) continue;
            float sq = (p.transform.position + Vector3.up * 0.9f - pos).sqrMagnitude;
            if (sq < bestSq)
            {
                bestSq = sq;
                best = p;
            }
        }
        return best;
    }
}
