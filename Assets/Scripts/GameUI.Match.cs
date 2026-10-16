using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Mode = MatchRules.GameMode;

/// <summary>Per-mode HUD text, team markers, the end-of-match summary (with your stats and the map vote).</summary>
public partial class GameUI
{
    // ------------------------------------------------------------------ your stats this match (local)

    static int statShots, statHits, statBestStreak;
    static readonly Dictionary<WeaponId, int> statKillsByWeapon = new();
    bool wasInLobbyForStats = true;

    public static void NoteShot() => statShots++;

    public static void NoteHit(bool killed, WeaponId weapon)
    {
        statHits++;
        if (killed) statKillsByWeapon[weapon] = statKillsByWeapon.TryGetValue(weapon, out int n) ? n + 1 : 1;
    }

    static void ResetMatchStats()
    {
        statShots = statHits = statBestStreak = 0;
        statKillsByWeapon.Clear();
        voteCounts = new int[3];
        myVote = -1;
    }

    /// <summary>Update: start counting when a match starts; track your best streak.</summary>
    void UpdateMatchStats(FpsPlayer local)
    {
        if (local == null) { wasInLobbyForStats = true; return; }
        bool inLobby = local.InLobby.Value;
        if (wasInLobbyForStats && !inLobby) ResetMatchStats();
        wasInLobbyForStats = inLobby;
        statBestStreak = Mathf.Max(statBestStreak, local.Streak.Value);
    }

    // ------------------------------------------------------------------ map vote (client side)

    static int[] voteCounts = new int[3];
    static int myVote = -1;

    public static void SetVoteCounts(int a, int b, int c) => voteCounts = new[] { a, b, c };

    // ------------------------------------------------------------------ HUD text per mode

    string ModeTopLine(FpsPlayer me, List<FpsPlayer> players)
    {
        switch (MatchRules.Mode)
        {
            case Mode.TeamDeathmatch:
            {
                int red = MatchRules.TeamKills(MatchRules.TeamRed), blue = MatchRules.TeamKills(MatchRules.TeamBlue);
                string mine = me.Team.Value != 0 ? $"  ·  You: <color=#{Hex(MatchRules.TeamColor(me.Team.Value))}>{MatchRules.TeamName(me.Team.Value)}</color>" : "";
                return $"<color=#ff5a50>RED {red}</color>  –  <color=#5a90ff>BLUE {blue}</color>  ·  first to {MatchRules.FragLimit}{mine}";
            }
            case Mode.GunGame:
            {
                int lvl = Mathf.Min(me.GunLevel.Value, MatchRules.GunLadder.Length - 1);
                var def = WeaponDefs.Get(MatchRules.GunLadder[lvl]);
                var lead = players.OrderByDescending(p => p.GunLevel.Value).FirstOrDefault();
                string leader = lead != null && lead != me ? $"  ·  Leader: {lead.DisplayName} ({lead.GunLevel.Value + 1})" : "";
                return $"GUN GAME  ·  Level {lvl + 1}/{MatchRules.GunLadder.Length}: {def?.Name}{leader}";
            }
            case Mode.OneInTheChamber:
            {
                int left = players.Count(p => !p.Eliminated.Value);
                return $"ONE IN THE CHAMBER  ·  Lives: {new string('♥', me.Lives.Value)}  ·  {left} left";
            }
            case Mode.Infection:
            {
                double rem = me.MatchTimerEnd.Value - (Unity.Netcode.NetworkManager.Singleton != null ? Unity.Netcode.NetworkManager.Singleton.ServerTime.Time : 0);
                int secs = Mathf.Max(0, (int)rem);
                int survivors = players.Count(p => p.Team.Value == MatchRules.Survivor);
                string role = me.Team.Value == MatchRules.Infected ? "<color=#73f24d>INFECTED: hunt them down</color>" : "SURVIVOR: stay alive";
                return $"INFECTION  ·  {secs / 60}:{secs % 60:00}  ·  {survivors} survivor{(survivors == 1 ? "" : "s")} left  ·  {role}";
            }
            default:
            {
                var leader = players.FirstOrDefault();
                string top = $"First to {MatchRules.FragLimit} kills  ·  You: {me.Kills.Value}";
                if (leader != null && leader != me) top += $"  ·  Leader: {leader.DisplayName} ({leader.Kills.Value})";
                return top;
            }
        }
    }

    static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

