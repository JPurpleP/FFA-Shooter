using System;
using System.Collections;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// F1 developer panel: bots, cheats, gore lab, debug overlays. Server actions work for the host only.
/// Command-line test flags:
///   -ffabots &lt;n&gt; [Dummy|Strafer|Turret|Fighter]   spawn bots after hosting
///   -ffagoretest                 repeatedly spawn a dummy in front of you and kill it with each weapon
///   -ffagive &lt;0-4&gt;               give the local player a weapon on spawn
///   -ffashots &lt;prefix&gt; &lt;count&gt; &lt;interval&gt;   take a series of screenshots
/// </summary>
public class DevPanel : MonoBehaviour
{
    public static bool Open { get; private set; }
    /// <summary>F1 panel (god mode, give weapons, bots...) only in the editor, development builds or with -ffadev.</summary>
    public static bool Enabled => Application.isEditor || Debug.isDebugBuild || CommandLine.Args.Contains("-ffadev");

    Vector2 scroll;
    int goreWeapon = (int)WeaponId.BarrettM82;
    int gorePart;
    bool goreClose = true;
    bool goreAuto;
    float goreNext;
    int botCounter;
    float slowMo = 1f;
    Material lineMat;

    static readonly string[] PartNames = { "Head", "Chest", "Pelvis", "Arm", "Leg", "Blast" };
    static readonly BodyModel.Part[] PartValues = { BodyModel.Part.Head, BodyModel.Part.Chest, BodyModel.Part.Pelvis, BodyModel.Part.UpperArmR, BodyModel.Part.UpperLegL, BodyModel.Part.Chest };

    static NetworkManager Net => NetworkManager.Singleton;
    static bool IsHost => Net != null && Net.IsServer;

