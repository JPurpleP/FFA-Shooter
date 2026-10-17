using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// Game modes on top of the free-for-all rules: Team Deathmatch, Gun Game, One in the Chamber and Infection, plus the
/// end of a match (final killcam, summary, map vote). Server side; clients read the mirrored values on FpsPlayer.
/// </summary>
public partial class MatchRules
{
    public const byte TeamRed = 1, TeamBlue = 2;            // Team Deathmatch
    public const byte Survivor = 1, Infected = 2;           // Infection
    public const int OitcLives = 3;
    public const float InfectionSeconds = 180f;
    public const float RestartDelaySeconds = 15f;           // final killcam + summary before the lobby / next match

    /// <summary>Gun Game: one kill with each to move on; the last step is a melee kill.</summary>
    public static readonly WeaponId[] GunLadder =
    {
        WeaponId.MP5, WeaponId.KrissVector, WeaponId.PPSh41, WeaponId.M4A1, WeaponId.M249, WeaponId.AA12, WeaponId.Remington870,
        WeaponId.GlockSwitch, WeaponId.Mosin, WeaponId.LeverAction, WeaponId.MP18, WeaponId.BarrettM82, WeaponId.Magnum44,
        WeaponId.Cattleman, WeaponId.M1911, WeaponId.Melee,
    };

    /// <summary>Guns, grenades and hooks lying around (and killstreaks) only in the free-for-all style modes.</summary>
    public static bool PickupsAllowed => Mode is GameMode.FreeForAll or GameMode.TeamDeathmatch;
    public static bool StreaksAllowed => PickupsAllowed;
    public static bool UsesScoreLimit => Mode is GameMode.FreeForAll or GameMode.TeamDeathmatch;

    public static string TeamName(byte team) => Mode == GameMode.Infection
        ? (team == Infected ? "INFECTED" : "SURVIVORS")
        : team == TeamRed ? "RED TEAM" : team == TeamBlue ? "BLUE TEAM" : "";

    public static Color TeamColor(byte team) => GameUI.Accessible(Mode == GameMode.Infection
        ? (team == Infected ? InfectedColorRaw : new Color(0.95f, 0.95f, 0.95f))
        : TeamColorRaw(team));

    /// <summary>Team colors before the colorblind setting is applied.</summary>
    public static Color TeamColorRaw(byte team) => team == TeamRed ? new Color(1f, 0.3f, 0.25f) : team == TeamBlue ? new Color(0.3f, 0.55f, 1f) : Color.white;
    public static readonly Color InfectedColorRaw = new(0.45f, 0.95f, 0.3f);

    public static int TeamKills(byte team) => FpsPlayer.All.Where(p => p.IsSpawned && p.Team.Value == team).Sum(p => p.Kills.Value);
    static int CountTeam(byte team) => FpsPlayer.All.Count(p => p.IsSpawned && p.Team.Value == team);

    float serverMatchEnd = -1f;
    static readonly Dictionary<ulong, int> votes = new();
    static bool mapVote = true;

    public static bool MapVoteEnabled => mapVote;

    public static void SetMapVote(bool on)
    {
        mapVote = on;
        foreach (var p in FpsPlayer.All)
            if (p.IsSpawned) p.MapVoteOn.Value = on;
    }

    /// <summary>Server, match start: teams, lives, levels and the Infection clock.</summary>
    static void SetUpMode(List<FpsPlayer> players)
    {
        var rng = new System.Random();
        var shuffled = players.OrderBy(_ => rng.Next()).ToList();
        for (int i = 0; i < shuffled.Count; i++)
        {
            var p = shuffled[i];
            p.Team.Value = Mode == GameMode.TeamDeathmatch ? (byte)(1 + i % 2) : Mode == GameMode.Infection ? Survivor : (byte)0;
            p.GunLevel.Value = 0;
            p.Lives.Value = OitcLives;
            p.Eliminated.Value = false;
        }
        if (Mode == GameMode.Infection)
        {
            int infected = Mathf.Max(1, shuffled.Count / 6);
            for (int i = 0; i < infected && i < shuffled.Count; i++) shuffled[i].Team.Value = Infected;
            instance.serverMatchEnd = Time.time + InfectionSeconds;
            double ends = Unity.Netcode.NetworkManager.Singleton.ServerTime.Time + InfectionSeconds;
            foreach (var p in players) p.MatchTimerEnd.Value = ends;
        }
        else
        {
            instance.serverMatchEnd = -1f;
            foreach (var p in players) p.MatchTimerEnd.Value = 0;
        }
        if (!PickupsAllowed)
            foreach (var w in WeaponPickup.All.ToArray())
                if (w != null && w.IsSpawned) w.Despawn();
        votes.Clear();
    }