    /// <summary>Scoreboard order and the mode's own column.</summary>
    static List<FpsPlayer> ModeOrder(List<FpsPlayer> players) => MatchRules.Mode switch
    {
        Mode.TeamDeathmatch => players.OrderBy(p => p.Team.Value).ThenByDescending(p => p.Kills.Value).ToList(),
        Mode.GunGame => players.OrderByDescending(p => p.GunLevel.Value).ThenByDescending(p => p.Kills.Value).ToList(),
        Mode.OneInTheChamber => players.OrderByDescending(p => p.Eliminated.Value ? 0 : 1).ThenByDescending(p => p.Lives.Value).ThenByDescending(p => p.Kills.Value).ToList(),
        Mode.Infection => players.OrderBy(p => p.Team.Value).ThenByDescending(p => p.Kills.Value).ToList(),
        _ => players,
    };

    static string ModeColumnTitle => MatchRules.Mode switch
    {
        Mode.TeamDeathmatch => "TEAM", Mode.GunGame => "LEVEL", Mode.OneInTheChamber => "LIVES", Mode.Infection => "STATUS", _ => "K/D",
    };

    static string ModeColumn(FpsPlayer p)
    {
        switch (MatchRules.Mode)
        {
            case Mode.TeamDeathmatch: return $"<color=#{Hex(MatchRules.TeamColor(p.Team.Value))}>{(p.Team.Value == MatchRules.TeamRed ? "RED" : "BLUE")}</color>";
            case Mode.GunGame: return $"{p.GunLevel.Value + 1}/{MatchRules.GunLadder.Length}";
            case Mode.OneInTheChamber: return p.Eliminated.Value ? "OUT" : new string('♥', p.Lives.Value);
            case Mode.Infection: return p.Team.Value == MatchRules.Infected ? "<color=#73f24d>INFECTED</color>" : "ALIVE";
            default:
                float kd = p.Deaths.Value == 0 ? p.Kills.Value : p.Kills.Value / (float)p.Deaths.Value;
                return kd.ToString("0.00");
        }
    }

    static float ModeProgress(FpsPlayer p) => MatchRules.Mode switch
    {
        Mode.TeamDeathmatch => MatchRules.TeamKills(p.Team.Value) / (float)MatchRules.FragLimit,
        Mode.GunGame => p.GunLevel.Value / (float)MatchRules.GunLadder.Length,
        Mode.OneInTheChamber => p.Lives.Value / (float)MatchRules.OitcLives,
        Mode.Infection => 0f,
        _ => p.Kills.Value / (float)MatchRules.FragLimit,
    };

    static string ModeGoal => MatchRules.Mode switch
    {
        Mode.TeamDeathmatch => $"First team to {MatchRules.FragLimit} kills wins",
        Mode.GunGame => $"Get a kill with all {MatchRules.GunLadder.Length} weapons",
        Mode.OneInTheChamber => "One bullet, one kill. Last one standing wins",
        Mode.Infection => "Survive the clock, or infect everyone",
        _ => $"First to {MatchRules.FragLimit} kills wins",
    };

    /// <summary>Teammates (Team DM) and the infected you can see (Infection) get a tag over their head.</summary>
    void DrawTeamMarkers(FpsPlayer me, float w, float h, float scale)
    {
        if (MatchRules.Mode != Mode.TeamDeathmatch && MatchRules.Mode != Mode.Infection) return;
        var cam = me.playerCamera;
        if (cam == null) return;
        var style = new GUIStyle(centerLabel) { fontSize = 12, fontStyle = FontStyle.Bold };
        foreach (var p in FpsPlayer.All)
        {
            if (p == me || !p.IsSpawned || p.IsDead || p.InLobby.Value) continue;
            bool mate = p.Team.Value == me.Team.Value;
            bool infectedSeen = MatchRules.Mode == Mode.Infection && p.Team.Value == MatchRules.Infected && !mate
                                && !Physics.Linecast(cam.transform.position, p.AimPoint, 1, QueryTriggerInteraction.Ignore);
            if (!mate && !infectedSeen) continue;
            Vector3 sp = cam.WorldToScreenPoint(p.AimPoint + Vector3.up * 0.45f);
            if (sp.z < 0.1f || sp.z > 150f) continue;
            var c = MatchRules.TeamColor(p.Team.Value);
            float x = sp.x / scale, y = (Screen.height - sp.y) / scale;
            Fill(new Rect(x - 4, y - 4, 8, 8), c);
            Shadowed(new Rect(x - 80, y - 24, 160, 18), infectedSeen ? "INFECTED" : p.DisplayName, style, c);
        }
    }

    // ------------------------------------------------------------------ end of match

    /// <summary>The match is over: winner, top players, your stats, and the map vote for the next one.</summary>
    void DrawMatchSummary(FpsPlayer me, float w, float h)
    {
        EnsureMenuStyles();
        Fill(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.55f));
        float pw = Mathf.Min(w - 60, 820), ph = Mathf.Min(h - 50, 600);
        var r = new Rect((w - pw) / 2f, (h - ph) / 2f, pw, ph);
        Fill(r, new Color(0.04f, 0.05f, 0.07f, 0.95f));
        Fill(new Rect(r.x, r.y, r.width, 3), new Color(1f, 0.82f, 0.3f));
        float x = r.x + 30, iw = pw - 60, y = r.y + 18;

