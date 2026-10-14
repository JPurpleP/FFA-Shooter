/// <summary>Testing toggles, changed from the F1 dev panel. Server-side ones only take effect on the host.</summary>
public static class DevSettings
{
    public static bool GodMode;
    public static bool InfiniteAmmo;
    public static bool BotsInfiniteAmmo = true;
    public static bool BotsTargetHumansOnly;
    public static float BotAccuracy = 0.5f;
    public static bool ShowDamageNumbers = true;
    public static bool ShowBotStatus; // admin overlay: names/health above bots
    public static bool ShowNavGraph;
    public static bool NoGrappleCooldown;
}