    IEnumerator Start()
    {
        var args = CommandLine.Args;
        string Arg(string flag, int offset = 1) { int i = Array.IndexOf(args, flag); return i >= 0 && i + offset < args.Length ? args[i + offset] : null; }

        // Wait until we're in a session with a local player.
        while (FpsPlayer.Local == null) yield return null;
        yield return new WaitForSeconds(1f);

        if (args.Contains("-ffahook") && IsHost) FpsPlayer.Local.ServerGiveWeapon(WeaponId.GrappleHook);
        if (int.TryParse(Arg("-ffastreak"), out int streak) && IsHost) // pretend you just got N kills in a row
            for (int i = 0; i < streak; i++) FpsPlayer.Local.ServerAddStreak();
        if (int.TryParse(Arg("-ffagive"), out int give) && IsHost)
        {
            var id = (WeaponId)Mathf.Clamp(give, 0, WeaponDefs.All.Length - 1);
            if (id != WeaponId.Fists) FpsPlayer.Local.ServerGiveWeapon(id); // includes throwables (22 smoke, 23 rope, 24 molotov)
        }
        if (int.TryParse(Arg("-ffabots"), out int bots) && IsHost)
        {
            var mode = Enum.TryParse<BotMode>(Arg("-ffabots", 2) ?? "", true, out var m) ? m : BotMode.Fighter;
            for (int i = 0; i < bots; i++) SpawnBot(mode);
            if (int.TryParse(Arg("-ffabotgive"), out int botWeapon))
            {
                yield return new WaitForSeconds(0.5f);
                foreach (var p in FpsPlayer.All)
                    if (p.IsBot.Value) { p.Brain.loadout = (WeaponId)botWeapon; p.ServerGiveWeapon((WeaponId)botWeapon); }
            }
        }
        if (args.Contains("-ffagoretest"))
            goreAuto = true;
        if (int.TryParse(Arg("-ffagorepart"), out int gp)) gorePart = Mathf.Clamp(gp, 0, PartNames.Length - 1);
        if (int.TryParse(Arg("-ffagoreweapon"), out int gw)) goreWeapon = Mathf.Clamp(gw, 0, WeaponDefs.Count - 1);
        // -ffaat x z yaw : teleport the host player (screenshot tests)
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (IsHost && float.TryParse(Arg("-ffaat"), System.Globalization.NumberStyles.Float, inv, out float atX)
            && float.TryParse(Arg("-ffaat", 2), System.Globalization.NumberStyles.Float, inv, out float atZ)
            && float.TryParse(Arg("-ffaat", 3), System.Globalization.NumberStyles.Float, inv, out float atYaw))
        {
            Vector3 at = new Vector3(atX, 60f, atZ);
            if (Physics.Raycast(at, Vector3.down, out var ground, 80f, 1, QueryTriggerInteraction.Ignore)) at = ground.point + Vector3.up * 0.05f;
            FpsPlayer.Local.ServerTeleport(at, atYaw);
            yield return new WaitForSeconds(0.3f);
        }
        if (float.TryParse(Arg("-ffadummy"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float dummyDist) && IsHost)
        {
            float.TryParse(Arg("-ffadummyyaw"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float dummyYaw);
            var d = SpawnFacingDummy(dummyDist, dummyYaw);
            if (int.TryParse(Arg("-ffadummypose"), out int pose)) d.SetPoseOverride(pose);
        }
        // -ffaskinline: one dummy per skin standing in a row in front of you (skin screenshots)
        if (args.Contains("-ffaskinline") && IsHost)
        {
            var me = FpsPlayer.Local;
            Vector3 fwd = Quaternion.Euler(0f, me.Yaw, 0f) * Vector3.forward, right = Quaternion.Euler(0f, me.Yaw, 0f) * Vector3.right;
            // optional: -ffaskinline <first> <count>
            int first = int.TryParse(Arg("-ffaskinline"), out int f0) ? Mathf.Clamp(f0, 0, Skins.Count - 1) : 0;
            int count = int.TryParse(Arg("-ffaskinline", 2), out int c0) ? Mathf.Min(c0, Skins.Count - first) : Skins.Count - first;
            for (int n = 0; n < count; n++)
            {
                int i = first + n;
                var d = FpsPlayer.SpawnBot(BotMode.Dummy, Skins.Get(i).Name);
                d.Brain.fixedSpawn = me.transform.position + fwd * 4.2f + right * (n - (count - 1) / 2f) * 1.25f;
                d.Brain.fixedYaw = me.Yaw + 180f + (float.TryParse(Arg("-ffaskinlineturn"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float lineTurn) ? lineTurn : 0f);
                d.Respawn();
                d.SkinId.Value = (byte)i;
            }
            yield return new WaitForSeconds(0.5f);
        }
        // -ffaduel: drag the first bot into a golden duel with you (duel screenshots / tests)
        if (args.Contains("-ffaduel") && IsHost)
        {
            var opp = FpsPlayer.All.FirstOrDefault(p => p.IsBot.Value && !p.IsDead);
            if (opp != null) DuelManager.TryStart(FpsPlayer.Local, opp);
            yield return new WaitForSeconds(0.5f);
        }
        if (float.TryParse(Arg("-ffaturn"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float turn))
            FpsPlayer.Local.TestTurn(turn);
        if (float.TryParse(Arg("-ffagrapple"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float grapplePitch))
            FpsPlayer.Local.TestGrapple(grapplePitch);
        if (Arg("-ffashots") is { } prefix)
        {
            int count = int.TryParse(Arg("-ffashots", 2), out int c) ? c : 5;
            float interval = float.TryParse(Arg("-ffashots", 3), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float iv) ? iv : 2f;
            for (int i = 0; i < count; i++)
            {
                yield return new WaitForSeconds(interval);
                ScreenCapture.CaptureScreenshot($"{prefix}{i:00}.png");
                Debug.Log($"[FFA] Screenshot {prefix}{i:00}.png");
            }
        }
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.f1Key.wasPressedThisFrame && FpsPlayer.Local != null && Enabled)
        {
            Open = !Open;
            GameUI.OnDevPanelToggled(Open);
        }
        if (FpsPlayer.Local == null && Open)
        {
            Open = false;
            GameUI.OnDevPanelToggled(false);
        }
        if (goreAuto && IsHost && FpsPlayer.Local != null && Time.time > goreNext)
        {
            goreNext = Time.time + 2.5f;
            RunGoreLab(advance: true);
        }
    }

    // ------------------------------------------------------------------ actions (host)

    FpsPlayer SpawnBot(BotMode mode, Action<BotBrain> configure = null)
    {
        if (!IsHost) return null;
        botCounter++;
        var p = FpsPlayer.SpawnBot(mode, $"{mode}Bot {botCounter}");
        return p;
    }

    /// <summary>Host: a dummy standing `distance` meters in front of you, facing you (for looking at characters).</summary>
    FpsPlayer SpawnFacingDummy(float distance, float yawOffset = 0f)
    {
        var me = FpsPlayer.Local;
        Vector3 fwd = Quaternion.Euler(0f, me.Yaw, 0f) * Vector3.forward;
        Vector3 spot = me.transform.position + fwd * distance;
        if (Physics.Raycast(spot + Vector3.up * 3f, Vector3.down, out var hit, 8f, 1, QueryTriggerInteraction.Ignore)) spot = hit.point;
        var dummy = FpsPlayer.SpawnBot(BotMode.Dummy, "Dummy");
        dummy.Brain.fixedSpawn = spot;
        dummy.Brain.fixedYaw = me.Yaw + 180f + yawOffset;
        dummy.Respawn();
        return dummy;
    }

    static void RemoveBots()
    {
        foreach (var p in FpsPlayer.All.ToArray())
            if (p.IsBot.Value && p.IsSpawned) p.NetworkObject.Despawn(true);
    }

    void RunGoreLab(bool advance)
    {
        var me = FpsPlayer.Local;
        if (me == null || !IsHost) return;
        // Spawn (or reuse) a dummy standing in front of the player, then execute it.
        Vector3 fwd = Quaternion.Euler(0f, me.Yaw, 0f) * Vector3.forward;
        float dist = goreClose ? 3.5f : 14f;
        Vector3 spot = me.transform.position + fwd * dist;
        if (Physics.Raycast(spot + Vector3.up * 3f, Vector3.down, out var hit, 8f, 1, QueryTriggerInteraction.Ignore)) spot = hit.point;
        float faceYaw = me.Yaw + 180f;

        var dummy = FpsPlayer.All.FirstOrDefault(p => p.IsBot.Value && p.Brain != null && p.Brain.botName == "Gore Dummy");
        if (dummy == null)
        {
            dummy = FpsPlayer.SpawnBot(BotMode.Dummy, "Gore Dummy");
            dummy.Brain.fixedSpawn = spot;
            dummy.Brain.fixedYaw = faceYaw;
            dummy.Respawn();
            StartCoroutine(KillNextFrame(dummy));
            return;
        }
        dummy.Brain.fixedSpawn = spot;
        dummy.Brain.fixedYaw = faceYaw;
        if (dummy.IsDead) return; // waiting for respawn; next tick
        Execute(dummy, me);
        if (advance)
        {
            gorePart = (gorePart + 1) % PartNames.Length;
            if (gorePart == 0) goreWeapon = (goreWeapon + 1) % WeaponDefs.Count;
        }
    }

    IEnumerator KillNextFrame(FpsPlayer dummy)
    {
        yield return new WaitForSeconds(0.6f);
        if (dummy != null && !dummy.IsDead) Execute(dummy, FpsPlayer.Local);
    }

    void Execute(FpsPlayer victim, FpsPlayer attacker)
    {
        float dist = goreClose ? 3.5f : 14f;
        victim.DevKill(attacker, (WeaponId)goreWeapon, PartValues[gorePart], dist, explosive: gorePart == PartNames.Length - 1);
    }

    // ------------------------------------------------------------------ UI

    void OnGUI()
    {
        float scale = Screen.height / 720f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        DrawBotLabels(scale);
        if (!Open) return;

        var r = new Rect(10, 40, 330, 640);
        GUI.Box(r, "DEV PANEL (F1)");
        GUILayout.BeginArea(new Rect(r.x + 10, r.y + 24, r.width - 20, r.height - 34));
        scroll = GUILayout.BeginScrollView(scroll);
        if (!IsHost)
            GUILayout.Label("<color=#ff8080>Server commands need the host.</color>", new GUIStyle(GUI.skin.label) { richText = true });

        GUILayout.Label("<b>Bots</b>", Rich);
        GUILayout.BeginHorizontal();
        foreach (BotMode mode in Enum.GetValues(typeof(BotMode)))
            if (GUILayout.Button("+" + mode)) SpawnBot(mode);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("+4 Fighters")) for (int i = 0; i < 4; i++) SpawnBot(BotMode.Fighter);
        if (GUILayout.Button("Dummy in front") && IsHost) SpawnFacingDummy(3f);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Bot pose:");
        foreach (var (label, pose) in new[] { ("Auto", -1), ("Stand", 0), ("Crouch", 1), ("Prone", 2), ("Slide", 3) })
            if (GUILayout.Button(label))
                foreach (var p in FpsPlayer.All) if (p.IsBot.Value) p.SetPoseOverride(pose);
        GUILayout.EndHorizontal();
        if (GUILayout.Button("Remove all bots")) RemoveBots();
        GUILayout.EndHorizontal();
        GUILayout.Label($"Bot accuracy: {DevSettings.BotAccuracy:0.00}");
        DevSettings.BotAccuracy = GUILayout.HorizontalSlider(DevSettings.BotAccuracy, 0f, 1f);
        DevSettings.BotsTargetHumansOnly = GUILayout.Toggle(DevSettings.BotsTargetHumansOnly, "Bots only shoot humans");
        DevSettings.BotsInfiniteAmmo = GUILayout.Toggle(DevSettings.BotsInfiniteAmmo, "Bots have infinite ammo");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Bots: invulnerable on/off"))
            foreach (var p in FpsPlayer.All) if (p.Brain != null && p.IsBot.Value) p.Brain.invulnerable = !p.Brain.invulnerable;
        if (GUILayout.Button("Kill all bots"))
            foreach (var p in FpsPlayer.All.ToArray()) if (p.IsBot.Value && !p.IsDead) p.DevKill(FpsPlayer.Local, (WeaponId)goreWeapon, PartValues[gorePart]);
        GUILayout.EndHorizontal();
        GUILayout.Label("Give bots weapon:");
        GUILayout.BeginHorizontal();
        for (int i = 0; i < WeaponDefs.Count; i++)
            if (GUILayout.Button(Short(i)))
                foreach (var p in FpsPlayer.All) if (p.IsBot.Value && !p.IsDead) { p.Brain.loadout = (WeaponId)i; p.ServerGiveWeapon((WeaponId)i); }
        GUILayout.EndHorizontal();

        GUILayout.Space(6);
        GUILayout.Label("<b>Me</b>", Rich);
        GUILayout.BeginHorizontal();
        for (int i = 0; i < WeaponDefs.Count; i++)
            if (GUILayout.Button(Short(i)) && IsHost && FpsPlayer.Local != null) FpsPlayer.Local.ServerGiveWeapon((WeaponId)i);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        foreach (var t in WeaponDefs.Throwables) // grenades (slot 3)
            if (GUILayout.Button(Short((int)t)) && IsHost && FpsPlayer.Local != null) FpsPlayer.Local.ServerGiveWeapon(t);
        GUILayout.EndHorizontal();
        DevSettings.GodMode = GUILayout.Toggle(DevSettings.GodMode, "God mode (host)");
        DevSettings.InfiniteAmmo = GUILayout.Toggle(DevSettings.InfiniteAmmo, "Infinite ammo");
        DevSettings.NoGrappleCooldown = GUILayout.Toggle(DevSettings.NoGrappleCooldown, "No grappling hook cooldown");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Respawn me") && IsHost) FpsPlayer.Local?.Respawn();
        if (GUILayout.Button("Kill me") && IsHost) FpsPlayer.Local?.DevKill(null, (WeaponId)goreWeapon, PartValues[gorePart]);
        GUILayout.EndHorizontal();

        GUILayout.Space(6);
        GUILayout.Label("<b>Gore lab</b>", Rich);
        GUILayout.Label("Weapon:");
        goreWeapon = GUILayout.SelectionGrid(goreWeapon, Enumerable.Range(0, WeaponDefs.Count).Select(Short).ToArray(), 3);
        GUILayout.Label("Hit:");
        gorePart = GUILayout.SelectionGrid(gorePart, PartNames, 6);
        goreClose = GUILayout.Toggle(goreClose, goreClose ? "Point blank (3.5 m)" : "Range (14 m)");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Spawn dummy & execute")) RunGoreLab(advance: false);
        goreAuto = GUILayout.Toggle(goreAuto, "Auto-cycle");
        GUILayout.EndHorizontal();
        GUILayout.Label($"Gore multiplier: {Gore.Multiplier:0.0}x");
        Gore.Multiplier = GUILayout.HorizontalSlider(Gore.Multiplier, 0.5f, 4f);
        if (GUILayout.Button("Test blast at crosshair (corpses)") && FpsPlayer.Local != null
            && Physics.Raycast(FpsPlayer.Local.Head.position, FpsPlayer.Local.Head.forward, out var blastHit, 60f, 1, QueryTriggerInteraction.Ignore))
            Gore.Explosion(blastHit.point + blastHit.normal * 0.3f, 5f, 18f);
        if (GUILayout.Button("Clear corpses & blood")) Gore.ClearAll();

        GUILayout.Space(6);
        GUILayout.Label("<b>World / debug</b>", Rich);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Respawn all weapon pickups") && IsHost) foreach (var s in WeaponSpawner.All) s.SpawnNow();
        GUILayout.EndHorizontal();
        GUILayout.Label($"Time scale (local): {slowMo:0.00}");
        float newSlow = GUILayout.HorizontalSlider(slowMo, 0.1f, 1f);
        if (!Mathf.Approximately(newSlow, slowMo)) { slowMo = newSlow; Time.timeScale = slowMo; }
        DevSettings.ShowDamageNumbers = GUILayout.Toggle(DevSettings.ShowDamageNumbers, "Damage numbers");
        DevSettings.ShowBotStatus = GUILayout.Toggle(DevSettings.ShowBotStatus, "Bot status labels (admin overlay)");
        DevSettings.ShowNavGraph = GUILayout.Toggle(DevSettings.ShowNavGraph, $"Show bot nav graph ({BotNav.NodeCount} nodes)");
        GUILayout.Label($"FPS: {1f / Mathf.Max(0.0001f, Time.smoothDeltaTime):0}   Players: {FpsPlayer.All.Count}   Pickups: {WeaponPickup.All.Count}");
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    static GUIStyle rich;
    static GUIStyle Rich => rich ??= new GUIStyle(GUI.skin.label) { richText = true };

    static string Short(int i) => (WeaponId)i switch
    {
        WeaponId.Glock17 => "Glock",
        WeaponId.MP5 => "MP5",
        WeaponId.M4A1 => "M4",
        WeaponId.Remington870 => "870",
        WeaponId.BarrettM82 => "M82",
        WeaponId.GoldenDeagle => "Deagle",
        WeaponId.M32 => "M32",
        WeaponId.RPG7 => "RPG",
        WeaponId.FragGrenade => "Frags",
        WeaponId.SmokeGrenade => "Smoke",
        WeaponId.RopeGrenade => "Rope",
        WeaponId.Molotov => "Molotov",
        WeaponId.AA12 => "AA-12",
        WeaponId.Mosin => "Mosin",
        WeaponId.PPSh41 => "PPSh",
        WeaponId.M249 => "M249",
        WeaponId.Cattleman => "SAA",
        WeaponId.Magnum44 => ".44",
        WeaponId.LeverAction => "Lever",
        WeaponId.M1911 => "1911",
        WeaponId.GlockSwitch => "Switch",
        WeaponId.KrissVector => "Vector",
        WeaponId.MP18 => "MP-18",
        _ => "?",
    };

    void DrawBotLabels(float scale)
    {
        if (!DevSettings.ShowBotStatus || FpsPlayer.Local == null) return;
        var cam = FpsPlayer.Local.playerCamera;
        if (cam == null || !cam.enabled) return;
        var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12 };
        foreach (var p in FpsPlayer.All)
        {
            if (p == FpsPlayer.Local || p.IsDead || !p.IsBot.Value) continue;
            Vector3 sp = cam.WorldToScreenPoint(p.transform.position + Vector3.up * 2.2f);
            if (sp.z < 0f || sp.z > 40f) continue;
            string status = p.Brain != null ? p.Brain.Status : "";
            var rect = new Rect(sp.x / scale - 100, (Screen.height - sp.y) / scale - 12, 200, 24);
            GUI.color = new Color(0, 0, 0, 0.7f);
            GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), $"{p.DisplayName} {p.Health.Value}hp {status}", style);
            GUI.color = Color.white;
            GUI.Label(rect, $"{p.DisplayName} {p.Health.Value}hp {status}", style);
        }
    }

    void OnRenderObject()
    {
        if (!DevSettings.ShowNavGraph || GameAssets.I == null) return;
        if (lineMat == null) lineMat = new Material(GameAssets.I.tracer);
        lineMat.SetPass(0);
        GL.Begin(GL.LINES);
        GL.Color(Color.cyan);
        BotNav.DrawGizmoLines((a, b) => { GL.Vertex(a); GL.Vertex(b); });
        GL.End();
    }
}
