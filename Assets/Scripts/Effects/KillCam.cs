using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Killcam: every client keeps the last few seconds of everyone's position and view. When the local player is
/// killed by someone, the moments before the kill are replayed through the killer's eyes (with their gun) while a
/// stand-in of you walks your last steps; your corpse appears at the moment of death. Purely local, nothing networked.
/// </summary>
public class KillCam : MonoBehaviour
{
    const float Keep = 6f;          // seconds of history kept per player
    const float Lead = 2.6f;        // replay starts this long before the kill
    const float Tail = 0.3f;        // and runs a little past it
    const float StartDelay = 0.9f;  // see your own ragdoll first

    struct Sample
    {
        public float t;
        public Vector3 root, eye;
        public float yaw, pitch;
        public Quaternion eyeRot;
        public Stance stance;
        public float ads;
    }

    static KillCam instance;
    readonly Dictionary<FpsPlayer, List<Sample>> history = new();
    readonly List<FpsPlayer> gone = new();
    Coroutine playing;
    // What the current replay created or hid, so it can always be put back.
    GameObject camGo, ghostRoot, gunGo;
    FpsPlayer hiddenKiller;
    Transform hiddenCorpse;

    /// <summary>True while the replay is on screen.</summary>
    public static bool Active { get; private set; }
    public static string KillerName { get; private set; } = "";
    public static string WeaponName { get; private set; } = "";
    public static Color KillerColor { get; private set; } = Color.white;
    /// <summary>0..1 through the replay.</summary>
    public static float Progress { get; private set; }
    /// <summary>The replay is the match-winning kill (everyone watches it).</summary>
    public static bool IsFinal { get; private set; }
    /// <summary>The killer is looking through a scope in the replay right now (the HUD draws the scope).</summary>
    public static bool Scoped { get; private set; }
    /// <summary>0..1: how far down the sights the killer is in the replay.</summary>
    public static float AdsAmount { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        var go = new GameObject("KillCam") { hideFlags = HideFlags.HideInHierarchy };
        DontDestroyOnLoad(go);
        instance = go.AddComponent<KillCam>();
    }

    // ------------------------------------------------------------------ recording

    void LateUpdate()
    {
        float now = Time.time;
        foreach (var p in FpsPlayer.All)
        {
            if (p == null || !p.IsSpawned || p.Head == null) continue;
            if (!history.TryGetValue(p, out var list)) history[p] = list = new List<Sample>(512);
            // Remote players are drawn InterpDelay in the past; line everyone up on the same clock.
            float t = p.IsOwner ? now : now - FpsPlayer.InterpDelay;
            list.Add(new Sample
            {
                t = t, root = p.transform.position, yaw = p.transform.eulerAngles.y,
                pitch = -Mathf.Asin(Mathf.Clamp(p.Head.forward.y, -1f, 1f)) * Mathf.Rad2Deg,
                eye = p.Head.position, eyeRot = p.Head.rotation, stance = p.CurrentStance, ads = p.AimAmount,
            });
            int drop = 0;
            while (drop < list.Count && list[drop].t < now - Keep) drop++;
            if (drop > 0) list.RemoveRange(0, drop);
        }
        gone.Clear();
        foreach (var p in history.Keys)
            if (p == null || !FpsPlayer.All.Contains(p)) gone.Add(p);
        foreach (var p in gone) history.Remove(p);
    }

    static bool SampleAt(List<Sample> list, float t, out Sample s)
    {
        s = default;
        if (list == null || list.Count == 0) return false;
        if (t <= list[0].t) { s = list[0]; return true; }
        for (int i = 1; i < list.Count; i++)
        {
            if (list[i].t < t) continue;
            Sample a = list[i - 1], b = list[i];
            float f = Mathf.Clamp01((t - a.t) / Mathf.Max(0.0001f, b.t - a.t));
            s = new Sample
            {
                t = t, root = Vector3.Lerp(a.root, b.root, f), eye = Vector3.Lerp(a.eye, b.eye, f),
                yaw = Mathf.LerpAngle(a.yaw, b.yaw, f), pitch = Mathf.Lerp(a.pitch, b.pitch, f),
                eyeRot = Quaternion.Slerp(a.eyeRot, b.eyeRot, f), stance = f < 0.5f ? a.stance : b.stance, ads = Mathf.Lerp(a.ads, b.ads, f),
            };
            return true;
        }
        s = list[list.Count - 1];
        return true;
    }

    // ------------------------------------------------------------------ playback

    /// <summary>The match-winning kill: everyone watches it through the winner's eyes.</summary>
    public static void PlayFinal(FpsPlayer killer, FpsPlayer victim, WeaponId weapon, Transform corpse)
    {
        if (instance == null || victim == null || killer == null || killer == victim) return;
        instance.Stop();
        IsFinal = true;
        instance.playing = instance.StartCoroutine(instance.Run(victim, killer, weapon, corpse, Time.time));
    }

    /// <summary>The local player was just killed by `killer` (call on the victim's client).</summary>
    public static void Play(FpsPlayer victim, FpsPlayer killer, WeaponId weapon, Transform corpse)
    {
        if (instance == null || victim == null || killer == null || killer == victim) return;
        if (IsFinal && Active) return; // the final kill replay takes priority
        instance.Stop();
        IsFinal = false;
        instance.playing = instance.StartCoroutine(instance.Run(victim, killer, weapon, corpse, Time.time));
    }

