using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// Server-side free-for-all rules: first to the frag limit wins. Games hosted from the menu start in a waiting lobby
/// where everyone readies up and the host starts the match; after a win everyone goes back to the lobby.
/// Command-line test sessions skip the lobby and simply reset scores and respawn everyone.
/// Lives on the NetworkManager object; results are broadcast through player RPCs.
/// </summary>
public partial class MatchRules : MonoBehaviour
{
    public int fragLimit = 25;
    public float restartDelay = RestartDelaySeconds;

    static MatchRules instance;

    /// <summary>True while the winner banner is showing (server only).</summary>
    public static bool IsMatchOver { get; private set; }
    public const int MinFragLimit = 10, MaxFragLimit = 100, DefaultFragLimit = 25;
    /// <summary>Kills to win. The server's value; clients read it off the player objects (FpsPlayer.MatchFragLimit).</summary>
    public static int FragLimit
    {
        get
        {
            var net = Unity.Netcode.NetworkManager.Singleton;
            if (net != null && net.IsListening && !net.IsServer)
                foreach (var p in FpsPlayer.All)
                    if (p.IsSpawned && p.MatchFragLimit.Value > 0) return p.MatchFragLimit.Value;
            return instance != null ? instance.fragLimit : DefaultFragLimit;
        }
    }

    /// <summary>Server: the host picked the score to win (lobby slider); everyone's display follows.</summary>
    public static void SetFragLimit(int kills)
    {
        if (instance == null) return;
        instance.fragLimit = Mathf.Clamp(kills, 1, MaxFragLimit);
        foreach (var p in FpsPlayer.All)
            if (p.IsSpawned && p.IsServer) p.MatchFragLimit.Value = (byte)instance.fragLimit;
    }
    /// <summary>This session uses the waiting lobby (server only).</summary>
    public static bool UseLobby { get; private set; }
    /// <summary>Players are waiting in the lobby; new arrivals join it instead of the match (server only).</summary>
    public static bool LobbyOpen { get; private set; }

    /// <summary>Server: call before starting the host.</summary>
    public static void BeginSession(bool lobby)
    {
        IsMatchOver = false;
        Medals.Reset();
        UseLobby = LobbyOpen = lobby;
    }

