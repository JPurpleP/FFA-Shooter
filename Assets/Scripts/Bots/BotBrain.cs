using System.Collections.Generic;
using UnityEngine;

public enum BotMode
{
    Dummy,   // stands still: target practice
    Strafer, // strafes side to side and hops, never shoots
    Turret,  // stands still and shoots at whoever it can see
    Fighter, // roams the map, grabs weapons, hunts players
}

/// <summary>
/// Drives an FpsPlayer by producing PlayerInput each frame. Runs only on the owner of the player
/// (the server for bots, or the local client for automated tests).
/// </summary>
public class BotBrain : MonoBehaviour
{
    public BotMode mode = BotMode.Fighter;
    public string botName = "Bot";
    public bool invulnerable;
    public WeaponId loadout = WeaponId.None;
    public Vector3? fixedSpawn;
    public int poseOverride = -1; // dev: force a Stance (0 stand, 1 crouch, 2 prone, 3 slide)
    public float fixedYaw;

    public float ExtraSpread => mode == BotMode.Turret || mode == BotMode.Fighter ? (1f - DevSettings.BotAccuracy) * 3f : 0f;
    public string Status { get; private set; } = "";

    FpsPlayer me;
    FpsPlayer target;
    public FpsPlayer CurrentTarget => target;
    float retargetTime, seenSince = -1f, aimNoiseTime, strafeFlip, burstToggle, jumpTime;
    Vector3 aimNoise;
    float strafeDir = 1f;
    bool burstOn = true, semiToggle;

    List<Vector3> path;
    int pathIndex;
    Vector3 goal;
    float repathTime, stuckTime;
    Vector3 lastPos;
    WeaponPickup goalPickup;
    Vector3 lastSeenTargetPos;
    float lastSeenTime = -10f;

    public PlayerInput Think(FpsPlayer player)
    {
        me = player;
        var input = new PlayerInput { SwitchTo = -1, AbsoluteLook = true, Yaw = me.Yaw, Pitch = me.Pitch };
        if (me.IsDead)
        {
            path = null;
            target = null;
            seenSince = -1f;
            return input;
        }
        if (me.InDuel)
        {
            Brawl(ref input);
            return input;
        }

        switch (mode)
        {
            case BotMode.Dummy:
                Status = "standing";
                break;
            case BotMode.Strafer:
                Strafe(ref input, 1.2f, 2.2f);
                if (Time.time > jumpTime) { input.Jump = Random.value < 0.4f; jumpTime = Time.time + Random.Range(1.5f, 3f); }
                Status = "strafing";
                break;
            case BotMode.Turret:
                PickWeapon(ref input);
                if (Combat(ref input, 70f)) Status = $"shooting {target.DisplayName}";
                else Status = "watching";
                break;
            case BotMode.Fighter:
                PickWeapon(ref input);
                bool fighting = Combat(ref input, 90f);
                if (fighting)
                {
                    Status = $"fighting {target.DisplayName}";
                    Strafe(ref input, 0.6f, 1.6f);
                    float dist = Vector3.Distance(me.transform.position, target.transform.position);
                    var def = me.CurrentDef;
                    bool closeRange = def != null && def.Pellets > 1;
                    if (dist > (closeRange ? 6f : 25f)) MoveAlongPath(ref input, target.transform.position, false, 0.8f);
                    else if (dist < 5f && !closeRange) input.Move.y = -0.7f;
                }
                else
                    Roam(ref input);
                break;
        }
        return input;
    }

    // ------------------------------------------------------------------ combat

    /// <summary>Golden duel: close in on the opponent and keep swinging, circling a little.</summary>
    void Brawl(ref PlayerInput input)
    {
        path = null;
        target = null;
        var opp = me.DuelOpponent;
        Status = opp != null ? $"brawling {opp.DisplayName}" : "brawling";
        if (opp == null || opp.IsDead) return;
        Vector3 to = opp.transform.position + Vector3.up * 1.3f - me.Head.position;
        float wantYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        float wantPitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
        float turn = Mathf.Lerp(240f, 600f, DevSettings.BotAccuracy) * Time.deltaTime;
        input.Yaw = Mathf.MoveTowardsAngle(me.Yaw, wantYaw, turn);
        input.Pitch = Mathf.MoveTowardsAngle(me.Pitch, wantPitch, turn);
        Vector3 flat = new Vector3(to.x, 0f, to.z);
        float dist = flat.magnitude;
        Vector3 local = Quaternion.Inverse(Quaternion.Euler(0f, me.Yaw, 0f)) * flat.normalized;
        if (dist > 1.3f) input.Move = new Vector2(local.x, local.z);
        Strafe(ref input, 0.5f, 1.4f);
        input.Move.x *= dist < 2.5f ? 0.6f : 0.2f;
        input.Sprint = dist > 4f;
        bool facing = Mathf.Abs(Mathf.DeltaAngle(me.Yaw, wantYaw)) < 25f;
        input.FireHeld = input.FirePressed = facing && dist < 1.9f && Random.value < 0.9f;
    }

