using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public enum ProjectileKind : byte { None, Grenade40, Rocket, Frag }

/// <summary>
/// Explosive projectile: 40mm grenade, RPG rocket or thrown frag grenade. The server simulates it and decides the
/// blast; clients draw it. Rockets and 40mm rounds fly deterministic ballistic paths from their launch state (so
/// every client draws them smoothly without position updates); bouncing frags stream their position.
/// </summary>
public class Projectile : NetworkBehaviour
{
    public readonly NetworkVariable<byte> Weapon = new();
    public readonly NetworkVariable<Vector3> StartPos = new();
    public readonly NetworkVariable<Vector3> StartVel = new();
    public readonly NetworkVariable<double> StartTime = new();
    public readonly NetworkVariable<Vector3> FragPos = new();

    public WeaponDef Def => WeaponDefs.Get(Weapon.Value);
    ProjectileKind Kind => Def != null ? Def.Projectile : ProjectileKind.None;

    // Server-side launch state
    WeaponId initWeapon;
    Vector3 initPos, initVel;
    float fuseAt;
    FpsPlayer shooter;
    Rigidbody fragBody;
    Vector3 lastPos;
    bool exploded;

    Transform visual;
    const float MaxFlightTime = 8f;
    static readonly RaycastHit[] hits = new RaycastHit[16];

