using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Two weapon slots with ammo plus slot 3, the throwables (a count per type and which type is selected).
/// Written by the owning client.
/// </summary>
public struct Inventory : INetworkSerializeByMemcpy
{
    public const int ThrowSlot = 2;
    public int W0, W1, M0, M1, R0, R1, Active;
    public int Frags, Smokes, Ropes, Molotovs, Throwable; // Throwable = selected type (index into WeaponDefs.Throwables)

    public static Inventory Default
    {
        get
        {
            var glock = WeaponDefs.Get(WeaponId.Glock17);
            return new Inventory { W0 = (int)WeaponId.Glock17, W1 = -1, M0 = glock.MagSize, R0 = glock.MagSize, Active = 0, Frags = 2 };
        }
    }

    public int ThrowCount(int type) => type switch { 0 => Frags, 1 => Smokes, 2 => Ropes, 3 => Molotovs, _ => 0 };
    public void SetThrowCount(int type, int n)
    {
        switch (type) { case 0: Frags = n; break; case 1: Smokes = n; break; case 2: Ropes = n; break; case 3: Molotovs = n; break; }
    }
    public int TotalThrowables => Frags + Smokes + Ropes + Molotovs;
    /// <summary>The next throwable type you have any of, cycling on from `from` (or -1 if none).</summary>
    public int NextThrowable(int from)
    {
        for (int i = 1; i <= 4; i++)
        {
            int t = (from + i) % 4;
            if (ThrowCount(t) > 0) return t;
        }
        return -1;
    }

    public int Weapon(int slot) => slot == 0 ? W0 : slot == 1 ? W1 : ThrowCount(Throwable) > 0 ? (int)WeaponDefs.Throwables[Throwable] : -1;
    public int Mag(int slot) => slot == 0 ? M0 : slot == 1 ? M1 : ThrowCount(Throwable);
    public int Reserve(int slot) => slot == 0 ? R0 : slot == 1 ? R1 : 0;
    public void Set(int slot, int weapon, int mag, int reserve)
    {
        if (slot == 0) { W0 = weapon; M0 = mag; R0 = reserve; }
        else { W1 = weapon; M1 = mag; R1 = reserve; }
    }
    public void SetMag(int slot, int mag) { if (slot == 0) M0 = mag; else M1 = mag; }
    public void SetReserve(int slot, int r) { if (slot == 0) R0 = r; else R1 = r; }
    public int SlotOf(int weapon) => W0 == weapon ? 0 : W1 == weapon ? 1 : -1;
    public int EmptySlot => W0 < 0 ? 0 : W1 < 0 ? 1 : -1;
    public bool Same(Inventory o) => W0 == o.W0 && W1 == o.W1 && M0 == o.M0 && M1 == o.M1 && R0 == o.R0 && R1 == o.R1 && Active == o.Active
                                     && Frags == o.Frags && Smokes == o.Smokes && Ropes == o.Ropes && Molotovs == o.Molotovs && Throwable == o.Throwable;
}

public enum Stance : byte { Stand, Crouch, Prone, Slide }

/// <summary>Per-frame control intent, from the keyboard/mouse or from a BotBrain.</summary>
public struct PlayerInput
{
    public Vector2 Move, Look;
    public bool AbsoluteLook;
    public float Yaw, Pitch;
    public bool Sprint, Jump, JumpHeld, FireHeld, FirePressed, Aim, Reload, Interact;
    public float Lean; // -1 = left, +1 = right
    public bool Grapple; // fire / release the grappling hook
    public bool CrouchDown, CrouchHeld, ProneToggle; // Ctrl (slide / crouch), Z (prone)
    public bool GrenadeHeld; // T: hold to cook, release to throw
    public int SwitchTo; // -1 none, 0/1/2 = slot (2 = throwables; again = next type), 3 = next slot, 4 = previous slot
}