    void Stop()
    {
        if (playing != null) StopCoroutine(playing);
        Cleanup();
    }

    void Cleanup()
    {
        playing = null;
        Active = false;
        Progress = 0f;
        IsFinal = false;
        Scoped = false;
        AdsAmount = 0f;
        if (hiddenKiller != null) hiddenKiller.SetHiddenForKillcam(false);
        if (hiddenCorpse != null) hiddenCorpse.gameObject.SetActive(true);
        if (ghostRoot != null) Destroy(ghostRoot);
        if (gunGo != null) Destroy(gunGo);
        if (camGo != null) camGo.SetActive(false); // kept for the next killcam (setting a camera up mid-match hitches)
        hiddenKiller = null;
        hiddenCorpse = null;
        ghostRoot = gunGo = null;
    }

    IEnumerator Run(FpsPlayer victim, FpsPlayer killer, WeaponId weapon, Transform corpse, float deathTime)
    {
        yield return new WaitForSeconds(StartDelay);
        if (victim == null || !victim.IsDead || killer == null || !killer.IsSpawned) { playing = null; yield break; }
        history.TryGetValue(killer, out var killerPath);
        history.TryGetValue(victim, out var victimPath);
        if (killerPath == null || killerPath.Count < 10) { playing = null; yield break; }

        float from = Mathf.Max(deathTime - Lead, killerPath[0].t), to = deathTime + Tail;
        var def = WeaponDefs.Get(weapon);
        KillerName = killer.DisplayName;
        KillerColor = killer.PlayerColor;
        WeaponName = def != null ? def.Name : "";

        // Camera through the killer's eyes, holding their gun at the hip.
        // One camera, made the first time and reused: building a camera plus its post-processing mid-match
        // stalled the frame (hundreds of ms in browsers).
        if (camGo == null)
        {
            camGo = new GameObject("KillCamCamera");
            camGo.AddComponent<Camera>();
        }
        camGo.SetActive(true);
        var cam = camGo.GetComponent<Camera>();
        if (victim.playerCamera != null)
        {
            cam.CopyFrom(victim.playerCamera);
            cam.depth = victim.playerCamera.depth + 2f;
        }
        cam.cullingMask &= ~(1 << GameAssets.ViewModelLayer);
        GraphicsQuality.AddTo(cam); // same color grading / AO as the game view (once)
        cam.fieldOfView = GameSettings.Fov;
        cam.nearClipPlane = 0.03f;
        Transform gunT = null;
        if (def != null && weapon != WeaponId.Fists && weapon != WeaponId.Melee)
        {
            var gun = WeaponModel.Build(weapon, cam.transform);
            gunGo = gun.gameObject;
            gunT = gun.transform;
            gun.transform.localPosition = def.HipOffset;
            foreach (var r in gun.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // A stand-in for you, walking your last steps; the real corpse appears when you die.
        BodyModel ghost = null;
        if (victimPath != null && victimPath.Count > 0)
        {
            ghostRoot = new GameObject("KillCamVictim");
            ghost = BodyModel.Build(ghostRoot.transform, victim.PlayerColor, false, victim.SkinId.Value);
        }
        if (corpse != null && corpse.gameObject.activeSelf)
        {
            hiddenCorpse = corpse;
            corpse.gameObject.SetActive(false);
        }
        hiddenKiller = killer;

        Active = true;
        float playhead = from, ads = 0f;
        while (playhead < to)
        {
            if (victim == null || (!victim.IsDead && !IsFinal)) break; // respawned: cut back to the game
            if (killer == null) break;
            killer.SetHiddenForKillcam(true);
            Progress = Mathf.InverseLerp(from, to, playhead);
            if (SampleAt(killerPath, playhead, out var k))
            {
                cam.transform.SetPositionAndRotation(k.eye, k.eyeRot);
                // Aiming down sights: the gun comes up to the eye and the view zooms, like the killer saw it.
                ads = Mathf.MoveTowards(ads, k.ads, Time.deltaTime * 6f);
                float eased = ads * ads * (3f - 2f * ads);
                AdsAmount = eased;
                if (def != null)
                {
                    cam.fieldOfView = Mathf.Lerp(GameSettings.Fov, def.AdsFov > 0f ? def.AdsFov : GameSettings.Fov, eased);
                    if (gunT != null) gunT.localPosition = Vector3.Lerp(def.HipOffset, new Vector3(0f, -def.SightHeight, def.AdsDistance), eased);
                    Scoped = def.HasScope && eased > 0.9f;
                    if (gunT != null) gunT.gameObject.SetActive(!Scoped);
                }
            }
            bool dead = playhead >= deathTime;
            if (ghostRoot != null)
            {
                ghostRoot.SetActive(!dead);
                if (!dead && SampleAt(victimPath, playhead, out var v))
                {
                    ghostRoot.transform.SetPositionAndRotation(v.root, Quaternion.Euler(0f, v.yaw, 0f));
                    ghost.crouch = v.stance == Stance.Crouch ? 1f : 0f;
                    ghost.prone = v.stance == Stance.Prone ? 1f : 0f;
                    ghost.slide = v.stance == Stance.Slide ? 1f : 0f;
                    ghost.aimPitch = v.pitch;
                }
            }
            if (dead && hiddenCorpse != null)
            {
                hiddenCorpse.gameObject.SetActive(true);
                hiddenCorpse = null;
            }
            yield return null;
            playhead += Time.deltaTime;
        }

        Cleanup();
    }
}