    /// <summary>Server: the host pressed Start. Fresh scores, fresh pickups, everyone into the map.</summary>
    public static void StartMatch()
    {
        if (!LobbyOpen) return;
        LobbyOpen = false;
        IsMatchOver = false;
        Medals.Reset();
        FpsPlayer announcer = null;
        var players = new List<FpsPlayer>();
        foreach (var p in FpsPlayer.All) if (p.IsSpawned) players.Add(p);
        SetUpMode(players);
        foreach (var p in players)
        {
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

    /// <summary>
    /// Server, lobby only: the host picked another map. Pickups left on the old map go away, every player object
    /// carries the new map id (clients switch when it changes), and the new map's spawners start up by themselves.
    /// </summary>
    public static void SetMap(int id)
    {
        if (!LobbyOpen) return;
        id = Mathf.Clamp(id, 0, MapCatalog.Count - 1);
        if (id == MapCatalog.Current) return;
        foreach (var p in WeaponPickup.All.ToArray())
            if (p != null && p.IsSpawned) p.Despawn();
        MapCatalog.Apply(id);
        Gore.ClearAll();
        foreach (var pl in FpsPlayer.All)
            if (pl.IsSpawned) pl.MapId.Value = (byte)id;
        Debug.Log($"[FFA] Lobby map: {MapCatalog.NameOf(id)}");
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
    public static bool FragLimitFromCommandLine { get; private set; }
    public static void OverrideFragLimit(int kills)
    {
        FragLimitFromCommandLine = true;
        SetFragLimit(kills);
    }

    public static void OnPlayerKilled(FpsPlayer killer, FpsPlayer victim, DeathInfo info)
    {
        var def = WeaponDefs.Get(info.WeaponId);
        string gore = GorePlan.For(info).Describe();
        string extra = (info.HitPart == BodyModel.Part.Head ? " · HEADSHOT" : "") + (gore != null ? " · " + gore : "");
        string text;
        if (info.Fell)
            text = $"{victim.DisplayName} fell to their death";
        else if (info.Train)
            text = $"{victim.DisplayName} was hit by the train";
        else if (killer == victim)
            text = $"{victim.DisplayName} blew themselves up [{def?.Name}]{extra}";
        else if (info.WeaponId == WeaponId.Fists && killer != null)
            text = $"{killer.DisplayName} beat {victim.DisplayName} to death in a GOLDEN DUEL";
        else if (killer == null)
            text = $"{victim.DisplayName} was executed [{def?.Name}]{extra}";
        else
            text = $"{killer.DisplayName} [{def?.Name}] {victim.DisplayName}{extra}";
        victim.BroadcastKillFeed(new FixedString128Bytes(text.Length > 120 ? text.Substring(0, 120) : text));

        ApplyModeRules(killer, victim, info);
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
        var all = new List<FpsPlayer>();
        foreach (var p in FpsPlayer.All) if (p.IsSpawned) all.Add(p);
        SetUpMode(all);
        foreach (var p in all)
        {
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
        ApplyVotes();
        Debug.Log("[FFA] Back to the lobby");
    }

    /// <summary>Quick-melee damage (game modes may change it).</summary>
    public static int MeleeDamage(FpsPlayer attacker) =>
        Mode is GameMode.GunGame or GameMode.OneInTheChamber || (Mode == GameMode.Infection && attacker.Team.Value == Infected) ? 100 : 55;

    // ------------------------------------------------------------------ game modes (more in the mode rules below)

    public enum GameMode : byte { FreeForAll, TeamDeathmatch, GunGame, OneInTheChamber, Infection }
    public static readonly GameMode[] Modes = { GameMode.FreeForAll, GameMode.TeamDeathmatch, GameMode.GunGame, GameMode.OneInTheChamber, GameMode.Infection };
    static GameMode serverMode;

    /// <summary>The mode being played (the server's choice; clients read it off the player objects).</summary>
    public static GameMode Mode
    {
        get
        {
            var net = Unity.Netcode.NetworkManager.Singleton;
            if (net != null && net.IsListening && !net.IsServer)
                foreach (var p in FpsPlayer.All)
                    if (p.IsSpawned) return (GameMode)p.MatchMode.Value;
            return serverMode;
        }
    }

    public static string ModeName(GameMode m) => m switch
    {
        GameMode.TeamDeathmatch => "Team Deathmatch",
        GameMode.GunGame => "Gun Game",
        GameMode.OneInTheChamber => "One in the Chamber",
        GameMode.Infection => "Infection",
        _ => "Free for All",
    };

    /// <summary>Server, lobby: the host picked a mode.</summary>
    public static void SetMode(GameMode m)
    {
        serverMode = m;
        foreach (var p in FpsPlayer.All)
            if (p.IsSpawned) p.MatchMode.Value = (byte)m;
    }

    // ------------------------------------------------------------------ kicking

    static readonly HashSet<string> banned = new();

    /// <summary>Server (host): remove a player; they can't come back into this game under that name.</summary>
    public static void Kick(FpsPlayer p)
    {
        if (p == null || !p.IsSpawned || p.IsHostPlayer) return;
        p.BroadcastKillFeed(new FixedString128Bytes($"{p.DisplayName} was kicked by the host"));
        if (p.IsBot.Value) { p.NetworkObject.Despawn(true); return; }
        banned.Add(p.PlayerName.Value.ToString().Trim().ToLowerInvariant());
        Unity.Netcode.NetworkManager.Singleton.DisconnectClient(p.OwnerClientId, "You were kicked from the game by the host.");
    }

    /// <summary>Did the local player (or their team) win, given the winner banner text?</summary>
    public static bool LocalWon(string winnerText)
    {
        var me = FpsPlayer.Local;
        if (me == null) return false;
        return Mode switch
        {
            GameMode.TeamDeathmatch => winnerText == TeamName(me.Team.Value),
            GameMode.Infection => (winnerText == "THE SURVIVORS" && me.Team.Value == Survivor) || (winnerText == "THE INFECTED" && me.Team.Value == Infected),
            _ => winnerText == me.DisplayName,
        };
    }

    public static bool IsBanned(string name) => banned.Contains((name ?? "").Trim().ToLowerInvariant());

    public static void ResetState()
    {
        IsMatchOver = UseLobby = LobbyOpen = false;
        banned.Clear();
    }
}
