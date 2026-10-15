using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Molotov fire: a pool of flames on the ground for a few seconds. Every client draws it; on the server (host) it also
/// burns anyone standing in it, credited to whoever threw the bottle.
/// </summary>
public class FireArea : MonoBehaviour
{
    const float Tick = 0.25f;
    static readonly bool logHits = System.Environment.GetCommandLineArgs().Contains("-ffalogfire"); // tests

    FpsPlayer owner;
    bool server;
    float born, nextTick;
    Light glow;
    AudioSource crackle;

    public static void Create(Vector3 ground, FpsPlayer owner, bool server)
    {
        var go = new GameObject("MolotovFire");
        go.transform.position = ground;
        var f = go.AddComponent<FireArea>();
        f.owner = owner;
        f.server = server;
        f.born = Time.time;
        f.nextTick = Time.time + Tick;
        f.Build();
        Gore.PlayAt(Sfx.Explosion, ground, 0.35f); // whoomp as the petrol catches
        Destroy(go, WeaponDefs.FireSeconds + 2f);
    }

    bool Burning => Time.time - born < WeaponDefs.FireSeconds;

    void Update()
    {
        float age = Time.time - born;
        if (glow != null)
            glow.intensity = Burning ? (2.2f + Mathf.PerlinNoise(Time.time * 6f, 0.3f) * 1.6f) * Mathf.Clamp01(age / 0.3f) : Mathf.MoveTowards(glow.intensity, 0f, Time.deltaTime * 3f);
        if (crackle != null && !Burning) crackle.volume = Mathf.MoveTowards(crackle.volume, 0f, Time.deltaTime);
        if (!server || !Burning || Time.time < nextTick) return;
        nextTick += Tick;
        Vector3 c = transform.position;
        foreach (var p in new List<FpsPlayer>(FpsPlayer.All))
        {
            if (!p.IsSpawned || p.IsDead) continue;
            Vector3 d = p.transform.position - c;
            if (d.y < -1f || d.y > 2.2f || new Vector2(d.x, d.z).magnitude > WeaponDefs.FireRadius) continue;
            var chest = p.ChestPoint;
            var info = new DeathInfo
            {
                Point = chest, Direction = Vector3.up, Weapon = (byte)WeaponId.Molotov, Part = (byte)BodyModel.Part.Chest,
                Distance = 0f, Seed = Random.Range(int.MinValue, int.MaxValue),
            };
            int dmg = Mathf.RoundToInt(WeaponDefs.FireDps * Tick);
            bool killed = p.TakeDamage(dmg, owner, info);
            if (logHits) Debug.Log($"[FFA] Fire: {p.DisplayName} health {p.Health.Value}");
            if (owner != null && owner != p && owner.IsSpawned) owner.ServerConfirmHit(dmg, false, killed, chest);
        }
    }

    void Build()
    {
        var a = GameAssets.I;
        // Flames licking up across the pool.
        var flames = new GameObject("Flames").AddComponent<ParticleSystem>();
        flames.transform.SetParent(transform, false);
        flames.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        flames.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = flames.main;
        main.loop = false;
        main.duration = WeaponDefs.FireSeconds;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.3f, 1f), new Color(1f, 0.4f, 0.1f, 1f));
        main.maxParticles = 600;
        var em = flames.emission;
        em.rateOverTime = 110f;
        var shape = flames.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = WeaponDefs.FireRadius * 0.9f;
        var sol = flames.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.15f));
        var col = flames.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(new Gradient
        {
            colorKeys = new[] { new GradientColorKey(new Color(1f, 0.9f, 0.5f), 0f), new GradientColorKey(new Color(1f, 0.3f, 0.05f), 1f) },
            alphaKeys = new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) },
        });
        var fr = flames.GetComponent<ParticleSystemRenderer>();
        fr.sharedMaterial = a.flashGlow;
        fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        flames.Play();

        // Black smoke rising off it.
        var smoke = new GameObject("Smoke").AddComponent<ParticleSystem>();
        smoke.transform.SetParent(transform, false);
        smoke.transform.localPosition = Vector3.up * 0.8f;
        smoke.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var sm = smoke.main;
        sm.loop = false;
        sm.duration = WeaponDefs.FireSeconds;
        sm.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3.5f);
        sm.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2f);
        sm.startSize = new ParticleSystem.MinMaxCurve(1f, 2f);
        sm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.12f, 0.11f, 0.1f, 0.6f), new Color(0.25f, 0.23f, 0.21f, 0.5f));
        sm.maxParticles = 200;
        var sem = smoke.emission;
        sem.rateOverTime = 14f;
        var ssh = smoke.shape;
        ssh.shapeType = ParticleSystemShapeType.Circle;
        ssh.radius = WeaponDefs.FireRadius * 0.6f;
        var ssol = smoke.sizeOverLifetime;
        ssol.enabled = true;
        ssol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.2f));
        var scol = smoke.colorOverLifetime;
        scol.enabled = true;
        scol.color = new ParticleSystem.MinMaxGradient(new Gradient
        {
            colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            alphaKeys = new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) },
        });
        var sr = smoke.GetComponent<ParticleSystemRenderer>();
        sr.sharedMaterial = a.dust;
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        smoke.Play();

        glow = new GameObject("FireLight").AddComponent<Light>();
        glow.transform.SetParent(transform, false);
        glow.transform.localPosition = Vector3.up * 1f;
        glow.type = LightType.Point;
        glow.color = new Color(1f, 0.55f, 0.2f);
        glow.range = WeaponDefs.FireRadius * 3f;
        glow.intensity = 0f;

        crackle = gameObject.AddComponent<AudioSource>();
        crackle.clip = Sfx.Slide;
        crackle.loop = true;
        crackle.pitch = 0.55f;
        crackle.volume = 0.6f;
        crackle.spatialBlend = 1f;
        crackle.minDistance = 3f;
        crackle.maxDistance = 40f;
        crackle.Play();
    }
}
