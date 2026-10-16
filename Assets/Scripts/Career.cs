using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Leaderboards;
using UnityEngine;

/// <summary>
/// Your career: kills, deaths, wins and matches, kept on this PC and posted to the global leaderboard
/// (Unity Leaderboards, id <see cref="LeaderboardId"/>: score = wins, with kills / deaths / matches as metadata).
/// Only matches with at least two human players count.
/// </summary>
public static class Career
{
    public const string LeaderboardId = "career_wins";

    public static int Kills => PlayerPrefs.GetInt("ffa.career.kills", 0);
    public static int Deaths => PlayerPrefs.GetInt("ffa.career.deaths", 0);
    public static int Wins => PlayerPrefs.GetInt("ffa.career.wins", 0);
    public static int Matches => PlayerPrefs.GetInt("ffa.career.matches", 0);
    public static float KD => Deaths == 0 ? Kills : Kills / (float)Deaths;

    public struct Row
    {
        public int Rank, Wins, Kills, Deaths;
        public string Name;
        public bool Me;
        public float KD => Deaths == 0 ? Kills : Kills / (float)Deaths;
    }

    [Serializable]
    struct Meta { public int kills, deaths, matches; }

    /// <summary>A match just ended: add the local player's result and post it.</summary>
    public static void RecordMatch(int kills, int deaths, bool won)
    {
        PlayerPrefs.SetInt("ffa.career.kills", Kills + kills);
        PlayerPrefs.SetInt("ffa.career.deaths", Deaths + deaths);
        PlayerPrefs.SetInt("ffa.career.wins", Wins + (won ? 1 : 0));
        PlayerPrefs.SetInt("ffa.career.matches", Matches + 1);
        PlayerPrefs.Save();
        Debug.Log($"[FFA] Career: {Kills} kills, {Deaths} deaths, {Wins} wins, {Matches} matches");
        _ = Submit();
    }

    static async Task Submit()
    {
        try
        {
            await RelayConnect.EnsureSignedIn();
            await SyncName();
            var meta = JsonUtility.ToJson(new Meta { kills = Kills, deaths = Deaths, matches = Matches });
            await LeaderboardsService.Instance.AddPlayerScoreAsync(LeaderboardId, Wins, new AddPlayerScoreOptions { Metadata = meta });
        }
        catch (Exception e) { Debug.LogWarning($"[FFA] Leaderboard post failed: {e.Message}"); }
    }

    /// <summary>Leaderboard names come from the Unity account: keep it matching the in-game name.</summary>
    static async Task SyncName()
    {
        string want = NameFilter.Clean(GameSettings.Name ?? "").Trim().Replace(' ', '_');
        if (want.Length == 0) return;
        if (want.Length > 30) want = want.Substring(0, 30);
        string have = AuthenticationService.Instance.PlayerName ?? "";
        int hash = have.LastIndexOf('#');
        if (hash >= 0) have = have.Substring(0, hash);
        if (have != want) await AuthenticationService.Instance.UpdatePlayerNameAsync(want);
    }

    /// <summary>The global top players by wins.</summary>
    public static async Task<List<Row>> Top(int count)
    {
        await RelayConnect.EnsureSignedIn();
        await SyncName();
        var page = await LeaderboardsService.Instance.GetScoresAsync(LeaderboardId, new GetScoresOptions { Limit = count, IncludeMetadata = true });
        string me = AuthenticationService.Instance.PlayerId;
        var rows = new List<Row>();
        foreach (var e in page.Results)
        {
            var meta = string.IsNullOrEmpty(e.Metadata) ? default : JsonUtility.FromJson<Meta>(e.Metadata);
            string name = e.PlayerName ?? "?";
            int hash = name.LastIndexOf('#');
            if (hash > 0) name = name.Substring(0, hash);
            rows.Add(new Row { Rank = e.Rank + 1, Name = NameFilter.Clean(name.Replace('_', ' ')), Wins = (int)e.Score, Kills = meta.kills, Deaths = meta.deaths, Me = e.PlayerId == me });
        }
        return rows;
    }
}
