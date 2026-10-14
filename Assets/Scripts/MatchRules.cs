using System.Collections;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// Server-side free-for-all rules: first to the frag limit wins, then scores reset and everyone respawns.
/// Lives on the NetworkManager object; results are broadcast through player RPCs.
/// </summary>
public class MatchRules : MonoBehaviour
{
    public int fragLimit = 25;
    public float restartDelay = 8f;

    static MatchRules instance;

    /// <summary>True while the winner banner is showing (server only).</summary>
    public static bool IsMatchOver { get; private set; }
    public static int FragLimit => instance != null ? instance.fragLimit : 25;

    void Awake() => instance = this;

    public static void OnPlayerKilled(FpsPlayer killer, FpsPlayer victim, DeathInfo info)
    {
        var def = WeaponDefs.Get(info.WeaponId);
        string gore = GorePlan.For(info).Describe();
        string extra = (info.HitPart == BodyModel.Part.Head ? " · HEADSHOT" : "") + (gore != null ? " · " + gore : "");
        string text;
        if (info.Fell)
            text = $"{victim.DisplayName} fell to their death";
        else if (killer == victim)
            text = $"{victim.DisplayName} blew themselves up [{def?.Name}]{extra}";
        else if (info.WeaponId == WeaponId.Fists && killer != null)
            text = $"{killer.DisplayName} beat {victim.DisplayName} to death in a GOLDEN DUEL";
        else if (killer == null)
            text = $"{victim.DisplayName} was executed [{def?.Name}]{extra}";
        else
            text = $"{killer.DisplayName} [{def?.Name}] {victim.DisplayName}{extra}";
        victim.BroadcastKillFeed(new FixedString128Bytes(text.Length > 120 ? text.Substring(0, 120) : text));

        if (instance == null || IsMatchOver || killer == null || killer == victim || killer.Kills.Value < instance.fragLimit)
            return;
        IsMatchOver = true;
        killer.BroadcastMatchState(new FixedString32Bytes(killer.DisplayName));
        instance.StartCoroutine(instance.RestartMatch());
    }

    IEnumerator RestartMatch()
    {
        yield return new WaitForSeconds(restartDelay);
        IsMatchOver = false;
        FpsPlayer announcer = null;
        foreach (var p in FpsPlayer.All.ToArray())
        {
            if (!p.IsSpawned) continue;
            p.Kills.Value = 0;
            p.Deaths.Value = 0;
            p.ServerResetStreak();
            p.Respawn();
            announcer = p;
        }
        WeaponSpawner.ResetForNewMatch();
        if (announcer != null)
            announcer.BroadcastMatchState(default);
    }

    public static void ResetState() => IsMatchOver = false;
}