    bool Combat(ref PlayerInput input, float range)
    {
        if (Time.time > retargetTime)
        {
            retargetTime = Time.time + 0.3f;
            var best = FindTarget(range);
            if (best != target) seenSince = best != null ? Time.time : -1f;
            target = best;
        }
        if (target == null || target.IsDead || !CanSee(target))
        {
            target = null;
            return false;
        }
        lastSeenTargetPos = target.transform.position;
        lastSeenTime = Time.time;

        // Aim with a wandering error that shrinks with accuracy (aimbot streak: none at all).
        float acc = me.AimbotActive ? 1f : DevSettings.BotAccuracy;
        if (Time.time > aimNoiseTime)
        {
            aimNoiseTime = Time.time + 0.35f;
            aimNoise = me.AimbotActive ? Vector3.zero : Random.insideUnitSphere * Mathf.Lerp(0.9f, 0.05f, acc);
        }
        Vector3 aimPoint = target.transform.position + Vector3.up * (acc > 0.85f && Random.value < 0.02f ? 1.7f : 1.25f) + aimNoise + target.Velocity * 0.05f;
        Vector3 to = aimPoint - me.Head.position;
        float wantYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        float wantPitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
        float turn = me.AimbotActive ? 100000f : Mathf.Lerp(180f, 720f, acc) * Time.deltaTime;
        input.Yaw = Mathf.MoveTowardsAngle(me.Yaw, wantYaw, turn);
        input.Pitch = Mathf.MoveTowardsAngle(me.Pitch, wantPitch, turn);

        var def = me.CurrentDef;
        float dist = to.magnitude;
        input.Aim = def != null && (def.HasScope || (dist > 18f && def.AdsSpread < def.HipSpread));
        float reaction = me.AimbotActive ? 0f : Mathf.Lerp(0.8f, 0.15f, acc);
        bool onTarget = Mathf.Abs(Mathf.DeltaAngle(input.Yaw, wantYaw)) < 4f && Mathf.Abs(Mathf.DeltaAngle(input.Pitch, wantPitch)) < 4f;
        if (def != null && onTarget && Time.time - seenSince > reaction && (!def.HasScope || me.AdsAmount > 0.95f))
        {
            if (def.Automatic)
            {
                if (Time.time > burstToggle)
                {
                    burstOn = !burstOn;
                    burstToggle = Time.time + (burstOn ? Random.Range(0.4f, 1f) : Random.Range(0.15f, 0.45f) * (1.2f - acc));
                }
                input.FireHeld = burstOn;
            }
            else
            {
                semiToggle = !semiToggle;
                input.FireHeld = semiToggle;
                input.FirePressed = semiToggle;
            }
        }
        if (me.CurrentMag == 0) input.Reload = true;
        return true;
    }

    FpsPlayer FindTarget(float range)
    {
        FpsPlayer best = null;
        float bestDist = range;
        foreach (var p in FpsPlayer.All)
        {
            if (p == me || !p.IsSpawned || p.IsDead || p.InDuel) continue;
            if (DevSettings.BotsTargetHumansOnly && p.IsBot.Value) continue;
            float d = Vector3.Distance(p.transform.position, me.transform.position);
            if (d < bestDist && CanSee(p))
            {
                bestDist = d;
                best = p;
            }
        }
        return best;
    }

    bool CanSee(FpsPlayer p)
    {
        Vector3 from = me.Head.position;
        Vector3 to = p.transform.position + Vector3.up * 1.3f;
        if (!Physics.Raycast(from, to - from, out var hit, Vector3.Distance(from, to) + 0.5f, GameAssets.ShotMask, QueryTriggerInteraction.Ignore))
            return false;
        return hit.collider.GetComponentInParent<FpsPlayer>() == p;
    }

    void PickWeapon(ref PlayerInput input)
    {
        var inv = me.Inv;
        int other = 1 - inv.Active;
        int cur = inv.Weapon(inv.Active), alt = inv.Weapon(other);
        bool curEmpty = inv.Mag(inv.Active) == 0 && inv.Reserve(inv.Active) == 0 && !DevSettings.BotsInfiniteAmmo;
        bool altUsable = alt >= 0 && (inv.Mag(other) > 0 || inv.Reserve(other) > 0);
        // Prefer anything over the starting pistol.
        if (altUsable && (curEmpty || (cur == (int)WeaponId.Glock17 && alt != (int)WeaponId.Glock17)))
            input.SwitchTo = other;
        if (target == null && me.CurrentDef != null && me.CurrentMag < me.CurrentDef.MagSize / 2 && me.CurrentReserve > 0 && !me.IsReloading)
            input.Reload = true;
    }

