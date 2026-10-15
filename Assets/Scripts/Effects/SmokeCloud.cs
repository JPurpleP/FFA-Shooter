using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Smoke grenade cloud: billows out to a ~5.5 m sphere, hangs for most of its life, then thins out. Purely visual for
/// players (it really hides you), and bots / the aimbot can't see through it (<see cref="Blocks"/>). Every client
/// spawns its own copy when the server says the grenade went off.
/// </summary>
public class SmokeCloud : MonoBehaviour
{
    static readonly List<SmokeCloud> active = new();

    float born;
    Vector3 center;

    public static void Create(Vector3 at)
    {
        var go = new GameObject("SmokeCloud");
        go.transform.position = at;
        var c = go.AddComponent<SmokeCloud>();
        c.born = Time.time;
        c.center = at + Vector3.up * 1.6f;
        c.Build();
        Gore.PlayAt(Sfx.Slide, at, 1f); // hiss as it vents
        Destroy(go, WeaponDefs.SmokeSeconds + 4f);
    }

    void OnEnable() => active.Add(this);
    void OnDisable() => active.Remove(this);

    /// <summary>Current radius: grows over the first 2 s, holds, then shrinks away over the last 3 s.</summary>
    float Radius
    {
        get
        {
            float age = Time.time - born, life = WeaponDefs.SmokeSeconds;
            float grow = Mathf.Clamp01(age / 2f), fade = Mathf.Clamp01((life - age) / 3f);
            return WeaponDefs.SmokeRadius * grow * fade;
        }
    }

    /// <summary>Does any smoke cloud hide the line from a to b?</summary>
    public static bool Blocks(Vector3 a, Vector3 b)
    {
        foreach (var c in active)
        {
            float r = c.Radius * 0.8f; // the thick core, not the wispy edge
            if (r < 0.5f) continue;
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(c.center - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
            if ((a + ab * t - c.center).sqrMagnitude < r * r) return true;
        }
        return false;
    }

    void Build()
    {
        var ps = new GameObject("Puffs").AddComponent<ParticleSystem>();
        ps.transform.SetParent(transform, false);
        ps.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // hemisphere opens upward
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false;
        main.duration = WeaponDefs.SmokeSeconds - 4f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 6.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(4.5f, 7.5f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.88f, 0.89f, 0.9f, 1f), new Color(0.74f, 0.76f, 0.78f, 1f));
        main.gravityModifier = -0.01f;
        main.maxParticles = 900;
        var em = ps.emission;
        em.rateOverTime = 40f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 120), new ParticleSystem.Burst(0.4f, 60) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 1.5f;
        var vel = ps.limitVelocityOverLifetime;
        vel.enabled = true;
        vel.limit = 0.5f;
        vel.dampen = 0.07f; // puffs shoot out, then hang in the air
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.5f));
        var rol = ps.rotationOverLifetime;
        rol.enabled = true;
        rol.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(new Gradient
        {
            colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            alphaKeys = new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) },
        });
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = GameAssets.I.dust;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.sortMode = ParticleSystemSortMode.Distance;
        ps.Play();
    }
}
