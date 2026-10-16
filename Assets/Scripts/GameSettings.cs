using UnityEngine;

/// <summary>Player preferences from the main menu, saved between launches.</summary>
public static class GameSettings
{
    public static string Name = "";
    public static int Skin;
    public static float Sensitivity = 0.1f;
    public static float AdsSensitivity = 1f; // extra multiplier while aiming down sights
    public static bool InvertY;
    public static float Fov = 75f;
    public static float Volume = 1f;
    public static float GoreLevel = 2f;
    public static int Map; // map the host starts (MapCatalog index)
    public static bool VSync = true;
    public static int Graphics = 2; // 0 low, 1 medium, 2 high (GraphicsQuality)
    public static int VoiceMode = 1; // 0 off, 1 push to talk, 2 open mic (VoiceChat)
    public static float VoiceVolume = 1f, MicGain = 1f, VoiceGate = 0.02f;
    public static string MicDevice = ""; // empty: the system default
    public static int FragLimit = 25;
    public static bool PublicGame = true; // list hosted games in Find Game
    public static int Mode;               // MatchRules.GameMode the host last picked
    public static bool MapVote = true;    // vote for the next map after each match // the host's last "score to win" (lobby slider)

    public static void Load()
    {
        Name = PlayerPrefs.GetString("ffa.name", "Player" + Random.Range(100, 1000));
        Skin = Mathf.Clamp(PlayerPrefs.GetInt("ffa.skin", 0), 0, Skins.Count - 1);
        Sensitivity = PlayerPrefs.GetFloat("ffa.sens", 0.1f);
        AdsSensitivity = PlayerPrefs.GetFloat("ffa.adssens", 1f);
        InvertY = PlayerPrefs.GetInt("ffa.inverty", 0) != 0;
        Fov = PlayerPrefs.GetFloat("ffa.fov", 75f);
        Volume = PlayerPrefs.GetFloat("ffa.volume", 1f);
        GoreLevel = PlayerPrefs.GetFloat("ffa.gore", 2f);
        Map = Mathf.Max(0, PlayerPrefs.GetInt("ffa.map", 0));
        VSync = PlayerPrefs.GetInt("ffa.vsync", 1) != 0;
        Graphics = Mathf.Clamp(PlayerPrefs.GetInt("ffa.graphics", 2), 0, 2);
        VoiceMode = Mathf.Clamp(PlayerPrefs.GetInt("ffa.voice", 1), 0, 2);
        VoiceVolume = PlayerPrefs.GetFloat("ffa.voicevol", 1f);
        MicGain = PlayerPrefs.GetFloat("ffa.micgain", 1f);
        VoiceGate = PlayerPrefs.GetFloat("ffa.voicegate", 0.02f);
        MicDevice = PlayerPrefs.GetString("ffa.mic", "");
        FragLimit = Mathf.Clamp(PlayerPrefs.GetInt("ffa.fraglimit", 25), 10, 100);
        PublicGame = PlayerPrefs.GetInt("ffa.public", 1) != 0;
        Mode = Mathf.Clamp(PlayerPrefs.GetInt("ffa.mode", 0), 0, 4);
        MapVote = PlayerPrefs.GetInt("ffa.mapvote", 1) != 0;
        Apply();
    }

    public static void Save()
    {
        PlayerPrefs.SetString("ffa.name", Name);
        PlayerPrefs.SetInt("ffa.skin", Skin);
        PlayerPrefs.SetFloat("ffa.sens", Sensitivity);
        PlayerPrefs.SetFloat("ffa.adssens", AdsSensitivity);
        PlayerPrefs.SetInt("ffa.inverty", InvertY ? 1 : 0);
        PlayerPrefs.SetFloat("ffa.fov", Fov);
        PlayerPrefs.SetFloat("ffa.volume", Volume);
        PlayerPrefs.SetFloat("ffa.gore", GoreLevel);
        PlayerPrefs.SetInt("ffa.map", Map);
        PlayerPrefs.SetInt("ffa.vsync", VSync ? 1 : 0);
        PlayerPrefs.SetInt("ffa.graphics", Graphics);
        PlayerPrefs.SetInt("ffa.voice", VoiceMode);
        PlayerPrefs.SetFloat("ffa.voicevol", VoiceVolume);
        PlayerPrefs.SetFloat("ffa.micgain", MicGain);
        PlayerPrefs.SetFloat("ffa.voicegate", VoiceGate);
        PlayerPrefs.SetString("ffa.mic", MicDevice);
        PlayerPrefs.SetInt("ffa.fraglimit", FragLimit);
        PlayerPrefs.SetInt("ffa.public", PublicGame ? 1 : 0);
        PlayerPrefs.SetInt("ffa.mode", Mode);
        PlayerPrefs.SetInt("ffa.mapvote", MapVote ? 1 : 0);
        PlayerPrefs.Save();
        Apply();
    }

    public static void Apply()
    {
        AudioListener.volume = Volume;
        QualitySettings.vSyncCount = VSync ? 1 : 0;
        if (!VSync) Application.targetFrameRate = 240;
        Gore.Multiplier = GoreLevel * 0.5f; // everything is half as gory as the raw setting (beta feedback)
    }

    public static void ResetToDefaults()
    {
        Sensitivity = 0.1f;
        AdsSensitivity = 1f;
        InvertY = false;
        Fov = 75f;
        Volume = 1f;
        GoreLevel = 2f;
        Save();
    }
}
