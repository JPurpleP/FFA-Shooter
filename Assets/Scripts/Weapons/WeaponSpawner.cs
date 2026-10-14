using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-side: keeps a weapon pickup at this spot, respawning it some time after it is taken.
/// Random spawners (the default) roll from <see cref="RandomPool"/> every time (golden Deagle at double odds).
/// Exclusive spawners only respawn when that weapon exists nowhere else on the map.
/// </summary>
public class WeaponSpawner : MonoBehaviour
{
    public static readonly List<WeaponSpawner> All = new();

    /// <summary>
    /// Everything a random spawner can roll (not the Glock everyone starts with, not fists). Each entry is one equal
    /// share; the golden Deagle is listed twice, so it's the most common pickup at twice the odds of any other.
    /// </summary>
    public static readonly WeaponId[] RandomPool =
    {
        WeaponId.GoldenDeagle, WeaponId.GoldenDeagle,
        WeaponId.MP5, WeaponId.M4A1, WeaponId.Remington870, WeaponId.BarrettM82, WeaponId.M32, WeaponId.RPG7,
        WeaponId.FragGrenade, WeaponId.AA12, WeaponId.Mosin, WeaponId.PPSh41, WeaponId.M249,
        WeaponId.Cattleman, WeaponId.Magnum44, WeaponId.LeverAction, WeaponId.M1911, WeaponId.GlockSwitch,
        WeaponId.KrissVector, WeaponId.MP18,
    };

    public bool randomWeapon = true;
    public WeaponId weapon = WeaponId.M4A1;
    public float respawnDelay = 60f;
    public bool exclusive;
    public bool oncePerMatch; // spawns at the start of each match and never again until the next one
    bool usedThisMatch;

    WeaponPickup current;
    float nextSpawn = -1f;
    bool wasServer;

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Update()
    {
        var net = NetworkManager.Singleton;
        bool server = net != null && net.IsServer && net.IsListening;
        if (!server)
        {
            wasServer = false;
            current = null;
            usedThisMatch = false;
            return;
        }
        if (!wasServer)
        {
            wasServer = true;
            nextSpawn = Time.time + 0.5f; // first spawn right after hosting starts
        }
        if (current != null && current.IsSpawned)
            return;
        if (oncePerMatch && usedThisMatch)
            return;
        if (nextSpawn < 0f)
            nextSpawn = Time.time + respawnDelay;
        if (Time.time < nextSpawn)
            return;
        if (exclusive && !randomWeapon && WeaponExistsAnywhere(weapon))
        {
            nextSpawn = Time.time + 5f; // check again later
            return;
        }
        SpawnNow();
    }

    public void SpawnNow()
    {
        if (current != null && current.IsSpawned) return;
        var id = randomWeapon ? RandomPool[Random.Range(0, RandomPool.Length)] : weapon;
        var def = WeaponDefs.Get(id);
        current = WeaponPickup.Spawn(id, def.MagSize, def.SpareAmmo, transform.position, this, -1f);
        nextSpawn = -1f;
        usedThisMatch = true;
    }

    /// <summary>Server: a new match started — once-per-match weapons come back.</summary>
    public static void ResetForNewMatch()
    {
        foreach (var s in All)
        {
            s.usedThisMatch = false;
            if (s.oncePerMatch && (s.current == null || !s.current.IsSpawned)) s.nextSpawn = Time.time + 0.5f;
        }
    }

    public void OnPickupGone()
    {
        current = null;
        nextSpawn = Time.time + respawnDelay;
    }

    public static bool WeaponExistsAnywhere(WeaponId id)
    {
        foreach (var p in WeaponPickup.All)
            if (p.Available && p.Id == id) return true;
        foreach (var player in FpsPlayer.All)
            if (player.IsSpawned && player.HasWeapon(id)) return true;
        return false;
    }

    void OnDrawGizmos()
    {
        var def = WeaponDefs.Get(weapon);
        Gizmos.color = def != null ? def.PickupGlow : Color.white;
        Gizmos.DrawWireCube(transform.position + Vector3.up * 0.9f, new Vector3(0.8f, 0.3f, 0.3f));
    }
}
