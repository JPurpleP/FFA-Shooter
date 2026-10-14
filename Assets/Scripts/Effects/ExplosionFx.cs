using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Explosion visuals and audio: fireball, flash of light, sparks and dirt, a rising smoke column, a scorch mark,
/// a deep boom and screen shake. Also tears apart any corpses caught in the blast.
/// </summary>
public static class ExplosionFx
{
    public static void Play(Vector3 at, WeaponDef def)
    {
        float radius = def != null ? def.ExplosionRadius : 6f;
        float scale = Mathf.Clamp(radius / 6f, 0.7f, 1.3f);
        var a = GameAssets.I;

        // Fireball: hot additive puffs that cool from yellow to dark red.
        var fire = NewSystem("Fireball", at, Vector3.up, a.sparkGlow);
        var main = fire.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f * scale, 9f * scale);
        main.startSize = new ParticleSystem.MinMaxCurve(1.4f * scale, 3.6f * scale);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.45f), new Color(1f, 0.55f, 0.2f));
        main.gravityModifier = -0.3f;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var col = fire.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(new Gradient
        {
            colorKeys = new[] { new GradientColorKey(new Color(1f, 0.95f, 0.7f), 0f), new GradientColorKey(new Color(1f, 0.45f, 0.1f), 0.4f), new GradientColorKey(new Color(0.4f, 0.08f, 0.02f), 1f) },
            alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) },
        });
        var sol = fire.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.5f, 1f, 1.4f));
        Emit(fire, 32, 80f);

        // Core flash card.
        var flash = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.DestroyImmediate(flash.GetComponent<Collider>());
        flash.transform.position = at;
        flash.transform.localScale = Vector3.one * 5f * scale;
        var fr = flash.GetComponent<Renderer>();
        fr.sharedMaterial = a.flashGlow;
        fr.shadowCastingMode = ShadowCastingMode.Off;
        flash.AddComponent<FaceCamera>();
        flash.AddComponent<FlashFade>().life = 0.09f;

        // Smoke column.
        var smoke = NewSystem("ExplosionSmoke", at, Vector3.up, a.dust);
        main = smoke.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 5.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 4.5f * scale);
        main.startSize = new ParticleSystem.MinMaxCurve(1.5f * scale, 3.5f * scale);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.22f, 0.21f, 0.2f, 0.75f), new Color(0.4f, 0.38f, 0.35f, 0.55f));
        main.gravityModifier = -0.08f;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        col = smoke.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(new Gradient
        {
            colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            alphaKeys = new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0f, 1f) },
        });
        sol = smoke.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.4f));
        var noise = smoke.noise;
        noise.enabled = true;
        noise.strength = 0.6f;
        noise.frequency = 0.5f;
        Emit(smoke, 26, 55f);

        // Sparks and dirt clods.
        ShotFx.Sparks(at, Vector3.up, 70, 26f * scale, 85f);
        Dirt(at, 14, 9f * scale);

        // Light flash that fades.
        var lightGo = new GameObject("ExplosionLight");
        lightGo.transform.position = at + Vector3.up * 0.5f;
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1f, 0.65f, 0.35f);
        l.range = 22f * scale;
        l.shadows = LightShadows.None;
        lightGo.AddComponent<LightFade>().Init(9f, 0.45f);

        // Scorch mark on whatever is underneath.
        if (Physics.Raycast(at + Vector3.up * 0.5f, Vector3.down, out var ground, 3f, 1, QueryTriggerInteraction.Ignore))
            Gore.Scorch(ground.point, ground.normal, 3.2f * scale);

        // Boom, screen shake, and the bodies.
        Gore.PlayAt(Sfx.Explosion, at, 1f);
        FpsPlayer.ShakeFromExplosion(at, radius);
        Gore.Explosion(at, radius * 1.1f, 15f);
    }

    static void Dirt(Vector3 at, int count, float speed)
    {
        var a = GameAssets.I;
        for (int i = 0; i < count; i++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Debris";
            go.layer = GameAssets.DebrisLayer;
            go.transform.position = at + Random.insideUnitSphere * 0.3f;
            go.transform.rotation = Random.rotation;
            go.transform.localScale = Vector3.one * Random.Range(0.04f, 0.12f);
            go.GetComponent<Renderer>().sharedMaterial = Random.value < 0.5f ? a.gunDark : a.dust;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.2f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.linearVelocity = (Random.insideUnitSphere + Vector3.up * 1.2f).normalized * Random.Range(speed * 0.4f, speed);
            rb.angularVelocity = Random.insideUnitSphere * 15f;
            Object.Destroy(go, Random.Range(5f, 8f));
        }
    }

    static ParticleSystem NewSystem(string name, Vector3 pos, Vector3 dir, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(dir));
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy;
        main.maxParticles = 500;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        return ps;
    }

    static void Emit(ParticleSystem ps, int count, float spread)
    {
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = spread;
        shape.radius = 0.3f;
        var emission = ps.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        ps.Play();
    }
}
