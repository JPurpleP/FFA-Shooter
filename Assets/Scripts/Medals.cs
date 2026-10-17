using System.Collections.Generic;
using UnityEngine;

public enum Medal : byte { FirstBlood, DoubleKill, TripleKill, MultiKill, Revenge, Buzzkill, CloseCall, LongShot, Sharpshooter }

/// <summary>
/// Medals for notable kills, decided by the server and shown to the player who earned them as a short pop-up with
/// its own sting (GameUI.ShowMedal).
/// </summary>
public static class Medals
{
    public const float ChainWindow = 4f;      // seconds between kills for a double / triple / multi kill
    public const float LongShotMetres = 60f;
    public const int CloseCallHealth = 15;

    static readonly Dictionary<FpsPlayer, (float time, int count)> chains = new();
    static readonly Dictionary<FpsPlayer, FpsPlayer> lastKilledBy = new();
    static readonly Dictionary<FpsPlayer, int> headshots = new();
    static bool firstBloodTaken;

    /// <summary>Server: a new match (or session) starts.</summary>
    public static void Reset()
    {
        chains.Clear();
        lastKilledBy.Clear();
        headshots.Clear();
        firstBloodTaken = false;
    }

    /// <summary>Server: `killer` just killed `victim`. victimStreak / killerHealth are from just before the kill.</summary>
    public static void ServerOnKill(FpsPlayer killer, FpsPlayer victim, DeathInfo info, int victimStreak, int killerHealth)
    {
        if (victim != null) headshots.Remove(victim);
        if (killer == null || killer == victim || !killer.IsSpawned)
        {
            if (victim != null) lastKilledBy.Remove(victim);
            return;
        }
        var earned = new List<Medal>();
        if (!firstBloodTaken) { firstBloodTaken = true; earned.Add(Medal.FirstBlood); }

        float now = Time.time;
        chains.TryGetValue(killer, out var c);
        c = now - c.time <= ChainWindow ? (now, c.count + 1) : (now, 1);
        chains[killer] = c;
        if (c.count == 2) earned.Add(Medal.DoubleKill);
        else if (c.count == 3) earned.Add(Medal.TripleKill);
        else if (c.count >= 4) earned.Add(Medal.MultiKill);

        if (lastKilledBy.TryGetValue(killer, out var nemesis) && nemesis == victim)
        {
            earned.Add(Medal.Revenge);
            lastKilledBy.Remove(killer);
        }
        if (victimStreak >= 3) earned.Add(Medal.Buzzkill);
        if (killerHealth > 0 && killerHealth <= CloseCallHealth) earned.Add(Medal.CloseCall);
        if (info.Distance >= LongShotMetres) earned.Add(Medal.LongShot);

        bool head = info.HitPart == BodyModel.Part.Head && !info.Explosive && !info.Fell && !info.Train;
        int hs = head ? (headshots.TryGetValue(killer, out int h) ? h + 1 : 1) : 0;
        headshots[killer] = hs;
        if (hs == 3) earned.Add(Medal.Sharpshooter);

        lastKilledBy[victim] = killer;
        foreach (var m in earned)
        {
            killer.AwardMedal(m);
            Debug.Log($"[FFA] Medal {Name(m)}: {killer.DisplayName}");
        }
    }

    public static string Name(Medal m) => m switch
    {
        Medal.FirstBlood => "FIRST BLOOD",
        Medal.DoubleKill => "DOUBLE KILL",
        Medal.TripleKill => "TRIPLE KILL",
        Medal.MultiKill => "MULTI KILL",
        Medal.Revenge => "REVENGE",
        Medal.Buzzkill => "BUZZKILL",
        Medal.CloseCall => "CLOSE CALL",
        Medal.LongShot => "LONG SHOT",
        Medal.Sharpshooter => "SHARPSHOOTER",
        _ => m.ToString().ToUpper(),
    };

    public static string Detail(Medal m) => m switch
    {
        Medal.FirstBlood => "First kill of the match",
        Medal.DoubleKill => "Two kills in quick succession",
        Medal.TripleKill => "Three kills in quick succession",
        Medal.MultiKill => "Four or more in quick succession",
        Medal.Revenge => "Killed the player who last killed you",
        Medal.Buzzkill => "Ended a killstreak",
        Medal.CloseCall => "Got the kill on almost no health",
        Medal.LongShot => $"Kill from {LongShotMetres:0} m or more",
        Medal.Sharpshooter => "Three headshot kills in a row",
        _ => "",
    };

    public static Color Tint(Medal m) => m switch
    {
        Medal.FirstBlood => new Color(1f, 0.35f, 0.3f),
        Medal.DoubleKill => new Color(1f, 0.75f, 0.3f),
        Medal.TripleKill => new Color(1f, 0.6f, 0.2f),
        Medal.MultiKill => new Color(1f, 0.45f, 0.15f),
        Medal.Revenge => new Color(0.85f, 0.45f, 1f),
        Medal.Buzzkill => new Color(0.45f, 0.85f, 1f),
        Medal.CloseCall => new Color(1f, 0.4f, 0.55f),
        Medal.LongShot => new Color(0.5f, 1f, 0.6f),
        Medal.Sharpshooter => new Color(1f, 0.9f, 0.4f),
        _ => Color.white,
    };

    // ------------------------------------------------------------------ stings (generated)

    static readonly Dictionary<Medal, AudioClip> stings = new();

    /// <summary>A short rising arpeggio; bigger medals get more notes and a higher top.</summary>
    public static AudioClip Sting(Medal m)
    {
        if (stings.TryGetValue(m, out var clip)) return clip;
        int notes = m switch { Medal.MultiKill => 4, Medal.TripleKill or Medal.Sharpshooter or Medal.FirstBlood => 3, _ => 2 };
        float root = m switch { Medal.Revenge => 392f, Medal.Buzzkill => 440f, Medal.CloseCall => 370f, Medal.LongShot => 494f, _ => 523f };
        float[] steps = { 1f, 1.26f, 1.5f, 2f };
        const int rate = 44100;
        float noteLen = 0.085f, len = noteLen * notes + 0.35f;
        var d = new float[(int)(rate * len)];
        for (int n = 0; n < notes; n++)
        {
            float f = root * steps[n];
            int start = (int)(n * noteLen * rate);
            for (int i = start; i < d.Length; i++)
            {
                float t = (i - start) / (float)rate;
                float env = Mathf.Clamp01(t / 0.006f) * Mathf.Exp(-t * (n == notes - 1 ? 7f : 14f));
                d[i] += (Mathf.Sin(t * f * 2f * Mathf.PI) * 0.6f + Mathf.Sin(t * f * 4f * Mathf.PI) * 0.2f + Mathf.Sin(t * f * 6f * Mathf.PI) * 0.08f) * env * 0.32f;
            }
        }
        clip = AudioClip.Create("medal-" + m, d.Length, 1, rate, false);
        clip.SetData(d, 0);
        stings[m] = clip;
        return clip;
    }
}
