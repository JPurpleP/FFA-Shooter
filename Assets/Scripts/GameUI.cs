using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Main menu (host / join), in-game HUD, scoreboard and pause menu, drawn with IMGUI.
/// Also reads command-line flags for quick testing:
///   -ffahost | -ffarelay | -ffaclient &lt;ip or join code&gt;   start immediately (-ffarelay hosts online with a join code;
///   -ffahost and IP joins are direct connections kept for automated tests only, the menu is online-only)
///   -ffaprofile &lt;name&gt;   separate Unity services sign-in, for several copies on one PC
///   -ffalobby   a command-line host starts in the waiting lobby like a menu host
///   -ffaready   ready up in the lobby   -ffastartwhenready   (host) start the match once everyone is ready
///   -ffaname &lt;name&gt;  -ffaport &lt;port&gt;
///   -ffaautotest   the local player aims and fires at the nearest enemy by itself
///   -ffaquitafter &lt;seconds&gt;   -ffascreenshot &lt;png path&gt; (taken after 4 seconds)
/// </summary>
public partial class GameUI : MonoBehaviour
{
    public Camera menuCamera;

    public static string LocalPlayerName = "Player";
    public static bool AutoTest { get; private set; }
    public static bool ForceAim { get; private set; } // -ffaaim: hold aim-down-sights (screenshot tests)
    public static float ForceLean { get; private set; } // -ffalean <-1..1>: hold a lean (screenshot tests)
    public static bool ForceScores { get; private set; } // -ffascores: keep the leaderboard open (screenshot tests)
    public static int TestStance { get; private set; } = -1;
    public static int TestThrowType { get; private set; } = -1; // -ffathrowtype 0-3: frag / smoke / rope / molotov (tests) // -ffastance 1|2|3: crouch / prone / sprint-and-slide (tests)
    static bool hideUi;
    public static bool ForceFire { get; private set; } // -ffafire: hold the trigger (effects screenshots)
    public static bool ForceThrow { get; private set; } // -ffathrow: cook and throw a grenade (tests)
    public static bool CenterDot { get; private set; }  // -ffacenterdot: mark the exact screen centre (sight alignment checks)
    static bool autoReady, autoStart, forcePause;
    static int lobbyMapTest = -1;
    static int modeOverride = -1; // -ffamode 0-4 (tests)
    /// <summary>-ffainspect x y z: hold the view model turned by these angles (checking hands for clipping).</summary>
    public static Vector3? InspectEuler { get; private set; }
    public static bool ReloadTest { get; private set; } // -ffareloadtest
    public static (Vector3 pos, Quaternion rot)? CinematicCam { get; private set; } // -ffacam
    bool wasSummary; // forcePause: -ffapause keeps the Esc menu up (screenshots) // -ffaready / -ffastartwhenready: drive the lobby without clicks (tests)
    public static bool CursorCaptured => Cursor.lockState == CursorLockMode.Locked && Time.unscaledTime > captureTime + 0.15f;

    static GameUI instance;
    AudioListener menuListener;
    static float captureTime;

    string address = "";
    string port = "7777";
    string status = "";
    readonly List<(string text, float time)> killFeed = new();
    float hitMarkerTime = -10f, damageFlashTime = -10f, deathTime, winnerTime;
    bool hitMarkerHeadshot, hitMarkerKill, wasInGame, wasDead;
    string winner = "";
    readonly List<(Vector3 point, int amount, bool head, float time)> damageNumbers = new();
    readonly List<(Vector2 pos, float size, float rot, float time)> screenBlood = new();

    Texture2D white;
    GUIStyle label, bigLabel, centerLabel, hugeLabel, boxStyle;

    static NetworkManager Net => NetworkManager.Singleton;
    static bool InSession => Net != null && (Net.IsClient || Net.IsServer);

    // ---------------------------------------------------------------- static hooks used by FpsPlayer

    public static void AddKillFeed(string text)
    {
        if (instance == null) return;
        instance.killFeed.Add((text, Time.time));
        if (instance.killFeed.Count > 6)
            instance.killFeed.RemoveAt(0);
    }

    public static void ShowHitMarker(bool headshot, bool killed, int damage, Vector3 point)
    {
        if (instance == null) return;
        instance.hitMarkerTime = Time.time;
        instance.hitMarkerHeadshot = headshot;
        instance.hitMarkerKill = killed;
        instance.damageNumbers.Add((point, damage, headshot, Time.time));
        if (instance.damageNumbers.Count > 30) instance.damageNumbers.RemoveAt(0);
    }

    public static void FlashDamage(int amount)
    {
        if (instance == null) return;
        instance.damageFlashTime = Time.time;
        // Blood splatters on the screen, more for bigger hits.
        int count = Mathf.Clamp(amount / 12, 1, 8);
        for (int i = 0; i < count; i++)
            instance.screenBlood.Add((new Vector2(UnityEngine.Random.value, UnityEngine.Random.value), UnityEngine.Random.Range(90f, 260f), UnityEngine.Random.Range(0f, 360f), Time.time));
        while (instance.screenBlood.Count > 24) instance.screenBlood.RemoveAt(0);
    }

    public static void OnDevPanelToggled(bool open) => SetCursorCaptured(!open);

    readonly List<(Vector3 from, float time)> damageFrom = new();

    /// <summary>The local player was hurt by something at `from`: show a red arc pointing at it.</summary>
    public static void AddDamageIndicator(Vector3 from)
    {
        if (instance == null) return;
        instance.damageFrom.Add((from, Time.time));
        if (instance.damageFrom.Count > 8) instance.damageFrom.RemoveAt(0);
    }

    void DrawDamageIndicators(FpsPlayer me, float w, float h, float t)
    {
        damageFrom.RemoveAll(d => t - d.time > 1.6f);
        if (me.playerCamera == null) return;
        var cam = me.playerCamera.transform;
        Vector3 fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
        if (fwd.sqrMagnitude < 0.001f) fwd = me.transform.forward;
        var c = new Vector2(w / 2f, h / 2f);
        foreach (var (from, time) in damageFrom)
        {
            Vector3 to = Vector3.ProjectOnPlane(from - cam.position, Vector3.up);
            if (to.sqrMagnitude < 0.01f) continue;
            float ang = Vector3.SignedAngle(fwd, to, Vector3.up) * Mathf.Deg2Rad;
            float a = Mathf.Clamp01(1f - (t - time) / 1.6f);
            var col = new Color(1f, 0.15f, 0.1f, 0.85f * a);
            const float r = 120f, half = 0.32f;
            for (int i = 0; i < 6; i++)
            {
                float a0 = ang - half + i * half / 3f, a1 = a0 + half / 3f;
                DrawLine(c + new Vector2(Mathf.Sin(a0), -Mathf.Cos(a0)) * r, c + new Vector2(Mathf.Sin(a1), -Mathf.Cos(a1)) * r, 7f, col);
            }
        }
    }

    static readonly string[] CompassNames = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

    /// <summary>Heading strip under the match info (the maps are big now).</summary>
    void DrawCompass(FpsPlayer me, float w)
    {
        const float span = 100f, width = 440f, y = 38f; // degrees shown either side, pixels
        float cx = w / 2f, yaw = me.Yaw;
        Fill(new Rect(cx - width / 2f, y, width, 22f), new Color(0f, 0f, 0f, 0.25f));
        var tick = new Color(1f, 1f, 1f, 0.55f);
        var style = St(centerLabel, fontSize: 12, fontStyle: FontStyle.Bold);
        for (int deg = 0; deg < 360; deg += 15)
        {
            float d = Mathf.DeltaAngle(yaw, deg);
            if (Mathf.Abs(d) > span) continue;
            float x = cx + d / span * width / 2f;
            float fade = 1f - Mathf.Abs(d) / span;
            if (deg % 45 == 0)
                Shadowed(new Rect(x - 20f, y + 2f, 40f, 18f), CompassNames[deg / 45], style, new Color(1f, deg == 0 ? 0.55f : 1f, deg == 0 ? 0.3f : 1f, fade));
            else
                Fill(new Rect(x - 0.5f, y + 7f, 1f, 8f), new Color(tick.r, tick.g, tick.b, tick.a * fade));
        }
        Fill(new Rect(cx - 1f, y + 20f, 2f, 6f), Accent);
    }

    // ---------------------------------------------------------------- ESP (5-kill streak)

    readonly List<Vector3> espBones = new();

