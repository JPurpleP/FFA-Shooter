using UnityEngine;

/// <summary>Player preferences from the main menu, saved between launches.</summary>
public static class GameSettings
{
    public static string Name = "";
    public static int Skin;
    public static float Sensitivity = 0.1f;
    public static float Fov = 75f;
    public static float Volume = 1f;
    public static float GoreLevel = 2f;
    public static int HostBots;
    public static int Map; // map the host starts (MapCatalog index)
    public static bool HostOnline = true; // host through Unity Relay (join code) instead of a direct IP

    public static void Load()
    {
        Name = PlayerPrefs.GetString("ffa.name", "Player" + Random.Range(100, 1000));
        Skin = Mathf.Clamp(PlayerPrefs.GetInt("ffa.skin", 0), 0, Skins.Count - 1);
        Sensitivity = PlayerPrefs.GetFloat("ffa.sens", 0.1f);
        Fov = PlayerPrefs.GetFloat("ffa.fov", 75f);
        Volume = PlayerPrefs.GetFloat("ffa.volume", 1f);
        GoreLevel = PlayerPrefs.GetFloat("ffa.gore", 2f);
        HostBots = PlayerPrefs.GetInt("ffa.hostbots", 0);
        Map = Mathf.Max(0, PlayerPrefs.GetInt("ffa.map", 0));
        HostOnline = PlayerPrefs.GetInt("ffa.hostonline", 1) != 0;
        Apply();
    }

    public static void Save()
    {
        PlayerPrefs.SetString("ffa.name", Name);
        PlayerPrefs.SetInt("ffa.skin", Skin);
        PlayerPrefs.SetFloat("ffa.sens", Sensitivity);
        PlayerPrefs.SetFloat("ffa.fov", Fov);
        PlayerPrefs.SetFloat("ffa.volume", Volume);
        PlayerPrefs.SetFloat("ffa.gore", GoreLevel);
        PlayerPrefs.SetInt("ffa.hostbots", HostBots);
        PlayerPrefs.SetInt("ffa.map", Map);
        PlayerPrefs.SetInt("ffa.hostonline", HostOnline ? 1 : 0);
        PlayerPrefs.Save();
        Apply();
    }

    public static void Apply()
    {
        AudioListener.volume = Volume;
        Gore.Multiplier = GoreLevel;
    }

    public static void ResetToDefaults()
    {
        Sensitivity = 0.1f;
        Fov = 75f;
        Volume = 1f;
        GoreLevel = 2f;
        Save();
    }
}
