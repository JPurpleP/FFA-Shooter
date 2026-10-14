using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Gunfire visuals: a glowing round that travels from the muzzle to the impact (impact effects fire on
/// arrival), and a layered muzzle flash — star burst, flame petals, sparks, smoke and a fading light.
/// </summary>
public static class ShotFx
{
    // ------------------------------------------------------------------ travelling round

    /// <summary>Fly a tracer from `from` to `to`; `onArrive` runs when it gets there (impact effects).</summary>
    public static void Round(Vector3 from, Vector3 to, WeaponDef def, Action onArrive)
    {
        float dist = Vector3.Distance(from, to);
        if (dist < 0.05f)
        {
            onArrive?.Invoke();
            return;
        }
        var go = new GameObject("Round");
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = GameAssets.I.tracerGlow;
        line.positionCount = 2;
        line.textureMode = LineTextureMode.Stretch;
        line.numCapVertices = 0;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        bool heavy = def.Id == WeaponId.BarrettM82;
        bool pellet = def.Pellets > 1;
        float width = heavy ? 0.075f : pellet ? 0.022f : def.Id == WeaponId.GoldenDeagle ? 0.045f : 0.034f;
        line.widthCurve = new AnimationCurve(new Keyframe(0f, width * 0.35f), new Keyframe(1f, width));
        var tint = heavy ? new Color(1f, 0.85f, 0.6f) : new Color(1f, 0.82f, 0.45f);
        line.startColor = new Color(tint.r, tint.g, tint.b, 0f);
        line.endColor = tint;
        var round = go.AddComponent<TracerRound>();
        round.Init(line, from, to, def.TracerSpeed, Mathf.Clamp(def.TracerSpeed * 0.012f, 1.5f, heavy ? 9f : 5f), onArrive);
    }

    // ------------------------------------------------------------------ muzzle flash

    /// <summary>
    /// Flash at the muzzle. `muzzle` parents the flame (so it rides the gun); `worldPos`/`worldDir` place the
    /// world-space parts (sparks, smoke, light) — for first person these are corrected into the main camera's space.
    /// </summary>
    public static void MuzzleFlash(Transform muzzle, int layer, WeaponDef def, Vector3 worldPos, Vector3 worldDir, bool firstPerson)
    {
        var a = GameAssets.I;
        bool heavy = def.Id == WeaponId.BarrettM82;
        bool shotgun = def.Pellets > 1;
        float size = heavy ? 0.42f : shotgun ? 0.3f : def.Id == WeaponId.GoldenDeagle ? 0.24f : def.Id == WeaponId.Glock17 ? 0.13f : def.Id == WeaponId.MP5 ? 0.12f : 0.17f;
        if (firstPerson) size *= 0.85f;

        // Flame: star burst facing back down the barrel, plus two crossed petals along it, randomly rolled.
        var root = new GameObject("MuzzleFlash").transform;
        root.SetParent(muzzle, false);
        root.localRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f));
        var flashColor = new Color(1f, 0.72f, 0.38f, 0.75f);
        FlashQuad(root, layer, Vector3.forward * 0.01f, Quaternion.identity, new Vector3(size * 1.1f, size * 1.1f, 1f), flashColor);
        for (int i = 0; i < 2; i++)
        {
            var rot = Quaternion.Euler(0f, 0f, i * 90f) * Quaternion.Euler(0f, 90f, 0f);
            float len = size * UnityEngine.Random.Range(1.8f, 2.6f);
            FlashQuad(root, layer, Vector3.forward * len * 0.45f, rot, new Vector3(len, size * 0.8f, 1f), flashColor);
        }
        if (heavy)
        {
            // Muzzle brake vents the blast out to both sides.
            foreach (float side in new[] { -1f, 1f })
                FlashQuad(root, layer, new Vector3(side * size * 0.6f, 0f, -0.04f), Quaternion.Euler(90f, 0f, 0f),
                    new Vector3(size * 1.4f, size * 0.5f, 1f), flashColor);
        }
        root.gameObject.AddComponent<FlashFade>().life = heavy ? 0.06f : 0.04f;

        // Sparks and a puff of smoke in world space (they stay behind as the gun moves).
        Sparks(worldPos, worldDir, heavy ? 14 : shotgun ? 10 : 6, heavy ? 16f : 11f, 22f);
        Smoke(worldPos + worldDir * 0.05f, worldDir, heavy ? 6 : shotgun ? 4 : 2, heavy ? 0.5f : 0.22f);

        // Short light flash that fades rather than popping off.
        var lightGo = new GameObject("FlashLight");
        lightGo.transform.position = worldPos;
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1f, 0.72f, 0.4f);
        l.range = heavy ? 12f : shotgun ? 8f : 5f;
        l.shadows = LightShadows.None;
        lightGo.AddComponent<LightFade>().Init(heavy ? 5f : 3.5f, 0.07f);
    }

    static void FlashQuad(Transform parent, int layer, Vector3 localPos, Quaternion localRot, Vector3 scale, Color tint)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        UnityEngine.Object.DestroyImmediate(q.GetComponent<Collider>());
        q.layer = layer;
        q.transform.SetParent(parent, false);
        q.transform.localPosition = localPos;
        q.transform.localRotation = localRot;
        q.transform.localScale = scale;
        var r = q.GetComponent<Renderer>();
        r.sharedMaterial = GameAssets.I.flashGlow;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        var block = new MaterialPropertyBlock();
        block.SetColor("_TintColor", tint);
        r.SetPropertyBlock(block);
    }

    // ------------------------------------------------------------------ particles

    public static void Sparks(Vector3 pos, Vector3 dir, int count, float speed, float spread)
    {
        if (count <= 0) return;
        var ps = NewSystem("Sparks", pos, dir, GameAssets.I.sparkGlow, stretched: true);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.06f, 0.18f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.03f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.6f), new Color(1f, 0.55f, 0.2f));
        main.gravityModifier = 0.6f;
        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(new Gradient
        {
            colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.4f, 0.1f), 1f) },
            alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) },
        });
        Emit(ps, count, spread);
    }

    public static void Smoke(Vector3 pos, Vector3 dir, int count, float size)
    {
        if (count <= 0) return;
        var ps = NewSystem("GunSmoke", pos, dir, GameAssets.I.dust, stretched: false);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size * 1.2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.8f, 0.8f, 0.8f, 0.35f), new Color(0.6f, 0.6f, 0.6f, 0.25f));
        main.gravityModifier = -0.05f; // drifts up
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.2f));
        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(new Gradient
        {
            colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) },
        });
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.4f;
        noise.frequency = 1.5f;
        Emit(ps, count, 18f);
    }

    static ParticleSystem NewSystem(string name, Vector3 pos, Vector3 dir, Material mat, bool stretched)
    {
        var go = new GameObject(name);
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(dir.sqrMagnitude > 0.0001f ? dir : Vector3.forward));
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        if (stretched)
        {
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.02f;
            r.lengthScale = 2f;
        }
        return ps;
    }

    static void Emit(ParticleSystem ps, int count, float spread)
    {
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = spread;
        shape.radius = 0.01f;
        var emission = ps.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        ps.Play();
    }
}