        bool won = MatchRules.LocalWon(winner);
        GUI.Label(new Rect(x, y, iw, 50), $"{winner} WINS!", new GUIStyle(h2Style) { fontSize = 34, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 0.85f, 0.25f) } });
        GUI.Label(new Rect(x, y + 48, iw, 22), won ? "VICTORY" : $"{MatchRules.ModeName(MatchRules.Mode).ToUpper()}  ·  {MapCatalog.NameOf(MapCatalog.Current).ToUpper()}",
            new GUIStyle(subtitleStyle) { alignment = TextAnchor.MiddleCenter });
        y += 84;

        // Top players.
        float colW = (iw - 30) / 2f;
        GUI.Label(new Rect(x, y, colW, 20), "TOP PLAYERS", smallStyle);
        var players = ModeOrder(FpsPlayer.All.Where(p => p.IsSpawned).OrderByDescending(p => p.Kills.Value).ThenBy(p => p.Deaths.Value).ToList());
        var row = new GUIStyle(bodyStyle) { fontSize = 15, wordWrap = false, clipping = TextClipping.Clip };
        float ry = y + 26;
        for (int i = 0; i < players.Count && i < 6; i++)
        {
            var p = players[i];
            Fill(new Rect(x, ry, colW, 30), new Color(1f, 1f, 1f, p == me ? 0.12f : 0.04f));
            GUI.Label(new Rect(x + 10, ry + 4, 30, 22), (i + 1).ToString(), row);
            GUI.Label(new Rect(x + 36, ry + 4, colW - 170, 22), p.DisplayName + (p == me ? " <size=12>(you)</size>" : ""), row);
            GUI.Label(new Rect(x + colW - 130, ry + 4, 120, 22), $"{p.Kills.Value} / {p.Deaths.Value}  {ModeColumn(p)}", new GUIStyle(row) { alignment = TextAnchor.MiddleRight });
            ry += 34;
        }

        // Your match.
        float sx = x + colW + 30;
        GUI.Label(new Rect(sx, y, colW, 20), "YOUR MATCH", smallStyle);
        int k = me.Kills.Value, d = me.Deaths.Value;
        var fav = statKillsByWeapon.OrderByDescending(kv => kv.Value).FirstOrDefault();
        string favName = statKillsByWeapon.Count > 0 ? $"{WeaponDefs.Get(fav.Key)?.Name} ({fav.Value})" : "—";
        var stats = new[]
        {
            ("Kills", k.ToString()), ("Deaths", d.ToString()), ("K/D", (d == 0 ? k : k / (float)d).ToString("0.00")),
            ("Best streak", statBestStreak.ToString()), ("Accuracy", statShots > 0 ? $"{100f * statHits / statShots:0}%" : "—"), ("Favorite weapon", favName),
        };
        float sy = y + 26;
        foreach (var (name, value) in stats)
        {
            Fill(new Rect(sx, sy, colW, 30), new Color(1f, 1f, 1f, 0.04f));
            GUI.Label(new Rect(sx + 10, sy + 4, colW / 2f, 22), name, row);
            GUI.Label(new Rect(sx + colW / 2f, sy + 4, colW / 2f - 10, 22), $"<b>{value}</b>", new GUIStyle(row) { alignment = TextAnchor.MiddleRight });
            sy += 34;
        }

        // Map vote and the countdown.
        float by = r.yMax - 92;
        float left = Mathf.Max(0f, MatchRules.RestartDelaySeconds - (Time.time - winnerTime));
        bool voting = me.MapVoteOn.Value && MapCatalog.Count >= 3;
        if (voting)
        {
            GUI.Label(new Rect(x, by - 24, iw, 20), "VOTE FOR THE NEXT MAP", smallStyle);
            float mw = (iw - 16) / 3f;
            for (int i = 0; i < 3; i++)
            {
                string text = $"{MapCatalog.NameOf(i).ToUpper()}  ({voteCounts[i]})";
                if (GUI.Button(new Rect(x + i * (mw + 8), by, mw, 40), text, myVote == i ? mapButtonActiveStyle : mapButtonStyle) && myVote != i)
                {
                    myVote = i;
                    me.CastVote(i);
                }
            }
        }
        GUI.Label(new Rect(x, r.yMax - 40, iw, 26), $"Next match in {Mathf.CeilToInt(left)}",
            new GUIStyle(bodyStyle) { alignment = TextAnchor.MiddleCenter });
    }
}
