using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Watching the players still in the match after you're out of lives: through their eyes (with their gun, aiming
/// down sights when they do) or a chase camera behind them. Fire / aim switch player, jump switches the view.
/// Drives the local player's own camera.
/// </summary>
public static class Spectator
{
    public static bool Active { get; private set; }
    public static bool FirstPerson { get; private set; } = true;
    public static FpsPlayer Target { get; private set; }
    /// <summary>The target is looking through a scope (the HUD draws it).</summary>
    public static bool Scoped { get; private set; }

    static int index;
    static FpsPlayer hidden;
    static GameObject gun;
    static WeaponId gunId = WeaponId.None;
    static float ads;
    static readonly List<FpsPlayer> alive = new();

    /// <summary>Local player, out of lives: aim the camera at someone. False when there's nobody left to watch.</summary>
    public static bool Drive(FpsPlayer me)
    {
        alive.Clear();
        foreach (var p in FpsPlayer.All)
            if (p != me && p != null && p.IsSpawned && !p.IsDead && !p.Eliminated.Value) alive.Add(p);
        if (alive.Count == 0) { Stop(); return false; }

        bool menus = !GameUI.CursorCaptured;
        var mouse = Mouse.current;
        var pad = Gamepad.current;
        if (!menus)
        {
            if ((mouse != null && mouse.leftButton.wasPressedThisFrame) || (pad != null && pad.rightShoulder.wasPressedThisFrame)) index++;
            if ((mouse != null && mouse.rightButton.wasPressedThisFrame) || (pad != null && pad.leftShoulder.wasPressedThisFrame)) index--;
            if (Bindings.Pressed(GameAction.Jump) || (pad != null && pad.buttonNorth.wasPressedThisFrame)) FirstPerson = !FirstPerson;
        }
        var target = alive[((index % alive.Count) + alive.Count) % alive.Count];
        if (target != Target) SetTarget(target);
        Active = true;

        var cam = me.playerCamera;
        if (FirstPerson)
        {
            if (hidden != target) { Unhide(); target.SetHiddenForKillcam(true); hidden = target; }
            cam.transform.SetPositionAndRotation(target.Head.position, target.Head.rotation);
            UpdateGun(cam, target);
        }
        else
        {
            Unhide();
            DestroyGun();
            Scoped = false;
            cam.fieldOfView = GameSettings.Fov;
            Vector3 want = target.Head.position - target.Head.forward * 3.2f + Vector3.up * 0.9f;
            // Don't put the camera inside a wall.
            Vector3 from = target.Head.position;
            if (Physics.SphereCast(from, 0.2f, want - from, out var hit, Vector3.Distance(from, want), ~0, QueryTriggerInteraction.Ignore))
                want = from + (want - from).normalized * Mathf.Max(0.3f, hit.distance - 0.1f);
            cam.transform.position = Vector3.Lerp(cam.transform.position, want, 1f - Mathf.Exp(-10f * Time.deltaTime));
            cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation,
                Quaternion.LookRotation(target.Head.position + target.Head.forward * 4f - cam.transform.position), 1f - Mathf.Exp(-10f * Time.deltaTime));
        }
        return true;
    }

    static void SetTarget(FpsPlayer t)
    {
        Unhide();
        DestroyGun();
        Target = t;
        ads = 0f;
    }

    static void UpdateGun(Camera cam, FpsPlayer target)
    {
        var def = target.CurrentDef;
        var id = def != null ? def.Id : WeaponId.None;
        if (id != gunId || gun == null)
        {
            DestroyGun();
            gunId = id;
            if (def != null && id != WeaponId.Fists && id != WeaponId.Melee && !def.IsThrowable)
            {
                gun = WeaponModel.Build(id, cam.transform).gameObject;
                foreach (var r in gun.GetComponentsInChildren<Renderer>())
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }
        ads = Mathf.MoveTowards(ads, target.AimAmount, Time.deltaTime * 6f);
        float eased = ads * ads * (3f - 2f * ads);
        cam.fieldOfView = Mathf.Lerp(GameSettings.Fov, def != null && def.AdsFov > 0f ? def.AdsFov : GameSettings.Fov, eased);
        Scoped = def != null && def.HasScope && eased > 0.9f;
        if (gun != null)
        {
            gun.transform.localPosition = Vector3.Lerp(def.HipOffset, new Vector3(0f, -def.SightHeight, def.AdsDistance), eased);
            gun.transform.localRotation = Quaternion.identity;
            gun.SetActive(!Scoped);
        }
    }

    static void Unhide()
    {
        if (hidden != null) hidden.SetHiddenForKillcam(false);
        hidden = null;
    }

    static void DestroyGun()
    {
        if (gun != null) Object.Destroy(gun);
        gun = null;
        gunId = WeaponId.None;
    }

    /// <summary>Back in the game (respawned, match over, left): put everything back.</summary>
    public static void Stop()
    {
        if (!Active && hidden == null && gun == null) return;
        Unhide();
        DestroyGun();
        Active = false;
        Scoped = false;
        Target = null;
    }
}
