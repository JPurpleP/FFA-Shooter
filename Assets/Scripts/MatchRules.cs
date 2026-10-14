using System.Collections;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// Server-side free-for-all rules: first to the frag limit wins. Games hosted from the menu start in a waiting lobby
/// where everyone readies up and the host starts the match; after a win everyone goes back to the lobby.
/// Command-line test sessions skip the lobby and simply reset scores and respawn everyone.
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
    /// <summary>This session uses the waiting lobby (server only).</summary>
    public static bool UseLobby { get; private set; }
    /// <summary>Players are waiting in the lobby; new arrivals join it instead of the match (server only).</summary>
    public static bool LobbyOpen { get; private set; }

    /// <summary>Server: call before starting the host.</summary>
    public static void BeginSession(bool lobby)
    {
        IsMatchOver = false;
        UseLobby = LobbyOpen = lobby;
    }

    /// <summary>Server: the host pressed Start. Fresh scores, fresh pickups, everyone into the map.</summary>
    public static void StartMatch()
    {
        if (!LobbyOpen) return;
        LobbyOpen = false;
        IsMatchOver = false;
        FpsPlayer announcer = null;
        foreach (var p in FpsPlayer.All.ToArray())
        {
            if (!p.IsSpawned) continue;
            p.Kills.Value = 0;
            p.Deaths.Value = 0;
            p.ServerResetStreak();
            p.ServerLeaveLobby();
            announcer = p;
        }
        WeaponSpawner.ResetForNewMatch();
        if (announcer != null) announcer.BroadcastMatchState(default);
        Debug.Log("[FFA] Match started");
    }

    /// <summary>Can the host start? Every human other than the host has readied up.</summary>
    public static bool EveryoneReady(out int waiting)
    {
        waiting = 0;
        foreach (var p in FpsPlayer.All)
            if (p.IsSpawned && !p.IsHostPlayer && !p.Ready.Value) waiting++;
        return waiting == 0;
    }

    void Awake() => instance = this;

    /// <summary>-ffafraglimit &lt;n&gt;: shorter matches for tests.</summary>
    public static void OverrideFragLimit(int kills)
    {
        if (instance != null) instance.fragLimit = Mathf.Max(1, kills);
    }

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
        if (UseLobby)
        {
            OpenLobby();
            yield break;
        }
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

    /// <summary>Server: everyone back to the waiting lobby (after a win).</summary>
    static void OpenLobby()
    {
        LobbyOpen = true;
        foreach (var p in FpsPlayer.All.ToArray())
            if (p.IsSpawned) p.ServerEnterLobby();
        Debug.Log("[FFA] Back to the lobby");
    }

    public static void ResetState() => IsMatchOver = UseLobby = LobbyOpen = false;
}