/// <summary>
/// Networked first-person player. Movement, aim and inventory are owner-authoritative; hits, damage,
/// kills, pickups and respawns are decided by the server. Bots are server-owned instances driven by BotBrain.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FpsPlayer : NetworkBehaviour
{
    public const int MaxHealth = 100;
    public const int KillHeal = 40; // health back for each kill
    public const float RespawnDelay = 4f;
    public const float BaseFov = 75f;

    public static readonly List<FpsPlayer> All = new();
    public static FpsPlayer Local { get; private set; }

    [Header("References")]
    public Transform head;
    public Camera playerCamera;
    public Camera viewModelCamera;
    public AudioListener audioListener;
    public AudioSource audioSource;

    [Header("Movement")]
    public float walkSpeed = 6f;
    public float sprintSpeed = 9f;
    public float jumpHeight = 1.2f;
    public float gravity = -20f;
    public float mouseSensitivity = 0.1f;

    public readonly NetworkVariable<int> Health = new(MaxHealth);
    public readonly NetworkVariable<int> Kills = new();
    public readonly NetworkVariable<int> Deaths = new();
    public readonly NetworkVariable<bool> IsBot = new();
    // Killstreaks: kills since your last death, each reward lasting 30 s:
    // 3 = speed hack (double move speed), 5 = ESP, 8 = flyhack, 10 = aimbot.
    public const int SpeedStreak = 3, EspStreak = 5, FlyStreak = 8, AimbotStreak = 10;
    public const float SpeedHackMultiplier = 2f, FlyCeiling = 45f;
    public const float FlySpeed = 13.5f;                // climb / sink speed while flying (1.5x the original 9 m/s)
    public const float FlyHorizontalMultiplier = 2f;    // flying doubles horizontal speed (stacks with the speed hack)
    public const float StreakRewardSeconds = 30f;
    public readonly NetworkVariable<int> Streak = new();
    public readonly NetworkVariable<double> EspUntil = new();      // server time the ESP runs out
    public readonly NetworkVariable<double> AimbotUntil = new();   // server time the aimbot runs out
    public readonly NetworkVariable<double> SpeedUntil = new();    // server time the speed hack runs out
    public readonly NetworkVariable<double> FlyUntil = new();      // server time the flyhack runs out
    double ServerNow => NetworkManager != null ? NetworkManager.ServerTime.Time : 0.0;
    public bool EspActive => IsSpawned && ServerNow < EspUntil.Value;
    public bool AimbotActive => IsSpawned && ServerNow < AimbotUntil.Value;
    public float EspSecondsLeft => EspActive ? (float)(EspUntil.Value - ServerNow) : 0f;
    public float AimbotSecondsLeft => AimbotActive ? (float)(AimbotUntil.Value - ServerNow) : 0f;
    public bool SpeedActive => IsSpawned && ServerNow < SpeedUntil.Value;
    public bool FlyActive => IsSpawned && ServerNow < FlyUntil.Value;
    public float SpeedSecondsLeft => SpeedActive ? (float)(SpeedUntil.Value - ServerNow) : 0f;
    public float FlySecondsLeft => FlyActive ? (float)(FlyUntil.Value - ServerNow) : 0f;
    /// <summary>Flying right now (flyhack, alive, not in a duel; humans only, bots stay on their nav graph).</summary>
    bool Flying => FlyActive && !IsDead && !InDuel && brain == null;
    // Spawn protection: can't be hurt for a few seconds after spawning, until you move or fire.
    public const float SpawnProtectSeconds = 3f;
    public readonly NetworkVariable<bool> Protected = new(); // server written; off when it runs out or is broken
    // Rope grenade: tethered to where it went off until this server time (free to move inside the rope's reach).
    public readonly NetworkVariable<double> SnaredUntil = new();
    public readonly NetworkVariable<Vector3> SnareAnchor = new();
    public readonly NetworkVariable<float> SnareStartDistance = new(); // how far out they were when caught
    public bool Snared => IsSpawned && !IsDead && ServerNow < SnaredUntil.Value;
    public float SnareSecondsLeft => Snared ? (float)(SnaredUntil.Value - ServerNow) : 0f;
    public bool SpawnProtected => IsSpawned && !IsDead && Protected.Value;
    public float SpawnProtectLeft => SpawnProtected ? Mathf.Max(0f, SpawnProtectSeconds - (Time.time - protectStartedLocal)) : 0f;
    bool protectionDropSent;
    float protectEndsAt;       // server: Time.time protection runs out
    float protectStartedLocal; // every client: when it switched on (for the countdown)
    public BodyModel Body => body;
    /// <summary>Where the aimbot points: the middle of the head hitbox.</summary>
    public Vector3 AimPoint => head.position + head.rotation * new Vector3(0f, 0.1f, 0f);

    /// <summary>Carrying the grappling hook (picked up; lost on death). Server written.</summary>
    public readonly NetworkVariable<bool> HasGrapple = new();
    /// <summary>0 = not duelling, else golden-duel ring index + 1 (server written).</summary>
    public readonly NetworkVariable<byte> DuelSlot = new();
    /// <summary>Waiting in the pre-match lobby: out of the world, can't be hit (server written).</summary>
    public readonly NetworkVariable<bool> InLobby = new();
    /// <summary>Kills to win this match (server written, mirrored on every player so clients can show it).</summary>
    public readonly NetworkVariable<byte> MatchFragLimit = new();
    /// <summary>Readied up in the lobby (bots are always ready).</summary>
    public readonly NetworkVariable<bool> Ready = new(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public bool IsHostPlayer => !botOnServer && !IsBot.Value && OwnerClientId == NetworkManager.ServerClientId;
    /// <summary>The host's map, fixed when the object is created; joining clients switch to it on spawn.</summary>
    public readonly NetworkVariable<byte> MapId = new(MapCatalog.CurrentByte);
    public readonly NetworkVariable<byte> SkinId = new(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public readonly NetworkVariable<FixedString32Bytes> PlayerName = new(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<Vector3> netPosition = new(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<Vector3> netLook = new(default, // pitch, yaw, lean
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<byte> netStance = new(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<Vector4> netGrapple = new(default, // xyz = anchor, w = 1 while hooked
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<Inventory> netInventory = new(Inventory.Default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public bool IsDead => Health.Value <= 0;
    public bool InDuel => DuelSlot.Value > 0;
    /// <summary>Local time this player's current duel began (every client).</summary>
    public float DuelStartedAt { get; private set; }
    public FpsPlayer DuelOpponent
    {
        get
        {
            if (!InDuel) return null;
            foreach (var p in All)
                if (p != this && p.IsSpawned && p.DuelSlot.Value == DuelSlot.Value) return p;
            return null;
        }
    }
    public bool IsLocalHuman => IsOwner && !botOnServer;
    public string DisplayName => PlayerName.Value.Length > 0 ? PlayerName.Value.ToString() : $"Player {OwnerClientId}";
    public Color PlayerColor => Color.HSVToRGB(NetworkObjectId * 0.618034f % 1f, 0.65f, 0.9f);
    public Inventory Inv => IsOwner ? inv : netInventory.Value;
    public WeaponDef CurrentDef => WeaponDefs.Get(Inv.Weapon(Inv.Active));
    public int CurrentMag => Inv.Mag(Inv.Active);
    public int CurrentReserve => Inv.Reserve(Inv.Active);
    public bool IsReloading => reloading;
    public float ReloadProgress => reloading ? Mathf.Clamp01(1f - (reloadEnd - Time.time) / Mathf.Max(0.01f, reloadDuration)) : 1f;
    public float AdsAmount => adsT;
    /// <summary>ADS blend with ease-in/out (weapon position and zoom); adsT itself stays linear for logic.</summary>
    float AdsEased => adsT * adsT * (3f - 2f * adsT);
    public bool IsScoped => adsT > 0.9f && CurrentDef != null && CurrentDef.HasScope && !IsDead;
    public float Lean => lean;
    public Stance CurrentStance => IsOwner ? (sliding ? Stance.Slide : stance) : (Stance)netStance.Value;
    public bool IsGrappling => grappling;
    public bool GrappleAimValid { get; private set; }
    public float GrappleCooldownLeft => DevSettings.NoGrappleCooldown ? 0f : Mathf.Max(0f, grappleReadyAt - Time.time);
    public float Yaw => yaw;
    public float Pitch => pitch;
    public bool Grounded => controller.isGrounded;
    public Vector3 Velocity { get; private set; }
    public Transform Head => head;
    public BotBrain Brain => brain;
    public WeaponPickup NearbyPickup { get; private set; }
    public string LastKillerName { get; private set; } = "";
    public string LastDeathDetail { get; private set; } = "";

    // Server-only: set before spawning bots.
    bool botOnServer;
    BotBrain brain;

    CharacterController controller;
    BodyModel body;
    int builtSkin = -1;
    FirstPersonArms fpArms;
    SphereCollider headHitbox;
    Transform weaponRig;
    WeaponModel weaponModel;
    int builtWeapon = -2;

    // Owner state
    Inventory inv = Inventory.Default;
    float pitch, yaw, verticalVelocity, recoilOffset, bloom, adsT, viewKick, lean;

    // Stances: crouch (tap Ctrl / Ctrl while slow), slide (hold Ctrl while sprinting), prone (Z)
    Stance stance;
    bool sliding;
    Vector3 slideDir;
    float slideSpeed, slideStart;
    float headHeight = 1.6f, crouchT, proneT, slideT; // smoothed for camera and body pose (all clients)
    float headHeightVel, crouchVel, proneVel, slideVel, leanVel, airT, airVel, airTimer; // SmoothDamp state
    float slideStartSpeed;
    bool airSlideQueued;          // Ctrl held in the air: land straight into a slide that keeps your speed
    Vector3 airHorizontal;        // horizontal velocity while airborne, carried into that landing slide
    bool wasGroundedOwner = true;
    float airVerticalSpeed, landTime = -10f, landAmount;
    Vector2 sway;
    float lastSwayYaw, lastSwayPitch, smoothBob, bobPhase;

    // Remote players are drawn slightly in the past, interpolating between received snapshots (smooth at any tick rate).
    struct NetSnap { public float t; public Vector3 pos, look; }
    readonly List<NetSnap> snaps = new();
    public const float InterpDelay = 0.1f;
    Stance appliedShape = (Stance)255;

    // Grappling hook (owner state; anchor is mirrored in netGrapple for everyone's rope visuals)
    public const float GrappleRange = 30f, GrappleSpeed = 24f, GrappleAccel = 70f, GrappleCooldown = 2.5f, RopeShootTime = 0.12f;
    bool grappling;
    Vector3 grappleAnchor, momentum;
    float grappleStart, grappleReadyAt;
    LineRenderer rope;
    Transform hookVisual;
    bool ropeWasActive;
    float ropeShownAt;
    float nextFireTime, drawEndTime, reloadEnd, reloadDuration;
    bool reloading, triggerWasDown;
    bool hasNetPosition;
    Transform deathCamTarget;
    Vector3 deathCamDir;
    Vector3 lastPosition;

    // Server state
    Coroutine respawnRoutine;
    float serverNextFire;

    static readonly RaycastHit[] hitBuffer = new RaycastHit[32];
    static readonly Queue<GameObject> shells = new();

    // ------------------------------------------------------------------ spawning

    /// <summary>Server: spawn a bot with the given behaviour.</summary>
    public static FpsPlayer SpawnBot(BotMode mode, string name)
    {
        var go = Instantiate(GameAssets.I.playerPrefab);
        var p = go.GetComponent<FpsPlayer>();
        p.botOnServer = true;
        p.brain = go.AddComponent<BotBrain>();
        p.brain.mode = mode;
        p.brain.botName = name;
        go.GetComponent<NetworkObject>().Spawn(true);
        return p;
    }

    /// <summary>Let a BotBrain drive this (human) player, for automated tests.</summary>
    public void AttachTestBrain(BotMode mode)
    {
        brain = gameObject.AddComponent<BotBrain>();
        brain.mode = mode;
    }

    void Awake() => controller = GetComponent<CharacterController>();

    public override void OnNetworkSpawn()
    {
        if (!IsServer) MapCatalog.Apply(MapId.Value);
        // The host can switch maps in the lobby: follow it.
        MapId.OnValueChanged += (_, map) =>
        {
            if (IsServer || map == MapCatalog.Current) return;
            MapCatalog.Apply(map);
            Gore.ClearAll();
        };
        All.Add(this);
        Health.OnValueChanged += OnHealthChanged;
        netPosition.OnValueChanged += (_, _) => { hasNetPosition = true; PushSnap(); };
        netLook.OnValueChanged += (_, _) => PushSnap();
        hasNetPosition = netPosition.Value != Vector3.zero;

        int skin = IsOwner ? (botOnServer ? Random.Range(0, Skins.Count) : GameSettings.Skin) : SkinId.Value;
        if (IsOwner) SkinId.Value = (byte)skin;
        body = BodyModel.Build(transform, PlayerColor, false, skin);
        builtSkin = skin;
        SkinId.OnValueChanged += (_, now) => RebuildBody(now);
        DuelSlot.OnValueChanged += OnDuelSlotChanged;
        if (InDuel) DuelStartedAt = Time.time;
        weaponRig = new GameObject("WeaponRig").transform;
        weaponRig.SetParent(head, false);

        // Head hitbox follows the head pivot, so a head leaning out of cover can be shot.
        var hb = new GameObject("HeadHitbox") { layer = GameAssets.HitboxLayer };
        hb.transform.SetParent(head, false);
        headHitbox = hb.AddComponent<SphereCollider>();
        headHitbox.center = new Vector3(0f, 0.1f, 0f);
        headHitbox.radius = 0.17f;

        if (IsServer && botOnServer)
            IsBot.Value = true;
        if (IsServer)
        {
            InLobby.Value = MatchRules.LobbyOpen;
            MatchFragLimit.Value = (byte)MatchRules.FragLimit;
        }
        InLobby.OnValueChanged += OnLobbyChanged;
        Protected.OnValueChanged += (_, on) => { if (on) protectStartedLocal = Time.time; };
        if (Protected.Value) protectStartedLocal = Time.time;
        Ready.OnValueChanged += (_, now) => { if (IsServer && InLobby.Value) Debug.Log($"[FFA] {DisplayName} is {(now ? "ready" : "not ready")}"); };

        if (IsOwner)
        {
            Ready.Value = botOnServer;
            PlayerName.Value = new FixedString32Bytes(botOnServer ? brain.botName : GameUI.LocalPlayerName);
            inv = Inventory.Default;
            yaw = transform.eulerAngles.y;
        }
        if (IsLocalHuman)
        {
            Local = this;
            playerCamera.enabled = true;
            viewModelCamera.enabled = true;
            audioListener.enabled = true;
            audioSource.spatialBlend = 0f;
            body.ConfigureFirstPerson();
            fpArms = FirstPersonArms.Build(head, Skins.Get(skin));
            if (GameUI.AutoTest) AttachTestBrain(BotMode.Fighter);
        }
        else
        {
            playerCamera.enabled = false;
            viewModelCamera.enabled = false;
            audioListener.enabled = false;
        }

        if (IsServer && !InLobby.Value)
            Respawn();

        ApplyAliveState();
        ApplyLobbyView();
        lastPosition = transform.position;
        Debug.Log($"[FFA] {(botOnServer ? "Bot" : "Player")} {DisplayName} spawned (owner={IsOwner}, server={IsServer})");
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && !NetworkManager.ShutdownInProgress) DuelManager.Abort(this);
        DuelSlot.OnValueChanged -= OnDuelSlotChanged;
        InLobby.OnValueChanged -= OnLobbyChanged;
        All.Remove(this);
        Health.OnValueChanged -= OnHealthChanged;
        if (Local == this) Local = null;
    }

    // ------------------------------------------------------------------ per frame

    void Update()
    {
        if (!IsSpawned) return;
        if (IsServer && Protected.Value && Time.time >= protectEndsAt) Protected.Value = false;
        if (IsOwner) OwnerUpdate();
        else RemoteUpdate();

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        Velocity = (transform.position - lastPosition) / dt;
        lastPosition = transform.position;
        UpdatePoseWeights();
        UpdateWeaponVisual();
        UpdateRopeVisual();
        UpdateSnareVisual();
    }

    void LateUpdate()
    {
        if (!IsSpawned || !IsLocalHuman) return;
        if (IsDead && deathCamTarget != null)
        {
            var cam = playerCamera.transform;
            Vector3 want = deathCamTarget.position - deathCamDir * 3.5f + Vector3.up * 2f;
            cam.position = Vector3.Lerp(cam.position, want, 1f - Mathf.Exp(-4f * Time.deltaTime));
            cam.rotation = Quaternion.Slerp(cam.rotation, Quaternion.LookRotation(deathCamTarget.position - cam.position), 1f - Mathf.Exp(-8f * Time.deltaTime));
        }
    }

    void OwnerUpdate()
    {
        if (InLobby.Value)
        {
            if (grappling) ReleaseGrapple(false);
            return;
        }
        var input = brain != null ? brain.Think(this) : ReadHumanInput();
        bool alive = !IsDead;
        if (alive && !protectionDropSent && SpawnProtected
            && (input.Move.sqrMagnitude > 0.01f || input.Jump || input.FireHeld || input.FirePressed || input.Grapple || input.GrenadeHeld))
        {
            protectionDropSent = true; // moving or attacking ends spawn protection
            DropProtectionRpc();
        }
        if (InDuel)
        {
            // Golden duel: fists only. No guns, grenades, hook, pickups or aiming.
            input.Grapple = input.GrenadeHeld = input.Interact = input.Aim = input.Reload = false;
            input.SwitchTo = -1;
            if (grappling) ReleaseGrapple(false);
        }
        var def = InDuel ? null : CurrentDef;

        // Look
        float fov = Mathf.Lerp(GameSettings.Fov, def != null ? def.AdsFov : GameSettings.Fov, adsT);
        if (input.AbsoluteLook)
        {
            yaw = input.Yaw;
            pitch = Mathf.Clamp(input.Pitch, -89f, 89f);
        }
        else if (alive)
        {
            float sens = GameSettings.Sensitivity * (fov / GameSettings.Fov) * Mathf.Lerp(1f, GameSettings.AdsSensitivity, adsT);
            yaw += input.Look.x * sens;
            pitch = Mathf.Clamp(pitch + (GameSettings.InvertY ? 1f : -1f) * input.Look.y * sens, -89f, 89f);
        }
        aimbotLock = null;
        if (alive && AimbotActive)
        {
            // Humans: the visible enemy nearest the crosshair. Bots: whoever the brain is fighting (if still in sight).
            var lockOn = IsLocalHuman ? AimbotTarget() : brain != null && brain.CurrentTarget != null && CanAimbotSee(brain.CurrentTarget) ? brain.CurrentTarget : null;
            aimbotLock = lockOn;
            if (lockOn != null && IsLocalHuman)
            {
                // Locked: aim straight at their head (recoil included, so the shot lands there).
                Vector3 to = lockOn.AimPoint - head.position;
                yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                pitch = Mathf.Clamp(-Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg - recoilOffset, -89f, 89f);
            }
        }
        recoilOffset = Mathf.MoveTowards(recoilOffset, 0f, Time.deltaTime * (12f + Mathf.Abs(recoilOffset) * 6f));
        bloom = Mathf.MoveTowards(bloom, 0f, Time.deltaTime * 4f);
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        float wantLean = alive && !sliding && stance != Stance.Prone ? Mathf.Clamp(input.Lean, -1f, 1f) : 0f;
        lean = Mathf.SmoothDamp(lean, LimitLean(wantLean), ref leanVel, 0.09f);
        ApplyHeadPose(Mathf.Clamp(pitch + recoilOffset, -89f, 89f), lean);

        // Move
        if (alive && controller.enabled)
        {
            if (Snared)
            {
                // Tethered: move freely inside the rope's reach (enforced after the move below), but no hooking out of it.
                input.Grapple = false;
                if (grappling) ReleaseGrapple(false);
            }
            if (Flying)
            {
                if (sliding) EndSlide(false);
                if (stance != Stance.Stand) TrySetStance(Stance.Stand);
            }
            else
                UpdateStance(input);
            float speedScale = def != null ? def.MoveSpeedScale : 1f;
            speedScale *= Mathf.Lerp(1f, 0.55f, adsT) * Mathf.Lerp(1f, 0.6f, Mathf.Abs(lean));
            speedScale *= stance == Stance.Prone ? 0.24f : stance == Stance.Crouch ? 0.55f : 1f;
            bool sprint = input.Sprint && input.Move.y > 0.1f && adsT < 0.2f && stance == Stance.Stand;
            Vector3 move = transform.TransformDirection(Vector3.ClampMagnitude(new Vector3(input.Move.x, 0f, input.Move.y), 1f))
                           * (sprint ? sprintSpeed : walkSpeed) * speedScale;
            if (SpeedActive && !InDuel) move *= SpeedHackMultiplier; // speed hack (slides inherit the doubled speed)
            if (Flying) move *= FlyHorizontalMultiplier;
            if (sliding)
                move = slideDir * slideSpeed + move * 0.12f; // momentum carries you; a touch of steering
            UpdateGrapple(input, ref move);
            if (Flying && !grappling)
            {
                // Flyhack: no gravity; Space rises, Ctrl sinks. Kept under a ceiling and inside the map.
                float lift = (input.JumpHeld ? 1f : 0f) - (input.CrouchHeld ? 1f : 0f);
                verticalVelocity = Mathf.MoveTowards(verticalVelocity, lift * FlySpeed, 40f * Time.deltaTime);
                if (transform.position.y > FlyCeiling && verticalVelocity > 0f) verticalVelocity = 0f;
                var b = BotNav.Bounds;
                Vector3 p = transform.position, flat = move + momentum;
                if ((p.x < b.xMin && flat.x < 0f) || (p.x > b.xMax && flat.x > 0f)) { move.x = 0f; momentum.x = 0f; }
                if ((p.z < b.yMin && flat.z < 0f) || (p.z > b.yMax && flat.z > 0f)) { move.z = 0f; momentum.z = 0f; }
                momentum = Vector3.MoveTowards(momentum, Vector3.zero, 6f * Time.deltaTime);
            }
            else if (!grappling)
            {
                if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
                if (input.Jump && controller.isGrounded)
                {
                    bool wasSliding = sliding;
                    if (sliding) EndSlide(false);
                    if (stance != Stance.Stand) TrySetStance(Stance.Stand);
                    else verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                    if (wasSliding) { momentum += slideDir * slideSpeed * 0.6f; slideSpeed = 0f; } // slide-jump keeps the speed
                }
                verticalVelocity += gravity * Time.deltaTime;
                // Leftover swing momentum bleeds off quickly on the ground, slowly in the air.
                momentum = Vector3.MoveTowards(momentum, Vector3.zero, (controller.isGrounded ? 30f : 2.5f) * Time.deltaTime);
            }
            var flags = controller.Move((move + momentum + Vector3.up * verticalVelocity) * Time.deltaTime);
            if (Snared) EnforceTether();
            bool groundedNow = controller.isGrounded;
            if (groundedNow && !wasGroundedOwner && airVerticalSpeed < -4f)
            {
                landAmount = Mathf.Clamp01(-airVerticalSpeed / 18f); // camera and weapon dip on landing
                landTime = Time.time;
            }
            if (!groundedNow) airVerticalSpeed = verticalVelocity;
            wasGroundedOwner = groundedNow;
            if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f) verticalVelocity = 0f;
            if ((flags & CollisionFlags.Sides) != 0) momentum = Vector3.MoveTowards(momentum, Vector3.zero, 20f * Time.deltaTime);
            if (transform.position.y < -30f) RequestRespawnRpc();
        }

        if (!alive && grappling) ReleaseGrapple(false);
        GrappleAimValid = IsLocalHuman && alive && HasGrapple.Value && !grappling && GrappleTarget(out _);
        netGrapple.Value = grappling ? new Vector4(grappleAnchor.x, grappleAnchor.y, grappleAnchor.z, 1f) : Vector4.zero;

        NearbyPickup = alive ? WeaponPickup.Nearest(head.position, 2.3f) : null;
        if (alive && input.Interact && NearbyPickup != null)
            InteractRpc(NearbyPickup.NetworkObjectId);

        UpdateWeapon(input, alive);
        UpdateGrenade(input, alive);

        if (IsLocalHuman)
        {
            playerCamera.fieldOfView = Mathf.Lerp(GameSettings.Fov, CurrentDef != null ? CurrentDef.AdsFov : GameSettings.Fov, AdsEased);
            viewModelCamera.cullingMask = IsScoped || IsDead ? 0 : 1 << GameAssets.ViewModelLayer;
        }

        netPosition.Value = transform.position;
        netLook.Value = new Vector3(pitch, yaw, lean);
        netStance.Value = (byte)CurrentStance;
        if (!inv.Same(netInventory.Value)) netInventory.Value = inv;
    }

    PlayerInput ReadHumanInput()
    {
        var input = new PlayerInput { SwitchTo = -1, Aim = GameUI.ForceAim, Lean = GameUI.ForceLean };
        if (GameUI.TestStance >= 0) return TestStanceInput(input);
        if (GameUI.ForceThrow)
        {
            throwTestTimer += Time.deltaTime;
            input.GrenadeHeld = throwTestTimer > 2.5f && throwTestTimer < 3.3f; // after test gives land; cook ~0.8 s, then throw
        }
        if (GameUI.ForceFire)
        {
            forceFireToggle = !forceFireToggle; // re-pull each frame so semi-autos keep firing too
            input.FireHeld = input.FirePressed = forceFireToggle;
        }
        if (!GameUI.CursorCaptured) return input;
        var mouse = Mouse.current;
        if (Keyboard.current != null)
        {
            // Rebindable keys (Options > Keybindings).
            if (Bindings.Held(GameAction.Forward)) input.Move.y += 1f;
            if (Bindings.Held(GameAction.Back)) input.Move.y -= 1f;
            if (Bindings.Held(GameAction.Right)) input.Move.x += 1f;
            if (Bindings.Held(GameAction.Left)) input.Move.x -= 1f;
            input.Sprint = Bindings.Held(GameAction.Sprint);
            input.Jump = Bindings.Pressed(GameAction.Jump);
            input.JumpHeld = Bindings.Held(GameAction.Jump);
            input.Reload = Bindings.Pressed(GameAction.Reload);
            input.Interact = Bindings.Pressed(GameAction.Interact);
            input.Grapple = Bindings.Pressed(GameAction.Grapple);
            input.CrouchDown = Bindings.Pressed(GameAction.Crouch);
            input.CrouchHeld = Bindings.Held(GameAction.Crouch);
            input.ProneToggle = Bindings.Pressed(GameAction.Prone);
            input.GrenadeHeld |= Bindings.Held(GameAction.Grenade);
            if (Bindings.Held(GameAction.LeanLeft)) input.Lean -= 1f;
            if (Bindings.Held(GameAction.LeanRight)) input.Lean += 1f;
            if (Bindings.Pressed(GameAction.Slot1)) input.SwitchTo = 0;
            if (Bindings.Pressed(GameAction.Slot2)) input.SwitchTo = 1;
            if (Bindings.Pressed(GameAction.Slot3)) input.SwitchTo = 2;
        }
        if (mouse != null)
        {
            input.Look = mouse.delta.ReadValue();
            input.FireHeld |= mouse.leftButton.isPressed;
            input.FirePressed |= mouse.leftButton.wasPressedThisFrame;
            input.Aim = mouse.rightButton.isPressed || GameUI.ForceAim;
            input.Grapple |= mouse.middleButton.wasPressedThisFrame;
            float wheel = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > 0.1f) input.SwitchTo = wheel < 0f ? 3 : 4; // wheel down = next slot
        }
        return input;
    }

    // ------------------------------------------------------------------ weapons (owner)

    void UpdateWeapon(PlayerInput input, bool alive)
    {
        if (InDuel)
        {
            adsT = 0f;
            reloading = false;
            UpdateFists(input, alive);
            return;
        }
        var def = CurrentDef;

        // Switching: 1 / 2 / 3, or the wheel through every slot you have something in. 3 again = next grenade type.
        int target = input.SwitchTo == 3 ? CycleSlot(1) : input.SwitchTo == 4 ? CycleSlot(-1) : input.SwitchTo;
        if (alive && target == Inventory.ThrowSlot)
        {
            int nt = inv.Active == Inventory.ThrowSlot && input.SwitchTo == 2 ? inv.NextThrowable(inv.Throwable)
                   : inv.ThrowCount(inv.Throwable) > 0 ? inv.Throwable : inv.NextThrowable(inv.Throwable);
            if (nt >= 0 && nt != inv.Throwable)
            {
                inv.Throwable = nt;
                if (inv.Active == Inventory.ThrowSlot)
                {
                    drawEndTime = Time.time + 0.25f;
                    audioSource.PlayOneShot(Sfx.Reload, 0.4f);
                }
            }
        }
        if (alive && target >= 0 && target != inv.Active && inv.Weapon(target) >= 0)
        {
            inv.Active = target;
            reloading = false;
            adsT = 0f;
            drawEndTime = Time.time + 0.35f;
            nextFireTime = Mathf.Max(nextFireTime, drawEndTime);
            audioSource.PlayOneShot(Sfx.Reload, 0.4f);
            def = CurrentDef;
        }
        if (def == null || !alive || inv.Active == Inventory.ThrowSlot)
        {
            // Nothing to shoot (slot 3 throws with the trigger in UpdateGrenade).
            adsT = Mathf.MoveTowards(adsT, 0f, Time.deltaTime * 8f);
            reloading = false;
            triggerWasDown = input.FireHeld;
            return;
        }

        int slot = inv.Active;
        int mag = inv.Mag(slot), reserve = inv.Reserve(slot);
        bool trigger = def.Automatic ? input.FireHeld : (input.FirePressed || (input.FireHeld && !triggerWasDown));
        triggerWasDown = input.FireHeld;

        // Reloading
        if (input.Reload && !reloading && mag < def.MagSize && (reserve > 0 || InfiniteAmmo))
            StartReload(def);
        if (reloading)
        {
            if (def.ReloadPerRound)
            {
                if (trigger && mag > 0) reloading = false; // pump guns can interrupt to fire
                else if (Time.time >= reloadEnd)
                {
                    inv.SetMag(slot, ++mag);
                    if (!InfiniteAmmo) inv.SetReserve(slot, --reserve);
                    audioSource.PlayOneShot(Sfx.ShellInsert, 0.5f);
                    if (mag >= def.MagSize || (reserve <= 0 && !InfiniteAmmo)) reloading = false;
                    else { reloadEnd = Time.time + def.ReloadTime; reloadDuration = def.ReloadTime; }
                }
            }
            else if (Time.time >= reloadEnd)
            {
                int take = InfiniteAmmo ? def.MagSize - mag : Mathf.Min(def.MagSize - mag, reserve);
                inv.SetMag(slot, mag + take);
                if (!InfiniteAmmo) inv.SetReserve(slot, reserve - take);
                reloading = false;
            }
        }

        bool wantAds = input.Aim && !reloading && Time.time >= drawEndTime;
        adsT = Mathf.MoveTowards(adsT, wantAds ? 1f : 0f, Time.deltaTime * (def.HasScope ? 5f : 8f));

        if (reloading && !def.ReloadPerRound) return;
        if (!trigger || Time.time < nextFireTime || Time.time < drawEndTime) return;

        mag = inv.Mag(slot);
        reserve = inv.Reserve(slot);
        if (mag <= 0)
        {
            audioSource.PlayOneShot(Sfx.DryFire, 0.6f);
            nextFireTime = Time.time + 0.25f;
            if (reserve > 0 || InfiniteAmmo) StartReload(def);
            return;
        }
        reloading = false;
        Fire(def, slot);
    }

    /// <summary>The next slot (dir +1) or previous one (-1) that has something in it, or -1.</summary>
    int CycleSlot(int dir)
    {
        for (int k = 1; k <= 3; k++)
        {
            int s = ((inv.Active + dir * k) % 3 + 3) % 3;
            if (s == Inventory.ThrowSlot ? inv.TotalThrowables > 0 : inv.Weapon(s) >= 0) return s;
        }
        return -1;
    }

    bool InfiniteAmmo => DevSettings.InfiniteAmmo && IsLocalHuman || (botOnServer && DevSettings.BotsInfiniteAmmo);

    void StartReload(WeaponDef def)
    {
        reloading = true;
        reloadDuration = def.ReloadPerRound ? def.ReloadTime + 0.3f : def.ReloadTime;
        reloadEnd = Time.time + reloadDuration;
        audioSource.PlayOneShot(Sfx.Reload, 0.5f);
    }

    void Fire(WeaponDef def, int slot)
    {
        if (!InfiniteAmmo) inv.SetMag(slot, inv.Mag(slot) - 1);
        nextFireTime = Time.time + def.FireInterval;

        if (def.Projectile != ProjectileKind.None)
        {
            // Launchers: a real projectile flies (server-simulated); here just the local kick and effects.
            Vector3 launchDir = AimbotLocked
                ? (aimbotLock.AimPoint - head.position).normalized // aimbot: dead on, no spread
                : Quaternion.Euler(Random.insideUnitSphere * Mathf.Lerp(def.HipSpread, def.AdsSpread, adsT)) * head.forward;
            PlayShotEffects(def, System.Array.Empty<ShotImpact>(), true);
            LaunchRpc(head.position, launchDir, (int)def.Id);
            float k = def.RecoilKick * Mathf.Lerp(1f, 0.7f, adsT);
            if (IsLocalHuman && def.RecoilKick > 6f) shake = Mathf.Max(shake, (def.RecoilKick - 6f) * 0.035f);
            pitch -= k * 0.4f;
            recoilOffset -= k * 0.6f;
            viewKick = 1f;
            return;
        }

        float moving = new Vector2(Velocity.x, Velocity.z).magnitude > 1f ? 1.2f : 0f;
        float spread = Mathf.Lerp(def.HipSpread, def.AdsSpread, adsT) + bloom + moving * (1f - adsT * 0.5f) + (controller.isGrounded ? 0f : 3f);
        if (brain != null) spread += brain.ExtraSpread;
        spread *= stance == Stance.Prone ? 0.5f : stance == Stance.Crouch ? 0.75f : 1f;
        if (sliding) spread += 2f;
        bloom = Mathf.Min(bloom + def.BloomPerShot, 3f);

        Vector3 origin = head.position;
        Vector3 dir = head.forward;
        if (AimbotLocked)
        {
            // Aimbot: every round (and every pellet) goes straight to their head, whatever the gun's spread,
            // movement, recoil or explosion shake.
            dir = (aimbotLock.AimPoint - origin).normalized;
            spread = 0f;
        }
        int seed = Random.Range(int.MinValue, int.MaxValue);
        var dirs = WeaponDefs.PelletDirections(dir, def.Pellets, spread, seed);
        var impacts = new ShotImpact[dirs.Length];
        for (int i = 0; i < dirs.Length; i++)
            impacts[i] = Trace(origin, dirs[i], def.Range, out _, out _);
        PlayShotEffects(def, impacts, true);
        ShootRpc(origin, dir, spread, seed, (int)def.Id);

        float kick = def.RecoilKick * Mathf.Lerp(1f, 0.7f, adsT);
        if (IsLocalHuman && def.RecoilKick > 6f) shake = Mathf.Max(shake, (def.RecoilKick - 6f) * 0.035f);
        pitch -= kick * 0.35f;
        recoilOffset -= kick * 0.65f;
        yaw += Random.Range(-0.2f, 0.2f) * kick;
        viewKick = 1f;
    }

    ShotImpact Trace(Vector3 origin, Vector3 dir, float range, out RaycastHit hit, out FpsPlayer victim)
    {
        victim = null;
        hit = default;
        int n = Physics.RaycastNonAlloc(origin, dir, hitBuffer, range, GameAssets.ShotMask, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            if (hitBuffer[i].collider == controller || hitBuffer[i].collider == headHitbox) continue;
            if (hitBuffer[i].distance < best)
            {
                best = hitBuffer[i].distance;
                hit = hitBuffer[i];
                found = true;
            }
        }
        if (!found)
            return new ShotImpact { Point = origin + dir * range, Normal = -dir, Kind = 0 };
        victim = hit.collider.GetComponentInParent<FpsPlayer>();
        if (victim != null && (victim.IsDead || victim == this)) victim = null;
        return new ShotImpact { Point = hit.point, Normal = hit.normal, Kind = (byte)(victim != null ? 2 : 1) };
    }

    // ------------------------------------------------------------------ weapon visuals

    void UpdateWeaponVisual()
    {
        var i = Inv;
        int want = IsDead ? -1 : InDuel ? FistsVisual : i.Weapon(i.Active);
        if (want != builtWeapon)
        {
            builtWeapon = want;
            if (weaponModel != null) Destroy(weaponModel.gameObject);
            weaponModel = null;
            if (fistR != null) { Destroy(fistR.gameObject); Destroy(fistL.gameObject); fistR = fistL = null; }
            body.rightHandTarget = body.leftHandTarget = null;
            if (fpArms != null) fpArms.SetTargets(null, null);
            var def = want == FistsVisual ? null : WeaponDefs.Get(want);
            if (want == FistsVisual)
            {
                // Bare fists up in a guard; the arms reach for these targets.
                fistR = new GameObject("FistR").transform;
                fistL = new GameObject("FistL").transform;
                fistR.SetParent(weaponRig, false);
                fistL.SetParent(weaponRig, false);
                body.rightHandTarget = fistR;
                body.leftHandTarget = fistL;
                if (fpArms != null) fpArms.SetTargets(fistR, fistL);
            }
            if (def != null)
            {
                weaponModel = WeaponModel.Build(def.Id, weaponRig);
                var rightHand = new GameObject("RightHand").transform;
                rightHand.SetParent(weaponModel.transform, false);
                rightHand.localPosition = new Vector3(0f, -0.035f, -0.01f);
                Transform leftHand = null;
                if (!def.OneHanded)
                {
                    leftHand = new GameObject("LeftHand").transform;
                    leftHand.SetParent(weaponModel.transform, false);
                    leftHand.localPosition = def.LeftHand;
                    // Break-action: the support hand holds the barrel, so it rides down with it when it tips open.
                    if (weaponModel.breakAction != null) leftHand.SetParent(weaponModel.breakAction, true);
                }
                heldRight = rightHand;
                body.rightHandTarget = rightHand;
                body.leftHandTarget = leftHand;
                if (fpArms != null) fpArms.SetTargets(rightHand, leftHand);
                if (IsLocalHuman)
                    GameAssets.SetLayerRecursive(weaponModel.gameObject, GameAssets.ViewModelLayer);
                else
                    foreach (var r in weaponModel.GetComponentsInChildren<Renderer>())
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }

        var d = WeaponDefs.Get(builtWeapon);
        if (weaponModel != null && weaponModel.warhead != null)
            weaponModel.warhead.gameObject.SetActive(Inv.Mag(Inv.Active) > 0 && !(IsOwner && reloading && Inv.Mag(Inv.Active) == 0));
        body.aimPitch = IsOwner ? pitch : netLook.Value.x;
        body.lean = IsOwner ? lean : netLook.Value.z;
        if (builtWeapon == FistsVisual)
        {
            AnimateFists();
            return;
        }
        if (d == null) return;
        if (IsLocalHuman)
        {
            // Hip → sights, plus kick and a little movement bob.
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            float ads = AdsEased;
            float free = 1f - ads; // bob, sway, tilt and dips all fade out at full ADS so the sights stay dead on the centre
            Vector3 adsPos = new Vector3(0f, -d.SightHeight, d.AdsDistance);
            Vector3 pos = Vector3.Lerp(d.HipOffset, adsPos, ads);
            // Walk bob: amplitude and tempo ease with speed (no snapping when you start or stop), none in the air.
            float speed = new Vector2(Velocity.x, Velocity.z).magnitude;
            float wantBob = controller.isGrounded && !sliding ? Mathf.Clamp01(speed / 6f) : 0f;
            smoothBob = Mathf.Lerp(smoothBob, wantBob, 1f - Mathf.Exp(-8f * dt));
            bobPhase += dt * Mathf.Lerp(4f, 8.5f, smoothBob);
            pos += new Vector3(Mathf.Sin(bobPhase) * 0.008f, -Mathf.Abs(Mathf.Cos(bobPhase)) * 0.01f, 0f) * smoothBob * free;
            pos.y += Mathf.Sin(Time.time * 1.6f) * 0.0015f * free; // breathing
            bool breakAction = weaponModel != null && weaponModel.breakAction != null;
            float breakTilt = 0f;
            if (reloading && !d.ReloadPerRound)
            {
                float r = Mathf.Sin(Mathf.SmoothStep(0f, 1f, ReloadProgress) * Mathf.PI);
                // Break-actions come up, in and nose-high so you watch the barrel tip open; others dip down.
                pos += (breakAction ? new Vector3(-0.08f, 0.11f, 0.03f) : new Vector3(0f, -0.08f, -0.05f)) * r;
                if (breakAction) breakTilt = r;
            }
            if (breakAction && heldRight != null)
            {
                // Firing hand: thumb the opening lever, reach up to the open breech and push a round in, back to the grip.
                var grip = new Vector3(0f, -0.035f, -0.01f);
                var lever = new Vector3(0.004f, -0.02f, 0.058f);
                var breech = new Vector3(0.008f, 0.052f, 0.085f);
                Vector3 hand = grip;
                if (reloading)
                {
                    float t = ReloadProgress;
                    if (t < 0.2f) hand = Vector3.Lerp(grip, lever, Mathf.SmoothStep(0f, 1f, t / 0.2f));
                    else if (t < 0.45f) hand = Vector3.Lerp(lever, breech + new Vector3(0f, 0.03f, -0.04f), Mathf.SmoothStep(0f, 1f, (t - 0.2f) / 0.25f));
                    else if (t < 0.7f) hand = Vector3.Lerp(breech + new Vector3(0f, 0.03f, -0.04f), breech, Mathf.SmoothStep(0f, 1f, (t - 0.45f) / 0.25f));
                    else hand = Vector3.Lerp(breech, grip, Mathf.SmoothStep(0f, 1f, (t - 0.7f) / 0.3f));
                }
                heldRight.localPosition = hand;
            }
            if (Time.time < drawEndTime)
            {
                float k = (drawEndTime - Time.time) / 0.35f;
                pos += Vector3.down * 0.15f * k * k; // rises fast, settles softly
            }
            viewKick *= Mathf.Exp(-12f * dt);
            pos += Vector3.back * viewKick * Mathf.Clamp(d.RecoilKick * 0.008f, 0.004f, 0.13f);
            // Sway: the gun trails a little behind the mouse.
            float yawRate = Mathf.DeltaAngle(lastSwayYaw, yaw) / dt, pitchRate = (pitch - lastSwayPitch) / dt;
            lastSwayYaw = yaw;
            lastSwayPitch = pitch;
            var swayTarget = new Vector2(Mathf.Clamp(-yawRate * 0.012f, -4f, 4f), Mathf.Clamp(pitchRate * 0.012f, -4f, 4f));
            sway = Vector2.Lerp(sway, swayTarget, 1f - Mathf.Exp(-10f * dt));
            float land = landAmount * Mathf.Sin(Mathf.Clamp01((Time.time - landTime) / 0.38f) * Mathf.PI);
            pos += new Vector3(sway.x * 0.004f, -sway.y * 0.003f - land * 0.05f, 0f) * free;
            weaponRig.localPosition = pos;
            weaponRig.localRotation = Quaternion.Euler(-24f * breakTilt, -10f * breakTilt, 14f * breakTilt)
                                      * Quaternion.Euler(-viewKick * d.RecoilKick * 0.6f + sway.y * free, sway.x * free, (slideT * 18f + sway.x * 1.5f) * free);
            if (weaponModel != null && weaponModel.breakAction != null) // break-action: barrels tip open to reload
                weaponModel.breakAction.localRotation = Quaternion.Euler(reloading ? Mathf.Sin(Mathf.SmoothStep(0f, 1f, ReloadProgress) * Mathf.PI) * 32f : 0f, 0f, 0f);
        }
        else
        {
            weaponRig.localPosition = d.HipOffset;
            weaponRig.localRotation = Quaternion.identity;
        }
    }

    void PlayShotEffects(WeaponDef def, ShotImpact[] impacts, bool local)
    {
        Transform muzzle = weaponModel != null ? weaponModel.muzzle : head;
        Vector3 from = MuzzleWorldPoint(muzzle);
        Vector3 aim = IsLocalHuman ? playerCamera.transform.forward : muzzle.forward;

        // Each round flies out of the barrel; its impact (dust, sparks, hole, blood) lands when it arrives.
        foreach (var imp in impacts)
        {
            var hit = imp;
            ShotFx.Round(from, hit.Point, def, () =>
            {
                if (hit.Kind == 1) Gore.WorldHit(hit.Point, hit.Normal);
                else if (hit.Kind == 2) Gore.FleshHit(hit.Point, (hit.Point - from).normalized, def.Damage);
            });
        }

        if (weaponModel != null)
        {
            ShotFx.MuzzleFlash(muzzle, weaponModel.gameObject.layer, def, from, aim, IsLocalHuman);
            weaponModel.OnFired();
            if (def.Projectile == ProjectileKind.None) EjectShell(def);
            if (def.Id == WeaponId.RPG7)
            {
                // Backblast: a big gout of smoke and sparks out of the rear of the tube.
                Vector3 back = transform.position + Vector3.up * 1.5f - head.forward * 1.1f;
                ShotFx.Smoke(back, -head.forward, 14, 0.9f);
                ShotFx.Sparks(back, -head.forward, 20, 12f, 30f);
            }
        }

        audioSource.pitch = Random.Range(0.94f, 1.06f);
        audioSource.PlayOneShot(Sfx.Gunshot(def.Id), local ? 0.7f : 1f);
        if (def.Id == WeaponId.Remington870)
            StartCoroutine(PlayDelayed(Sfx.Pump, 0.25f, 0.6f));
        else if (def.Id == WeaponId.Mosin || def.Id == WeaponId.LeverAction)
            StartCoroutine(PlayDelayed(Sfx.Pump, 0.2f, 0.5f)); // working the bolt / lever
        else if (def.Id == WeaponId.Cattleman)
            StartCoroutine(PlayDelayed(Sfx.ShellInsert, 0.22f, 0.6f)); // thumbing the hammer back
    }

    IEnumerator PlayDelayed(AudioClip clip, float delay, float volume)
    {
        yield return new WaitForSeconds(delay);
        audioSource.PlayOneShot(clip, volume);
    }

    void EjectShell(WeaponDef def)
    {
        if (weaponModel.ejectionPort == null) return;
        bool shotgun = def.Pellets > 1;
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "Shell";
        go.layer = GameAssets.DebrisLayer;
        var port = weaponModel.ejectionPort;
        go.transform.SetPositionAndRotation(MuzzleWorldPoint(port), port.rotation * Quaternion.Euler(90f, 0f, 0f));
        float caliber = def.Id == WeaponId.BarrettM82 ? 0.02f : shotgun ? 0.02f : def.Id == WeaponId.GoldenDeagle ? 0.013f : 0.009f;
        float length = def.Id == WeaponId.BarrettM82 ? 0.1f : shotgun ? 0.065f : def.Id == WeaponId.M4A1 ? 0.045f : def.Id == WeaponId.GoldenDeagle ? 0.033f : 0.02f;
        go.transform.localScale = new Vector3(caliber, length * 0.5f, caliber);
        go.GetComponent<Renderer>().sharedMaterial = shotgun ? GameAssets.I.shellRed : GameAssets.I.brass;
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = 0.02f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.linearVelocity = Velocity + port.right * Random.Range(2f, 3.5f) + port.up * Random.Range(1f, 2f);
        rb.angularVelocity = Random.insideUnitSphere * 25f;
        Destroy(go, 4f);
        shells.Enqueue(go);
        while (shells.Count > 40) { var old = shells.Dequeue(); if (old != null) Destroy(old); }
    }

    /// <summary>
    /// Where the muzzle is in the world as the player sees it. The first-person gun is drawn by a separate
    /// camera with its own field of view, so project its on-screen muzzle position into the main camera's space.
    /// </summary>
    Vector3 MuzzleWorldPoint(Transform muzzle)
    {
        if (!IsLocalHuman || viewModelCamera == null || playerCamera == null) return muzzle.position;
        Vector3 screen = viewModelCamera.WorldToScreenPoint(muzzle.position);
        if (screen.z <= 0f) return muzzle.position;
        screen.z = Mathf.Max(0.35f, Vector3.Distance(viewModelCamera.transform.position, muzzle.position));
        return playerCamera.ScreenToWorldPoint(screen);
    }

    // ------------------------------------------------------------------ remote

    /// <summary>Record a received position/look (both arrive in the same tick and are merged) for interpolation.</summary>
    void PushSnap()
    {
        if (IsOwner) return;
        var s = new NetSnap { t = Time.time, pos = netPosition.Value, look = netLook.Value };
        int n = snaps.Count;
        if (n > 0 && s.t - snaps[n - 1].t < 0.004f) { snaps[n - 1] = s; return; }
        if (n > 0 && (s.pos - snaps[n - 1].pos).sqrMagnitude > 36f) snaps.Clear(); // teleport / respawn: no sweep across the map
        snaps.Add(s);
        if (snaps.Count > 40) snaps.RemoveAt(0);
    }

    void RemoteUpdate()
    {
        if (!hasNetPosition) return;
        Vector3 pos, look;
        float rt = Time.time - InterpDelay;
        if (snaps.Count == 0)
        {
            pos = netPosition.Value;
            look = netLook.Value;
        }
        else if (snaps.Count == 1 || rt <= snaps[0].t)
        {
            pos = snaps[0].pos;
            look = snaps[0].look;
        }
        else
        {
            int k = 0;
            while (k + 1 < snaps.Count && snaps[k + 1].t <= rt) k++;
            int drop = Mathf.Max(0, k - 1); // keep one snapshot behind the bracket for extrapolation
            if (drop > 0) { snaps.RemoveRange(0, drop); k -= drop; }
            if (k + 1 < snaps.Count)
            {
                NetSnap a = snaps[k], b = snaps[k + 1];
                float f = Mathf.Clamp01((rt - a.t) / Mathf.Max(0.001f, b.t - a.t));
                pos = Vector3.Lerp(a.pos, b.pos, f);
                look = new Vector3(Mathf.Lerp(a.look.x, b.look.x, f), Mathf.LerpAngle(a.look.y, b.look.y, f), Mathf.Lerp(a.look.z, b.look.z, f));
            }
            else
            {
                // Ran past the newest snapshot (packet late): carry on briefly at the last velocity, then hold.
                NetSnap a = snaps[snaps.Count - 2], b = snaps[snaps.Count - 1];
                float span = Mathf.Max(0.001f, b.t - a.t);
                float ahead = Mathf.Min(rt - b.t, 0.1f);
                pos = b.pos + (b.pos - a.pos) / span * ahead;
                look = b.look;
            }
        }
        transform.position = pos;
        transform.rotation = Quaternion.Euler(0f, look.y, 0f);
        lean = look.z;
        ApplyControllerShape((Stance)netStance.Value);
        ApplyHeadPose(look.x, lean);
    }

    // ------------------------------------------------------------------ server: shooting and damage

    [Rpc(SendTo.Server)]
    void DropProtectionRpc()
    {
        if (Protected.Value) Debug.Log($"[FFA] {DisplayName}: spawn protection ended (moved)");
        Protected.Value = false;
    }

    [Rpc(SendTo.Server)]
    void ShootRpc(Vector3 origin, Vector3 direction, float spread, int seed, int weapon)
    {
        if (IsDead || InDuel || MatchRules.IsMatchOver) return;
        Protected.Value = false; // firing ends spawn protection
        var def = WeaponDefs.Get(weapon);
        if (def == null || !HasWeapon((WeaponId)weapon)) return;
        if (Time.time < serverNextFire) return;
        serverNextFire = Time.time + def.FireInterval * 0.7f; // tolerate network jitter
        if ((origin - head.position).sqrMagnitude > 6f) origin = head.position;

        Physics.SyncTransforms();
        var dirs = WeaponDefs.PelletDirections(direction.normalized, def.Pellets, Mathf.Clamp(spread, 0f, 30f), seed);
        var impacts = new ShotImpact[dirs.Length];
        var damage = new Dictionary<FpsPlayer, (float dmg, BodyModel.Part part, Vector3 point, Vector3 dir, float dist, float best)>();
        for (int i = 0; i < dirs.Length; i++)
        {
            impacts[i] = Trace(origin, dirs[i], def.Range, out var hit, out var victim);
            if (victim == null) continue;
            var part = hit.collider.gameObject.layer == GameAssets.HitboxLayer ? BodyModel.Part.Head
                : BodyModel.ClassifyHit(victim.transform, hit.point, victim.CurrentStance, victim.controller.height);
            float dmg = def.Damage * BodyModel.DamageMultiplier(part, def) * def.DamageScaleAt(hit.distance);
            if (damage.TryGetValue(victim, out var acc))
                damage[victim] = (acc.dmg + dmg, dmg > acc.best ? part : acc.part, dmg > acc.best ? hit.point : acc.point, acc.dir, acc.dist, Mathf.Max(dmg, acc.best));
            else
                damage[victim] = (dmg, part, hit.point, dirs[i], hit.distance, dmg);
        }

        foreach (var kv in damage)
        {
            var v = kv.Value;
            if (kv.Key.SpawnProtected) continue;
            // The golden gun doesn't hurt: it drags both of you into a fist fight to the death.
            if (def.Id == WeaponId.GoldenDeagle && DuelManager.TryStart(this, kv.Key))
            {
                HitConfirmRpc(0, false, false, v.point);
                break;
            }
            int amount = Mathf.Max(1, Mathf.RoundToInt(v.dmg));
            var info = new DeathInfo
            {
                Point = v.point, Direction = v.dir, Weapon = (byte)def.Id, Part = (byte)v.part,
                Distance = v.dist, Seed = seed ^ (int)kv.Key.NetworkObjectId,
            };
            bool killed = kv.Key.TakeDamage(amount, this, info);
            HitConfirmRpc(amount, v.part == BodyModel.Part.Head, killed, v.point);
        }
        ShotEffectsRpc(weapon, impacts);
    }

    [Rpc(SendTo.NotOwner)]
    void ShotEffectsRpc(int weapon, ShotImpact[] impacts)
    {
        var def = WeaponDefs.Get(weapon);
        if (def != null) PlayShotEffects(def, impacts, false);
    }

    [Rpc(SendTo.Owner)]
    void HitConfirmRpc(int damage, bool headshot, bool killed, Vector3 point)
    {
        if (!IsLocalHuman) return;
        GameUI.ShowHitMarker(headshot, killed, damage, point);
        audioSource.PlayOneShot(Sfx.HitTick, killed ? 0.8f : 0.5f);
    }

    /// <summary>Server only. Returns true if this damage killed the player.</summary>
    public bool TakeDamage(int amount, FpsPlayer attacker, DeathInfo info)
    {
        if (!IsServer || IsDead || InLobby.Value) return false;
        if (SpawnProtected && amount < 9999) return false; // falls and executions still count
        if (DevSettings.GodMode && !botOnServer && OwnerClientId == NetworkManager.ServerClientId && amount < 9999) return false;
        if (botOnServer && brain != null && brain.invulnerable && amount < 9999) return false;
        Health.Value = Mathf.Max(0, Health.Value - amount);
        if (!IsDead) return false;

        Deaths.Value++;
        Streak.Value = 0;
        if (attacker != null && attacker != this)
        {
            attacker.Kills.Value++;
            attacker.ServerAddStreak();
            if (!attacker.IsDead) attacker.Health.Value = Mathf.Min(MaxHealth, attacker.Health.Value + KillHeal); // a kill heals
        }
        // Losing a golden duel: your guns fall back on the map, where the duel began.
        Vector3 dropAt = DuelManager.OnDeath(this, out var duelDropAt) ? duelDropAt : transform.position;
        DropWeapons(dropAt);
        if (HasGrapple.Value)
        {
            HasGrapple.Value = false;
            WeaponPickup.Spawn(WeaponId.GrappleHook, 0, 0, dropAt - transform.forward * 0.5f, null, 30f);
        }
        bool byOther = attacker != null && attacker != this;
        DeathRpc(info, byOther ? new FixedString32Bytes(attacker.DisplayName) : default, byOther ? attacker.NetworkObjectId : ulong.MaxValue);
        MatchRules.OnPlayerKilled(attacker, this, info);

        if (respawnRoutine != null) StopCoroutine(respawnRoutine);
        respawnRoutine = StartCoroutine(RespawnAfterDelay());
        return true;
    }

    /// <summary>Server: kill outright as if shot by `attacker` with `weapon` in `part` (dev tools).</summary>
    public void DevKill(FpsPlayer attacker, WeaponId weapon, BodyModel.Part part, float distance = -1f, bool explosive = false)
    {
        Vector3 partPoint = transform.TransformPoint(part switch
        {
            BodyModel.Part.Head => new Vector3(0f, 1.72f, 0.05f),
            BodyModel.Part.Pelvis => new Vector3(0f, 0.95f, 0.1f),
            BodyModel.Part.UpperLegL => new Vector3(-0.1f, 0.6f, 0.05f),
            BodyModel.Part.UpperArmR => new Vector3(0.3f, 1.35f, 0f),
            _ => new Vector3(0f, 1.3f, 0.12f),
        });
        Vector3 from = attacker != null && attacker != this ? attacker.head.position : partPoint + transform.forward * 6f;
        float dist = distance >= 0f ? distance : Vector3.Distance(from, partPoint);
        var info = new DeathInfo
        {
            Point = partPoint, Direction = (partPoint - from).normalized, Weapon = (byte)weapon, Part = (byte)part,
            Distance = dist, Seed = Random.Range(int.MinValue, int.MaxValue),
            Explosive = explosive,
            ExplosionCenter = transform.position + Vector3.up * 0.3f - (partPoint - from).normalized * 0.8f,
        };
        TakeDamage(99999, null, info); // executions don't count toward anyone's score
    }

    void DropWeapons(Vector3 at)
    {
        var i = Inv;
        for (int slot = 0; slot < 2; slot++)
        {
            int w = i.Weapon(slot);
            if (w < 0 || w == (int)WeaponId.Glock17) continue; // everyone respawns with a Glock anyway
            Vector3 offset = transform.right * (slot == 0 ? -0.6f : 0.6f) + transform.forward * 0.3f;
            Vector3 pos = at + offset;
            if (Physics.Raycast(pos + Vector3.up * 1.5f, Vector3.down, out var hit, 4f, 1, QueryTriggerInteraction.Ignore))
                pos = hit.point;
            float life = w == (int)WeaponId.BarrettM82 ? 60f : 30f;
            WeaponPickup.Spawn((WeaponId)w, i.Mag(slot), i.Reserve(slot), pos, null, life);
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    void DeathRpc(DeathInfo info, FixedString32Bytes killer, ulong killerId)
    {
        Transform corpse = body != null ? Gore.SpawnCorpse(body, info) : null;
        ApplyAliveState();
        if (IsLocalHuman)
        {
            deathCamTarget = corpse;
            deathCamDir = info.Direction;
            deathCamDir.y = 0f;
            if (deathCamDir.sqrMagnitude < 0.01f) deathCamDir = transform.forward;
            deathCamDir.Normalize();
            LastKillerName = killer.ToString();
            var def = WeaponDefs.Get(info.WeaponId);
            var gore = GorePlan.For(info).Describe();
            LastDeathDetail = info.Fell ? "You fell to your death" :
                $"{(def != null ? def.Name : "")}{(info.HitPart == BodyModel.Part.Head ? " · headshot" : "")}{(gore != null ? " · " + gore.ToLower() : "")}";
            if (!InDuel && !info.Fell && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(killerId, out var killerObj))
                KillCam.Play(this, killerObj.GetComponent<FpsPlayer>(), info.WeaponId, corpse);
        }
    }

    IEnumerator RespawnAfterDelay()
    {
        yield return new WaitForSeconds(RespawnDelay);
        respawnRoutine = null;
        Respawn();
    }

    /// <summary>Server only: restore health and move the player to the safest spawn point.</summary>
    public void Respawn()
    {
        DuelManager.Abort(this);
        if (InDuel) DuelSlot.Value = 0;
        if (respawnRoutine != null)
        {
            StopCoroutine(respawnRoutine);
            respawnRoutine = null;
        }
        Health.Value = MaxHealth;
        SnaredUntil.Value = 0;
        ServerProtect();
        Vector3 position;
        float spawnYaw;
        if (brain != null && brain.fixedSpawn.HasValue)
        {
            position = brain.fixedSpawn.Value;
            spawnYaw = brain.fixedYaw;
        }
        else
            SpawnPoint.Pick(this, out position, out spawnYaw);
        transform.SetPositionAndRotation(position, Quaternion.Euler(0f, spawnYaw, 0f));
        TeleportRpc(position, spawnYaw);
    }

    /// <summary>Server: move this player somewhere (dev tools / screenshot tests). Keeps health and weapons.</summary>
    public void ServerTeleport(Vector3 position, float newYaw)
    {
        var keep = Inv;
        transform.SetPositionAndRotation(position, Quaternion.Euler(0f, newYaw, 0f));
        TeleportRpc(position, newYaw);
        if (keep.W1 >= 0) ServerGiveWeapon((WeaponId)keep.W1);
        for (int t = 0; t < WeaponDefs.Throwables.Length; t++) // and the grenades (the teleport resets to the spawn loadout)
        {
            int extra = keep.ThrowCount(t) - Inventory.Default.ThrowCount(t);
            if (extra > 0) GiveGrenadesRpc(t, extra);
        }
    }

    [Rpc(SendTo.Owner)]
    void TeleportRpc(Vector3 position, float newYaw)
    {
        controller.enabled = false;
        transform.SetPositionAndRotation(position, Quaternion.Euler(0f, newYaw, 0f));
        yaw = newYaw;
        protectionDropSent = false;
        pitch = recoilOffset = bloom = adsT = verticalVelocity = lean = 0f;
        grappling = false;
        momentum = Vector3.zero;
        netGrapple.Value = Vector4.zero;
        sliding = false;
        stance = Stance.Stand;
        slideSpeed = 0f;
        airSlideQueued = false;
        airVerticalSpeed = 0f;
        ApplyControllerShape(Stance.Stand);
        netStance.Value = 0;
        inv = Inventory.Default;
        if (brain != null && brain.loadout != WeaponId.None && brain.loadout != WeaponId.Glock17)
        {
            var d = WeaponDefs.Get(brain.loadout);
            inv.Set(1, (int)d.Id, d.MagSize, d.SpareAmmo);
            inv.Active = 1;
        }
        reloading = false;
        netPosition.Value = position;
        netInventory.Value = inv;
        deathCamTarget = null;
        playerCamera.transform.localPosition = Vector3.zero;
        playerCamera.transform.localRotation = Quaternion.identity;
        ApplyAliveState();
    }

    [Rpc(SendTo.Server)]
    void RequestRespawnRpc()
    {
        if (!IsDead) TakeDamage(99999, null, new DeathInfo { Fell = true, Point = transform.position, Seed = 1 });
    }

    // ------------------------------------------------------------------ server: pickups

    public bool HasWeapon(WeaponId id) => Inv.SlotOf((int)id) >= 0;

    [Rpc(SendTo.Server)]
    void InteractRpc(ulong pickupId)
    {
        if (IsDead || InDuel) return;
        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(pickupId, out var obj)) return;
        var pickup = obj.GetComponent<WeaponPickup>();
        if (pickup == null || !pickup.Available) return;
        if ((pickup.transform.position + Vector3.up * 0.9f - head.position).sqrMagnitude > 3.5f * 3.5f) return;

        var cur = Inv;
        int weapon = pickup.Weapon.Value, mag = pickup.Mag.Value, reserve = pickup.Reserve.Value;
        if (weapon == (int)WeaponId.GrappleHook)
        {
            if (HasGrapple.Value) return; // already carrying one
            HasGrapple.Value = true;
            HookPickedUpRpc();
            pickup.Despawn();
            return;
        }
        int throwType = WeaponDefs.ThrowableIndex((WeaponId)weapon);
        if (throwType >= 0)
        {
            if (cur.ThrowCount(throwType) >= WeaponDefs.MaxCarry[throwType]) return;
            GiveGrenadesRpc(throwType, mag);
            pickup.Despawn();
            return;
        }
        int have = cur.SlotOf(weapon);
        if (have >= 0)
            GiveAmmoRpc(have, mag + reserve);
        else if (cur.EmptySlot >= 0)
            GiveWeaponRpc(cur.EmptySlot, weapon, mag, reserve);
        else
        {
            int slot = cur.Active;
            WeaponPickup.Spawn((WeaponId)cur.Weapon(slot), cur.Mag(slot), cur.Reserve(slot), pickup.transform.position, null, 30f);
            GiveWeaponRpc(slot, weapon, mag, reserve);
        }
        Debug.Log($"[FFA] Pickup: {DisplayName} took {WeaponDefs.Get(weapon)?.Name}");
        pickup.Despawn();
    }

    [Rpc(SendTo.Owner)]
    void HookPickedUpRpc() => audioSource.PlayOneShot(Sfx.GrappleFire, 0.6f);

    /// <summary>Server: give a weapon (dev tools / tests). Fills an empty slot, else replaces the active one.</summary>
    public void ServerGiveWeapon(WeaponId id)
    {
        if (id == WeaponId.GrappleHook) { HasGrapple.Value = true; return; }
        var def = WeaponDefs.Get(id);
        var cur = Inv;
        if (def.IsThrowable) { GiveGrenadesRpc(WeaponDefs.ThrowableIndex(id), def.MagSize); return; }
        int have = cur.SlotOf((int)id);
        if (have >= 0) { GiveAmmoRpc(have, def.SpareAmmo + def.MagSize); return; }
        int slot = cur.EmptySlot >= 0 ? cur.EmptySlot : cur.Active;
        GiveWeaponRpc(slot, (int)id, def.MagSize, def.SpareAmmo);
    }

    [Rpc(SendTo.Owner)]
    void GiveWeaponRpc(int slot, int weapon, int mag, int reserve)
    {
        inv.Set(slot, weapon, mag, reserve);
        inv.Active = slot;
        reloading = false;
        adsT = 0f;
        drawEndTime = Time.time + 0.4f;
        audioSource.PlayOneShot(Sfx.Pickup, 0.6f);
    }

    [Rpc(SendTo.Owner)]
    void GiveGrenadesRpc(int type, int amount)
    {
        if (type < 0 || type >= WeaponDefs.Throwables.Length) return;
        inv.SetThrowCount(type, Mathf.Min(WeaponDefs.MaxCarry[type], inv.ThrowCount(type) + amount));
        if (inv.ThrowCount(inv.Throwable) == 0) inv.Throwable = type; // nothing selected yet: select what you picked up
        audioSource.PlayOneShot(Sfx.Pickup, 0.6f);
    }

    // ------------------------------------------------------------------ killstreaks

    /// <summary>Server: count a kill toward the streak and hand out the rewards at 5 and 10.</summary>
    public void ServerAddStreak()
    {
        Streak.Value++;
        if (Streak.Value == SpeedStreak)
        {
            SpeedUntil.Value = ServerNow + StreakRewardSeconds;
            BroadcastKillFeed(new FixedString128Bytes($"{DisplayName} is on a {SpeedStreak}-kill streak: SPEED HACK"));
            StreakRewardRpc(3);
        }
        else if (Streak.Value == FlyStreak)
        {
            FlyUntil.Value = ServerNow + StreakRewardSeconds;
            BroadcastKillFeed(new FixedString128Bytes($"{DisplayName} is on a {FlyStreak}-kill streak: FLYHACK"));
            StreakRewardRpc(4);
        }
        else if (Streak.Value == EspStreak)
        {
            EspUntil.Value = ServerNow + StreakRewardSeconds;
            BroadcastKillFeed(new FixedString128Bytes($"{DisplayName} is on a {EspStreak}-kill streak: ESP"));
            StreakRewardRpc(1);
        }
        else if (Streak.Value == AimbotStreak)
        {
            AimbotUntil.Value = ServerNow + StreakRewardSeconds;
            BroadcastKillFeed(new FixedString128Bytes($"{DisplayName} is on a {AimbotStreak}-kill streak: AIMBOT"));
            StreakRewardRpc(2);
        }
    }

    public void ServerResetStreak()
    {
        Streak.Value = 0;
        EspUntil.Value = 0;
        AimbotUntil.Value = 0;
        SpeedUntil.Value = 0;
        FlyUntil.Value = 0;
    }

    [Rpc(SendTo.Owner)]
    void StreakRewardRpc(int reward)
    {
        if (!IsLocalHuman) return;
        if (reward == 3) GameUI.ShowBanner($"{SpeedStreak} KILL STREAK: SPEED HACK", "Double running, walking and sliding speed for 30 s", new Color(1f, 0.85f, 0.25f));
        else if (reward == 4) GameUI.ShowBanner($"{FlyStreak} KILL STREAK: FLYHACK", $"Fly for 30 s: {Bindings.Label(GameAction.Jump)} to rise, {Bindings.Label(GameAction.Crouch)} to sink", new Color(0.6f, 0.75f, 1f));
        else if (reward == 1) GameUI.ShowBanner($"{EspStreak} KILL STREAK: ESP", "See every enemy through walls for 30 s", new Color(0.35f, 1f, 0.55f));
        else GameUI.ShowBanner($"{AimbotStreak} KILL STREAK: AIMBOT", "Your aim locks onto enemies in view for 30 s", new Color(1f, 0.3f, 0.25f));
        audioSource.PlayOneShot(Sfx.Bell, 0.6f);
    }

    /// <summary>Enemy (or duel opponent) worth targeting: alive, in the same arena context.</summary>
    public bool IsEnemyOf(FpsPlayer other) => other != null && other != this && other.IsSpawned && !other.IsDead && !other.InLobby.Value && other.DuelSlot.Value == DuelSlot.Value;

    FpsPlayer aimbotLock; // owner: who the aimbot is locked onto this frame
    bool AimbotLocked => aimbotLock != null && AimbotActive && !aimbotLock.IsDead;

    bool CanAimbotSee(FpsPlayer p)
    {
        if (!IsEnemyOf(p)) return false;
        Vector3 to = p.AimPoint - head.position;
        if (SmokeCloud.Blocks(head.position, p.AimPoint)) return false;
        return Physics.Raycast(head.position, to, out var hit, to.magnitude + 0.5f, GameAssets.ShotMask, QueryTriggerInteraction.Ignore)
               && hit.collider.GetComponentInParent<FpsPlayer>() == p;
    }

    /// <summary>Aimbot: the visible enemy nearest the crosshair, inside the view.</summary>
    FpsPlayer AimbotTarget()
    {
        FpsPlayer best = null;
        float bestAngle = (playerCamera != null ? playerCamera.fieldOfView : 75f) * 0.75f;
        foreach (var p in All)
        {
            if (!IsEnemyOf(p)) continue;
            Vector3 to = p.AimPoint - head.position;
            if (to.sqrMagnitude > 200f * 200f) continue;
            float angle = Vector3.Angle(head.forward, to);
            if (angle >= bestAngle) continue;
            if (!Physics.Raycast(head.position, to, out var hit, to.magnitude + 0.5f, GameAssets.ShotMask, QueryTriggerInteraction.Ignore)) continue;
            if (hit.collider.GetComponentInParent<FpsPlayer>() != p) continue; // something in the way
            bestAngle = angle;
            best = p;
        }
        return best;
    }

    /// <summary>Owner test hook (-ffaturn): swing the view by some degrees.</summary>
    public void TestTurn(float degrees)
    {
        if (IsOwner) yaw += degrees;
    }

    // ------------------------------------------------------------------ golden duel (fists)

    const int FistsVisual = 1000;      // builtWeapon marker: bare fists instead of a gun model
    const float PunchInterval = 0.42f, PunchReach = 1.9f;
    Transform fistR, fistL;
    Transform heldRight; // firing-hand target on the current gun (animated for break-action reloads)
    int punchHand;
    float punchTime = -10f;

    /// <summary>Server: send this player into duel ring `slot` at full health (weapons kept, but unusable).</summary>
    public void ServerEnterDuel(int slot, Vector3 position, float newYaw)
    {
        if (respawnRoutine != null) { StopCoroutine(respawnRoutine); respawnRoutine = null; }
        DuelSlot.Value = (byte)(slot + 1);
        Health.Value = MaxHealth;
        transform.SetPositionAndRotation(position, Quaternion.Euler(0f, newYaw, 0f));
        DuelTeleportRpc(position, newYaw);
    }

    /// <summary>Server: bring a duellist back to where the duel started, at full health.</summary>
    public void ServerExitDuel(Vector3 position, float newYaw, bool won)
    {
        DuelSlot.Value = 0;
        Health.Value = MaxHealth;
        if (won) ServerProtect();
        transform.SetPositionAndRotation(position, Quaternion.Euler(0f, newYaw, 0f));
        DuelTeleportRpc(position, newYaw);
        DuelResultRpc(won);
    }

    public void ServerClearDuel()
    {
        if (DuelSlot.Value != 0) DuelSlot.Value = 0;
    }

    /// <summary>Owner: move without touching the inventory (unlike a respawn).</summary>
    [Rpc(SendTo.Owner)]
    void DuelTeleportRpc(Vector3 position, float newYaw)
    {
        controller.enabled = false;
        transform.SetPositionAndRotation(position, Quaternion.Euler(0f, newYaw, 0f));
        yaw = newYaw;
        pitch = recoilOffset = bloom = adsT = verticalVelocity = lean = 0f;
        if (grappling) ReleaseGrapple(false);
        grappling = false;
        momentum = Vector3.zero;
        netGrapple.Value = Vector4.zero;
        sliding = false;
        stance = Stance.Stand;
        slideSpeed = 0f;
        airSlideQueued = false;
        airVerticalSpeed = 0f;
        ApplyControllerShape(Stance.Stand);
        netStance.Value = 0;
        reloading = cooking = false;
        netPosition.Value = position;
        ApplyAliveState();
    }

    [Rpc(SendTo.Owner)]
    void DuelResultRpc(bool won)
    {
        if (!IsLocalHuman) return;
        if (won) GameUI.ShowBanner("YOU WON THE GOLDEN DUEL", "Back where it started · full health", new Color(1f, 0.82f, 0.25f));
        else GameUI.ShowBanner("DUEL CALLED OFF", "", Color.white);
        audioSource.PlayOneShot(Sfx.Bell, 0.7f);
    }

    void OnDuelSlotChanged(byte previous, byte now)
    {
        builtWeapon = -2; // swap gun <-> fists
        if (now == 0) return;
        DuelStartedAt = Time.time;
        if (IsLocalHuman)
        {
            var opp = DuelOpponent;
            GameUI.ShowBanner("GOLDEN DUEL", $"Fists only{(opp != null ? " vs " + opp.DisplayName : "")} · fight to the death", new Color(1f, 0.82f, 0.25f));
            audioSource.PlayOneShot(Sfx.Bell, 0.8f);
        }
    }

    /// <summary>Owner: left click throws alternating punches (hold to keep swinging).</summary>
    void UpdateFists(PlayerInput input, bool alive)
    {
        bool pressed = input.FirePressed || input.FireHeld;
        triggerWasDown = input.FireHeld;
        if (!alive || !pressed || Time.time < nextFireTime) return;
        nextFireTime = Time.time + PunchInterval;
        punchHand = 1 - punchHand;
        punchTime = Time.time;
        audioSource.PlayOneShot(Sfx.Swing, 0.6f);

        // What's in front of the fist?
        FpsPlayer victim = null;
        Vector3 point = head.position + head.forward * PunchReach;
        float best = float.MaxValue;
        int n = Physics.SphereCastNonAlloc(head.position, 0.22f, head.forward, hitBuffer, PunchReach, GameAssets.ShotMask, QueryTriggerInteraction.Ignore);
        for (int k = 0; k < n; k++)
        {
            var h = hitBuffer[k];
            if (h.collider == controller || h.collider == headHitbox) continue;
            var p = h.collider.GetComponentInParent<FpsPlayer>();
            if (p == this) continue;
            if (h.distance < best)
            {
                best = h.distance;
                victim = p != null && !p.IsDead ? p : null;
                point = h.distance > 0f ? h.point : h.collider.ClosestPoint(head.position);
            }
        }
        PunchRpc(victim != null ? victim.NetworkObjectId : 0UL, punchHand, point);
    }

    [Rpc(SendTo.Server)]
    void PunchRpc(ulong victimId, int hand, Vector3 point)
    {
        if (IsDead || !InDuel || Time.time < serverNextFire) return;
        serverNextFire = Time.time + PunchInterval * 0.7f;
        FpsPlayer victim = null;
        if (victimId != 0 && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(victimId, out var obj))
            victim = obj.GetComponent<FpsPlayer>();
        bool hit = victim != null && !victim.IsDead && victim.DuelSlot.Value == DuelSlot.Value
                   && (victim.transform.position - transform.position).sqrMagnitude < 3.2f * 3.2f;
        if (hit)
        {
            bool headHit = point.y > victim.transform.position.y + victim.controller.height * 0.8f;
            int dmg = Mathf.RoundToInt(Random.Range(16, 25) * (headHit ? 1.4f : 1f));
            var info = new DeathInfo
            {
                Point = point, Direction = head.forward, Weapon = (byte)WeaponId.Fists,
                Part = (byte)(headHit ? BodyModel.Part.Head : BodyModel.Part.Chest), Distance = 1f,
                Seed = Random.Range(int.MinValue, int.MaxValue),
            };
            bool killed = victim.TakeDamage(dmg, this, info);
            if (!killed) victim.ServerPush(Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized * 4f + Vector3.up * 1.5f);
            HitConfirmRpc(dmg, headHit, killed, point);
        }
        PunchFxRpc(hand, hit, point);
    }

    [Rpc(SendTo.ClientsAndHost)]
    void PunchFxRpc(int hand, bool hit, Vector3 point)
    {
        if (!IsOwner)
        {
            punchHand = hand;
            punchTime = Time.time;
            audioSource.PlayOneShot(Sfx.Swing, 0.5f);
        }
        if (hit)
        {
            audioSource.PlayOneShot(Sfx.Punch, 1f);
            Gore.FleshHit(point, head.forward, 25);
        }
    }

    /// <summary>Guard pose with alternating jabs (both the view model and the third-person arms follow these).</summary>
    void AnimateFists()
    {
        weaponRig.localPosition = Vector3.zero;
        weaponRig.localRotation = Quaternion.identity;
        float t = (Time.time - punchTime) / 0.26f;
        float jab = t >= 0f && t < 1f ? Mathf.Sin(t * Mathf.PI) : 0f;
        float speed = new Vector2(Velocity.x, Velocity.z).magnitude;
        float bob = Mathf.Sin(Time.time * 6f) * 0.012f * Mathf.Clamp01(speed / 5f + 0.3f);
        var guardR = new Vector3(0.16f, -0.17f + bob, 0.32f);
        var guardL = new Vector3(-0.15f, -0.14f - bob, 0.29f);
        fistR.localPosition = Vector3.Lerp(guardR, new Vector3(0.04f, -0.08f, 0.62f), punchHand == 0 ? jab : 0f);
        fistL.localPosition = Vector3.Lerp(guardL, new Vector3(-0.04f, -0.08f, 0.62f), punchHand == 1 ? jab : 0f);
        fistR.localRotation = Quaternion.Euler(0f, -12f, -15f); // knuckles forward, like gripping a bar
        fistL.localRotation = Quaternion.Euler(0f, 12f, 15f);
    }

    // ------------------------------------------------------------------ explosives

    /// <summary>
    /// Owner: throw the selected throwable. Hold T (from any slot) or the trigger with slot 3 out; release to throw.
    /// Frags cook while held and go off in your hand if held too long; the others start their fuse on release.
    /// </summary>
    void UpdateGrenade(PlayerInput input, bool alive)
    {
        if (!alive || InDuel) { cooking = false; return; }
        if (GameUI.TestThrowType >= 0 && inv.ThrowCount(GameUI.TestThrowType) > 0) inv.Throwable = GameUI.TestThrowType; // tests
        if (inv.ThrowCount(inv.Throwable) <= 0)
        {
            int nt = inv.NextThrowable(inv.Throwable);
            if (nt >= 0) inv.Throwable = nt;
        }
        bool slotOut = inv.Active == Inventory.ThrowSlot && Time.time >= drawEndTime;
        bool held = input.GrenadeHeld || (slotOut && input.FireHeld);
        if (held && !cooking && inv.ThrowCount(inv.Throwable) > 0 && Time.time >= nextGrenadeTime && !sliding)
        {
            cooking = true;
            cookType = inv.Throwable;
            cookStart = Time.time;
            audioSource.PlayOneShot(Sfx.DryFire, 0.8f); // pin / lighter
        }
        if (!cooking) return;
        float heldFor = Time.time - cookStart;
        bool frag = cookType == 0;
        if (!held || (frag && heldFor >= WeaponDefs.FragFuse))
        {
            cooking = false;
            inv.SetThrowCount(cookType, Mathf.Max(0, inv.ThrowCount(cookType) - 1));
            nextGrenadeTime = Time.time + 0.8f;
            drawEndTime = Time.time + 0.45f; // weapon dips while throwing
            float fuse = frag ? Mathf.Max(0f, WeaponDefs.FragFuse - heldFor)
                : cookType == 1 ? WeaponDefs.SmokeFuse : cookType == 2 ? WeaponDefs.RopeFuse : WeaponDefs.MolotovMaxFlight;
            bool inHand = frag && fuse <= 0f;
            Vector3 origin = inHand ? head.position : head.position + head.forward * 0.45f + head.right * 0.15f;
            Vector3 vel = inHand ? Vector3.zero : head.forward * 14f + Vector3.up * 3.5f + Velocity * 0.5f;
            ThrowGrenadeRpc(origin, vel, fuse, cookType);
            audioSource.PlayOneShot(Sfx.Reload, 0.5f);
            if (inv.ThrowCount(cookType) == 0)
            {
                // Out of that kind: pick another, or put the guns back up.
                int nt = inv.NextThrowable(cookType);
                if (nt >= 0) inv.Throwable = nt;
                else if (inv.Active == Inventory.ThrowSlot) inv.Active = inv.W1 >= 0 ? 1 : 0;
            }
        }
    }

    [Rpc(SendTo.Server)]
    void ThrowGrenadeRpc(Vector3 origin, Vector3 velocity, float fuse, int type)
    {
        if (IsDead || InDuel) return;
        if ((origin - head.position).sqrMagnitude > 4f) origin = head.position;
        Protected.Value = false; // throwing ends spawn protection
        var id = WeaponDefs.Throwables[Mathf.Clamp(type, 0, WeaponDefs.Throwables.Length - 1)];
        float maxFuse = id == WeaponId.FragGrenade ? WeaponDefs.FragFuse : WeaponDefs.MolotovMaxFlight;
        Projectile.Launch(id, origin, Vector3.ClampMagnitude(velocity, 30f), Mathf.Clamp(fuse, 0f, maxFuse), this);
    }

    [Rpc(SendTo.Server)]
    void LaunchRpc(Vector3 origin, Vector3 direction, int weapon)
    {
        if (IsDead || InDuel || MatchRules.IsMatchOver) return;
        var def = WeaponDefs.Get(weapon);
        if (def == null || def.Projectile == ProjectileKind.None || !HasWeapon((WeaponId)weapon)) return;
        if (Time.time < serverNextFire) return;
        serverNextFire = Time.time + def.FireInterval * 0.7f;
        if ((origin - head.position).sqrMagnitude > 6f) origin = head.position;
        Vector3 dir = direction.normalized;
        Projectile.Launch(def.Id, origin + dir * 0.9f, dir * def.ProjectileSpeed, 0f, this);
        ShotEffectsRpc(weapon, System.Array.Empty<ShotImpact>());
    }

    /// <summary>Server: knock a survivor around (blast push / rocket jumps).</summary>
    public void ServerPush(Vector3 velocity) => PushRpc(velocity);

    [Rpc(SendTo.Owner)]
    void PushRpc(Vector3 velocity)
    {
        momentum += new Vector3(velocity.x, 0f, velocity.z);
        verticalVelocity = Mathf.Max(verticalVelocity, 0f) + velocity.y;
    }

    /// <summary>Server: show the attacker a hit marker for explosive damage.</summary>
    public void ServerConfirmHit(int damage, bool headshot, bool killed, Vector3 point) => HitConfirmRpc(damage, headshot, killed, point);

    /// <summary>Every client: shake the local player's view if they're near a blast.</summary>
    public static void ShakeFromExplosion(Vector3 at, float radius)
    {
        if (Local == null || Local.playerCamera == null) return;
        float d = Vector3.Distance(Local.head.position, at);
        float k = 1f - Mathf.Clamp01(d / (radius * 5f));
        if (k <= 0f) return;
        shake = Mathf.Max(shake, k * k * 0.6f); // fine jitter on top of the rock
        // Rock: a big swaying roll and pitch that rings out, away from the side the blast came from.
        var cam = Local.playerCamera.transform;
        Vector3 local = cam.InverseTransformDirection(at - cam.position);
        if (k * k > rockStrength * Mathf.Exp(-(Time.time - rockTime) * 2.2f))
        {
            rockStrength = k * k;
            rockTime = Time.time;
            rockSide = local.x >= 0f ? -1f : 1f;
        }
        // Dust and debris thrown across the screen, heavier toward the blast side, for close ones.
        float cover = 1f - Mathf.Clamp01(d / (radius * 3f));
        if (cover > 0f)
        {
            var dir = new Vector2(local.x, local.z < 0f ? -0.4f : local.y);
            GameUI.ExplosionOverlay(cover, dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.zero);
        }
    }

    static float rockStrength, rockTime = -10f, rockSide = 1f;

    [Rpc(SendTo.Owner)]
    void GiveAmmoRpc(int slot, int amount)
    {
        inv.SetReserve(slot, Mathf.Min(999, inv.Reserve(slot) + amount));
        audioSource.PlayOneShot(Sfx.Pickup, 0.6f);
    }

    // ------------------------------------------------------------------ server: broadcasts

    public void BroadcastKillFeed(FixedString128Bytes text) => KillFeedRpc(text);

    [Rpc(SendTo.ClientsAndHost)]
    void KillFeedRpc(FixedString128Bytes text)
    {
        GameUI.AddKillFeed(text.ToString());
        Debug.Log($"[FFA] Kill: {text}");
    }

    public void BroadcastMatchState(FixedString32Bytes winner) => MatchStateRpc(winner);

    [Rpc(SendTo.ClientsAndHost)]
    void MatchStateRpc(FixedString32Bytes winner) => GameUI.SetWinner(winner.ToString());

    // ------------------------------------------------------------------ shared

    // ------------------------------------------------------------------ grappling hook

    /// <summary>Is the crosshair on a visible surface within hook range?</summary>
    bool GrappleTarget(out RaycastHit hit)
    {
        // Invisible boundary walls have no renderer and can't be hooked.
        return Physics.Raycast(head.position, head.forward, out hit, GrappleRange, 1, QueryTriggerInteraction.Ignore)
               && hit.collider.GetComponent<Renderer>() != null;
    }

    void UpdateGrapple(PlayerInput input, ref Vector3 move)
    {
        if (input.Grapple)
        {
            if (grappling) ReleaseGrapple(false);
            else if (HasGrapple.Value) TryFireGrapple();
        }
        if (!grappling) return;

        Vector3 to = grappleAnchor - (transform.position + Vector3.up);
        float dist = to.magnitude;
        float age = Time.time - grappleStart;
        if (age < RopeShootTime) { move *= 0.5f; return; } // rope still flying out
        if (input.Jump) { ReleaseGrapple(true); return; }
        if (dist < 1.6f || age > 3.5f) { ReleaseGrapple(to.y > 0.3f); return; } // arrived: pop up onto the ledge

        momentum = Vector3.MoveTowards(momentum, to / dist * GrappleSpeed, GrappleAccel * Time.deltaTime);
        verticalVelocity = 0f;
        move *= 0.35f; // a little air control while reeling in
    }

    void TryFireGrapple()
    {
        if (GrappleCooldownLeft > 0f || !GrappleTarget(out var hit))
        {
            audioSource.PlayOneShot(Sfx.DryFire, 0.5f);
            return;
        }
        if (sliding) EndSlide(false);
        if (stance != Stance.Stand) TrySetStance(Stance.Stand);
        grappling = true;
        grappleAnchor = hit.point;
        grappleStart = Time.time;
        grappleReadyAt = Time.time + GrappleCooldown;
        momentum += Vector3.up * verticalVelocity; // keep current fall/jump speed as part of the swing
        verticalVelocity = 0f;
        audioSource.PlayOneShot(Sfx.GrappleFire, 0.8f);
    }

    void ReleaseGrapple(bool hop)
    {
        grappling = false;
        verticalVelocity = momentum.y + (hop ? 5f : 0f);
        momentum.y = 0f;
    }

    /// <summary>Owner test hook (-ffagrapple): look up a little and fire.</summary>
    public void TestGrapple(float pitchUp)
    {
        if (!IsOwner) return;
        if (IsServer) HasGrapple.Value = true;
        pitch = -pitchUp;
        head.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        TryFireGrapple();
        Debug.Log($"[FFA] Test grapple: hasHook={HasGrapple.Value} hooked={grappling}");
    }

    void UpdateRopeVisual()
    {
        bool active = !IsDead && (IsOwner ? grappling : netGrapple.Value.w > 0.5f);
        Vector3 anchor = IsOwner ? grappleAnchor : (Vector3)netGrapple.Value;
        if (active && !ropeWasActive)
        {
            ropeShownAt = Time.time;
            if (!IsOwner) audioSource.PlayOneShot(Sfx.GrappleFire, 0.8f);
        }
        ropeWasActive = active;

        if (rope == null)
        {
            var ropeGo = new GameObject("GrappleRope");
            rope = ropeGo.AddComponent<LineRenderer>();
            rope.sharedMaterial = GameAssets.I.tracer;
            rope.positionCount = 12;
            rope.widthMultiplier = 0.015f;
            rope.startColor = rope.endColor = new Color(0.12f, 0.12f, 0.12f);
            rope.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            hookVisual = BuildHook().transform;
        }
        rope.enabled = active;
        hookVisual.gameObject.SetActive(active);
        if (!active) return;

        // From the left wrist (bottom-left of the view in first person) out to the hook.
        Vector3 start = IsLocalHuman
            ? playerCamera.transform.TransformPoint(new Vector3(-0.22f, -0.28f, 0.45f))
            : transform.TransformPoint(new Vector3(-0.3f, 1.3f, 0.25f));
        float shoot = Mathf.Clamp01((Time.time - ropeShownAt) / RopeShootTime);
        Vector3 end = Vector3.Lerp(start, anchor, shoot);
        float wobble = 0.15f * (1f - Mathf.Clamp01((Time.time - ropeShownAt) / 0.5f));
        Vector3 side = Vector3.Cross((end - start).normalized, Vector3.up);
        if (side.sqrMagnitude < 0.01f) side = Vector3.right;
        for (int i = 0; i < rope.positionCount; i++)
        {
            float t = i / (rope.positionCount - 1f);
            Vector3 p = Vector3.Lerp(start, end, t) + side.normalized * Mathf.Sin(t * Mathf.PI * 3f + Time.time * 30f) * wobble * Mathf.Sin(t * Mathf.PI);
            rope.SetPosition(i, p);
        }
        hookVisual.position = end;
        if ((end - start).sqrMagnitude > 0.001f) hookVisual.rotation = Quaternion.LookRotation(end - start);
    }

    static GameObject BuildHook()
    {
        var root = new GameObject("GrappleHook");
        void Piece(PrimitiveType type, Vector3 pos, Vector3 scale, Vector3 euler)
        {
            var go = GameObject.CreatePrimitive(type);
            DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = GameAssets.I.gunMetal;
        }
        Piece(PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.04f, 0.08f, 0.04f), new Vector3(90f, 0f, 0f));
        for (int i = 0; i < 3; i++)
        {
            var dir = Quaternion.Euler(0f, 0f, i * 120f) * Vector3.up;
            Piece(PrimitiveType.Cube, dir * 0.06f + Vector3.forward * 0.03f, new Vector3(0.015f, 0.1f, 0.015f), new Vector3(-40f, 0f, i * 120f));
        }
        return root;
    }

    public override void OnDestroy()
    {
        if (rope != null) Destroy(rope.gameObject);
        if (hookVisual != null) Destroy(hookVisual.gameObject);
        base.OnDestroy();
    }

    // ------------------------------------------------------------------ stances: crouch, prone, slide

    static float ShapeHeight(Stance s) => s switch { Stance.Crouch => 1.4f, Stance.Prone => 0.8f, Stance.Slide => 1.0f, _ => 2f };
    static float EyeHeight(Stance s) => s switch { Stance.Crouch => 1.15f, Stance.Prone => 0.4f, Stance.Slide => 0.85f, _ => 1.6f };

    void UpdateStance(PlayerInput input)
    {
        if (brain != null && brain.poseOverride >= 0)
        {
            // Dev/test: hold a fixed pose.
            var forced = (Stance)brain.poseOverride;
            sliding = forced == Stance.Slide;
            stance = sliding ? Stance.Stand : forced;
            slideSpeed = 0f;
            ApplyControllerShape(CurrentStance);
            return;
        }

        float speed = new Vector2(Velocity.x, Velocity.z).magnitude;
        bool grounded = controller.isGrounded;
        // Holding Ctrl in the air queues a slide: touch down and you keep all your speed, sliding along the ground.
        if (!grounded && !sliding)
        {
            airHorizontal = new Vector3(Velocity.x, 0f, Velocity.z);
            if (input.CrouchHeld && stance == Stance.Stand) airSlideQueued = true;
        }
        if (!input.CrouchHeld) airSlideQueued = false;
        if (grounded && airSlideQueued && !sliding)
        {
            airSlideQueued = false;
            if (stance == Stance.Stand && airHorizontal.magnitude > 3f)
            {
                BeginSlide(airHorizontal, Mathf.Max(airHorizontal.magnitude, walkSpeed) + 1f);
                momentum = Vector3.zero; // folded into the slide (it was part of the airborne velocity)
            }
            else
                TrySetStance(Stance.Crouch);
        }
        if (input.CrouchDown && !sliding && (grounded || stance != Stance.Stand))
        {
            if (stance == Stance.Stand && controller.isGrounded && speed > 5.5f)
                StartSlide(speed);
            else if (stance == Stance.Crouch)
                TrySetStance(Stance.Stand);
            else
                TrySetStance(Stance.Crouch);
        }
        if (input.ProneToggle)
        {
            if (sliding) EndSlide(false);
            if (stance == Stance.Prone) { if (!TrySetStance(Stance.Stand)) TrySetStance(Stance.Crouch); }
            else TrySetStance(Stance.Prone);
        }
        if (sliding)
        {
            slideSpeed = Mathf.MoveTowards(slideSpeed, 0f, 8.5f * Time.deltaTime);
            float maxTime = 1.3f + Mathf.Max(0f, slideStartSpeed - 12.5f) * 0.08f; // faster entries slide further
            bool done = !input.CrouchHeld || slideSpeed < 3f || Time.time - slideStart > maxTime;
            if (done) EndSlide(input.CrouchHeld);
        }
    }

    // Grenades and camera shake
    bool cooking;
    int cookType;
    float cookStart, nextGrenadeTime;
    public bool IsCookingGrenade => cooking && cookType == 0; // only frags have a cook timer
    public float CookTimeLeft => cooking ? Mathf.Max(0f, WeaponDefs.FragFuse - (Time.time - cookStart)) : 0f;
    static float shake;
    public Vector3 ChestPoint => transform.position + Vector3.up * controller.height * 0.62f;

    float testStanceTimer;
    bool forceFireToggle;
    float throwTestTimer;
    bool testStanceFired;

    /// <summary>Scripted input for -ffastance tests: crouch, go prone, or sprint forward and slide.</summary>
    PlayerInput TestStanceInput(PlayerInput input)
    {
        testStanceTimer += Time.deltaTime;
        int mode = GameUI.TestStance;
        if (mode == 5)
        {
            // Flyhack: hold Space for 2 s, then hover.
            input.JumpHeld = testStanceTimer > 0.5f && testStanceTimer < 2.5f;
            return input;
        }
        if (mode == 4)
        {
            // Air slide: sprint, jump, hold Ctrl in the air and keep holding through the landing.
            input.Move = new Vector2(0f, 1f);
            input.Sprint = true;
            input.Jump = testStanceTimer > 1.0f && !testStanceFired;
            if (input.Jump) testStanceFired = true;
            input.CrouchHeld = testStanceTimer > 1.15f && testStanceTimer < 3.5f;
            return input;
        }
        if (mode == 3)
        {
            input.Move = new Vector2(0f, 1f);
            input.Sprint = true;
            if (testStanceTimer > 1.2f)
            {
                input.CrouchHeld = true;
                input.CrouchDown = !testStanceFired;
                testStanceFired = true;
            }
            return input;
        }
        if (testStanceTimer > 0.5f && !testStanceFired)
        {
            testStanceFired = true;
            if (mode == 1) input.CrouchDown = true;
            if (mode == 2) input.ProneToggle = true;
        }
        return input;
    }

    void StartSlide(float speed) => BeginSlide(new Vector3(Velocity.x, 0f, Velocity.z), Mathf.Max(speed, sprintSpeed) + 3.5f);

    void BeginSlide(Vector3 horizontalVelocity, float speed)
    {
        slideDir = horizontalVelocity.sqrMagnitude > 0.1f ? horizontalVelocity.normalized : transform.forward;
        slideSpeed = speed;
        slideStartSpeed = speed;
        if (GameUI.TestStance >= 0) Debug.Log($"[FFA] Slide: start speed {speed:0.0} m/s (moving {horizontalVelocity.magnitude:0.0})");
        slideStart = Time.time;
        sliding = true;
        ApplyControllerShape(Stance.Slide);
        audioSource.PlayOneShot(Sfx.Slide, 0.7f);
    }

    void EndSlide(bool toCrouch)
    {
        sliding = false;
        if (toCrouch || !TrySetStance(Stance.Stand))
            TrySetStance(Stance.Crouch);
        ApplyControllerShape(stance);
    }

    /// <summary>Change stance if there is room (standing up under a low ceiling fails).</summary>
    bool TrySetStance(Stance s)
    {
        float newH = ShapeHeight(s);
        if (newH > controller.height + 0.01f)
        {
            float r = controller.radius;
            Vector3 p = transform.position;
            if (Physics.CheckCapsule(p + Vector3.up * (r + 0.05f), p + Vector3.up * (newH - r), r * 0.9f, 1, QueryTriggerInteraction.Ignore))
                return false;
        }
        stance = s;
        ApplyControllerShape(sliding ? Stance.Slide : s);
        return true;
    }

    void ApplyControllerShape(Stance s)
    {
        if (appliedShape == s) return;
        appliedShape = s;
        float h = ShapeHeight(s);
        controller.height = h;
        controller.center = new Vector3(0f, h * 0.5f, 0f);
    }

    /// <summary>Smoothly blend camera height and body pose toward the current stance (runs on every client).</summary>
    void UpdatePoseWeights()
    {
        var s = CurrentStance;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        // Everything eases in and out (no linear ramps that start and stop with a jolt).
        headHeight = Mathf.SmoothDamp(headHeight, EyeHeight(s), ref headHeightVel, s == Stance.Slide ? 0.07f : 0.11f);
        crouchT = Mathf.SmoothDamp(crouchT, s == Stance.Crouch ? 1f : 0f, ref crouchVel, 0.1f);
        proneT = Mathf.SmoothDamp(proneT, s == Stance.Prone ? 1f : 0f, ref proneVel, 0.2f);
        slideT = Mathf.SmoothDamp(slideT, s == Stance.Slide ? 1f : 0f, ref slideVel, 0.07f);
        // Airborne (for the body's tucked-legs pose): our own controller, or a short ground probe for others.
        bool off = IsOwner ? !controller.isGrounded && !sliding
                           : !Physics.Raycast(transform.position + Vector3.up * 0.25f, Vector3.down, 0.6f, 1, QueryTriggerInteraction.Ignore);
        airTimer = off && !IsDead ? airTimer + dt : 0f;
        airT = Mathf.SmoothDamp(airT, airTimer > 0.12f ? 1f : 0f, ref airVel, 0.12f);
        if (body != null)
        {
            body.crouch = crouchT;
            body.prone = proneT;
            body.slide = slideT;
            body.airborne = airT;
        }
        if (IsLocalHuman && playerCamera != null)
            playerCamera.fieldOfView += slideT * 8f;
    }

    public void SetPoseOverride(int pose)
    {
        if (brain != null) brain.poseOverride = pose;
    }
    const float LeanDistance = 0.36f, LeanRoll = 14f;

    /// <summary>Head pivot (camera, weapon, head hitbox) offset sideways and rolled by the lean amount.</summary>
    void ApplyHeadPose(float headPitch, float leanAmount)
    {
        float landDip = IsLocalHuman ? landAmount * 0.08f * Mathf.Sin(Mathf.Clamp01((Time.time - landTime) / 0.38f) * Mathf.PI) : 0f;
        head.localPosition = new Vector3(leanAmount * LeanDistance, headHeight - Mathf.Abs(leanAmount) * 0.07f - landDip, 0f);
        head.localRotation = Quaternion.Euler(0f, 0f, -leanAmount * LeanRoll - slideT * 7f) * Quaternion.Euler(headPitch, 0f, 0f);
        if (IsLocalHuman)
        {
            float rt = Time.time - rockTime;
            float env = rockStrength * Mathf.Exp(-rt * 2.2f);
            if (env > 0.002f)
                head.localRotation *= Quaternion.Euler(-Mathf.Sin(rt * 7f + 0.6f) * 5f * env, Mathf.Sin(rt * 5.3f) * 2.5f * env,
                                                       Mathf.Sin(rt * 9f) * 9f * env * rockSide);
        }
        if (IsLocalHuman && shake > 0.001f)
        {
            head.localRotation *= Quaternion.Euler(Random.Range(-1f, 1f) * shake * 4f, Random.Range(-1f, 1f) * shake * 4f, Random.Range(-1f, 1f) * shake * 2f);
            shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 2.2f);
        }
    }

    /// <summary>Stop the lean short of walls so the camera never pokes through them.</summary>
    float LimitLean(float want)
    {
        if (Mathf.Abs(want) < 0.01f) return 0f;
        Vector3 from = transform.TransformPoint(new Vector3(0f, headHeight, 0f));
        Vector3 dir = transform.right * Mathf.Sign(want);
        float dist = Mathf.Abs(want) * LeanDistance;
        if (Physics.SphereCast(from, 0.18f, dir, out var hit, dist, 1, QueryTriggerInteraction.Ignore))
            return Mathf.Sign(want) * Mathf.Max(0f, hit.distance - 0.02f) / LeanDistance;
        return want;
    }

    /// <summary>A remote player's skin arrived (or changed): rebuild their body in the new outfit.</summary>
    void RebuildBody(int skin)
    {
        if (skin == builtSkin || body == null) return;
        builtSkin = skin;
        Destroy(body.gameObject);
        body = BodyModel.Build(transform, PlayerColor, false, skin);
        if (IsLocalHuman) body.ConfigureFirstPerson();
        builtWeapon = -2; // re-attach the hands to the weapon
        ApplyAliveState();
    }

    void OnHealthChanged(int previous, int current)
    {
        ApplyAliveState();
        if (IsLocalHuman && current < previous) GameUI.FlashDamage(previous - current);
    }

    /// <summary>Server: start the short spawn invulnerability (respawns, golden-duel winners).</summary>
    void ServerProtect()
    {
        Protected.Value = true;
        protectEndsAt = Time.time + SpawnProtectSeconds;
        protectStartedLocal = Time.time;
    }

    // ------------------------------------------------------------------ rope snare

    /// <summary>Server: a rope grenade went off at `anchor` and caught this player.</summary>
    public void ServerSnare(Vector3 anchor, float seconds)
    {
        SnareAnchor.Value = anchor;
        SnareStartDistance.Value = Vector3.Distance(anchor, ChestPoint);
        SnaredUntil.Value = ServerNow + seconds;
        SnaredRpc();
    }

    [Rpc(SendTo.Owner)]
    void SnaredRpc()
    {
        momentum = Vector3.zero;
        if (IsLocalHuman) GameUI.ShowBanner("SNARED!", "Tied to the rope grenade for 10 s: move freely, but you can't get any farther from it.", new Color(0.95f, 0.7f, 0.35f));
    }

    /// <summary>Rope length: how far from the grenade they were when it caught them. They can roam anywhere inside it.</summary>
    public float TetherReach => Mathf.Max(WeaponDefs.MinTether, SnareStartDistance.Value);

    /// <summary>Owner: the rope stops you at its reach (you're held at the edge and lose any outward speed).</summary>
    void EnforceTether()
    {
        Vector3 anchor = SnareAnchor.Value;
        Vector3 waist = transform.position + Vector3.up * controller.height * 0.5f;
        Vector3 off = waist - anchor;
        float dist = off.magnitude, reach = TetherReach;
        if (dist <= reach || dist < 0.01f) return;
        Vector3 dir = off / dist;
        controller.Move(-dir * (dist - reach));
        // Kill any speed carrying you further out (swings, blast pushes, slides, rising while flying).
        float outward = Vector3.Dot(momentum, dir);
        if (outward > 0f) momentum -= dir * outward;
        if (sliding && Vector3.Dot(slideDir, dir) > 0.3f) EndSlide(false);
        if (verticalVelocity > 0f && dir.y > 0.5f) verticalVelocity = 0f;
    }

    static Material ropeMaterial;
    LineRenderer[] snareLines;

    /// <summary>Every client: ropes from the grenade to the snared player, and loops binding their waist and knees.</summary>
    void UpdateSnareVisual()
    {
        bool on = Snared && body != null;
        if (!on)
        {
            if (snareLines != null) foreach (var l in snareLines) l.enabled = false;
            return;
        }
        if (snareLines == null)
        {
            if (ropeMaterial == null) ropeMaterial = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.78f, 0.62f, 0.38f) };
            snareLines = new LineRenderer[5];
            for (int i = 0; i < snareLines.Length; i++)
            {
                var l = new GameObject("SnareRope").AddComponent<LineRenderer>();
                l.transform.SetParent(transform, false);
                l.sharedMaterial = ropeMaterial;
                l.widthMultiplier = i < 3 ? 0.035f : 0.045f;
                l.numCapVertices = 2;
                l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                l.useWorldSpace = true;
                snareLines[i] = l;
            }
        }
        Vector3 anchor = SnareAnchor.Value, root = transform.position;
        float h = controller != null ? controller.height : 1.8f;
        // Two ropes from the grenade to the waist: slack when you're close, pulled straight at full reach.
        float slack = 1f - Mathf.Clamp01(Vector3.Distance(anchor, root + Vector3.up * h * 0.5f) / Mathf.Max(0.5f, TetherReach));
        for (int i = 0; i < 3; i++)
        {
            var l = snareLines[i];
            l.enabled = i < 2;
            if (!l.enabled) continue;
            Vector3 side = transform.right * ((i - 0.5f) * 0.14f);
            Vector3 end = root + Vector3.up * h * 0.5f + side;
            const int n = 12;
            l.positionCount = n;
            for (int k = 0; k < n; k++)
            {
                float t = k / (n - 1f);
                Vector3 p = Vector3.Lerp(anchor + Vector3.up * 0.1f, end, t);
                p.y -= Mathf.Sin(t * Mathf.PI) * (0.08f + 1.2f * slack); // sag
                l.SetPosition(k, p);
            }
        }
        // A loop around the waist (the knees are free now).
        snareLines[4].enabled = false;
        for (int i = 3; i < 4; i++)
        {
            var l = snareLines[i];
            l.enabled = true;
            float y = h * 0.52f;
            const int n = 14;
            l.positionCount = n;
            for (int k = 0; k < n; k++)
            {
                float a = k / (n - 1f) * Mathf.PI * 2f;
                l.SetPosition(k, root + new Vector3(Mathf.Cos(a) * 0.27f, y + Mathf.Sin(a * 3f) * 0.02f, Mathf.Sin(a) * 0.22f));
            }
        }
    }

    // ------------------------------------------------------------------ lobby

    /// <summary>Server: pull this player out of the world into the waiting lobby (match over / host back in lobby).</summary>
    public void ServerEnterLobby()
    {
        DuelManager.Abort(this);
        if (InDuel) DuelSlot.Value = 0;
        if (respawnRoutine != null)
        {
            StopCoroutine(respawnRoutine);
            respawnRoutine = null;
        }
        Health.Value = MaxHealth;
        InLobby.Value = true;
    }

    /// <summary>Server: drop into the match at a spawn point with a fresh loadout.</summary>
    public void ServerLeaveLobby()
    {
        InLobby.Value = false;
        Respawn();
    }

    void OnLobbyChanged(bool previous, bool now)
    {
        if (IsOwner && now) Ready.Value = botOnServer; // everyone readies up again for the next match
        ApplyAliveState();
        ApplyLobbyView();
    }

    /// <summary>The local player's cameras and ears are off while waiting (the menu camera flies over the map).</summary>
    void ApplyLobbyView()
    {
        if (!IsLocalHuman) return;
        bool inWorld = !InLobby.Value;
        playerCamera.enabled = inWorld;
        viewModelCamera.enabled = inWorld;
        audioListener.enabled = inWorld;
        if (fpArms != null) fpArms.gameObject.SetActive(inWorld);
    }

    /// <summary>The killcam looks through this player's (past) eyes: keep their live body out of the shot.</summary>
    public void SetHiddenForKillcam(bool hidden)
    {
        if (!hidden) { ApplyAliveState(); return; }
        if (body != null) body.SetVisible(false);
        if (weaponRig != null) weaponRig.gameObject.SetActive(false);
    }

    void ApplyAliveState()
    {
        bool alive = !IsDead && !InLobby.Value;
        controller.enabled = alive;
        if (headHitbox != null) headHitbox.enabled = alive;
        if (body != null) body.SetVisible(alive);
        if (weaponRig != null) weaponRig.gameObject.SetActive(alive);
    }
}