    void Strafe(ref PlayerInput input, float minTime, float maxTime)
    {
        if (Time.time > strafeFlip)
        {
            strafeDir = -strafeDir;
            strafeFlip = Time.time + Random.Range(minTime, maxTime);
        }
        input.Move.x = strafeDir;
    }

    // ------------------------------------------------------------------ navigation

    void Roam(ref PlayerInput input)
    {
        bool wantsWeapon = me.Inv.W1 < 0 || me.Inv.Weapon(me.Inv.Active) == (int)WeaponId.Glock17;
        if (goalPickup != null && !goalPickup.Available) { goalPickup = null; path = null; }
        if (path == null || Time.time > repathTime)
        {
            goalPickup = wantsWeapon ? NearestPickup() : null;
            if (goalPickup != null) goal = goalPickup.transform.position;
            else if (Time.time - lastSeenTime < 6f) goal = lastSeenTargetPos;
            else if (me.EspActive && NearestEnemy() is { } prey) goal = prey.transform.position; // ESP: hunt through walls
            else if (path == null || pathIndex >= path.Count) goal = BotNav.RandomNode();
            path = BotNav.FindPath(me.transform.position, goal);
            pathIndex = 0;
            repathTime = Time.time + 6f;
        }
        Status = goalPickup != null ? $"going for {goalPickup.Def?.Name}" : "roaming";
        MoveAlongPath(ref input, goal, true, 1f);

        if (goalPickup != null && Vector3.Distance(me.Head.position, goalPickup.transform.position + Vector3.up * 0.9f) < 2f)
            input.Interact = me.NearbyPickup == goalPickup;
        else if (me.NearbyPickup != null && wantsWeapon)
            input.Interact = true;
    }

    FpsPlayer NearestEnemy()
    {
        FpsPlayer best = null;
        float bestD = float.MaxValue;
        foreach (var p in FpsPlayer.All)
        {
            if (!me.IsEnemyOf(p)) continue;
            float d = (p.transform.position - me.transform.position).sqrMagnitude;
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }

    WeaponPickup NearestPickup()
    {
        WeaponPickup best = null;
        float bestD = 45f;
        foreach (var p in WeaponPickup.All)
        {
            if (!p.Available || p.Id == WeaponId.Glock17 || p.Id == WeaponId.FragGrenade || p.Id == WeaponId.GrappleHook) continue;
            float d = Vector3.Distance(p.transform.position, me.transform.position);
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }

    void MoveAlongPath(ref PlayerInput input, Vector3 destination, bool steerLook, float speed)
    {
        if (path == null || (path.Count > 0 && Vector3.Distance(path[path.Count - 1], destination) > 3f && Time.time > repathTime - 5f))
        {
            path = BotNav.FindPath(me.transform.position, destination);
            pathIndex = 0;
            repathTime = Time.time + 6f;
        }
        if (path == null || pathIndex >= path.Count)
        {
            path = null;
            return;
        }

        Vector3 next = path[pathIndex];
        Vector3 flat = next - me.transform.position;
        float dy = flat.y;
        flat.y = 0f;
        if (flat.magnitude < 0.7f && Mathf.Abs(dy) < 1.2f)
        {
            pathIndex++;
            return;
        }

        Vector3 local = Quaternion.Inverse(Quaternion.Euler(0f, me.Yaw, 0f)) * flat.normalized;
        float strafe = input.Move.x;
        input.Move = new Vector2(local.x, local.z) * speed;
        if (Mathf.Abs(strafe) > 0f && !steerLook) input.Move.x = Mathf.Clamp(input.Move.x + strafe * 0.6f, -1f, 1f);
        input.Sprint = steerLook && flat.magnitude > 3f;
        if (steerLook)
        {
            float wantYaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            input.Yaw = Mathf.MoveTowardsAngle(me.Yaw, wantYaw, 400f * Time.deltaTime);
            input.Pitch = Mathf.MoveTowards(me.Pitch, 0f, 200f * Time.deltaTime);
        }

        // Unstick: hop, then give up on this route.
        float moved = Vector3.Distance(me.transform.position, lastPos);
        lastPos = me.transform.position;
        stuckTime = moved < 0.5f * Time.deltaTime * 6f ? stuckTime + Time.deltaTime : 0f;
        if (stuckTime > 0.6f) input.Jump = true;
        if (stuckTime > 2f)
        {
            stuckTime = 0f;
            path = null;
            goal = BotNav.RandomNode();
            goalPickup = null;
        }
    }
}
