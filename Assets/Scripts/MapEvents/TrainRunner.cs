using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Dust Ridge: a freight train runs the south track every couple of minutes, alternating direction. Its position
/// comes from the shared server clock, so every player sees it in the same place without any network traffic.
/// It blows the horn on the way in, you can ride the flatcars, and it kills anyone standing in its way (server).
/// </summary>
[DefaultExecutionOrder(-50)] // moves before players, so riders are carried the same frame
public class TrainRunner : MonoBehaviour
{
    public const float Period = 120f, Speed = 15f, StartX = -190f, EndX = 190f, FirstPassAt = 45f;
    public float trackZ = -59f;

    static readonly List<TrainRunner> active = new();
    static readonly RaycastHit[] hits = new RaycastHit[8];

    /// <summary>The train moved this much this frame (riders move with it).</summary>
    public Vector3 FrameDelta { get; private set; }
    public bool Running { get; private set; }

    AudioSource horn, chug;
    Collider[] colliders;
    int hornedPass = -1, warnedPass = -1;
    Vector3 lastPos;

    void Awake()
    {
        colliders = GetComponentsInChildren<Collider>();
        Debug.Log($"[FFA] Train ready ({colliders.Length} colliders)");
        horn = gameObject.AddComponent<AudioSource>();
        horn.clip = MakeHorn();
        Spatial(horn, 260f);
        chug = gameObject.AddComponent<AudioSource>();
        chug.clip = MakeChug();
        chug.loop = true;
        Spatial(chug, 110f);
    }

    static void Spatial(AudioSource s, float range)
    {
        s.playOnAwake = false;
        s.spatialBlend = 1f;
        s.rolloffMode = AudioRolloffMode.Linear;
        s.minDistance = 12f;
        s.maxDistance = range;
        s.dopplerLevel = 0.6f;
    }

    void OnEnable() { active.Add(this); lastPos = transform.position; }
    void OnDisable() { active.Remove(this); FrameDelta = Vector3.zero; if (chug != null) chug.Stop(); }

    static double Now
    {
        get
        {
            var net = NetworkManager.Singleton;
            return net != null && net.IsListening ? net.ServerTime.Time : Time.timeAsDouble;
        }
    }

    void Update()
    {
        double t = Now - FirstPassAt;
        int pass = (int)System.Math.Floor(t / Period);
        float into = (float)(t - pass * (double)Period);
        float travel = (EndX - StartX) / Speed;
        bool east = (pass & 1) == 0;
        Running = t >= 0 && into < travel;
        float x = Running ? (east ? StartX + into * Speed : EndX - into * Speed) : 1000f;

        var pos = new Vector3(x, 0f, trackZ);
        transform.SetPositionAndRotation(Running ? pos : new Vector3(0f, -200f, 0f), Quaternion.Euler(0f, east ? 180f : 0f, 0f));
        FrameDelta = Running && lastPos.y > -100f ? transform.position - lastPos : Vector3.zero;
        lastPos = transform.position;

        if (Running && !chug.isPlaying) chug.Play();
        if (!Running && chug.isPlaying) chug.Stop();
        if (Running && hornedPass != pass) { hornedPass = pass; horn.Play(); Debug.Log($"[FFA] Train pass {pass} ({(east ? "east" : "west")}bound)"); }
        // Second blast as it reaches the edge of town.
        if (Running && Mathf.Abs(x) < 105f && Mathf.Abs(x) > 95f && !horn.isPlaying) horn.Play();

        // A heads-up for anyone near the tracks a few seconds before it arrives.
        float untilNext = into < travel ? 0f : Period - into;
        var me = FpsPlayer.Local;
        if (me != null && !me.IsDead && warnedPass != pass + 1 && untilNext > 0f && untilNext < 7f && me.transform.position.z < trackZ + 16f)
        {
            warnedPass = pass + 1;
            GameUI.ShowBanner("TRAIN COMING", "Get off the tracks (or hop on a flatcar).", new Color(1f, 0.8f, 0.35f));
        }

        if (Running) ServerRunOver(east ? Vector3.right : Vector3.left);
    }

