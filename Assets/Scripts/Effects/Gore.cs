using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Client-side ragdolls and gore. Everything here is purely visual and spawned locally on each client;
/// GorePlan (seeded from the server) keeps dismemberment identical everywhere.
/// </summary>
public static class Gore
{
    static readonly Queue<GameObject> decals = new();
    static readonly Queue<GameObject> holes = new();
    static readonly Queue<GameObject> gibs = new();
    static readonly Queue<GameObject> corpses = new();
    const int MaxDecals = 650, MaxHoles = 150, MaxGibs = 520, MaxCorpses = 16;

    static readonly Color BloodA = new(0.55f, 0f, 0f, 1f);
    static readonly Color BloodB = new(0.25f, 0f, 0.01f, 1f);

    public static float Multiplier = 2f; // blood/gib volume; the dev panel can crank this further

    // ------------------------------------------------------------------ corpses

    /// <summary>Clone the live body into a physics ragdoll, throw it according to the weapon, and tear it apart.</summary>
    public static Transform SpawnCorpse(BodyModel body, DeathInfo info)
    {
        var def = WeaponDefs.Get(info.WeaponId);
        var plan = GorePlan.For(info);

        var sourceRenderers = body.GetComponentsInChildren<Renderer>(true);
        var go = Object.Instantiate(body.gameObject, body.transform.position, body.transform.rotation);
        go.name = "Corpse";
        // Instantiate doesn't copy per-renderer property blocks (player colors), so carry them over.
        var cloneRenderers = go.GetComponentsInChildren<Renderer>(true);
        var block = new MaterialPropertyBlock();
        for (int i = 0; i < cloneRenderers.Length && i < sourceRenderers.Length; i++)
        {
            sourceRenderers[i].GetPropertyBlock(block);
            cloneRenderers[i].SetPropertyBlock(block);
        }
        var model = go.GetComponent<BodyModel>();
        var parts = (Transform[])model.parts.Clone();
        Object.DestroyImmediate(model);
        GameAssets.SetLayerRecursive(go, GameAssets.DebrisLayer);
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            r.enabled = true;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        // Physics bodies
        var rbs = new Rigidbody[BodyModel.PartCount];
        float totalMass = 0f;
        for (int i = 0; i < BodyModel.PartCount; i++)
        {
            var spec = BodyModel.Specs[i];
            var t = parts[i];
            var p = (BodyModel.Part)i;
            if (p == BodyModel.Part.Pelvis)
            {
                var c = t.gameObject.AddComponent<BoxCollider>();
                c.size = new Vector3(0.34f, 0.2f, 0.22f);
            }
            else if (p == BodyModel.Part.Chest)
            {
                var c = t.gameObject.AddComponent<BoxCollider>();
                c.center = new Vector3(0f, 0.25f, 0f);
                c.size = new Vector3(0.4f, 0.48f, 0.25f);
            }
            else if (p == BodyModel.Part.Head)
            {
                var c = t.gameObject.AddComponent<SphereCollider>();
                c.center = new Vector3(0f, 0.15f, 0f);
                c.radius = 0.13f;
            }
            else
            {
                var c = t.gameObject.AddComponent<CapsuleCollider>();
                c.direction = 1;
                c.radius = spec.Radius;
                c.height = spec.Length + spec.Radius * 2f;
                c.center = new Vector3(0f, -spec.Length * 0.5f, 0f);
            }
            var rb = t.gameObject.AddComponent<Rigidbody>();
            rb.mass = spec.Mass;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.3f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.maxAngularVelocity = 40f;
            rb.maxDepenetrationVelocity = 6f;
            rbs[i] = rb;
            totalMass += spec.Mass;
        }
        for (int i = 0; i < BodyModel.PartCount; i++)
        {
            var spec = BodyModel.Specs[i];
            if (!spec.HasParent) continue;
            var j = parts[i].gameObject.AddComponent<CharacterJoint>();
            j.connectedBody = rbs[(int)spec.Parent];
            j.anchor = Vector3.zero;
            j.axis = Vector3.right;
            j.swingAxis = Vector3.forward;
            var p = (BodyModel.Part)i;
            bool knee = p == BodyModel.Part.LowerLegL || p == BodyModel.Part.LowerLegR;
            bool elbow = p == BodyModel.Part.LowerArmL || p == BodyModel.Part.LowerArmR;
            j.lowTwistLimit = new SoftJointLimit { limit = knee ? -5f : elbow ? -10f : -35f };
            j.highTwistLimit = new SoftJointLimit { limit = knee ? 120f : elbow ? 130f : 35f };
            j.swing1Limit = new SoftJointLimit { limit = knee || elbow ? 8f : p == BodyModel.Part.Chest ? 25f : 70f };
            j.swing2Limit = new SoftJointLimit { limit = knee || elbow ? 8f : p == BodyModel.Part.Head ? 40f : 50f };
            j.enableProjection = true;
            j.projectionDistance = 0.02f;
            j.projectionAngle = 30f;
        }

        // Throw it. Shotguns hit much harder up close.
        Vector3 dir = info.Direction.sqrMagnitude > 0.001f ? info.Direction.normalized : -body.transform.forward;
        float impulse = info.Explosive ? 950f : def != null ? def.RagdollImpulse : 40f;
        if (def != null && def.Pellets > 1 && !info.Explosive)
            impulse *= Mathf.Lerp(0.35f, 1.4f, 1f - Mathf.Clamp01(info.Distance / 15f));
        float lift = info.Explosive ? 0.7f : def != null ? def.RagdollLift : 0.1f;
        float spin = info.Explosive ? 160f : def != null ? def.RagdollSpin : 5f;
        var rng = new System.Random(info.Seed);
        Vector3 RandomDir() => new Vector3((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f);

        var hitRb = rbs[Mathf.Clamp(info.Part, 0, BodyModel.PartCount - 1)];
        foreach (var rb in rbs)
        {
            float share = rb.mass / totalMass;
            rb.AddForce((dir * 0.45f + Vector3.up * lift) * impulse * share, ForceMode.Impulse);
        }
        hitRb.AddForceAtPosition(dir * impulse * 0.55f, info.Point, ForceMode.Impulse);
        rbs[(int)BodyModel.Part.Chest].AddTorque(RandomDir() * spin, ForceMode.Impulse);
        rbs[(int)BodyModel.Part.Pelvis].AddTorque(RandomDir() * spin * 0.6f, ForceMode.Impulse);

        if (!info.Fell)
            ApplyGore(parts, rbs, info, plan, dir, impulse, rng);

        var corpse = go.AddComponent<Corpse>();
        corpse.bodies = rbs;
        corpse.parts = parts;
        Track(corpses, go, MaxCorpses);
        PlayAt(plan.AnyGibs ? Sfx.Splatter : Sfx.Squelch, info.Point, 0.9f);
        return parts[(int)BodyModel.Part.Chest];
    }

    static void ApplyGore(Transform[] parts, Rigidbody[] rbs, DeathInfo info, GorePlan plan, Vector3 dir, float impulse, System.Random rng)
    {
        float m = Multiplier;
        int intensity = plan.Intensity + (plan.AnyGibs ? 1 : 0);

        // Entry/exit sprays and wall splatter behind the victim.
        Burst(info.Point, -dir, (int)(20 * m), 1.5f, 5f, 40f, 0.06f, true);
        Burst(info.Point, dir, (int)((50 + 45 * intensity) * m), 3f, 10f + 3f * intensity, 25f + 8f * intensity, 0.08f, true);
        Burst(info.Point, dir, (int)((10 + 10 * intensity) * m), 0.5f, 2.5f, 80f, 0.16f, false); // wet mist
        SplatterRays(info.Point, dir, (3 + 3 * intensity) * 2, 6f + 2f * intensity, 0.6f + 0.3f * intensity, rng);
        Gibs(info.Point, dir * 3f, (int)((2 + 3 * intensity) * m), 3f + intensity, rng);

        if (plan.Obliterate)
        {
            Obliterate(parts, rbs, info, dir, impulse, rng);
            Corpse.SchedulePool(parts[(int)BodyModel.Part.Pelvis], 2.6f);
            Corpse.SchedulePool(parts[(int)BodyModel.Part.Chest], 2.2f);
            return;
        }
        Fountain(parts[Mathf.Clamp(info.Part, 0, BodyModel.PartCount - 1)], parts[Mathf.Clamp(info.Part, 0, BodyModel.PartCount - 1)].InverseTransformPoint(info.Point),
            parts[Mathf.Clamp(info.Part, 0, BodyModel.PartCount - 1)].InverseTransformDirection(-dir), 3f, 25f, 0.6f);

        for (int i = 0; i < BodyModel.PartCount; i++)
        {
            var p = (BodyModel.Part)i;
            if (!plan.Severed(p)) continue;
            if (p == BodyModel.Part.Head) { if (plan.HeadOff) Sever(parts, rbs, p, dir + Vector3.up * 0.8f, impulse * 1.3f, rng); else ExplodeHead(parts, rbs, dir, rng); }
            else if (p == BodyModel.Part.Chest) CutInHalf(parts, rbs, dir, impulse, rng);
            else Sever(parts, rbs, p, dir, impulse, rng);
        }

        if (plan.ChestBurst)
        {
            var chest = parts[(int)BodyModel.Part.Chest];
            Vector3 c = chest.TransformPoint(0f, 0.2f, 0f);
            Gibs(c, dir * 6f, (int)(34 * m), 10f, rng, boneShare: 0.25f);
            Burst(c, Vector3.up, (int)(140 * m), 3f, 12f, 80f, 0.1f, false);
            Mist(c, 70);
            Guts(rbs[(int)BodyModel.Part.Chest], chest.TransformPoint(0f, 0.15f, 0.1f), 14, rng);
            Fountain(chest, new Vector3(0f, 0.25f, 0.12f), Vector3.forward, 8f, 80f, 1.3f);
        }

        // A pool of blood slowly spreads under the body.
        Corpse.SchedulePool(parts[(int)BodyModel.Part.Chest], (0.8f + plan.Intensity * 0.35f) * 1.6f);
    }

    /// <summary>
    /// Total dismemberment: every joint fails (limbs break at both joints), the head bursts or flies off,
    /// the torso splits and everything is blown away from the blast point with a shower of meat.
    /// </summary>
    static void Obliterate(Transform[] parts, Rigidbody[] rbs, DeathInfo info, Vector3 dir, float impulse, System.Random rng)
    {
        float m = Multiplier;
        Vector3 chestPos = parts[(int)BodyModel.Part.Chest].position;
        Vector3 center = info.Explosive ? info.ExplosionCenter : chestPos - dir * 0.6f;

        if (rng.NextDouble() < 0.5) ExplodeHead(parts, rbs, dir, rng);
        else Sever(parts, rbs, BodyModel.Part.Head, dir + Vector3.up, impulse * 1.5f, rng);
        foreach (var p in new[] { BodyModel.Part.UpperArmL, BodyModel.Part.UpperArmR, BodyModel.Part.UpperLegL, BodyModel.Part.UpperLegR,
                                  BodyModel.Part.LowerArmL, BodyModel.Part.LowerArmR, BodyModel.Part.LowerLegL, BodyModel.Part.LowerLegR })
            Sever(parts, rbs, p, (parts[(int)p].position - center).normalized, impulse, rng);
        CutInHalf(parts, rbs, dir, impulse, rng);

        // Blow every piece outward from the blast (velocity-based so heavy and light parts both scatter, not vanish).
        float speed = info.Explosive ? 16f : 10f;
        foreach (var rb in rbs)
        {
            if (rb == null) continue;
            rb.AddExplosionForce(speed * rb.mass, center, 5f, 1.1f, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * 2f * rb.mass, ForceMode.Impulse);
        }

        // Meat shower, guts, red mist, falling blood and a ring of splatter.
        Gibs(chestPos, Vector3.up * 4f, (int)(60 * m), 14f, rng, boneShare: 0.3f);
        Guts(rbs[(int)BodyModel.Part.Pelvis], chestPos, 14, rng);
        Guts(rbs[(int)BodyModel.Part.Chest], chestPos, 10, rng);
        Burst(chestPos, Vector3.up, (int)(260 * m), 5f, 16f, 75f, 0.09f, true);
        Mist(chestPos, 140);
        SplatterRing(chestPos, 28, 9f, 1.2f, rng);
        PlayAt(Sfx.Splatter, chestPos, 1f);
    }

    /// <summary>
    /// For explosions: tears apart and throws any corpses within `radius` of `center` (joints near the blast
    /// are likely to fail) and splashes blood around. Live players are handled by the damage code.
    /// </summary>
    /// <param name="speed">peak launch speed (m/s) for pieces at the blast center</param>
    public static void Explosion(Vector3 center, float radius, float speed)
    {
        var rng = new System.Random(center.GetHashCode());
        bool anyFlesh = false;
        foreach (var go in corpses)
        {
            if (go == null) continue;
            var corpse = go.GetComponent<Corpse>();
            if (corpse == null || corpse.bodies == null) continue;
            if (Time.time - corpse.born < 0.15f) continue; // just created by this same blast: already torn apart
            foreach (var rb in corpse.bodies)
            {
                if (rb == null) continue;
                float d = Vector3.Distance(rb.worldCenterOfMass, center);
                if (d > radius) continue;
                anyFlesh = true;
                float k = 1f - d / radius;
                if (rb.GetComponent<CharacterJoint>() != null && rng.NextDouble() < k * 1.3f)
                    SeverJoint(rb.transform, (rb.position - center).normalized, speed * rb.mass * k, rng);
                rb.AddExplosionForce(speed * rb.mass, center, radius, 1f, ForceMode.Impulse);
            }
        }
        if (anyFlesh)
        {
            Gibs(center + Vector3.up * 0.5f, Vector3.up * 5f, (int)(30 * Multiplier), 12f, rng, boneShare: 0.3f);
            Mist(center + Vector3.up, 90);
            SplatterRing(center + Vector3.up * 0.5f, 18, radius, 1f, rng);
        }
    }

    /// <summary>Dark blast mark on the ground.</summary>
    public static void Scorch(Vector3 point, Vector3 normal, float size)
    {
        var go = MakeQuad("Scorch", point, normal, size, GameAssets.I.bulletHole, 0.008f);
        var block = new MaterialPropertyBlock();
        block.SetColor("_Color", new Color(0.15f, 0.13f, 0.12f, 0.9f));
        go.GetComponent<Renderer>().SetPropertyBlock(block);
        Track(decals, go, MaxDecals);
    }

    static void SplatterRing(Vector3 origin, int rays, float range, float size, System.Random rng)
    {
        for (int i = 0; i < rays; i++)
        {
            float ang = (float)(i / (double)rays * System.Math.PI * 2.0 + rng.NextDouble() * 0.3);
            Vector3 d = new Vector3(Mathf.Cos(ang), -0.35f - (float)rng.NextDouble() * 0.5f, Mathf.Sin(ang)).normalized;
            if (Physics.Raycast(origin, d, out var hit, range, 1, QueryTriggerInteraction.Ignore))
                Decal(hit.point, hit.normal, size * Mathf.Lerp(0.6f, 1.6f, (float)rng.NextDouble()), false);
        }
    }

    /// <summary>Slow, lingering red cloud.</summary>
    public static void Mist(Vector3 pos, int count)
    {
        var ps = NewSystem("BloodMist", pos, Quaternion.LookRotation(Vector3.up), GameAssets.I.bloodParticle, false);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.5f, 0f, 0f, 0.45f), new Color(0.3f, 0f, 0f, 0.3f));
        main.gravityModifier = 0.15f;
        main.maxParticles = 400;
        var sol = ps.sizeOverLifetime;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));
        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(new Gradient
        {
            colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) },
        });
        Emit(ps, (int)(count * Multiplier), 89f);
    }

    static void Sever(Transform[] parts, Rigidbody[] rbs, BodyModel.Part p, Vector3 dir, float impulse, System.Random rng)
    {
        var t = parts[(int)p];
        if (t == null || !t.gameObject.activeSelf) return;
        SeverJoint(t, dir, impulse, rng);
    }

    static void SeverJoint(Transform t, Vector3 dir, float impulse, System.Random rng)
    {
        var joint = t.GetComponent<CharacterJoint>();
        if (joint == null) return; // already detached
        var parent = joint.connectedBody != null ? joint.connectedBody.transform : null;
        Object.Destroy(joint);
        Vector3 jointPos = t.position;

        // Stump on the parent, torn end on the piece, both gushing.
        if (parent != null)
        {
            Vector3 outward = (jointPos - parent.position).normalized;
            Stump(parent, parent.InverseTransformPoint(jointPos), parent.InverseTransformDirection(outward));
            Fountain(parent, parent.InverseTransformPoint(jointPos), parent.InverseTransformDirection(outward), 7f, 60f, 1.1f);
        }
        Stump(t, Vector3.zero, Vector3.up);
        Fountain(t, Vector3.zero, Vector3.up, 3.5f, 35f, 0.8f);
        Burst(jointPos, dir + Vector3.up * 0.3f, (int)(80 * Multiplier), 2f, 9f, 55f, 0.08f, true);
        Gibs(jointPos, dir * 3f, (int)(8 * Multiplier), 5f, rng, boneShare: 0.25f);
        SplatterRays(jointPos, dir, 4, 5f, 0.6f, rng);

        var rb = t.GetComponent<Rigidbody>();
        float maxImpulse = rb.mass * 22f;
        Vector3 kick = dir * impulse * 0.35f + new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble(), (float)rng.NextDouble() - 0.5f) * impulse * 0.25f;
        rb.AddForce(Vector3.ClampMagnitude(kick, maxImpulse), ForceMode.Impulse);
        rb.AddTorque(Random.insideUnitSphere * 8f, ForceMode.Impulse);
        AddTrail(t.gameObject, 0.6f, 0.07f);
    }

    static void ExplodeHead(Transform[] parts, Rigidbody[] rbs, Vector3 dir, System.Random rng)
    {
        var head = parts[(int)BodyModel.Part.Head];
        if (head == null || !head.gameObject.activeSelf) return;
        Vector3 center = head.TransformPoint(0f, 0.15f, 0f);
        var chest = parts[(int)BodyModel.Part.Chest];
        head.gameObject.SetActive(false);
        Object.Destroy(head.gameObject, 0.1f);

        float m = Multiplier;
        Gibs(center, dir * 7f + Vector3.up * 2f, (int)(28 * m), 8f, rng, boneShare: 0.4f);
        Burst(center, dir, (int)(170 * m), 4f, 17f, 45f, 0.08f, true);
        Burst(center, Vector3.up, (int)(90 * m), 1f, 6f, 90f, 0.12f, false);   // red spray
        Mist(center, 50);
        SplatterRays(center, dir, 20, 10f, 1f, rng);
        Vector3 neck = chest.InverseTransformPoint(head.position);
        Stump(chest, neck, Vector3.up);
        Fountain(chest, neck, Vector3.up, 11f, 100f, 1.5f);
    }

    static void CutInHalf(Transform[] parts, Rigidbody[] rbs, Vector3 dir, float impulse, System.Random rng)
    {
        var chest = parts[(int)BodyModel.Part.Chest];
        var pelvis = parts[(int)BodyModel.Part.Pelvis];
        var joint = chest.GetComponent<CharacterJoint>();
        if (joint != null) Object.Destroy(joint);
        Vector3 waist = chest.position;
        float m = Multiplier;

        Stump(chest, Vector3.zero, Vector3.down, 0.3f);
        Stump(pelvis, pelvis.InverseTransformPoint(waist), Vector3.up, 0.3f);
        Fountain(chest, Vector3.zero, Vector3.down, 7f, 100f, 1.4f);
        Fountain(pelvis, pelvis.InverseTransformPoint(waist), Vector3.up, 7f, 100f, 1.4f);
        Gibs(waist, dir * 6f, (int)(38 * m), 9f, rng, boneShare: 0.25f);
        Burst(waist, dir, (int)(180 * m), 3f, 15f, 60f, 0.09f, true);
        Mist(waist, 50);
        Guts(rbs[(int)BodyModel.Part.Pelvis], waist, 18, rng);
        Guts(rbs[(int)BodyModel.Part.Chest], waist, 12, rng);
        SplatterRays(waist, dir, 18, 9f, 1.1f, rng);

        rbs[(int)BodyModel.Part.Chest].AddForce((dir * 0.5f + Vector3.up * 0.6f) * impulse * 0.3f, ForceMode.Impulse);
        rbs[(int)BodyModel.Part.Chest].AddTorque(Random.insideUnitSphere * 40f, ForceMode.Impulse);
    }

    // ------------------------------------------------------------------ hits on the living and on the world

    public static void FleshHit(Vector3 point, Vector3 dir, int damage)
    {
        float m = Multiplier;
        int amount = Mathf.Clamp(damage, 10, 60);
        Burst(point, -dir, (int)(amount * 0.3f * m), 1f, 3.5f, 40f, 0.04f, true);
        Burst(point, dir, (int)(amount * 0.8f * m), 2.5f, 7f, 20f, 0.05f, true);
        if (Physics.Raycast(point + dir * 0.6f, dir, out var hit, 4f, 1, QueryTriggerInteraction.Ignore))
            Decal(hit.point, hit.normal, Random.Range(0.35f, 0.8f), false);
    }

    public static void WorldHit(Vector3 point, Vector3 normal)
    {
        var a = GameAssets.I;
        var hole = MakeQuad("BulletHole", point, normal, Random.Range(0.05f, 0.08f), a.bulletHole, 0.003f);
        Track(holes, hole, MaxHoles);
        var ps = NewSystem("Dust", point, Quaternion.LookRotation(normal), a.dust, false);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.18f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.72f, 0.65f, 0.8f), new Color(0.5f, 0.48f, 0.45f, 0.6f));
        main.gravityModifier = 0.3f;
        Emit(ps, 10, 25f);
        ShotFx.Sparks(point + normal * 0.02f, normal, Random.Range(4, 9), 7f, 50f);
    }

    // ------------------------------------------------------------------ building blocks

    public static void Burst(Vector3 pos, Vector3 dir, int count, float minSpeed, float maxSpeed, float spread, float size, bool stretched)
    {
        if (count <= 0) return;
        var ps = NewSystem("Blood", pos, Quaternion.LookRotation(dir.sqrMagnitude > 0.001f ? dir : Vector3.up), GameAssets.I.bloodParticle, stretched);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size * 1.5f);
        main.startColor = new ParticleSystem.MinMaxGradient(BloodA, BloodB);
        main.gravityModifier = 1.6f;
        main.maxParticles = Mathf.Max(count, 10);
        AddCollision(ps, decalEvery: stretched ? 6 : 12);
        Emit(ps, count, spread);
    }

    public static void Fountain(Transform parent, Vector3 localPos, Vector3 localDir, float duration, float rate, float strength)
    {
        if (parent == null) return;
        var ps = NewSystem("BloodFountain", parent.TransformPoint(localPos), Quaternion.LookRotation(parent.TransformDirection(localDir)), GameAssets.I.bloodParticle, true);
        ps.transform.SetParent(parent, true);
        var main = ps.main;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f * strength, 4.5f * strength);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
        main.startColor = new ParticleSystem.MinMaxGradient(BloodA, BloodB);
        main.gravityModifier = 1.5f;
        main.maxParticles = 400;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 18f;
        shape.radius = 0.02f;
        var emission = ps.emission;
        emission.rateOverTime = rate * Multiplier;
        AddCollision(ps, decalEvery: 14);
        var f = ps.gameObject.AddComponent<BloodFountain>();
        f.duration = duration * 1.5f;
        f.baseRate = rate * Multiplier;
        ps.Play();
    }

    public static void Gibs(Vector3 pos, Vector3 velocity, int count, float scatter, System.Random rng, float boneShare = 0.12f)
    {
        var a = GameAssets.I;
        for (int i = 0; i < count; i++)
        {
            float r = (float)rng.NextDouble();
            bool isBone = r < boneShare;
            var go = GameObject.CreatePrimitive(isBone ? PrimitiveType.Cube : (rng.NextDouble() < 0.5 ? PrimitiveType.Sphere : PrimitiveType.Cube));
            go.name = "Gib";
            go.layer = GameAssets.DebrisLayer;
            float s = rng.NextDouble() < 0.15 ? Mathf.Lerp(0.14f, 0.24f, (float)rng.NextDouble()) : Mathf.Lerp(0.04f, 0.13f, (float)rng.NextDouble());
            go.transform.position = pos + Random.insideUnitSphere * 0.15f;
            go.transform.rotation = Random.rotation;
            go.transform.localScale = isBone ? new Vector3(s * 0.5f, s * 0.5f, s * 1.8f) : new Vector3(s, s * Mathf.Lerp(0.5f, 1.2f, (float)rng.NextDouble()), s * 0.9f);
            go.GetComponent<Renderer>().sharedMaterial = isBone ? a.bone : (rng.NextDouble() < 0.5 ? a.flesh : a.gore);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.2f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.linearVelocity = velocity + Random.insideUnitSphere * scatter + Vector3.up * scatter * 0.4f;
            rb.angularVelocity = Random.insideUnitSphere * 20f;
            if (rng.NextDouble() < 0.6) AddTrail(go, 0.35f, s * 0.8f);
            go.AddComponent<Gib>();
            Object.Destroy(go, Random.Range(25f, 35f));
            Track(gibs, go, MaxGibs);
        }
    }

    /// <summary>A dangling chain of intestine segments attached to a body.</summary>
    public static void Guts(Rigidbody anchor, Vector3 pos, int segments, System.Random rng)
    {
        var a = GameAssets.I;
        Rigidbody prev = anchor;
        Vector3 p = pos;
        for (int i = 0; i < segments; i++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Guts";
            go.layer = GameAssets.DebrisLayer;
            go.transform.position = p;
            go.transform.rotation = Quaternion.Euler((float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f, 0f);
            go.transform.localScale = new Vector3(0.06f, 0.06f, 0.06f);
            go.GetComponent<Renderer>().sharedMaterial = i % 3 == 0 ? a.gore : a.flesh;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.15f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.linearDamping = 0.3f;
            var joint = go.AddComponent<CharacterJoint>();
            joint.connectedBody = prev;
            joint.swing1Limit = new SoftJointLimit { limit = 70f };
            joint.swing2Limit = new SoftJointLimit { limit = 70f };
            joint.enableProjection = true;
            rb.linearVelocity = Random.insideUnitSphere * 3f;
            prev = rb;
            p += Random.onUnitSphere * 0.05f + Vector3.down * 0.06f;
            if (anchor != null) go.transform.SetParent(anchor.transform.parent, true);
        }
    }

    static void Stump(Transform parent, Vector3 localPos, Vector3 localNormal, float size = 0.14f)
    {
        var a = GameAssets.I;
        var meat = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.DestroyImmediate(meat.GetComponent<Collider>());
        meat.name = "Stump";
        meat.layer = GameAssets.DebrisLayer;
        meat.transform.SetParent(parent, false);
        meat.transform.localPosition = localPos;
        meat.transform.localRotation = Quaternion.FromToRotation(Vector3.up, localNormal);
        meat.transform.localScale = new Vector3(size, size * 0.45f, size);
        meat.GetComponent<Renderer>().sharedMaterial = a.gore;
        var boneGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.DestroyImmediate(boneGo.GetComponent<Collider>());
        boneGo.name = "Bone";
        boneGo.layer = GameAssets.DebrisLayer;
        boneGo.transform.SetParent(meat.transform, false);
        boneGo.transform.localPosition = new Vector3(0f, 0.6f, 0f);
        boneGo.transform.localScale = new Vector3(0.28f, 0.8f, 0.28f);
        boneGo.GetComponent<Renderer>().sharedMaterial = a.bone;
    }

    static void SplatterRays(Vector3 origin, Vector3 dir, int rays, float range, float size, System.Random rng)
    {
        for (int i = 0; i < rays; i++)
        {
            Vector3 d = (dir + new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.6f, (float)rng.NextDouble() - 0.5f) * 1.2f).normalized;
            if (Physics.Raycast(origin, d, out var hit, range, 1, QueryTriggerInteraction.Ignore))
                Decal(hit.point, hit.normal, size * Mathf.Lerp(0.5f, 1.4f, (float)rng.NextDouble()), false);
        }
        // Something always lands on the floor below.
        if (Physics.Raycast(origin, Vector3.down, out var floor, 4f, 1, QueryTriggerInteraction.Ignore))
            Decal(floor.point, floor.normal, size * 1.3f, false);
    }

    static int decalSerial;

    public static GameObject Decal(Vector3 point, Vector3 normal, float size, bool grow)
    {
        var go = MakeQuad("Blood", point, normal, size, GameAssets.I.bloodDecal, 0.004f + (decalSerial++ % 50) * 0.0002f);
        var block = new MaterialPropertyBlock();
        float v = Random.Range(0.6f, 1f);
        block.SetColor("_Color", new Color(v, v, v, Random.Range(0.85f, 1f)));
        go.GetComponent<Renderer>().SetPropertyBlock(block);
        if (grow) go.AddComponent<BloodPool>().targetScale = size;
        Track(decals, go, MaxDecals);
        return go;
    }

    static GameObject MakeQuad(string name, Vector3 point, Vector3 normal, float size, Material mat, float offset)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.name = name;
        go.layer = GameAssets.DebrisLayer;
        go.transform.SetPositionAndRotation(point + normal * offset, Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));
        go.transform.localScale = Vector3.one * size;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    static void AddTrail(GameObject go, float time, float width)
    {
        if (go.TryGetComponent<TrailRenderer>(out _)) return; // already trailing blood
        var trail = go.AddComponent<TrailRenderer>();
        trail.sharedMaterial = GameAssets.I.bloodParticle;
        trail.time = time;
        trail.startWidth = width;
        trail.endWidth = 0f;
        trail.startColor = BloodA;
        trail.endColor = new Color(BloodB.r, BloodB.g, BloodB.b, 0f);
        trail.minVertexDistance = 0.05f;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static ParticleSystem NewSystem(string name, Vector3 pos, Quaternion rot, Material mat, bool stretched)
    {
        var go = new GameObject(name);
        go.layer = GameAssets.DebrisLayer;
        go.transform.SetPositionAndRotation(pos, rot);
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
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (stretched)
        {
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.04f;
            r.lengthScale = 1.5f;
        }
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.4f));
        return ps;
    }

    static void Emit(ParticleSystem ps, int count, float spread)
    {
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = Mathf.Clamp(spread, 0f, 89f);
        shape.radius = 0.03f;
        var emission = ps.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Clamp(count, 1, 1000)) });
        ps.Play();
    }

    static void AddCollision(ParticleSystem ps, int decalEvery)
    {
        var col = ps.collision;
        col.enabled = true;
        col.type = ParticleSystemCollisionType.World;
        col.mode = ParticleSystemCollisionMode.Collision3D;
        col.collidesWith = 1; // world geometry only
        col.dampen = 0.8f;
        col.bounce = 0.05f;
        col.lifetimeLoss = 0.7f;
        col.quality = ParticleSystemCollisionQuality.Medium;
        col.sendCollisionMessages = true;
        ps.gameObject.AddComponent<BloodDrops>().decalEvery = decalEvery;
    }

    static void Track(Queue<GameObject> queue, GameObject go, int max)
    {
        queue.Enqueue(go);
        while (queue.Count > max)
        {
            var old = queue.Dequeue();
            if (old != null) Object.Destroy(old);
        }
    }

    public static void PlayAt(AudioClip clip, Vector3 pos, float volume)
    {
        var go = new GameObject("Sfx");
        go.transform.position = pos;
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.volume = volume;
        src.spatialBlend = 1f;
        src.minDistance = 4f;
        src.maxDistance = 60f;
        src.pitch = Random.Range(0.9f, 1.1f);
        src.Play();
        Object.Destroy(go, clip.length / Mathf.Max(0.1f, src.pitch) + 0.1f);
    }

    public static void ClearAll()
    {
        foreach (var q in new[] { decals, holes, gibs, corpses })
        {
            while (q.Count > 0)
            {
                var go = q.Dequeue();
                if (go != null) Object.Destroy(go);
            }
        }
        foreach (var f in Object.FindObjectsByType<BloodFountain>())
            Object.Destroy(f.gameObject);
    }
}
