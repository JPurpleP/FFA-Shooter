using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-side golden-gun duels. A hit from the Golden Deagle sends the shooter and the victim to a free
/// DuelArena, both at full health, fists only. Whoever dies loses (weapons drop back where they stood on the
/// map, normal respawn); the winner returns to exactly where they were when the shot landed, at full health.
/// After <see cref="SuddenDeathAfter"/> seconds both fighters start bleeding out so a duel always ends.
/// Lives on the NetworkManager object.
/// </summary>
public class DuelManager : MonoBehaviour
{
    public const float SuddenDeathAfter = 45f, SuddenDeathDps = 5f, WinnerReturnDelay = 2.5f;

    class Duel
    {
        public FpsPlayer a, b;
        public int slot;
        public Vector3 aPos, bPos;
        public float aYaw, bYaw, start, bleed;
        public bool over;
    }

    static readonly List<Duel> active = new();
    static DuelManager instance;

    void Awake() => instance = this;

    static Duel Find(FpsPlayer p)
    {
        foreach (var d in active)
            if (d.a == p || d.b == p) return d;
        return null;
    }

    static int FreeSlot()
    {
        for (int i = 0; i < DuelArena.Count; i++)
        {
            bool used = false;
            foreach (var d in active) used |= d.slot == i;
            if (!used && DuelArena.Get(i) != null) return i;
        }
        return -1;
    }

    /// <summary>Server: the golden gun hit `victim`. Returns false (normal damage applies) if no duel can start.</summary>
    public static bool TryStart(FpsPlayer shooter, FpsPlayer victim)
    {
        if (instance == null || shooter == null || victim == null || shooter == victim) return false;
        if (shooter.IsDead || victim.IsDead || shooter.InDuel || victim.InDuel || Find(shooter) != null || Find(victim) != null) return false;
        int slot = FreeSlot();
        if (slot < 0) return false;
        var arena = DuelArena.Get(slot);
        var d = new Duel
        {
            a = shooter, b = victim, slot = slot, start = Time.time,
            aPos = shooter.transform.position, aYaw = shooter.transform.eulerAngles.y,
            bPos = victim.transform.position, bYaw = victim.transform.eulerAngles.y,
        };
        active.Add(d);
        shooter.ServerEnterDuel(slot, arena.spawnA.position, arena.spawnA.eulerAngles.y);
        victim.ServerEnterDuel(slot, arena.spawnB.position, arena.spawnB.eulerAngles.y);
        shooter.BroadcastKillFeed(new FixedString128Bytes($"{shooter.DisplayName} dragged {victim.DisplayName} into a GOLDEN DUEL"));
        Debug.Log($"[FFA] Duel: {shooter.DisplayName} vs {victim.DisplayName} (ring {slot + 1})");
        return true;
    }

    /// <summary>
    /// Server: `dead` just died. If they were duelling, the opponent wins and `dropAt` is where the loser's
    /// weapons should fall (back on the map, where they were shot from / at).
    /// </summary>
    public static bool OnDeath(FpsPlayer dead, out Vector3 dropAt)
    {
        dropAt = default;
        var d = Find(dead);
        if (d == null || d.over) return false;
        d.over = true;
        var winner = d.a == dead ? d.b : d.a;
        dropAt = d.a == dead ? d.aPos : d.bPos;
        dead.ServerClearDuel();
        Debug.Log($"[FFA] Duel: {(winner != null ? winner.DisplayName : "?")} beat {dead.DisplayName}");
        instance.StartCoroutine(instance.ReturnWinner(d, winner));
        return true;
    }

    IEnumerator ReturnWinner(Duel d, FpsPlayer winner)
    {
        yield return new WaitForSeconds(WinnerReturnDelay);
        if (!active.Contains(d)) yield break; // aborted meanwhile (new match, disconnect)
        active.Remove(d);
        if (winner != null && winner.IsSpawned && winner.InDuel)
            winner.ServerExitDuel(winner == d.a ? d.aPos : d.bPos, winner == d.a ? d.aYaw : d.bYaw, true);
    }

    /// <summary>Server: `p` left the game or is being respawned (new match). End their duel and send the other fighter home.</summary>
    public static void Abort(FpsPlayer p)
    {
        var d = Find(p);
        if (d == null) return;
        active.Remove(d);
        foreach (var f in new[] { d.a, d.b })
        {
            if (f == null || !f.IsSpawned) continue;
            if (f == p) f.ServerClearDuel();
            else if (f.InDuel && !f.IsDead) f.ServerExitDuel(f == d.a ? d.aPos : d.bPos, f == d.a ? d.aYaw : d.bYaw, d.over); // over = they already won; otherwise the duel was called off
            else f.ServerClearDuel();
        }
    }

    public static void ResetAll() => active.Clear();

    void Update()
    {
        var net = NetworkManager.Singleton;
        if (net == null || !net.IsServer || !net.IsListening)
        {
            active.Clear();
            return;
        }
        // Sudden death: both fighters bleed out, each tick credited to the other, until one drops.
        foreach (var d in active.ToArray())
        {
            if (d.over) continue;
            if (d.a == null || d.b == null || !d.a.IsSpawned || !d.b.IsSpawned)
            {
                Abort(d.a != null && d.a.IsSpawned ? d.b : d.a);
                continue;
            }
            if (Time.time - d.start < SuddenDeathAfter) continue;
            d.bleed += SuddenDeathDps * Time.deltaTime;
            if (d.bleed < 1f) continue;
            int dmg = Mathf.FloorToInt(d.bleed);
            d.bleed -= dmg;
            var info = new DeathInfo { Weapon = (byte)WeaponId.Fists, Part = (byte)BodyModel.Part.Chest, Distance = 1f, Seed = Random.Range(int.MinValue, int.MaxValue) };
            info.Point = d.a.transform.position + Vector3.up * 1.2f;
            info.Direction = d.b.transform.forward;
            d.a.TakeDamage(dmg, d.b, info);
            if (d.over) continue;
            info.Point = d.b.transform.position + Vector3.up * 1.2f;
            info.Direction = d.a.transform.forward;
            d.b.TakeDamage(dmg, d.a, info);
        }
    }
}