    /// <summary>Server: anyone the train drives into is killed.</summary>
    void ServerRunOver(Vector3 dir)
    {
        var net = NetworkManager.Singleton;
        if (net == null || !net.IsServer) return;
        foreach (var p in FpsPlayer.All)
        {
            if (p == null || !p.IsSpawned || p.IsDead || p.InLobby.Value) continue;
            Vector3 knees = p.transform.position + Vector3.up * 0.5f, chest = p.transform.position + Vector3.up * 1.2f;
            bool struck = false;
            foreach (var c in colliders)
            {
                if (c == null || !c.enabled) continue;
                var b = c.bounds;
                b.Expand(0.25f);
                if (b.Contains(knees) || b.Contains(chest)) { struck = true; break; }
            }
            if (!struck) continue;
            p.TakeDamage(99999, null, new DeathInfo
            {
                Point = chest, Direction = dir, Train = true, Explosive = true, ExplosionCenter = chest - dir * 2f - Vector3.up * 0.5f,
                Seed = Random.Range(int.MinValue, int.MaxValue),
            });
        }
    }

    /// <summary>Owner movement: standing on a moving train? Then move with it.</summary>
    public static bool Carry(Vector3 feet, out Vector3 delta)
    {
        delta = Vector3.zero;
        foreach (var r in active)
        {
            if (!r.Running || r.FrameDelta == Vector3.zero) continue;
            if (Mathf.Abs(feet.z - r.trackZ) > 3f) continue;
            int n = Physics.RaycastNonAlloc(feet + Vector3.up * 0.3f, Vector3.down, hits, 0.75f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (hits[i].collider.transform.IsChildOf(r.transform))
                {
                    delta = r.FrameDelta;
                    return true;
                }
        }
        return false;
    }

    // ------------------------------------------------------------------ sounds (generated, like the rest)

    static AudioClip MakeHorn()
    {
        const int rate = 44100;
        float len = 2.2f;
        var d = new float[(int)(rate * len)];
        float[] hz = { 220f, 277.2f, 329.6f };
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)rate;
            float env = Mathf.Clamp01(t / 0.12f) * Mathf.Clamp01((len - t) / 0.35f);
            float s = 0f;
            foreach (float f in hz)
            {
                float ph = (t * f * (1f + 0.002f * Mathf.Sin(t * 5f))) % 1f;
                s += (ph < 0.5f ? 1f : -1f) * 0.35f + (ph * 2f - 1f) * 0.25f; // reedy square + saw
            }
            d[i] = s * env * 0.28f;
        }
        // Soften the edges a little.
        for (int i = 1; i < d.Length; i++) d[i] = d[i - 1] + (d[i] - d[i - 1]) * 0.35f;
        var clip = AudioClip.Create("train-horn", d.Length, 1, rate, false);
        clip.SetData(d, 0);
        return clip;
    }

    static AudioClip MakeChug()
    {
        const int rate = 44100;
        float len = 1f; // four chuffs a second, loops seamlessly
        var d = new float[(int)(rate * len)];
        var rng = new System.Random(7);
        float lp = 0f, rumble = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)rate;
            float beat = (t * 4f) % 1f;
            float chuff = Mathf.Exp(-beat * 9f);
            float n = (float)rng.NextDouble() * 2f - 1f;
            lp += (n - lp) * 0.08f;
            rumble += ((float)rng.NextDouble() * 2f - 1f - rumble) * 0.01f;
            float clack = (t % 0.5f) < 0.012f ? Mathf.Sin(t * 2f * Mathf.PI * 900f) * 0.5f : 0f;
            d[i] = lp * chuff * 0.9f + rumble * 2.2f + clack * 0.4f;
        }
        var clip = AudioClip.Create("train-chug", d.Length, 1, rate, false);
        clip.SetData(d, 0);
        return clip;
    }
}
