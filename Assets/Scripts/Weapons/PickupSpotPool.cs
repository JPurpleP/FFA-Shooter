using UnityEngine;

/// <summary>
/// Walkable ground spots across one map (sampled when the map is built). Random weapon spawners move to a new spot
/// from here every time they respawn, so pickups turn up all over the map during a match.
/// </summary>
public class PickupSpotPool : MonoBehaviour
{
    public Vector3[] spots;

    const float MinFromPickup = 8f, MinFromPlayer = 6f;

    /// <summary>A random spot away from other pickups and from anyone standing nearby.</summary>
    public Vector3 PickFree()
    {
        if (spots == null || spots.Length == 0) return transform.position;
        for (int tries = 0; tries < 30; tries++)
        {
            var p = spots[Random.Range(0, spots.Length)];
            if (Clear(p)) return p;
        }
        return spots[Random.Range(0, spots.Length)];
    }

    static bool Clear(Vector3 p)
    {
        foreach (var w in WeaponPickup.All)
            if (w != null && w.Available && (w.transform.position - p).sqrMagnitude < MinFromPickup * MinFromPickup) return false;
        foreach (var pl in FpsPlayer.All)
            if (pl != null && pl.IsSpawned && !pl.IsDead && (pl.transform.position - p).sqrMagnitude < MinFromPlayer * MinFromPlayer) return false;
        return true;
    }
}