    /// <summary>Server: someone joined while a match is running.</summary>
    public static void SetUpLateJoiner(FpsPlayer p)
    {
        p.GunLevel.Value = 0;
        p.Eliminated.Value = false;
        p.Lives.Value = 1; // One in the Chamber: one life when you join late
        if (Mode == GameMode.TeamDeathmatch) p.Team.Value = CountTeam(TeamRed) <= CountTeam(TeamBlue) ? TeamRed : TeamBlue;
        else if (Mode == GameMode.Infection) p.Team.Value = Infected;
        else p.Team.Value = 0;
        if (Mode == GameMode.Infection && instance != null && instance.serverMatchEnd > 0f)
            p.MatchTimerEnd.Value = Unity.Netcode.NetworkManager.Singleton.ServerTime.Time + (instance.serverMatchEnd - Time.time);
    }

    /// <summary>Server, after the kill feed: apply the mode's rules and end the match if someone won.</summary>
    static void ApplyModeRules(FpsPlayer killer, FpsPlayer victim, DeathInfo info)
    {
        if (instance == null || IsMatchOver) return;
        bool byOther = killer != null && killer != victim;
        switch (Mode)
        {
            case GameMode.FreeForAll:
                if (byOther && killer.Kills.Value >= instance.fragLimit) EndMatch(killer.DisplayName, killer, victim, info);
                break;

            case GameMode.TeamDeathmatch:
                if (byOther && killer.Team.Value != 0 && TeamKills(killer.Team.Value) >= instance.fragLimit)
                    EndMatch(TeamName(killer.Team.Value), killer, victim, info);
                break;

            case GameMode.GunGame:
                if (byOther && info.WeaponId == WeaponId.Melee && victim.GunLevel.Value > 0)
                {
                    victim.GunLevel.Value--;
                    victim.BroadcastKillFeed(new FixedString128Bytes($"{victim.DisplayName} was knocked down a level"));
                }
                if (byOther)
                {
                    killer.GunLevel.Value++;
                    if (killer.GunLevel.Value >= GunLadder.Length) EndMatch(killer.DisplayName, killer, victim, info);
                    else killer.ServerApplyLoadout();
                }
                break;

            case GameMode.OneInTheChamber:
                if (byOther) killer.ServerGiveBullet();
                if (victim.Lives.Value > 0) victim.Lives.Value--;
                if (victim.Lives.Value == 0 && !victim.Eliminated.Value)
                {
                    victim.Eliminated.Value = true;
                    victim.BroadcastKillFeed(new FixedString128Bytes($"{victim.DisplayName} is out!"));
                }
                var alive = FpsPlayer.All.Where(p => p.IsSpawned && !p.Eliminated.Value).ToList();
                if (alive.Count <= 1) EndMatch(alive.Count == 1 ? alive[0].DisplayName : "NOBODY", killer, victim, info);
                break;

            case GameMode.Infection:
                if (victim.Team.Value == Survivor)
                {
                    victim.Team.Value = Infected;
                    victim.BroadcastKillFeed(new FixedString128Bytes($"{victim.DisplayName} has been infected!"));
                }
                if (CountTeam(Survivor) == 0) EndMatch("THE INFECTED", killer, victim, info);
                break;
        }
    }

    /// <summary>Server: somebody won. Everyone sees the final kill replayed, then the summary, then the lobby.</summary>
    static void EndMatch(string winner, FpsPlayer killer, FpsPlayer victim, DeathInfo info)
    {
        IsMatchOver = true;
        var announcer = killer != null ? killer : victim != null ? victim : FpsPlayer.All.FirstOrDefault(p => p.IsSpawned);
        if (announcer == null) return;
        announcer.BroadcastMatchState(new FixedString32Bytes(winner.Length > 28 ? winner.Substring(0, 28) : winner));
        if (killer != null && victim != null && killer != victim)
            killer.BroadcastFinalKill(victim.NetworkObjectId, info.Weapon);
        Debug.Log($"[FFA] Match won by {winner} ({ModeName(Mode)})");
        instance.StartCoroutine(instance.RestartMatch());
    }

    void Update()
    {
        // Infection: the survivors win if the clock runs out.
        var net = Unity.Netcode.NetworkManager.Singleton;
        if (net == null || !net.IsServer || IsMatchOver || LobbyOpen || Mode != GameMode.Infection || serverMatchEnd < 0f) return;
        if (Time.time >= serverMatchEnd && CountTeam(Survivor) > 0)
            EndMatch("THE SURVIVORS", null, null, default);
    }

    // ------------------------------------------------------------------ map vote (during the summary)

    public static void CastVote(ulong clientId, int map, FpsPlayer voter)
    {
        if (!IsMatchOver || !mapVote || map < 0 || map >= MapCatalog.Count) return;
        votes[clientId] = map;
        var counts = new int[5];
        foreach (var v in votes.Values) if (v >= 0 && v < 5) counts[v]++;
        voter.BroadcastVotes(counts[0], counts[1], counts[2], counts[3], counts[4]);
    }

    /// <summary>Server, back in the lobby: switch to the most-voted map (ties broken at random).</summary>
    static void ApplyVotes()
    {
        if (!mapVote || votes.Count == 0) return;
        var tally = votes.Values.GroupBy(v => v).Select(g => (map: g.Key, n: g.Count())).ToList();
        int best = tally.Max(t => t.n);
        var top = tally.Where(t => t.n == best).ToList();
        int pick = top[Random.Range(0, top.Count)].map;
        votes.Clear();
        SetMap(pick);
    }
}
