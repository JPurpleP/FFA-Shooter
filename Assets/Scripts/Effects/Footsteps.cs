using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Footsteps for every player (yours, remote players, bots), timed by distance covered and sounding like what's
/// underfoot: snow crunch, ice, hollow wood, metal clank, grass swish, dirt / sand, hard ground. Heard in 3D, louder
/// when sprinting, quiet crouched, silent prone or sliding; a heavier step when landing.
/// </summary>
public class Footsteps : MonoBehaviour
{
    public enum Surface { Hard, Snow, Ice, Wood, Metal, Grass, Dirt }

    FpsPlayer player;
    AudioSource source;
    float travelled;
    bool wasGrounded = true;
    float airTime;
    static readonly RaycastHit[] hits = new RaycastHit[6];
    static readonly Dictionary<Surface, AudioClip[]> clips = new();
    static readonly Dictionary<Mesh, int[]> submeshStarts = new();
    static readonly bool logSteps = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-ffasteplog") >= 0; // tests

    public void Init(FpsPlayer p)
    {
        player = p;
        var go = new GameObject("Footsteps");
        go.transform.SetParent(p.transform, false);
        source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = p.IsLocalHuman ? 0.4f : 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 2f;
        source.maxDistance = 30f;
        source.dopplerLevel = 0f;
    }

    void Update()
    {
        if (player == null || !player.IsSpawned || player.IsDead || player.InLobby.Value || Application.isBatchMode) return;
        var v = player.Velocity;
        float speed = new Vector2(v.x, v.z).magnitude;
        bool grounded = Grounded(out var hit);
        if (!grounded) { airTime += Time.deltaTime; wasGrounded = false; return; }
        if (!wasGrounded && airTime > 0.35f) Play(hit, 1.15f); // landing
        wasGrounded = true;
        airTime = 0f;

        var stance = player.CurrentStance;
        if (stance == Stance.Prone || stance == Stance.Slide || speed < 0.8f) { travelled = 0f; return; }
        float stride = Mathf.Lerp(1.35f, 2.3f, Mathf.InverseLerp(2f, 9f, speed));
        travelled += speed * Time.deltaTime;
        if (travelled < stride) return;
        travelled = 0f;
        float loud = stance == Stance.Crouch ? 0.3f : Mathf.Lerp(0.55f, 1f, Mathf.InverseLerp(3f, 8f, speed));
        Play(hit, loud);
    }

    bool Grounded(out RaycastHit ground)
    {
        ground = default;
        Vector3 from = player.transform.position + Vector3.up * 0.3f;
        int n = Physics.RaycastNonAlloc(from, Vector3.down, hits, 0.55f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            if (hits[i].collider.transform.IsChildOf(player.transform)) continue;
            if (hits[i].distance < best) { best = hits[i].distance; ground = hits[i]; }
        }
        return best < float.MaxValue;
    }

    void Play(RaycastHit hit, float loudness)
    {
        var surface = SurfaceOf(hit);
        if (logSteps && player.IsLocalHuman) Debug.Log($"[FFA] Step {surface} ({hit.collider.name})");
        var set = Clips(surface);
        source.pitch = Random.Range(0.9f, 1.1f);
        source.PlayOneShot(set[Random.Range(0, set.Length)], loudness * (player.IsLocalHuman ? 0.45f : 0.9f));
    }

    // ------------------------------------------------------------------ what's underfoot

    public static Surface SurfaceOf(RaycastHit hit)
    {
        var col = hit.collider;
        if (col == null) return Surface.Hard;
        var r = col.GetComponent<Renderer>();
        if (r == null) return FromName(col.name);
        var mats = r.sharedMaterials;
        int index = 0;
        // Big ground meshes have one submesh per material: find the one under the triangle we hit.
        if (mats.Length > 1 && col is MeshCollider mc && mc.sharedMesh != null && hit.triangleIndex >= 0)
        {
            var starts = SubmeshStarts(mc.sharedMesh);
            int idx = hit.triangleIndex * 3;
            for (int i = starts.Length - 1; i >= 0; i--) if (idx >= starts[i]) { index = i; break; }
        }
        var m = index < mats.Length ? mats[index] : null;
        return m != null ? FromName(m.name) : FromName(col.name);
    }

    static int[] SubmeshStarts(Mesh mesh)
    {
        if (submeshStarts.TryGetValue(mesh, out var s)) return s;
        s = new int[mesh.subMeshCount];
        for (int i = 0; i < s.Length; i++) s[i] = (int)mesh.GetSubMesh(i).indexStart;
        submeshStarts[mesh] = s;
        return s;
    }