    /// <summary>Box outline, name and live skeleton of every enemy, drawn over everything (walls included).</summary>
    void DrawEsp(FpsPlayer me, float w, float h, float scale)
    {
        var cam = me.playerCamera;
        if (cam == null) return;
        Vector2? Screen2(Vector3 world)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z < 0.1f) return null;
            return new Vector2(sp.x / scale, (Screen.height - sp.y) / scale);
        }
        var espColor = Accessible(new Color(0.35f, 1f, 0.5f, 0.95f));
        var nameStyle = St(centerLabel, fontSize: 13, fontStyle: FontStyle.Bold);
        foreach (var p in FpsPlayer.All)
        {
            if (!me.IsEnemyOf(p)) continue;
            // Box from the feet to just over the head, sized by the current stance.
            Vector3 feet = p.transform.position, topPt = p.AimPoint + Vector3.up * 0.22f;
            var corners = new[] { feet + p.transform.right * 0.4f, feet - p.transform.right * 0.4f, topPt + p.transform.right * 0.4f, topPt - p.transform.right * 0.4f };
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            bool visible = true;
            foreach (var c in corners)
            {
                var s = Screen2(c);
                if (s == null) { visible = false; break; }
                minX = Mathf.Min(minX, s.Value.x); maxX = Mathf.Max(maxX, s.Value.x);
                minY = Mathf.Min(minY, s.Value.y); maxY = Mathf.Max(maxY, s.Value.y);
            }
            if (!visible) continue;
            float bw = Mathf.Max(maxX - minX, (maxY - minY) * 0.45f), cx = (minX + maxX) / 2f;
            var box = new Rect(cx - bw / 2f, minY, bw, maxY - minY);
            DrawLine(new Vector2(box.xMin, box.yMin), new Vector2(box.xMax, box.yMin), 1.6f, espColor);
            DrawLine(new Vector2(box.xMin, box.yMax), new Vector2(box.xMax, box.yMax), 1.6f, espColor);
            DrawLine(new Vector2(box.xMin, box.yMin), new Vector2(box.xMin, box.yMax), 1.6f, espColor);
            DrawLine(new Vector2(box.xMax, box.yMin), new Vector2(box.xMax, box.yMax), 1.6f, espColor);
            // Name, distance and a health bar.
            float dist = Vector3.Distance(me.transform.position, p.transform.position);
            Shadowed(new Rect(cx - 100f, box.yMin - 20f, 200f, 18f), $"{p.DisplayName}  <size=11>{dist:0}m</size>", nameStyle, espColor);
            float hp = Mathf.Clamp01(p.Health.Value / (float)FpsPlayer.MaxHealth);
            Fill(new Rect(box.xMin - 5f, box.yMin, 3f, box.height), new Color(0f, 0f, 0f, 0.6f));
            Fill(new Rect(box.xMin - 5f, box.yMax - box.height * hp, 3f, box.height * hp), HealthColor(hp));
            // Skeleton.
            if (p.Body == null) continue;
            p.Body.GetBones(espBones);
            for (int i = 0; i + 1 < espBones.Count; i += 2)
            {
                var a = Screen2(espBones[i]);
                var b = Screen2(espBones[i + 1]);
                if (a != null && b != null) DrawLine(a.Value, b.Value, 1.4f, new Color(1f, 1f, 1f, 0.9f));
            }
        }
    }

    static readonly Color ProtectColor = new(0.45f, 0.85f, 1f);

    /// <summary>Your own spawn-protection timer, and a tag over protected enemies you can actually see.</summary>
    void DrawSpawnProtection(FpsPlayer me, float w, float h, float scale)
    {
        if (me.SpawnProtected)
        {
            float left = me.SpawnProtectLeft;
            Shadowed(new Rect(0, h * 0.62f, w, 26), $"<b>SPAWN PROTECTION</b>  {left:0.0}s", St(centerLabel, fontSize: 18), ProtectColor);
            Shadowed(new Rect(0, h * 0.62f + 24, w, 20), "Moving or firing ends it", St(centerLabel, fontSize: 13), new Color(1f, 1f, 1f, 0.7f));
            Fill(new Rect(w / 2 - 80, h * 0.62f + 48, 160 * left / FpsPlayer.SpawnProtectSeconds, 3), ProtectColor);
        }
        var cam = me.playerCamera;
        if (cam == null) return;
        var tag = St(centerLabel, fontSize: 12, fontStyle: FontStyle.Bold);
        foreach (var p in FpsPlayer.All)
        {
            if (!me.IsEnemyOf(p) || !p.SpawnProtected) continue;
            Vector3 top = p.AimPoint + Vector3.up * 0.35f;
            Vector3 sp = cam.WorldToScreenPoint(top);
            if (sp.z < 0.1f || sp.z > 80f) continue;
            if (Physics.Linecast(cam.transform.position, p.AimPoint, 1, QueryTriggerInteraction.Ignore)) continue; // behind cover
            Shadowed(new Rect(sp.x / scale - 60, (Screen.height - sp.y) / scale - 10, 120, 20), "PROTECTED", tag, ProtectColor);
        }
    }

    /// <summary>The key currently bound to an action, for on-screen hints.</summary>
    static string K(GameAction a) => GamepadInput.UsingGamepad ? GamepadInput.Label(a) : Bindings.Label(a);

    static string ThrowShort(int type) => type switch { 0 => "Frag", 1 => "Smoke", 2 => "Rope", _ => "Molotov" };

    static void DrawLine(Vector2 a, Vector2 b, float width, Color color)
    {
        Vector2 d = b - a;
        float len = d.magnitude;
        if (len < 0.5f) return;
        var oldMatrix = GUI.matrix;
        var oldColor = GUI.color;
        GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
        GUI.color = color;
        GUI.DrawTexture(new Rect(a.x, a.y - width / 2f, len, width), Texture2D.whiteTexture);
        GUI.matrix = oldMatrix;
        GUI.color = oldColor;
    }

    // ---------------------------------------------------------------- explosion dust / debris on the screen

    struct Grit { public Vector2 pos, vel; public float size, rot, spin, born, life; public Color color; public bool cloud; }
    readonly List<Grit> grit = new();
    float blastFlashTime = -10f, blastFlash;
    Texture2D dustTex, speckTex;

    /// <summary>A nearby blast: dust clouds and flying debris across part of the screen, toward `dir` (screen space, y up).</summary>
    public static void ExplosionOverlay(float strength, Vector2 dir)
    {
        if (instance == null) return;
        instance.blastFlashTime = Time.time;
        instance.blastFlash = Mathf.Max(instance.blastFlash * 0.5f, strength);
        var center = new Vector2(0.5f + dir.x * 0.32f, 0.5f - dir.y * 0.25f);
        int clouds = Mathf.RoundToInt(3 + strength * 7), specks = Mathf.RoundToInt(10 + strength * 45);
        var r = new System.Random();
        float R(float a, float b) => a + (float)r.NextDouble() * (b - a);
        for (int i = 0; i < clouds; i++)
        {
            var p = center + new Vector2(R(-0.35f, 0.35f), R(-0.3f, 0.3f)) * (0.6f + strength * 0.6f);
            float grey = R(0.45f, 0.65f);
            instance.grit.Add(new Grit
            {
                pos = p, vel = (p - new Vector2(0.5f, 0.5f)) * R(0.02f, 0.06f) + new Vector2(0f, R(0.005f, 0.02f)),
                size = R(260f, 560f) * (0.6f + strength * 0.6f), rot = R(0f, 360f), spin = R(-8f, 8f),
                born = Time.time, life = R(2f, 3.4f), color = new Color(grey * 1.05f, grey * 0.95f, grey * 0.82f, R(0.6f, 0.9f) * (0.5f + strength * 0.5f)), cloud = true,
            });
        }
        for (int i = 0; i < specks; i++)
        {
            var p = center + new Vector2(R(-0.45f, 0.45f), R(-0.4f, 0.4f));
            float c = R(0.08f, 0.3f);
            instance.grit.Add(new Grit
            {
                pos = p, vel = new Vector2(R(-0.15f, 0.15f), R(0.05f, 0.35f)), size = R(5f, 22f), rot = R(0f, 360f), spin = R(-200f, 200f),
                born = Time.time, life = R(0.8f, 2.2f), color = new Color(c * 1.1f, c, c * 0.85f, 1f), cloud = false,
            });
        }
        while (instance.grit.Count > 160) instance.grit.RemoveAt(0);
    }

    void DrawExplosionOverlay(float w, float h, float t)
    {
        if (dustTex == null)
        {
            // Soft, lumpy dust cloud and a hard-edged chunk, generated once.
            const int S = 128;
            dustTex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            speckTex = new Texture2D(32, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x - S / 2f) / (S / 2f), dy = (y - S / 2f) / (S / 2f);
                    float n = Mathf.PerlinNoise(x * 0.06f + 3f, y * 0.06f + 7f) * 0.6f + Mathf.PerlinNoise(x * 0.15f, y * 0.15f) * 0.4f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) * (1.15f - n * 0.5f));
                    dustTex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float dx = (x - 15.5f) / 16f, dy = (y - 15.5f) / 16f;
                    float edge = 0.75f + 0.25f * Mathf.PerlinNoise(x * 0.4f, y * 0.4f);
                    speckTex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Sqrt(dx * dx + dy * dy) < edge ? 1f : 0f));
                }
            dustTex.Apply();
            speckTex.Apply();
        }
        float flash = blastFlash * Mathf.Clamp01(1f - (t - blastFlashTime) / 0.35f);
        if (flash > 0f) Fill(new Rect(0, 0, w, h), new Color(1f, 0.85f, 0.6f, 0.35f * flash));
        grit.RemoveAll(g => t - g.born > g.life);
        var old = GUI.color;
        var oldMatrix = GUI.matrix;
        foreach (var g in grit)
        {
            float age = t - g.born, k = age / g.life;
            float alpha = g.cloud ? Mathf.Clamp01(age / 0.12f) * (1f - k * k) : 1f - k;
            Vector2 p = g.pos + g.vel * age + (g.cloud ? Vector2.zero : new Vector2(0f, 0.25f * age * age));
            float size = g.size * (g.cloud ? 1f + k * 0.5f : 1f);
            var rect = new Rect(p.x * w - size / 2f, p.y * h - size / 2f, size, size);
            GUI.matrix = oldMatrix;
            GUIUtility.RotateAroundPivot(g.rot + g.spin * age, rect.center);
            GUI.color = new Color(g.color.r, g.color.g, g.color.b, g.color.a * alpha);
            GUI.DrawTexture(rect, g.cloud ? dustTex : speckTex);
        }
        GUI.matrix = oldMatrix;
        GUI.color = old;
    }

    string bannerText = "", bannerSub = "";
    Color bannerColor = Color.white;
    float bannerTime = -10f;

    /// <summary>Big centred announcement for a few seconds (golden duel start / result).</summary>
    public static void ShowBanner(string text, string sub, Color color)
    {
        if (instance == null) return;
        instance.bannerText = text;
        instance.bannerSub = sub;
        instance.bannerColor = color;
        instance.bannerTime = Time.time;
    }

    public static void SetWinner(string name)
    {
        if (instance == null) return;
        // A match just ended: count it in your career (only real matches: two or more humans).
        if (!string.IsNullOrEmpty(name) && string.IsNullOrEmpty(instance.winner) && FpsPlayer.Local != null)
        {
            int humans = 0;
            foreach (var p in FpsPlayer.All) if (p.IsSpawned && !p.IsBot.Value) humans++;
            if (humans >= 2)
                Career.RecordMatch(FpsPlayer.Local.Kills.Value, FpsPlayer.Local.Deaths.Value, MatchRules.LocalWon(name));
        }
        instance.winner = name;
        instance.winnerTime = Time.time;
        if (!string.IsNullOrEmpty(name))
            Debug.Log($"[FFA] Match won by {name}");
    }

    // ---------------------------------------------------------------- lifecycle

    void Awake()
    {
        instance = this;
        GameSettings.Load();
        LocalPlayerName = GameSettings.Name;
    }

    void Start()
    {
        Net.OnClientDisconnectCallback += OnClientDisconnect;
        GraphicsQuality.Apply();
        VoiceChat.Ensure();
        Prewarm.Begin(); // synthesise every sound now (in the menu) instead of on first use mid-match
        UpdateCheck.Begin();
        GamepadDriver.Ensure();
        // Same-version games only: clients send their version, the host turns away anyone else.
        Net.NetworkConfig.ConnectionApproval = true;
        Net.NetworkConfig.ConnectionData = UpdateCheck.Payload;
        Net.ConnectionApprovalCallback = UpdateCheck.Approve;

        var args = CommandLine.Args;
        string Arg(string flag) { int i = Array.IndexOf(args, flag); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        if (Arg("-ffaname") is { } n) LocalPlayerName = n;
        if (Arg("-ffaport") is { } p) port = p;
        AutoTest = args.Contains("-ffaautotest");
        ForceAim = args.Contains("-ffaaim");
        ForceScores = args.Contains("-ffascores");
        ForceFire = args.Contains("-ffafire");
        ForceThrow = args.Contains("-ffathrow");
        CenterDot = args.Contains("-ffacenterdot");
        autoReady = args.Contains("-ffaready");
        autoStart = args.Contains("-ffastartwhenready");
        forcePause = args.Contains("-ffapause");
        if (int.TryParse(Arg("-ffamode"), out int fm)) modeOverride = Mathf.Clamp(fm, 0, 4);
        if (int.TryParse(Arg("-ffalobbymap"), out int lm)) lobbyMapTest = lm; // host switches to this map once someone joins (tests)
        if (args.Contains("-ffalistgames")) TestListGames();               // tests: log the public game list
        if (args.Contains("-ffaleaderboard")) TestLeaderboard();           // tests: log the global top 10
        if (Arg("-ffachat") is { } say) StartCoroutine(TestChat(say));     // tests: say something once in a game
        if (int.TryParse(Arg("-ffafraglimit"), out int fragLimit)) MatchRules.OverrideFragLimit(fragLimit);
        if (int.TryParse(Arg("-ffamenutab"), out int mt)) tab = (MenuTab)Mathf.Clamp(mt, 0, 5); // screenshot tests
        if (int.TryParse(Arg("-ffaoptionstab"), out int ot)) optionsTab = (OptionsTab)Mathf.Clamp(ot, 0, 4);
        if (int.TryParse(Arg("-ffaskin"), out int sk)) GameSettings.Skin = Mathf.Clamp(sk, 0, Skins.Count - 1);
        if (int.TryParse(Arg("-ffastance"), out int ts)) TestStance = ts;
        if (int.TryParse(Arg("-ffathrowtype"), out int tt)) TestThrowType = Mathf.Clamp(tt, 0, 3);
        // -ffatopdown [size] [x] [z]: orthographic map overview with the UI hidden (map review screenshots)
        if (args.Contains("-ffatopdown") && menuCamera != null)
        {
            hideUi = true;
            var orbitCam = menuCamera.GetComponent<MenuCameraOrbit>();
            if (orbitCam != null) orbitCam.enabled = false;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            float size = float.TryParse(Arg("-ffatopdown"), System.Globalization.NumberStyles.Float, inv, out float sz) ? sz : 60f;
            int i0 = Array.IndexOf(args, "-ffatopdown");
            float cx = i0 + 2 < args.Length && float.TryParse(args[i0 + 2], System.Globalization.NumberStyles.Float, inv, out float vx) ? vx : 0f;
            float cz = i0 + 3 < args.Length && float.TryParse(args[i0 + 3], System.Globalization.NumberStyles.Float, inv, out float vz) ? vz : 10f;
            menuCamera.orthographic = true;
            menuCamera.orthographicSize = size;
            menuCamera.farClipPlane = 600f;
            menuCamera.transform.SetPositionAndRotation(new Vector3(cx, 250f, cz), Quaternion.Euler(90f, 0f, 0f));
        }
        if (float.TryParse(Arg("-ffalean"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float forcedLean))
            ForceLean = Mathf.Clamp(forcedLean, -1f, 1f);
        if (Array.IndexOf(args, "-ffainspect") is var ii and >= 0 && ii + 3 < args.Length)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float.TryParse(args[ii + 1], System.Globalization.NumberStyles.Float, ci, out float ix);
            float.TryParse(args[ii + 2], System.Globalization.NumberStyles.Float, ci, out float iy);
            float.TryParse(args[ii + 3], System.Globalization.NumberStyles.Float, ci, out float iz);
            InspectEuler = new Vector3(ix, iy, iz);
        }
        ReloadTest = args.Contains("-ffareloadtest");
        if (args.Contains("-ffanohud")) hideUi = true; // screenshots
        // -ffacam x y z pitch yaw: a fixed cinematic camera during the match (screenshots / trailers)
        if (Array.IndexOf(args, "-ffacam") is var camArg and >= 0 && camArg + 5 < args.Length)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            float F(int k) => float.Parse(args[camArg + k], System.Globalization.NumberStyles.Float, inv);
            CinematicCam = (new Vector3(F(1), F(2), F(3)), Quaternion.Euler(F(4), F(5), 0f));
        }
        if (int.TryParse(Arg("-ffalobbybots"), out int lb)) GameSettings.LobbyBots = Mathf.Clamp(lb, 0, 8); // tests (not saved)
        if (float.TryParse(Arg("-ffafov"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float tf)) GameSettings.Fov = tf; // tests (not saved)
        if (int.TryParse(Arg("-ffagraphics"), out int gfx)) { GameSettings.Graphics = Mathf.Clamp(gfx, 0, 2); GraphicsQuality.Apply(); }
        if (float.TryParse(Arg("-ffabench"), out float benchSeconds)) FrameBench.Begin(benchSeconds);
        if (float.TryParse(Arg("-ffaquitafter"), out float quitAfter))
            Invoke(nameof(QuitGame), quitAfter);
        if (int.TryParse(Arg("-ffamap"), out int testMap)) // test runs: host this map without touching the saved choice
        {
            mapOverride = Mathf.Clamp(testMap, 0, MapCatalog.Count - 1);
            MapCatalog.Apply(HostMap);
        }
        if (args.Contains("-ffapractice"))
            StartCoroutine(PracticeWhenWarm()); // tests: like a real player, start once the warm-up is done
        else if (args.Contains("-ffahost") || args.Contains("-ffarelay"))
            Host(args.Contains("-ffarelay"), lobby: args.Contains("-ffalobby")); // test sessions skip the lobby unless asked
        else if (Arg("-ffaclient") is { } ip)
        {
            address = ip;
            Join();
        }
        if (AutoTest)
            InvokeRepeating(nameof(LogScores), 5f, 5f);
        if (Arg("-ffascreenshot") is { } shotPath)
            StartCoroutine(TakeScreenshot(shotPath));
    }

    static async void TestListGames()
    {
        await System.Threading.Tasks.Task.Delay(6000);
        try
        {
            var list = await LobbyBrowser.Query();
            Debug.Log($"[FFA] Public games: {list.Count}");
            foreach (var g in list) Debug.Log($"[FFA]   {g.Name} | {g.Map} | {g.Mode} | {g.Players} | {g.State} | code {g.Code}");
        }
        catch (Exception e) { Debug.Log($"[FFA] Public games query failed: {e.Message}"); }
    }

    static async void TestLeaderboard()
    {
        try
        {
            var rows = await Career.Top(10);
            Debug.Log($"[FFA] Leaderboard rows: {rows.Count}");
        }
        catch (Exception e) { Debug.Log($"[FFA] Leaderboard failed: {e.Message}"); }
    }

    System.Collections.IEnumerator TestChat(string text)
    {
        while (FpsPlayer.Local == null) yield return null;
        yield return new WaitForSeconds(2f);
        FpsPlayer.Local.SendChat(text);
    }

    void OnDestroy()
    {
        if (Net != null)
            Net.OnClientDisconnectCallback -= OnClientDisconnect;
    }

    void Update()
    {
        var local = FpsPlayer.Local;
        bool inGame = local != null && !local.InLobby.Value;
        if (menuCamera != null)
        {
            menuCamera.enabled = !inGame;
            // Its listener too: left on, it heard the whole match from high above the map, so every shot
            // anywhere sounded the same distance away.
            if (menuListener == null) menuListener = menuCamera.GetComponent<AudioListener>();
            if (menuListener != null) menuListener.enabled = !inGame;
        }

        if (local != null && local.InLobby.Value)
        {
            if (autoReady && !Net.IsServer && !local.Ready.Value) local.Ready.Value = true;
            if (lobbyMapTest >= 0 && Net.IsServer && MatchRules.LobbyOpen && FpsPlayer.All.Count >= 2)
            {
                MatchRules.SetMap(lobbyMapTest);
                lobbyMapTest = -1;
            }
            if (autoStart && Net.IsServer && MatchRules.LobbyOpen && FpsPlayer.All.Count >= 2 && MatchRules.EveryoneReady(out _) && lobbyMapTest < 0)
                MatchRules.StartMatch();
        }

        if (inGame && !wasInGame)
            SetCursorCaptured(true);
        else if (!inGame && wasInGame)
            SetCursorCaptured(false);
        wasInGame = inGame;

        LobbyBrowser.Tick();
        UpdateMatchStats(local);
        // The summary needs the mouse (map vote); give it back when play resumes.
        bool summary = inGame && !string.IsNullOrEmpty(winner) && !KillCam.Active;
        if (summary && Cursor.lockState == CursorLockMode.Locked) SetCursorCaptured(false);
        if (!summary && wasSummary && inGame) SetCursorCaptured(true);
        wasSummary = summary;
        bool escUsed = UpdateRebind(); // Esc while waiting for a key cancels the rebind, nothing else
        escUsed |= UpdateChatKeys(inGame, local != null && local.InLobby.Value);
        if (inGame)
        {
            var kb = Keyboard.current;
            if (((kb != null && kb.escapeKey.wasPressedThisFrame) || GamepadInput.PausePressed) && !DevPanel.Open && !escUsed)
            {
                if ((pauseOptions || pausePlayers) && Cursor.lockState != CursorLockMode.Locked) pauseOptions = pausePlayers = false; // back to the pause menu
                else SetCursorCaptured(Cursor.lockState != CursorLockMode.Locked);
            }

            if (local.IsDead && !wasDead)
                deathTime = Time.time;
            wasDead = local.IsDead;
        }
    }

    void OnClientDisconnect(ulong clientId)
    {
        if (Net.IsServer)
        {
            Debug.Log($"[FFA] Client {clientId} left");
            return;
        }
        MapCatalog.Apply(HostMap); // back to our own pick behind the menu
        RelayConnect.ClearCode();
        status =string.IsNullOrEmpty(Net.DisconnectReason)
            ? (wasInGame ? "Disconnected from host." : "Could not connect to host.")
            : Net.DisconnectReason;
        Debug.Log($"[FFA] {status}");
    }

    // ---------------------------------------------------------------- connection

    int mapOverride = -1;
    int HostMap => mapOverride >= 0 ? mapOverride : GameSettings.Map;

    bool connecting; // waiting on Unity Relay (sign-in / allocation) before the session starts

    /// <param name="online">Host through Unity Relay and show a join code; otherwise listen on the port for direct IP joins.</param>
    /// <param name="lobby">Hold everyone in the waiting lobby until the host starts the match.</param>
    async void Host(bool online, bool lobby)
    {
        if (connecting || InSession) return;
        ushort p = 0;
        if (!online && !TryGetPort(out p)) return;
        LocalPlayerName = SanitizeName(LocalPlayerName);
        MapCatalog.Apply(HostMap);
        var transport = Net.GetComponent<UnityTransport>();
        RelayConnect.ClearCode();
        if (online)
        {
            connecting = true;
            status = "Going online…";
            try { await RelayConnect.PrepareHost(transport); }
            catch (Exception e) { status = RelayConnect.Describe(e); return; }
            finally { connecting = false; }
        }
        else
            transport.SetConnectionData("127.0.0.1", p, "0.0.0.0");
        MatchRules.BeginSession(lobby);
        if (lobby && !MatchRules.FragLimitFromCommandLine) MatchRules.SetFragLimit(GameSettings.FragLimit);
        if (lobby)
        {
            MatchRules.SetMode(modeOverride >= 0 ? (MatchRules.GameMode)modeOverride : (MatchRules.GameMode)GameSettings.Mode);
            MatchRules.SetMapVote(GameSettings.MapVote);
        }
        status = Net.StartHost() ? "" : online ? "Could not start the online game." : $"Could not host on port {p} (already in use?)";
        if (status != "") RelayConnect.ClearCode();
        if (status == "" && online && lobby && GameSettings.PublicGame) LobbyBrowser.Publish(RelayConnect.Code, LocalPlayerName);
        Debug.Log(online ? $"[FFA] Hosting online, join code {RelayConnect.Code}: {(status == "" ? "ok" : status)}"
                         : $"[FFA] Hosting on port {p}: {(status == "" ? "ok" : status)}");
    }

    async void Join()
    {
        if (connecting || InSession) return;
        LocalPlayerName = SanitizeName(LocalPlayerName);
        var transport = Net.GetComponent<UnityTransport>();
        RelayConnect.ClearCode();
        string target = address.Trim();
        if (RelayConnect.LooksLikeJoinCode(target))
        {
            connecting = true;
            status = "Finding game…";
            try { await RelayConnect.PrepareJoin(transport, target); }
            catch (Exception e) { status = RelayConnect.Describe(e); return; }
            finally { connecting = false; }
            status = Net.StartClient() ? "" : "Could not start client.";
            Debug.Log($"[FFA] Joining online game {RelayConnect.Code}");
            return;
        }
        if (!TryGetPort(out ushort p)) return;
        string ip = ResolveIPv4(target);
        if (ip == null)
        {
            status = $"Unknown address '{address}'";
            return;
        }
        transport.SetConnectionData(ip, p);
        status = Net.StartClient() ? "" : "Could not start client.";
        Debug.Log($"[FFA] Joining {ip}:{p}");
    }

    // ---------------------------------------------------------------- practice

    /// <summary>This session is an offline practice match against bots.</summary>
    public static bool Practice { get; private set; }
    public static readonly string[] BotNames =
    {
        "Sarge", "Moose", "Rook", "Viper", "Gizmo", "Tank", "Ghost", "Biscuit", "Maverick", "Pickles", "Duke", "Nova",
    };
    public static readonly string[] Difficulties = { "EASY", "NORMAL", "HARD" };
    public static readonly float[] DifficultyAccuracy = { 0.25f, 0.5f, 0.8f };

    /// <summary>Offline match against bots: a local-only host (no Relay, not listed), bots fill the lobby, go.
    /// In the browser it hosts through Relay instead (browsers can't open a local server); still not listed.</summary>
    async void StartPractice()
    {
        if (connecting || InSession) return;
        LocalPlayerName = SanitizeName(GameSettings.Name);
        MapCatalog.Apply(HostMap);
        RelayConnect.ClearCode();
        var transport = Net.GetComponent<UnityTransport>();
#if UNITY_WEBGL && !UNITY_EDITOR
        connecting = true;
        status = "Starting practice…";
        try { await RelayConnect.PrepareHost(transport); }
        catch (Exception e) { status = RelayConnect.Describe(e); return; }
        finally { connecting = false; }
        RelayConnect.ClearCode(); // nobody else is meant to join
#else
        transport.SetConnectionData("127.0.0.1", FreeUdpPort(), "127.0.0.1"); // nobody else can reach it
        await System.Threading.Tasks.Task.CompletedTask;
#endif
        MatchRules.BeginSession(true);
        MatchRules.SetFragLimit(GameSettings.PracticeScore);
        MatchRules.SetMode((MatchRules.GameMode)(modeOverride >= 0 ? modeOverride : GameSettings.PracticeMode));
        MatchRules.SetMapVote(GameSettings.MapVote);
        DevSettings.BotAccuracy = DifficultyAccuracy[GameSettings.PracticeDifficulty];
        Practice = true;
        if (!Net.StartHost())
        {
            Practice = false;
            status = "Couldn't start the practice match.";
            return;
        }
        status = "";
        Debug.Log($"[FFA] Practice: {GameSettings.PracticeBots} bots, {Difficulties[GameSettings.PracticeDifficulty]}, {MatchRules.ModeName(MatchRules.Mode)}");
        StartCoroutine(FillWithBots(GameSettings.PracticeBots));
    }

    System.Collections.IEnumerator FillWithBots(int count)
    {
        float give = Time.unscaledTime + 10f;
        while (FpsPlayer.Local == null && Time.unscaledTime < give) yield return null;
        if (!Practice || !Net.IsServer) yield break;
        var names = BotNames.OrderBy(_ => UnityEngine.Random.value).ToArray();
        for (int i = 0; i < count; i++)
        {
            FpsPlayer.SpawnBot(BotMode.Fighter, names[i % names.Length]);
            yield return null;
        }
        yield return null;
        MatchRules.StartMatch();
    }

    System.Collections.IEnumerator PracticeWhenWarm()
    {
        float give = Time.unscaledTime + 60f;
        while (!Prewarm.Done && Time.unscaledTime < give) yield return null;
        StartPractice();
    }

    static ushort FreeUdpPort()
    {
        using var u = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return (ushort)((IPEndPoint)u.Client.LocalEndPoint).Port;
    }

    void Disconnect()
    {
        GetComponent<MatchRules>()?.StopAllCoroutines();
        MatchRules.ResetState();
        LobbyBrowser.Unpublish();
        Net.Shutdown();
        chat.Clear();
        pauseOptions = pausePlayers = chatOpen = quickChatOpen = false;
        RelayConnect.ClearCode();
        killFeed.Clear();
        Gore.ClearAll();
        DuelManager.ResetAll();
        MapCatalog.Apply(HostMap);
        winner = "";
        status = "";
        lobbyBusyUntil = 0f;
        DevSettings.BotAccuracy = 0.5f; // practice / lobby bots set their own
        Spectator.Stop();
        Practice = false;
    }

    bool TryGetPort(out ushort p)
    {
        if (ushort.TryParse(port.Trim(), out p) && p > 0) return true;
        status = "Port must be a number between 1 and 65535.";
        return false;
    }

    static string SanitizeName(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) name = "Player";
        return name.Length > 16 ? name.Substring(0, 16) : name;
    }

    static string ResolveIPv4(string host)
    {
        if (IPAddress.TryParse(host, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
            return ip.ToString();
        try
        {
            return Dns.GetHostAddresses(host).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString();
        }
        catch { return null; }
    }

    static void SetCursorCaptured(bool captured)
    {
        if (AutoTest || BackgroundTestWindow.Active) return; // test windows never grab the mouse
        Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !captured;
        if (captured) captureTime = Time.unscaledTime;
    }

    void QuitGame()
    {
        LogScores();
        LobbyBrowser.Unpublish();
        if (InSession) Net.Shutdown();
        // Settings are saved as they change in the menu; saving here would also persist test-flag overrides.
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    static System.Collections.IEnumerator TakeScreenshot(string path)
    {
        yield return new WaitForSeconds(4f);
        ScreenCapture.CaptureScreenshot(path);
        Debug.Log($"[FFA] Screenshot saved to {path}");
    }

    void LogScores()
    {
        foreach (var p in FpsPlayer.All)
            Debug.Log($"[FFA] Score {p.DisplayName}: kills={p.Kills.Value} deaths={p.Deaths.Value} health={p.Health.Value}");
    }

    // ---------------------------------------------------------------- drawing

    void EnsureStyles()
    {
        if (white != null) return;
        white = Texture2D.whiteTexture;
        label = new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true };
        bigLabel = St(label, fontSize: 34, fontStyle: FontStyle.Bold);
        centerLabel = St(label, alignment: TextAnchor.MiddleCenter);
        hugeLabel = St(centerLabel, fontSize: 40, fontStyle: FontStyle.Bold);
        boxStyle = new GUIStyle(GUI.skin.box);
    }

    void OnGUI()
    {
        if (hideUi) return;
        EnsureStyles();
        float scale = Screen.height / 720f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        float w = Screen.width / scale, h = 720f;

        if (!InSession)
            DrawMainMenu(w, h);
        else if (FpsPlayer.Local != null && FpsPlayer.Local.InLobby.Value)
            DrawLobby(w, h);
        else if (FpsPlayer.Local == null)
            DrawConnecting(w, h);
        else
            DrawHud(w, h);
        if (InSession) DrawVoice(w, h);
    }

    /// <summary>Replay through the killer's eyes: letterbox bars, a title and who / what killed you (replaces the HUD).</summary>
    void DrawKillCamOverlay(float w, float h)
    {
        if (KillCam.Scoped) DrawScope(w, h); // the killer was looking through a scope
        Fill(new Rect(0, 0, w, 56), new Color(0f, 0f, 0f, 0.8f));
        Fill(new Rect(0, h - 64, w, 64), new Color(0f, 0f, 0f, 0.8f));
        Fill(new Rect(0, 56, w, 2), new Color(1f, 0.3f, 0.3f, 0.8f));
        Shadowed(new Rect(30, 10, 400, 36), KillCam.IsFinal ? "<b>FINAL KILL</b>" : "<b>KILLCAM</b>", St(label, fontSize: 26),
            KillCam.IsFinal ? new Color(1f, 0.82f, 0.3f) : new Color(1f, 0.3f, 0.3f));
        float remaining = Mathf.Max(0f, FpsPlayer.RespawnDelay - (Time.time - deathTime));
        if (!KillCam.IsFinal)
            Shadowed(new Rect(w - 330, 16, 300, 26), $"Respawning in {remaining:0.0}", St(label, alignment: TextAnchor.MiddleRight, fontSize: 16), new Color(1f, 1f, 1f, 0.8f));
        else if (!string.IsNullOrEmpty(winner))
            Shadowed(new Rect(w - 430, 16, 400, 26), $"{winner} WINS", St(label, alignment: TextAnchor.MiddleRight, fontSize: 18, fontStyle: FontStyle.Bold), new Color(1f, 0.85f, 0.25f));
        string killerHex = ColorUtility.ToHtmlStringRGB(KillCam.KillerColor);
        Shadowed(new Rect(0, h - 58, w, 22), KillCam.IsFinal ? "WINNING KILL BY" : "KILLED BY", St(centerLabel, fontSize: 12, fontStyle: FontStyle.Bold), new Color(1f, 1f, 1f, 0.6f));
        Shadowed(new Rect(0, h - 38, w, 30), $"<color=#{killerHex}>■</color> <b>{KillCam.KillerName}</b>   <size=14>{KillCam.WeaponName}</size>",
            St(centerLabel, fontSize: 20), Color.white);
        Fill(new Rect(0, h - 3, w * KillCam.Progress, 3), new Color(1f, 0.3f, 0.3f, 0.9f));
        float c2 = w / 2, cy2 = h / 2; // the killer's crosshair
        var ch = new Color(1f, 1f, 1f, 0.85f * (1f - KillCam.AdsAmount)); // fades out on the sights
        Fill(new Rect(c2 - 12, cy2 - 1, 8, 2), ch); Fill(new Rect(c2 + 4, cy2 - 1, 8, 2), ch);
        Fill(new Rect(c2 - 1, cy2 - 12, 2, 8), ch); Fill(new Rect(c2 - 1, cy2 + 4, 2, 8), ch);
        Fill(new Rect(c2 - 1, cy2 - 1, 2, 2), ch);
    }

    void DrawHud(float w, float h)
    {
        var me = FpsPlayer.Local;
        if (KillCam.Active)
        {
            DrawKillCamOverlay(w, h);
            return;
        }
        if (!string.IsNullOrEmpty(winner))
        {
            DrawMatchSummary(me, w, h);
            return;
        }
        float t = Time.time;
        float scale = Screen.height / 720f;
        var def = me.InDuel ? WeaponDefs.Get(WeaponId.Fists) : me.CurrentDef;

        if (me.IsScoped) DrawScope(w, h);

        // Blood on the screen, then a red flash
        if (GameAssets.I != null && GameAssets.I.bloodSplatTexture != null)
        {
            screenBlood.RemoveAll(b => t - b.time > 3.5f);
            foreach (var b in screenBlood)
            {
                float a = Mathf.Clamp01(1f - (t - b.time) / 3.5f);
                var old = GUI.color;
                GUI.color = new Color(0.55f, 0f, 0f, 0.85f * a);
                GUI.DrawTexture(new Rect(b.pos.x * w - b.size / 2, b.pos.y * h - b.size / 2 + (t - b.time) * 12f, b.size, b.size), GameAssets.I.bloodSplatTexture);
                GUI.color = old;
            }
        }
        DrawExplosionOverlay(w, h, t);
        float flash = 1f - (t - damageFlashTime) / 0.4f;
        if (flash > 0f)
            Fill(new Rect(0, 0, w, h), new Color(0.8f, 0f, 0f, 0.3f * flash));
        if (!me.IsDead && me.Health.Value <= 30)
            Fill(new Rect(0, 0, w, h), new Color(0.6f, 0f, 0f, 0.12f + 0.08f * Mathf.Sin(t * 5f)));

        if (me.EspActive && !me.IsDead) DrawEsp(me, w, h, scale);
        if (!me.IsDead) DrawSpawnProtection(me, w, h, scale);
        if (!me.IsDead) DrawDamageIndicators(me, w, h, t);
        if (!me.IsDead) DrawCompass(me, w);
        if (!me.IsDead) DrawTeamMarkers(me, w, h, scale);
        DrawChat(w, h);
        DrawMedals(w, h);

        // Crosshair (fades out while aiming down sights) + hit marker
        float cx = w / 2, cy = h / 2;
        if (!me.IsDead && me.AdsAmount < 0.6f && !me.IsScoped)
        {
            float gap = 2f + (def != null ? Mathf.Lerp(def.HipSpread, def.AdsSpread, me.AdsAmount) * 1.8f : 2f);
            DrawCrosshair(cx, cy, gap, 1f - me.AdsAmount);
        }
        // Grappling hook (if you've picked one up): cyan brackets on hookable spots, cooldown below.
        if (!me.IsDead && !me.InDuel && me.HasGrapple.Value)
        {
            if (me.GrappleAimValid && me.GrappleCooldownLeft <= 0f)
            {
                var hc = new Color(0.3f, 0.9f, 1f, 0.9f);
                Fill(new Rect(cx - 16, cy - 16, 6, 2), hc); Fill(new Rect(cx - 16, cy - 16, 2, 6), hc);
                Fill(new Rect(cx + 10, cy - 16, 6, 2), hc); Fill(new Rect(cx + 14, cy - 16, 2, 6), hc);
                Fill(new Rect(cx - 16, cy + 14, 6, 2), hc); Fill(new Rect(cx - 16, cy + 10, 2, 6), hc);
                Fill(new Rect(cx + 10, cy + 14, 6, 2), hc); Fill(new Rect(cx + 14, cy + 10, 2, 6), hc);
            }
            float cd = me.GrappleCooldownLeft;
            string hook = me.IsGrappling ? $"HOOKED · {K(GameAction.Grapple)}/{K(GameAction.Jump)} release" : cd > 0f ? $"HOOK {cd:0.0}s" : $"[{K(GameAction.Grapple)}] HOOK READY";
            Shadowed(new Rect(0, h - 120, w, 22), $"<size=13>{hook}</size>", centerLabel,
                cd > 0f && !me.IsGrappling ? new Color(1f, 1f, 1f, 0.5f) : new Color(0.5f, 0.92f, 1f, 0.9f));
        }

        if (t - hitMarkerTime < 0.25f)
        {
            var c = hitMarkerKill ? new Color(1f, 0.2f, 0.2f) : hitMarkerHeadshot ? new Color(1f, 0.85f, 0.2f) : Color.white;
            DrawHitMarker(cx, cy, c);
        }

        // Floating damage numbers
        damageNumbers.RemoveAll(d => t - d.time > 1.2f);
        if (DevSettings.ShowDamageNumbers && me.playerCamera != null)
        {
            var dmgStyle = St(centerLabel, fontSize: 18, fontStyle: FontStyle.Bold);
            foreach (var d in damageNumbers)
            {
                float age = t - d.time;
                Vector3 sp = me.playerCamera.WorldToScreenPoint(d.point + Vector3.up * (0.3f + age * 0.8f));
                if (sp.z < 0f) continue;
                var col = d.head ? new Color(1f, 0.85f, 0.2f, 1f - age / 1.2f) : new Color(1f, 1f, 1f, 1f - age / 1.2f);
                Shadowed(new Rect(sp.x / scale - 40, (Screen.height - sp.y) / scale - 12, 80, 24), d.amount.ToString(), dmgStyle, col);
            }
        }

        // Killstreak and active rewards
        float perkY = h - 124;
        if (me.AimbotActive)
        {
            Shadowed(new Rect(32, perkY, 260, 22), $"AIMBOT  {Mathf.CeilToInt(me.AimbotSecondsLeft)}s", label, new Color(1f, 0.35f, 0.3f));
            perkY -= 22;
        }
        if (me.FlyActive)
        {
            Shadowed(new Rect(32, perkY, 300, 22), $"FLYHACK  {Mathf.CeilToInt(me.FlySecondsLeft)}s  <size=12>{K(GameAction.Jump)} up · {K(GameAction.Crouch)} down</size>", label, new Color(0.6f, 0.75f, 1f));
            perkY -= 22;
        }
        if (me.EspActive)
        {
            Shadowed(new Rect(32, perkY, 260, 22), $"ESP  {Mathf.CeilToInt(me.EspSecondsLeft)}s", label, new Color(0.4f, 1f, 0.6f));
            perkY -= 22;
        }
        if (me.SpeedActive)
        {
            Shadowed(new Rect(32, perkY, 260, 22), $"SPEED HACK  {Mathf.CeilToInt(me.SpeedSecondsLeft)}s", label, new Color(1f, 0.85f, 0.25f));
            perkY -= 22;
        }
        if (me.Streak.Value > 0)
        {
            int s = me.Streak.Value;
            var (next, name) = s < FpsPlayer.SpeedStreak ? (FpsPlayer.SpeedStreak, "SPEED") : s < FpsPlayer.EspStreak ? (FpsPlayer.EspStreak, "ESP")
                : s < FpsPlayer.FlyStreak ? (FpsPlayer.FlyStreak, "FLY") : s < FpsPlayer.AimbotStreak ? (FpsPlayer.AimbotStreak, "AIMBOT") : (0, "");
            Shadowed(new Rect(32, perkY, 300, 22), $"<size=13>STREAK {s}{(next > 0 ? $"  ·  {name} at {next}" : "")}</size>", label, new Color(1f, 1f, 1f, 0.75f));
        }

        // Stance
        var st = me.CurrentStance;
        if (st != Stance.Stand && !me.IsDead)
            Shadowed(new Rect(32, h - 98, 200, 22), $"<size=13>{(st == Stance.Slide ? "SLIDING" : st == Stance.Prone ? "PRONE" : "CROUCHED")}</size>", label, new Color(1f, 1f, 1f, 0.75f));

        // Your own health, slots and ammo (not while spectating someone else).
        var inv = me.Inv;
        if (!Spectator.Active)
        {
            // Health
            var hpColor = me.Health.Value > 50 ? Color.white : me.Health.Value > 25 ? new Color(1f, 0.8f, 0.3f) : new Color(1f, 0.3f, 0.3f);
            Shadowed(new Rect(30, h - 70, 300, 50), $"+ {me.Health.Value}", bigLabel, hpColor);
            Fill(new Rect(32, h - 22, 160, 6), new Color(0f, 0f, 0f, 0.5f));
            Fill(new Rect(32, h - 22, 160 * me.Health.Value / (float)FpsPlayer.MaxHealth, 6), hpColor);

            // Weapon slots and ammo
            var right = St(label, alignment: TextAnchor.UpperRight);
            var ammoStyle = St(bigLabel, alignment: TextAnchor.UpperRight);
            if (me.InDuel)
            {
                // Golden duel: fists, the opponent's health and the sudden-death clock.
                var gold = new Color(1f, 0.82f, 0.25f);
                Shadowed(new Rect(w - 330, h - 75, 300, 50), "FISTS", ammoStyle, gold);
                Shadowed(new Rect(w - 330, h - 98, 300, 24), "[LMB] punch", right, new Color(1f, 1f, 1f, 0.7f));
                var opp = me.DuelOpponent;
                Shadowed(new Rect(0, 40, w, 30), "GOLDEN DUEL" + (opp != null ? $"  ·  vs {opp.DisplayName}" : ""), centerLabel, gold);
                if (opp != null)
                {
                    float bw = 260f;
                    Fill(new Rect(w / 2 - bw / 2, 70, bw, 8), new Color(0f, 0f, 0f, 0.5f));
                    Fill(new Rect(w / 2 - bw / 2, 70, bw * Mathf.Clamp01(opp.Health.Value / (float)FpsPlayer.MaxHealth), 8), new Color(1f, 0.3f, 0.25f));
                }
                float sd = DuelManager.SuddenDeathAfter - (t - me.DuelStartedAt);
                Shadowed(new Rect(0, 82, w, 24), sd > 0f ? $"<size=13>Sudden death in {Mathf.CeilToInt(sd)}s</size>" : "<size=15>SUDDEN DEATH</size>",
                    centerLabel, sd > 0f ? new Color(1f, 1f, 1f, 0.7f) : new Color(1f, 0.3f, 0.3f, 0.6f + 0.4f * Mathf.Sin(t * 8f)));
            }
            else if (def != null && def.IsThrowable)
            {
                // Slot 3: how many of the selected throwable are left.
                Shadowed(new Rect(w - 330, h - 75, 300, 50), $"{me.CurrentMag}", ammoStyle, Color.white);
                Shadowed(new Rect(w - 530, h - 98, 500, 24), $"<size=13>Hold LMB / {K(GameAction.Grenade)}, release to throw  ·  {K(GameAction.Slot3)}: next type</size>", right, new Color(1f, 1f, 1f, 0.7f));
            }
            else if (def != null)
            {
                string ammo = me.IsReloading && !def.ReloadPerRound
                    ? $"RELOADING {Mathf.RoundToInt(me.ReloadProgress * 100)}%"
                    : $"{me.CurrentMag} <size=22>/ {me.CurrentReserve}</size>";
                bool empty = me.CurrentMag == 0 && !me.IsReloading;
                Shadowed(new Rect(w - 330, h - 75, 300, 50), ammo, ammoStyle, empty ? new Color(1f, 0.3f, 0.3f) : Color.white);
                if (empty && me.CurrentReserve == 0)
                    Shadowed(new Rect(w - 330, h - 98, 300, 24), "NO AMMO", right, new Color(1f, 0.3f, 0.3f));
                else if (empty || (me.CurrentMag < def.MagSize / 4 && !me.IsReloading))
                    Shadowed(new Rect(w - 330, h - 98, 300, 24), $"[{K(GameAction.Reload)}] RELOAD", right, new Color(1f, 0.8f, 0.3f));
            }
            for (int slot = 0; slot < 2; slot++)
            {
                var sd = WeaponDefs.Get(inv.Weapon(slot));
                bool active = slot == inv.Active;
                string text = sd != null ? $"[{slot + 1}] {sd.Name}  <size=12>{sd.Caliber}</size>" : $"[{slot + 1}] —";
                Shadowed(new Rect(w - 530, h - 174 + slot * 24, 500, 24), text, right,
                    active ? Color.white : new Color(1f, 1f, 1f, 0.45f));
            }

            // Slot 3: throwables, the selected kind highlighted (3 again cycles, T throws from any slot).
            var parts = new System.Collections.Generic.List<string>();
            for (int tt = 0; tt < WeaponDefs.Throwables.Length; tt++)
            {
                int n = inv.ThrowCount(tt);
                if (n <= 0) continue;
                string nm = ThrowShort(tt);
                parts.Add(tt == inv.Throwable ? $"<b>{nm} × {n}</b>" : $"<color=#ffffff88>{nm} × {n}</color>");
            }
            bool throwActive = inv.Active == Inventory.ThrowSlot;
            string throwText = parts.Count > 0 ? $"[{K(GameAction.Slot3)}] {string.Join("   ", parts)}  <size=12>[{K(GameAction.Grenade)}] throw</size>" : $"[{K(GameAction.Slot3)}] —";
            Shadowed(new Rect(w - 530, h - 126, 500, 24), throwText, right,
                throwActive ? Color.white : parts.Count > 0 ? new Color(0.8f, 0.9f, 0.7f, 0.8f) : new Color(1f, 1f, 1f, 0.35f));
            if (me.Snared)
                Shadowed(new Rect(0, h / 2 + 90, w, 30), $"<b>SNARED</b>  {me.SnareSecondsLeft:0.0}s", St(centerLabel, fontSize: 20), new Color(0.95f, 0.7f, 0.35f));
            if (me.IsCookingGrenade)
                Shadowed(new Rect(0, h / 2 + 30, w, 26), $"COOKING  {me.CookTimeLeft:0.0}", centerLabel, me.CookTimeLeft < 1f ? new Color(1f, 0.3f, 0.2f) : new Color(1f, 0.8f, 0.3f));
        }

        // Pickup prompt
        var pickup = me.NearbyPickup;
        if (pickup != null && pickup.Def != null && !me.IsDead && pickup.Id == WeaponId.GrappleHook)
            Shadowed(new Rect(0, h / 2 + 60, w, 26), me.HasGrapple.Value ? "You already have a grappling hook" : $"[{K(GameAction.Interact)}] Pick up Grappling Hook  <size=13>({K(GameAction.Grapple)} to fire)</size>", centerLabel, pickup.Def.PickupGlow);
        else if (pickup != null && pickup.Def != null && !me.IsDead && pickup.Def.IsThrowable)
        {
            int pt = WeaponDefs.ThrowableIndex(pickup.Id);
            bool full = pt >= 0 && inv.ThrowCount(pt) >= WeaponDefs.MaxCarry[pt];
            string what = pickup.Mag.Value > 1 ? $"{pickup.Mag.Value} × {pickup.Def.Name}" : pickup.Def.Name;
            Shadowed(new Rect(0, h / 2 + 60, w, 26), full ? $"{pickup.Def.Name}: carrying the most you can" : $"[{K(GameAction.Interact)}] Pick up {what}", centerLabel, pickup.Def.PickupGlow);
        }
        else if (pickup != null && pickup.Def != null && !me.IsDead)
        {
            var pd = pickup.Def;
            string verb = inv.SlotOf((int)pd.Id) >= 0 ? $"Take ammo for {pd.Name}"
                : inv.EmptySlot >= 0 ? $"Pick up {pd.Name}"
                : $"Swap {def?.Name} for {pd.Name}";
            Shadowed(new Rect(0, h / 2 + 60, w, 26), $"[{K(GameAction.Interact)}] {verb}  <size=13>({pickup.Mag.Value} + {pickup.Reserve.Value})</size>", centerLabel, pd.PickupGlow);
        }

        // Match status
        var players = FpsPlayer.All.Where(p => p.IsSpawned)
            .OrderByDescending(p => p.Kills.Value).ThenBy(p => p.Deaths.Value).ToList();
        var leader = players.FirstOrDefault();
        string top = ModeTopLine(me, players);
        Shadowed(new Rect(0, 10, w, 24), top, centerLabel, Color.white);
        // Top-left: how friends join (online code, or the host's LAN address).
        if (!DevPanel.Open)
        {
            string how = RelayConnect.Code != "" ? $"JOIN CODE  <b>{RelayConnect.Code}</b>" : "";
            if (how != "") Shadowed(new Rect(12, 10, 500, 24), $"<size=13>{how}</size>", label, new Color(1, 1, 1, 0.75f));
        }

        // Kill feed
        var feedStyle = St(label, alignment: TextAnchor.UpperRight, fontSize: 14);
        float y = 40;
        foreach (var (text, time) in killFeed)
        {
            if (t - time > 7f) continue;
            Shadowed(new Rect(w - 620, y, 600, 22), text, feedStyle, Color.white);
            y += 22;
        }

        // Death / winner messages
        if (me.IsDead && Spectator.Active)
            DrawSpectating(w, h);
        else if (me.IsDead)
        {
            float remaining = Mathf.Max(0f, FpsPlayer.RespawnDelay - (t - deathTime));
            string who = me.Eliminated.Value ? "ELIMINATED" : string.IsNullOrEmpty(me.LastKillerName) ? "YOU DIED" : $"KILLED BY {me.LastKillerName.ToUpper()}";
            Shadowed(new Rect(0, h / 2 - 110, w, 50), who, hugeLabel, new Color(1f, 0.3f, 0.3f));
            Shadowed(new Rect(0, h / 2 - 62, w, 30), me.LastDeathDetail, centerLabel, new Color(1f, 0.85f, 0.85f));
            if (me.Eliminated.Value)
                Shadowed(new Rect(0, h / 2 + 80, w, 30), string.IsNullOrEmpty(me.SpectatingName) ? "Out of lives" : $"Spectating {me.SpectatingName}  <size=13>(click to switch)</size>", centerLabel, Color.white);
            else
                Shadowed(new Rect(0, h / 2 + 80, w, 30), $"Respawning in {remaining:0.0}", centerLabel, Color.white);
        }
        float bannerAge = t - bannerTime;
        if (bannerAge < 3.5f && !me.IsDead)
        {
            var c = bannerColor;
            c.a = Mathf.Clamp01((3.5f - bannerAge) / 0.6f);
            Shadowed(new Rect(0, h / 2 - 150, w, 60), bannerText, hugeLabel, c);
            if (!string.IsNullOrEmpty(bannerSub))
                Shadowed(new Rect(0, h / 2 - 95, w, 30), bannerSub, centerLabel, new Color(1f, 1f, 1f, c.a));
        }

        var kb = Keyboard.current;
        if (Bindings.Held(GameAction.Scoreboard) || GamepadInput.ScoreboardHeld || ForceScores)
            DrawScoreboard(w, h, ModeOrder(players), me);

        if (CenterDot) // test flag: the exact screen centre, where every shot goes
            Fill(new Rect(w / 2 - 1f, h / 2 - 1f, 2f, 2f), new Color(1f, 0f, 1f, 1f));

        if (forcePause || (Cursor.lockState != CursorLockMode.Locked && !AutoTest && !DevPanel.Open && !BackgroundTestWindow.Active))
            DrawPauseMenu(w, h);
    }

    Texture2D scopeMask;

    /// <summary>Out of lives, watching someone: their name, health and gun along the bottom, and the controls.</summary>
    void DrawSpectating(float w, float h)
    {
        var target = Spectator.Target;
        if (target == null) return;
        if (Spectator.Scoped) DrawScope(w, h);
        if (Spectator.FirstPerson && !Spectator.Scoped)
        {
            float cx = w / 2, cy = h / 2;
            var ch = new Color(1f, 1f, 1f, 0.6f * (1f - target.AimAmount));
            Fill(new Rect(cx - 1, cy - 1, 2, 2), ch);
        }
        Fill(new Rect(0, h - 92, w, 92), new Color(0f, 0f, 0f, 0.55f));
        Fill(new Rect(0, h - 92, w, 2), new Color(target.PlayerColor.r, target.PlayerColor.g, target.PlayerColor.b, 0.9f));
        Shadowed(new Rect(0, h - 86, w, 18), "<b>SPECTATING</b>  ·  you're out of lives", St(centerLabel, fontSize: 12), new Color(1f, 1f, 1f, 0.6f));
        string hex = ColorUtility.ToHtmlStringRGB(target.PlayerColor);
        var def = target.CurrentDef;
        Shadowed(new Rect(0, h - 66, w, 30),
            $"<color=#{hex}>■</color> <b>{target.DisplayName}</b>   <size=15>{Mathf.Max(0, target.Health.Value)} HP  ·  {(def != null ? def.Name : "Fists")}  ·  {target.Kills.Value} kills</size>",
            St(centerLabel, fontSize: 22), Color.white);
        bool pad = UnityEngine.InputSystem.Gamepad.current != null && GamepadInput.UsingGamepad;
        string keys = pad ? "LB / RB  switch player   ·   Y  " : $"Click / right-click  switch player   ·   {Bindings.Label(GameAction.Jump)}  ";
        Shadowed(new Rect(0, h - 32, w, 22), keys + (Spectator.FirstPerson ? "third-person view" : "first-person view"),
            St(centerLabel, fontSize: 13), new Color(1f, 1f, 1f, 0.7f));
    }

    void DrawScope(float w, float h)
    {
        if (scopeMask == null)
        {
            const int size = 512;
            scopeMask = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            for (int yy = 0; yy < size; yy++)
                for (int xx = 0; xx < size; xx++)
                {
                    float dx = (xx - size / 2f) / (size / 2f), dy = (yy - size / 2f) / (size / 2f);
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    byte a = (byte)(Mathf.Clamp01((r - 0.94f) / 0.04f) * 255);
                    px[yy * size + xx] = new Color32(0, 0, 0, a);
                }
            scopeMask.SetPixels32(px);
            scopeMask.Apply();
        }
        float side = h;
        float x0 = (w - side) / 2f;
        GUI.DrawTexture(new Rect(x0, 0, side, side), scopeMask);
        Fill(new Rect(0, 0, x0 + 1, h), Color.black);
        Fill(new Rect(x0 + side - 1, 0, w - x0 - side + 1, h), Color.black);
        // Mil-dot style reticle
        var c = new Color(0f, 0f, 0f, 0.95f);
        Fill(new Rect(x0, h / 2 - 1, side, 2), c);
        Fill(new Rect(w / 2 - 1, 0, 2, h), c);
        Fill(new Rect(x0 + side * 0.1f, h / 2 - 3, side * 0.3f, 6), c);
        Fill(new Rect(x0 + side * 0.6f, h / 2 - 3, side * 0.3f, 6), c);
        Fill(new Rect(w / 2 - 3, h * 0.6f, 6, h * 0.3f), c);
        for (int i = 1; i <= 4; i++)
        {
            Fill(new Rect(w / 2 - 4, h / 2 + i * 18 - 1, 8, 2), c);
            Fill(new Rect(w / 2 + i * 18 - 1, h / 2 - 4, 2, 8), c);
            Fill(new Rect(w / 2 - i * 18 - 1, h / 2 - 4, 2, 8), c);
        }
    }

    void DrawScoreboard(float w, float h, List<FpsPlayer> players, FpsPlayer me)
    {
        float rowH = 28, bw = 560, bh = 112 + rowH * players.Count;
        var r = new Rect(w / 2 - bw / 2, Mathf.Max(70f, h / 2 - bh / 2), bw, bh);
        Fill(r, new Color(0.03f, 0.04f, 0.06f, 0.82f));
        Fill(new Rect(r.x, r.y, bw, 3), new Color(1f, 0.82f, 0.3f, 0.9f));
        Shadowed(new Rect(r.x + 20, r.y + 12, 300, 30), "<b>LEADERBOARD</b>", St(label, fontSize: 20), Color.white);
        string codeText = RelayConnect.Code != "" ? $"Join code <b>{RelayConnect.Code}</b>   ·   " : "";
        Shadowed(new Rect(r.x + bw - 380, r.y + 16, 360, 24), $"{codeText}{ModeGoal}",
            St(label, alignment: TextAnchor.UpperRight, fontSize: 14), new Color(1f, 1f, 1f, 0.7f));

        float hy = r.y + 52;
        var head = St(label, fontSize: 12, fontStyle: FontStyle.Bold);
        var headR = St(head, alignment: TextAnchor.UpperRight);
        var c = new Color(1f, 1f, 1f, 0.55f);
        Shadowed(new Rect(r.x + 20, hy, 40, 20), "#", head, c);
        Shadowed(new Rect(r.x + 56, hy, 200, 20), "PLAYER", head, c);
        Shadowed(new Rect(r.x + 300, hy, 50, 20), "KILLS", headR, c);
        Shadowed(new Rect(r.x + 360, hy, 60, 20), "DEATHS", headR, c);
        Shadowed(new Rect(r.x + 420, hy, 60, 20), ModeColumnTitle, headR, c);
        Fill(new Rect(r.x + 16, hy + 22, bw - 32, 1), new Color(1f, 1f, 1f, 0.15f));

        var cell = St(label, fontSize: 15);
        var cellR = St(cell, alignment: TextAnchor.UpperRight);
        float y = hy + 30;
        int rank = 0, prevKills = int.MinValue, shown = 0;
        foreach (var p in players)
        {
            shown++;
            if (p.Kills.Value != prevKills) { rank = shown; prevKills = p.Kills.Value; }
            if (p == me) Fill(new Rect(r.x + 10, y - 3, bw - 20, rowH), new Color(1f, 1f, 1f, 0.1f));
            var rankColor = rank == 1 ? new Color(1f, 0.82f, 0.3f) : rank == 2 ? new Color(0.8f, 0.82f, 0.88f) : rank == 3 ? new Color(0.85f, 0.55f, 0.3f) : Color.white;
            Shadowed(new Rect(r.x + 20, y, 40, 24), rank.ToString(), cell, rankColor);
            Fill(new Rect(r.x + 56, y + 6, 10, 10), p.Team.Value != 0 ? MatchRules.TeamColor(p.Team.Value) : p.PlayerColor);
            string name = p.IsBot.Value ? $"{p.DisplayName} <size=11>[BOT]</size>" : p.DisplayName;
            if (p == me) name += " <size=11>(you)</size>";
            Shadowed(new Rect(r.x + 74, y, 220, 24), name, cell, p.IsDead ? new Color(1f, 1f, 1f, 0.5f) : Color.white);
            Shadowed(new Rect(r.x + 300, y, 50, 24), p.Kills.Value.ToString(), cellR, Color.white);
            Shadowed(new Rect(r.x + 360, y, 60, 24), p.Deaths.Value.ToString(), cellR, Color.white);
            Shadowed(new Rect(r.x + 410, y, 70, 24), ModeColumn(p), St(cellR, richText: true), Color.white);
            // Progress toward winning (per mode)
            float frac = Mathf.Clamp01(ModeProgress(p));
            Fill(new Rect(r.x + 496, y + 9, 44, 5), new Color(1f, 1f, 1f, 0.12f));
            Fill(new Rect(r.x + 496, y + 9, 44 * frac, 5), rankColor);
            y += rowH;
        }
    }

    float codeCopiedTime = -10f;
    bool confirmLeave;

    /// <summary>Esc menu: resume, quick settings, join code, leave / quit (styled like the main menu).</summary>
    bool pauseOptions; // the Esc menu is showing Options

    void DrawPauseMenu(float w, float h)
    {
        EnsureMenuStyles();
        if (pausePlayers) { DrawPlayerList(w, h); return; }
        if (pauseOptions)
        {
            Fill(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.55f));
            float ow = Mathf.Min(w - 80, 760), oh = Mathf.Min(h - 60, 620);
            var orect = new Rect((w - ow) / 2f, (h - oh) / 2f, ow, oh);
            Fill(orect, new Color(0.04f, 0.05f, 0.07f, 0.96f));
            Fill(new Rect(orect.x, orect.y, orect.width, 3), Accent);
            var inner = new Rect(orect.x + 30, orect.y + 20, orect.width - 60, orect.height - 90);
            DrawOptions(inner, "OPTIONS");
            var back = St(navStyle, alignment: TextAnchor.MiddleCenter, padding: new RectOffset(0, 0, 0, 0), fontSize: 16);
            if (GUI.Button(new Rect(orect.x + 30, orect.yMax - 62, 180, 44), "BACK", back)) { pauseOptions = false; rebinding = -1; }
            return;
        }
        bool online = RelayConnect.Code != "";
        Fill(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.45f));
        float pw = 440, ph = (online ? 400 : 346) + (Net.IsServer ? 58 : 0);
        var r = new Rect((w - pw) / 2f, (h - ph) / 2f, pw, ph);
        Fill(r, new Color(0.04f, 0.05f, 0.07f, 0.94f));
        Fill(new Rect(r.x, r.y, r.width, 3), Accent);
        float x = r.x + 30, iw = pw - 60, y = r.y + 20;
        GUI.Label(new Rect(x, y, iw, 40), "PAUSED", h2Style);
        y += 54;
        if (GUI.Button(new Rect(x, y, iw, 50), "RESUME", bigButtonStyle)) { confirmLeave = false; SetCursorCaptured(true); }
        y += 64;

        if (online)
        {
            GUI.Label(new Rect(x, y, 120, 34), "JOIN CODE", St(smallStyle, alignment: TextAnchor.MiddleLeft));
            GUI.Label(new Rect(x + 96, y - 2, 150, 38), $"<b>{RelayConnect.Code}</b>", St(h3Style, fontSize: 24, richText: true));
            var copyStyle = St(navStyle, alignment: TextAnchor.MiddleCenter, padding: new RectOffset(0, 0, 0, 0), fontSize: 14);
            if (GUI.Button(new Rect(x + iw - 110, y, 110, 34), Time.unscaledTime - codeCopiedTime < 1.5f ? "COPIED!" : "COPY", copyStyle))
            {
                GUIUtility.systemCopyBuffer = RelayConnect.Code;
                codeCopiedTime = Time.unscaledTime;
            }
            y += 54;
        }

        if (GUI.Button(new Rect(x, y, iw, 46), "OPTIONS", St(navStyle, alignment: TextAnchor.MiddleCenter, padding: new RectOffset(0, 0, 0, 0), fontSize: 17)))
        {
            pauseOptions = true;
            confirmLeave = false;
        }
        y += 58;
        if (GUI.Button(new Rect(x, y, iw, 46), Net.IsServer ? "PLAYERS  (MUTE / KICK)" : "PLAYERS  (MUTE)", St(navStyle, alignment: TextAnchor.MiddleCenter, padding: new RectOffset(0, 0, 0, 0), fontSize: 17)))
        {
            pausePlayers = true;
            confirmLeave = false;
        }
        y += 58;

        float half = (iw - 12) / 2f;
        var secondary = St(navStyle, alignment: TextAnchor.MiddleCenter, padding: new RectOffset(0, 0, 0, 0), fontSize: 16);
        string leave = confirmLeave ? "CLICK TO CONFIRM" : Net.IsServer ? "END GAME" : "LEAVE GAME";
        if (GUI.Button(new Rect(x, r.yMax - 70, half, 48), leave, confirmLeave ? navActiveStyle : secondary))
        {
            if (confirmLeave) { confirmLeave = false; Disconnect(); }
            else confirmLeave = true;
        }
        if (GUI.Button(new Rect(x + half + 12, r.yMax - 70, half, 48), "QUIT TO DESKTOP", secondary)) QuitGame();
    }

    void CompactSlider(ref float y, Rect r, string text, ref float value, float min, float max, Func<float, string> fmt, ref bool changed)
    {
        GUI.Label(new Rect(r.x, y, 260, 20), text, smallStyle);
        GUI.Label(new Rect(r.x + r.width - 90, y - 4, 90, 26), fmt(value), valueStyle);
        float v = GUI.HorizontalSlider(new Rect(r.x, y + 26, r.width, 20), value, min, max);
        if (!Mathf.Approximately(v, value)) { value = v; changed = true; }
        y += 58;
    }

    /// <summary>Waiting for the host to accept us (or for Relay): spinner, status and Cancel.</summary>
    void DrawConnecting(float w, float h)
    {
        EnsureMenuStyles();
        Fill(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.5f));
        var r = new Rect(w / 2 - 200, h / 2 - 90, 400, 180);
        Fill(r, new Color(0.04f, 0.05f, 0.07f, 0.94f));
        Fill(new Rect(r.x, r.y, r.width, 3), Accent);
        string dots = new string('.', 1 + (int)(Time.unscaledTime * 2f) % 3);
        GUI.Label(new Rect(r.x, r.y + 30, r.width, 40), $"CONNECTING{dots}", St(h3Style, alignment: TextAnchor.MiddleCenter, fontSize: 24));
        GUI.Label(new Rect(r.x, r.y + 70, r.width, 24), RelayConnect.Code != "" ? $"Joining game {RelayConnect.Code}" : "Joining game",
            St(smallStyle, alignment: TextAnchor.MiddleCenter));
        var cancel = St(navStyle, alignment: TextAnchor.MiddleCenter, padding: new RectOffset(0, 0, 0, 0), fontSize: 16);
        if (GUI.Button(new Rect(r.x + 120, r.yMax - 62, 160, 42), "CANCEL", cancel))
            Disconnect();
    }

    void DrawHitMarker(float cx, float cy, Color c)
    {
        // Four short diagonal strokes made of small squares.
        for (int d = 6; d <= 12; d += 2)
        {
            Fill(new Rect(cx + d - 1, cy + d - 1, 2.5f, 2.5f), c);
            Fill(new Rect(cx - d - 1, cy + d - 1, 2.5f, 2.5f), c);
            Fill(new Rect(cx + d - 1, cy - d - 1, 2.5f, 2.5f), c);
            Fill(new Rect(cx - d - 1, cy - d - 1, 2.5f, 2.5f), c);
        }
    }

    void Fill(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, white);
        GUI.color = old;
    }

    static void Shadowed(Rect r, string text, GUIStyle style, Color color)
    {
        var old = GUI.color;
        GUI.color = new Color(0, 0, 0, 0.75f * color.a);
        GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, style);
        GUI.color = color;
        GUI.Label(r, text, style);
        GUI.color = old;
    }
}