    public static void Launch(WeaponId weapon, Vector3 pos, Vector3 vel, float fuse, FpsPlayer shooter)
    {
        var go = Instantiate(GameAssets.I.projectilePrefab, pos, Quaternion.LookRotation(vel.sqrMagnitude > 0.01f ? vel : Vector3.forward));
        var p = go.GetComponent<Projectile>();
        p.initWeapon = weapon;
        p.initPos = pos;
        p.initVel = vel;
        p.fuseAt = Time.time + fuse;
        p.shooter = shooter;
        go.GetComponent<NetworkObject>().Spawn(true);
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            Weapon.Value = (byte)initWeapon;
            StartPos.Value = initPos;
            StartVel.Value = initVel;
            StartTime.Value = NetworkManager.ServerTime.Time;
            FragPos.Value = initPos;
            lastPos = initPos;
        }
        BuildVisual();
        if (IsServer && Kind == ProjectileKind.Frag)
        {
            // Physical, bouncing grenade (on the Debris layer: it rattles off the world but passes through players).
            var col = gameObject.AddComponent<SphereCollider>();
            col.radius = 0.06f;
            col.material = new PhysicsMaterial("Frag") { bounciness = 0.35f, dynamicFriction = 0.6f, staticFriction = 0.6f, bounceCombine = PhysicsMaterialCombine.Maximum };
            gameObject.layer = GameAssets.DebrisLayer;
            fragBody = gameObject.AddComponent<Rigidbody>();
            fragBody.mass = 0.4f;
            fragBody.interpolation = RigidbodyInterpolation.Interpolate;
            fragBody.linearDamping = 0.15f;
            fragBody.angularDamping = 0.5f;
            fragBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            fragBody.linearVelocity = initVel;
            fragBody.angularVelocity = Random.insideUnitSphere * 12f;
            if (Time.time >= fuseAt) Explode(initPos); // cooked too long: goes off in hand
        }
    }

    void BuildVisual()
    {
        var def = Def;
        if (def == null) return;
        visual = new GameObject("Visual").transform;
        visual.SetParent(transform, false);
        var a = GameAssets.I;
        var b = new WeaponModel.Builder(visual);
        switch (def.Projectile)
        {
            case ProjectileKind.Frag:
                WeaponModel.Build(WeaponId.FragGrenade, visual);
                break;
            case ProjectileKind.Grenade40:
                b.Cyl(null, "Shell", Vector3.zero, 0.02f, 0.07f, a.olive, WeaponModel.Builder.Axis.Z);
                b.Cyl(null, "Nose", new Vector3(0f, 0f, 0.045f), 0.015f, 0.025f, a.brass, WeaponModel.Builder.Axis.Z);
                Trail(0.08f, 1.2f, 0.25f);
                break;
            case ProjectileKind.Rocket:
                b.Cyl(null, "Motor", new Vector3(0f, 0f, -0.1f), 0.022f, 0.3f, a.olive, WeaponModel.Builder.Axis.Z);
                b.Cyl(null, "Warhead", new Vector3(0f, 0f, 0.12f), 0.042f, 0.2f, a.olive, WeaponModel.Builder.Axis.Z);
                b.Cyl(null, "Ogive", new Vector3(0f, 0f, 0.25f), 0.028f, 0.07f, a.olive, WeaponModel.Builder.Axis.Z);
                b.Cyl(null, "Tip", new Vector3(0f, 0f, 0.3f), 0.012f, 0.05f, a.gunMetal, WeaponModel.Builder.Axis.Z);
                for (int i = 0; i < 4; i++)
                    b.Box(null, "Fin", new Vector3(0f, 0f, -0.24f), new Vector3(0.12f, 0.004f, 0.06f), a.gunMetal, new Vector3(0f, 0f, i * 45f));
                Trail(0.18f, 2.5f, 0.6f);
                // Motor flame and glow.
                var flame = GameObject.CreatePrimitive(PrimitiveType.Quad);
                DestroyImmediate(flame.GetComponent<Collider>());
                flame.transform.SetParent(visual, false);
                flame.transform.localPosition = new Vector3(0f, 0f, -0.32f);
                flame.transform.localScale = Vector3.one * 0.35f;
                var fr = flame.GetComponent<Renderer>();
                fr.sharedMaterial = a.flashGlow;
                flame.AddComponent<FaceCamera>();
                var glow = new GameObject("MotorLight").AddComponent<Light>();
                glow.transform.SetParent(visual, false);
                glow.transform.localPosition = new Vector3(0f, 0f, -0.4f);
                glow.type = LightType.Point;
                glow.color = new Color(1f, 0.6f, 0.3f);
                glow.range = 6f;
                glow.intensity = 2.5f;
                break;
        }
        foreach (var r in visual.GetComponentsInChildren<Renderer>())
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    void Trail(float size, float life, float alpha)
    {
        var go = new GameObject("SmokeTrail");
        go.transform.SetParent(visual, false);
        go.transform.localPosition = new Vector3(0f, 0f, -0.3f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.duration = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(size, size * 1.8f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.85f, 0.85f, alpha), new Color(0.6f, 0.6f, 0.6f, alpha * 0.7f));
        main.maxParticles = 600;
        var em = ps.emission;
        em.rateOverTime = 0f;
        em.rateOverDistance = 12f;
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 2.5f));
        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(new Gradient
        {
            colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) },
        });
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = GameAssets.I.dust;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
    }

    /// <summary>Ballistic position/velocity at time t after launch (rockets fall far less than grenades).</summary>
    void Ballistic(float t, out Vector3 pos, out Vector3 vel)
    {
        float g = Physics.gravity.y * (Def != null ? Def.ProjectileGravity : 1f);
        Vector3 v0 = StartVel.Value;
        pos = StartPos.Value + v0 * t + 0.5f * g * t * t * Vector3.up;
        vel = v0 + g * t * Vector3.up;
    }

    void Update()
    {
        if (!IsSpawned || exploded || Def == null) return;
        if (Kind == ProjectileKind.Frag)
        {
            if (IsServer)
            {
                FragPos.Value = transform.position;
                if (Time.time >= fuseAt) Explode(transform.position + Vector3.up * 0.1f);
            }
            else
            {
                transform.position = Vector3.Lerp(transform.position, FragPos.Value, 1f - Mathf.Exp(-25f * Time.deltaTime));
                if (visual != null) visual.Rotate(400f * Time.deltaTime, 250f * Time.deltaTime, 0f, Space.Self);
            }
            return;
        }

        float t = (float)(NetworkManager.ServerTime.Time - StartTime.Value);
        Ballistic(Mathf.Max(0f, t), out var pos, out var vel);
        if (vel.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(vel);
        if (IsServer)
        {
            // Sweep the path covered since last frame; the shooter's own body never blocks their shot.
            Vector3 delta = pos - lastPos;
            float dist = delta.magnitude;
            if (dist > 0.0001f)
            {
                int n = Physics.SphereCastNonAlloc(lastPos, 0.08f, delta / dist, hits, dist, GameAssets.ShotMask, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue;
                Vector3 impact = Vector3.zero, normal = Vector3.up;
                for (int i = 0; i < n; i++)
                {
                    if (shooter != null && hits[i].collider.GetComponentInParent<FpsPlayer>() == shooter) continue;
                    if (hits[i].distance < best)
                    {
                        best = hits[i].distance;
                        impact = hits[i].distance > 0f ? hits[i].point : lastPos;
                        normal = hits[i].normal;
                    }
                }
                if (best < float.MaxValue)
                {
                    transform.position = impact;
                    Explode(impact + normal * 0.15f);
                    return;
                }
            }
            lastPos = pos;
            if (t > MaxFlightTime || pos.y < -20f) Explode(pos);
        }
        transform.position = pos;
    }

    // ------------------------------------------------------------------ the blast (server decides, everyone sees)

    void Explode(Vector3 at)
    {
        if (!IsServer || exploded) return;
        exploded = true;
        if (fragBody != null) fragBody.isKinematic = true;
        var def = Def;
        ApplyBlast(at, def, shooter);
        ExplodeRpc(at);
        StartCoroutine(DespawnSoon());
    }

    IEnumerator DespawnSoon()
    {
        yield return new WaitForSeconds(0.25f);
        if (IsSpawned) NetworkObject.Despawn(true);
    }

    [Rpc(SendTo.ClientsAndHost)]
    void ExplodeRpc(Vector3 at)
    {
        exploded = true;
        if (visual != null) visual.gameObject.SetActive(false);
        ExplosionFx.Play(at, Def);
    }

    /// <summary>Server: damage and knock back everyone in range. Walls between you and the blast absorb most of it.</summary>
    public static void ApplyBlast(Vector3 at, WeaponDef def, FpsPlayer shooter)
    {
        if (def == null) return;
        float radius = def.ExplosionRadius;
        Physics.SyncTransforms();
        foreach (var p in new List<FpsPlayer>(FpsPlayer.All))
        {
            if (!p.IsSpawned || p.IsDead) continue;
            Vector3 chest = p.ChestPoint;
            float d = Vector3.Distance(at, chest);
            if (d > radius) continue;
            float k = 1f - d / radius;
            bool blocked = Physics.Linecast(at, chest, 1, QueryTriggerInteraction.Ignore);
            float dmg = def.ExplosionDamage * Mathf.Clamp01(k * 1.35f) * (blocked ? 0.25f : 1f);
            if (dmg < 1f) continue;
            var info = new DeathInfo
            {
                Point = chest,
                Direction = (chest - at).sqrMagnitude > 0.001f ? (chest - at).normalized : Vector3.up,
                Weapon = (byte)def.Id,
                Part = (byte)BodyModel.Part.Chest,
                Distance = d,
                Seed = Random.Range(int.MinValue, int.MaxValue),
                Explosive = true,
                ExplosionCenter = at,
            };
            bool killed = p.TakeDamage(Mathf.RoundToInt(dmg), shooter, info);
            if (shooter != null && shooter != p) shooter.ServerConfirmHit(Mathf.RoundToInt(dmg), false, killed, chest);
            if (!killed && !blocked)
                p.ServerPush((chest - at).normalized * 11f * k + Vector3.up * 5f * k);
        }
    }
}