    static Surface FromName(string n)
    {
        n = n.ToLowerInvariant();
        if (n.Contains("ice")) return Surface.Ice;
        if (n.Contains("snow")) return Surface.Snow;
        if (n.Contains("metal") || n.Contains("steel") || n.Contains("grate") || n.Contains("manhole") || n.Contains("rail") || n.Contains("pad")) return Surface.Metal;
        if (n.Contains("grass") || n.Contains("lawn") || n.Contains("fern") || n.Contains("forest") || n.Contains("moss") || n.Contains("leaf") || n.Contains("needles") || n.Contains("hay")) return Surface.Grass;
        if (n.Contains("wood") || n.Contains("plank") || n.Contains("deck") || n.Contains("floor") || n.Contains("porch") || n.Contains("boardwalk") || n.Contains("log") || n.Contains("bark") || n.Contains("crate")) return Surface.Wood;
        if (n.Contains("sand") || n.Contains("dirt") || n.Contains("mud") || n.Contains("gravel") || n.Contains("trail") || n.Contains("path") || n.Contains("desert") || n.Contains("ground")) return Surface.Dirt;
        return Surface.Hard;
    }

    // ------------------------------------------------------------------ sounds (generated, three of each)

    static AudioClip[] Clips(Surface s)
    {
        if (clips.TryGetValue(s, out var c)) return c;
        c = new AudioClip[3];
        for (int i = 0; i < 3; i++) c[i] = Make(s, 101 + i * 17 + (int)s * 7);
        clips[s] = c;
        return c;
    }

    static AudioClip Make(Surface s, int seed)
    {
        const int rate = 44100;
        var rng = new System.Random(seed);
        float R() => (float)rng.NextDouble() * 2f - 1f;
        float len = s switch { Surface.Snow => 0.24f, Surface.Grass => 0.22f, Surface.Metal => 0.3f, Surface.Wood => 0.2f, _ => 0.16f };
        var d = new float[(int)(rate * len)];
        float lp = 0f, lp2 = 0f, hp = 0f, prev = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)rate;
            float n = R();
            lp += (n - lp) * 0.25f;
            lp2 += (lp - lp2) * 0.08f;
            hp = n - prev; prev = n;
            float v;
            switch (s)
            {
                case Surface.Snow:
                    // Dense crunch: many tiny granular bursts under a soft envelope.
                    float crunch = rng.NextDouble() < 0.06 ? R() * 2.5f : 0f;
                    v = (lp * 0.5f + crunch * 0.35f) * Mathf.Sin(Mathf.Clamp01(t / len) * Mathf.PI) * 0.9f;
                    break;
                case Surface.Ice:
                    v = (hp * 0.35f * Mathf.Exp(-t * 60f) + Mathf.Sin(t * 2f * Mathf.PI * 2400f) * 0.12f * Mathf.Exp(-t * 45f) + lp2 * Mathf.Exp(-t * 30f));
                    break;
                case Surface.Wood:
                    // Hollow knock: a low resonance plus the heel click.
                    v = Mathf.Sin(t * 2f * Mathf.PI * (140f + 30f * (seed % 3))) * Mathf.Exp(-t * 28f) * 0.8f + hp * 0.25f * Mathf.Exp(-t * 90f);
                    break;
                case Surface.Metal:
                    v = (Mathf.Sin(t * 2f * Mathf.PI * 620f) * 0.35f + Mathf.Sin(t * 2f * Mathf.PI * 1470f) * 0.25f + Mathf.Sin(t * 2f * Mathf.PI * 2310f) * 0.15f) * Mathf.Exp(-t * 16f)
                        + lp2 * Mathf.Exp(-t * 40f) * 1.5f;
                    break;
                case Surface.Grass:
                    v = hp * 0.22f * Mathf.Sin(Mathf.Clamp01(t / len) * Mathf.PI) + lp2 * 0.6f * Mathf.Exp(-t * 25f);
                    break;
                case Surface.Dirt:
                    float grit = rng.NextDouble() < 0.03 ? R() * 1.6f : 0f;
                    v = (lp * 0.45f + grit * 0.3f) * Mathf.Exp(-t * 22f) + lp2 * Mathf.Exp(-t * 35f);
                    break;
                default:
                    // Hard ground: a short heel tap and scuff.
                    v = hp * 0.3f * Mathf.Exp(-t * 70f) + lp * 0.4f * Mathf.Exp(-t * 30f) + lp2 * 1.2f * Mathf.Exp(-t * 50f);
                    break;
            }
            d[i] = Mathf.Clamp(v * Mathf.Clamp01(t / 0.004f), -1f, 1f);
        }
        var clip = AudioClip.Create($"step-{s}-{seed}", d.Length, 1, rate, false);
        clip.SetData(d, 0);
        return clip;
    }
}
