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
///   -ffahost | -ffaclient &lt;ip&gt;   start immediately
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
    public static int TestStance { get; private set; } = -1; // -ffastance 1|2|3: crouch / prone / sprint-and-slide (tests)
    static bool hideUi;
    public static bool ForceFire { get; private set; } // -ffafire: hold the trigger (effects screenshots)
    public static bool ForceThrow { get; private set; } // -ffathrow: cook and throw a grenade (tests)
    public static bool CenterDot { get; private set; }  // -ffacenterdot: mark the exact screen centre (sight alignment checks)
    public static bool CursorCaptured => Cursor.lockState == CursorLockMode.Locked && Time.unscaledTime > captureTime + 0.15f;

    static GameUI instance;
    static float captureTime;

    string address = "127.0.0.1";
    string port = "7777";
    string status = "";
    string localAddresses = "";
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
        var espColor = new Color(0.35f, 1f, 0.5f, 0.95f);
        var nameStyle = new GUIStyle(centerLabel) { fontSize = 13, fontStyle = FontStyle.Bold };
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
            Fill(new Rect(box.xMin - 5f, box.yMax - box.height * hp, 3f, box.height * hp), Color.Lerp(new Color(1f, 0.2f, 0.2f), new Color(0.3f, 1f, 0.4f), hp));
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
        localAddresses = GetLocalIPv4Addresses();
    }

    void Start()
    {
        Net.OnClientDisconnectCallback += OnClientDisconnect;

        var args = Environment.GetCommandLineArgs();
        string Arg(string flag) { int i = Array.IndexOf(args, flag); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        if (Arg("-ffaname") is { } n) LocalPlayerName = n;
        if (Arg("-ffaport") is { } p) port = p;
        AutoTest = args.Contains("-ffaautotest");
        ForceAim = args.Contains("-ffaaim");
        ForceScores = args.Contains("-ffascores");
        ForceFire = args.Contains("-ffafire");
        ForceThrow = args.Contains("-ffathrow");
        CenterDot = args.Contains("-ffacenterdot");
        if (int.TryParse(Arg("-ffamenutab"), out int mt)) tab = (MenuTab)Mathf.Clamp(mt, 0, 2); // screenshot tests
        if (int.TryParse(Arg("-ffaskin"), out int sk)) GameSettings.Skin = Mathf.Clamp(sk, 0, Skins.Count - 1);
        if (int.TryParse(Arg("-ffastance"), out int ts)) TestStance = ts;
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
        if (float.TryParse(Arg("-ffaquitafter"), out float quitAfter))
            Invoke(nameof(QuitGame), quitAfter);
        if (int.TryParse(Arg("-ffamap"), out int testMap)) // test runs: host this map without touching the saved choice
        {
            mapOverride = Mathf.Clamp(testMap, 0, MapCatalog.Count - 1);
            MapCatalog.Apply(HostMap);
        }
        if (args.Contains("-ffahost"))
            Host();
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

    void OnDestroy()
    {
        if (Net != null)
            Net.OnClientDisconnectCallback -= OnClientDisconnect;
    }

    void Update()
    {
        var local = FpsPlayer.Local;
        bool inGame = local != null;
        if (menuCamera != null)
            menuCamera.enabled = !inGame;

        if (inGame && !wasInGame)
            SetCursorCaptured(true);
        else if (!inGame && wasInGame)
            SetCursorCaptured(false);
        wasInGame = inGame;

        if (inGame)
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame && !DevPanel.Open)
                SetCursorCaptured(Cursor.lockState != CursorLockMode.Locked);

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
        status = string.IsNullOrEmpty(Net.DisconnectReason)
            ? (wasInGame ? "Disconnected from host." : "Could not connect to host.")
            : Net.DisconnectReason;
        Debug.Log($"[FFA] {status}");
    }

    // ---------------------------------------------------------------- connection

    int mapOverride = -1;
    int HostMap => mapOverride >= 0 ? mapOverride : GameSettings.Map;

    void Host()
    {
        if (!TryGetPort(out ushort p)) return;
        LocalPlayerName = SanitizeName(LocalPlayerName);
        MapCatalog.Apply(HostMap);
        Net.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", p, "0.0.0.0");
        status = Net.StartHost() ? "" : $"Could not host on port {p} (already in use?)";
        Debug.Log($"[FFA] Hosting on port {p}: {(status == "" ? "ok" : status)}");
    }

    void Join()
    {
        if (!TryGetPort(out ushort p)) return;
        LocalPlayerName = SanitizeName(LocalPlayerName);
        string ip = ResolveIPv4(address.Trim());
        if (ip == null)
        {
            status = $"Unknown address '{address}'";
            return;
        }
        Net.GetComponent<UnityTransport>().SetConnectionData(ip, p);
        status = Net.StartClient() ? "" : "Could not start client.";
        Debug.Log($"[FFA] Joining {ip}:{p}");
    }

    void Disconnect()
    {
        GetComponent<MatchRules>()?.StopAllCoroutines();
        MatchRules.ResetState();
        Net.Shutdown();
        killFeed.Clear();
        Gore.ClearAll();
        DuelManager.ResetAll();
        MapCatalog.Apply(HostMap);
        winner = "";
        status = "";
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

    static string GetLocalIPv4Addresses()
    {
        try
        {
            return string.Join(", ", Dns.GetHostEntry(Dns.GetHostName()).AddressList
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                .Select(a => a.ToString()));
        }
        catch { return ""; }
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
        bigLabel = new GUIStyle(label) { fontSize = 34, fontStyle = FontStyle.Bold };
        centerLabel = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
        hugeLabel = new GUIStyle(centerLabel) { fontSize = 40, fontStyle = FontStyle.Bold };
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
        else if (FpsPlayer.Local == null)
        {
            Shadowed(new Rect(0, h / 2 - 40, w, 30), "Connecting...", centerLabel, Color.white);
            if (GUI.Button(new Rect(w / 2 - 60, h / 2, 120, 30), "Cancel"))
                Disconnect();
        }
        else
            DrawHud(w, h);
    }

    void DrawHud(float w, float h)
    {
        var me = FpsPlayer.Local;
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

        // Crosshair (fades out while aiming down sights) + hit marker
        float cx = w / 2, cy = h / 2;
        if (!me.IsDead && me.AdsAmount < 0.6f && !me.IsScoped)
        {
            float gap = 2f + (def != null ? Mathf.Lerp(def.HipSpread, def.AdsSpread, me.AdsAmount) * 1.8f : 2f);
            const float len = 4.5f, th = 1.5f;
            var c = new Color(1, 1, 1, 0.9f * (1f - me.AdsAmount));
            Fill(new Rect(cx - gap - len, cy - th / 2, len, th), c);
            Fill(new Rect(cx + gap, cy - th / 2, len, th), c);
            Fill(new Rect(cx - th / 2, cy - gap - len, th, len), c);
            Fill(new Rect(cx - th / 2, cy + gap, th, len), c);
            Fill(new Rect(cx - 0.75f, cy - 0.75f, 1.5f, 1.5f), c);
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
            string hook = me.IsGrappling ? "HOOKED · G/Space release" : cd > 0f ? $"HOOK {cd:0.0}s" : "[G] HOOK READY";
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
            var dmgStyle = new GUIStyle(centerLabel) { fontSize = 18, fontStyle = FontStyle.Bold };
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
        if (me.EspActive)
        {
            Shadowed(new Rect(32, perkY, 260, 22), $"ESP  {Mathf.CeilToInt(me.EspSecondsLeft)}s", label, new Color(0.4f, 1f, 0.6f));
            perkY -= 22;
        }
        if (me.Streak.Value > 0)
        {
            int next = me.Streak.Value < FpsPlayer.EspStreak ? FpsPlayer.EspStreak : me.Streak.Value < FpsPlayer.AimbotStreak ? FpsPlayer.AimbotStreak : 0;
            Shadowed(new Rect(32, perkY, 300, 22), $"<size=13>STREAK {me.Streak.Value}{(next > 0 ? $"  ·  {(next == FpsPlayer.EspStreak ? "ESP" : "AIMBOT")} at {next}" : "")}</size>", label, new Color(1f, 1f, 1f, 0.75f));
        }

        // Stance
        var st = me.CurrentStance;
        if (st != Stance.Stand && !me.IsDead)
            Shadowed(new Rect(32, h - 98, 200, 22), $"<size=13>{(st == Stance.Slide ? "SLIDING" : st == Stance.Prone ? "PRONE" : "CROUCHED")}</size>", label, new Color(1f, 1f, 1f, 0.75f));

        // Health
        var hpColor = me.Health.Value > 50 ? Color.white : me.Health.Value > 25 ? new Color(1f, 0.8f, 0.3f) : new Color(1f, 0.3f, 0.3f);
        Shadowed(new Rect(30, h - 70, 300, 50), $"+ {me.Health.Value}", bigLabel, hpColor);
        Fill(new Rect(32, h - 22, 160, 6), new Color(0f, 0f, 0f, 0.5f));
        Fill(new Rect(32, h - 22, 160 * me.Health.Value / (float)FpsPlayer.MaxHealth, 6), hpColor);

        // Weapon slots and ammo
        var inv = me.Inv;
        var right = new GUIStyle(label) { alignment = TextAnchor.UpperRight };
        var ammoStyle = new GUIStyle(bigLabel) { alignment = TextAnchor.UpperRight };
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
                Shadowed(new Rect(w - 330, h - 98, 300, 24), "[R] RELOAD", right, new Color(1f, 0.8f, 0.3f));
        }
        for (int slot = 0; slot < 2; slot++)
        {
            var sd = WeaponDefs.Get(inv.Weapon(slot));
            bool active = slot == inv.Active;
            string text = sd != null ? $"[{slot + 1}] {sd.Name}  <size=12>{sd.Caliber}</size>" : $"[{slot + 1}] —";
            Shadowed(new Rect(w - 430, h - 150 + slot * 24, 400, 24), text, right,
                active ? Color.white : new Color(1f, 1f, 1f, 0.45f));
        }

        // Grenades
        Shadowed(new Rect(w - 430, h - 174, 400, 24), $"[T] Frags × {inv.Grenades}", right, inv.Grenades > 0 ? new Color(0.75f, 0.9f, 0.6f) : new Color(1f, 1f, 1f, 0.35f));
        if (me.IsCookingGrenade)
            Shadowed(new Rect(0, h / 2 + 30, w, 26), $"COOKING  {me.CookTimeLeft:0.0}", centerLabel, me.CookTimeLeft < 1f ? new Color(1f, 0.3f, 0.2f) : new Color(1f, 0.8f, 0.3f));

        // Pickup prompt
        var pickup = me.NearbyPickup;
        if (pickup != null && pickup.Def != null && !me.IsDead && pickup.Id == WeaponId.GrappleHook)
            Shadowed(new Rect(0, h / 2 + 60, w, 26), me.HasGrapple.Value ? "You already have a grappling hook" : "[F] Pick up Grappling Hook  <size=13>(G to fire)</size>", centerLabel, pickup.Def.PickupGlow);
        else if (pickup != null && pickup.Def != null && !me.IsDead && pickup.Def.IsThrowable)
            Shadowed(new Rect(0, h / 2 + 60, w, 26), inv.Grenades >= WeaponDefs.MaxGrenades ? "Grenades full" : $"[F] Pick up {pickup.Mag.Value} frag grenades", centerLabel, pickup.Def.PickupGlow);
        else if (pickup != null && pickup.Def != null && !me.IsDead)
        {
            var pd = pickup.Def;
            string verb = inv.SlotOf((int)pd.Id) >= 0 ? $"Take ammo for {pd.Name}"
                : inv.EmptySlot >= 0 ? $"Pick up {pd.Name}"
                : $"Swap {def?.Name} for {pd.Name}";
            Shadowed(new Rect(0, h / 2 + 60, w, 26), $"[F] {verb}  <size=13>({pickup.Mag.Value} + {pickup.Reserve.Value})</size>", centerLabel, pd.PickupGlow);
        }

        // Match status
        var players = FpsPlayer.All.Where(p => p.IsSpawned)
            .OrderByDescending(p => p.Kills.Value).ThenBy(p => p.Deaths.Value).ToList();
        var leader = players.FirstOrDefault();
        string top = $"First to {MatchRules.FragLimit} kills  ·  You: {me.Kills.Value}";
        if (leader != null && leader != me) top += $"  ·  Leader: {leader.DisplayName} ({leader.Kills.Value})";
        Shadowed(new Rect(0, 10, w, 24), top, centerLabel, Color.white);
        if (Net.IsServer && !string.IsNullOrEmpty(localAddresses) && !DevPanel.Open)
            Shadowed(new Rect(10, 10, 500, 24), $"<size=12>Hosting · LAN: {localAddresses} · port {port}</size>", label, new Color(1, 1, 1, 0.7f));

        // Kill feed
        var feedStyle = new GUIStyle(label) { alignment = TextAnchor.UpperRight, fontSize = 14 };
        float y = 40;
        foreach (var (text, time) in killFeed)
        {
            if (t - time > 7f) continue;
            Shadowed(new Rect(w - 620, y, 600, 22), text, feedStyle, Color.white);
            y += 22;
        }

        // Death / winner messages
        if (me.IsDead)
        {
            float remaining = Mathf.Max(0f, FpsPlayer.RespawnDelay - (t - deathTime));
            string who = string.IsNullOrEmpty(me.LastKillerName) ? "YOU DIED" : $"KILLED BY {me.LastKillerName.ToUpper()}";
            Shadowed(new Rect(0, h / 2 - 110, w, 50), who, hugeLabel, new Color(1f, 0.3f, 0.3f));
            Shadowed(new Rect(0, h / 2 - 62, w, 30), me.LastDeathDetail, centerLabel, new Color(1f, 0.85f, 0.85f));
            Shadowed(new Rect(0, h / 2 + 80, w, 30), $"Respawning in {remaining:0.0}", centerLabel, Color.white);
        }
        if (!string.IsNullOrEmpty(winner))
            Shadowed(new Rect(0, 120, w, 60), $"{winner} WINS!", hugeLabel, new Color(1f, 0.85f, 0.2f));
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
        if ((kb != null && kb.tabKey.isPressed) || ForceScores || !string.IsNullOrEmpty(winner))
            DrawScoreboard(w, h, players, me);

        if (CenterDot) // test flag: the exact screen centre, where every shot goes
            Fill(new Rect(w / 2 - 1f, h / 2 - 1f, 2f, 2f), new Color(1f, 0f, 1f, 1f));

        if (Cursor.lockState != CursorLockMode.Locked && !AutoTest && !DevPanel.Open && !BackgroundTestWindow.Active)
            DrawPauseMenu(w, h);
    }

    Texture2D scopeMask;

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
        Shadowed(new Rect(r.x + 20, r.y + 12, 300, 30), "<b>LEADERBOARD</b>", new GUIStyle(label) { fontSize = 20 }, Color.white);
        Shadowed(new Rect(r.x + bw - 320, r.y + 16, 300, 24), $"First to {MatchRules.FragLimit} kills wins",
            new GUIStyle(label) { alignment = TextAnchor.UpperRight, fontSize = 14 }, new Color(1f, 1f, 1f, 0.7f));

        float hy = r.y + 52;
        var head = new GUIStyle(label) { fontSize = 12, fontStyle = FontStyle.Bold };
        var headR = new GUIStyle(head) { alignment = TextAnchor.UpperRight };
        var c = new Color(1f, 1f, 1f, 0.55f);
        Shadowed(new Rect(r.x + 20, hy, 40, 20), "#", head, c);
        Shadowed(new Rect(r.x + 56, hy, 200, 20), "PLAYER", head, c);
        Shadowed(new Rect(r.x + 300, hy, 50, 20), "KILLS", headR, c);
        Shadowed(new Rect(r.x + 360, hy, 60, 20), "DEATHS", headR, c);
        Shadowed(new Rect(r.x + 430, hy, 50, 20), "K/D", headR, c);
        Fill(new Rect(r.x + 16, hy + 22, bw - 32, 1), new Color(1f, 1f, 1f, 0.15f));

        var cell = new GUIStyle(label) { fontSize = 15 };
        var cellR = new GUIStyle(cell) { alignment = TextAnchor.UpperRight };
        float y = hy + 30;
        int rank = 0, prevKills = int.MinValue, shown = 0;
        foreach (var p in players)
        {
            shown++;
            if (p.Kills.Value != prevKills) { rank = shown; prevKills = p.Kills.Value; }
            if (p == me) Fill(new Rect(r.x + 10, y - 3, bw - 20, rowH), new Color(1f, 1f, 1f, 0.1f));
            var rankColor = rank == 1 ? new Color(1f, 0.82f, 0.3f) : rank == 2 ? new Color(0.8f, 0.82f, 0.88f) : rank == 3 ? new Color(0.85f, 0.55f, 0.3f) : Color.white;
            Shadowed(new Rect(r.x + 20, y, 40, 24), rank.ToString(), cell, rankColor);
            Fill(new Rect(r.x + 56, y + 6, 10, 10), p.PlayerColor);
            string name = p.IsBot.Value ? $"{p.DisplayName} <size=11>[BOT]</size>" : p.DisplayName;
            if (p == me) name += " <size=11>(you)</size>";
            Shadowed(new Rect(r.x + 74, y, 220, 24), name, cell, p.IsDead ? new Color(1f, 1f, 1f, 0.5f) : Color.white);
            Shadowed(new Rect(r.x + 300, y, 50, 24), p.Kills.Value.ToString(), cellR, Color.white);
            Shadowed(new Rect(r.x + 360, y, 60, 24), p.Deaths.Value.ToString(), cellR, Color.white);
            float kd = p.Deaths.Value == 0 ? p.Kills.Value : p.Kills.Value / (float)p.Deaths.Value;
            Shadowed(new Rect(r.x + 430, y, 50, 24), kd.ToString("0.00"), cellR, Color.white);
            // Progress toward the frag limit
            float frac = Mathf.Clamp01(p.Kills.Value / (float)MatchRules.FragLimit);
            Fill(new Rect(r.x + 496, y + 9, 44, 5), new Color(1f, 1f, 1f, 0.12f));
            Fill(new Rect(r.x + 496, y + 9, 44 * frac, 5), rankColor);
            y += rowH;
        }
    }

    void DrawPauseMenu(float w, float h)
    {
        var r = new Rect(w / 2 - 120, h / 2 - 90, 240, 180);
        GUI.Box(r, "Paused", boxStyle);
        if (GUI.Button(new Rect(r.x + 20, r.y + 35, 200, 36), "Resume")) SetCursorCaptured(true);
        if (GUI.Button(new Rect(r.x + 20, r.y + 80, 200, 36), Net.IsServer ? "End Game" : "Leave Game")) Disconnect();
        if (GUI.Button(new Rect(r.x + 20, r.y + 125, 200, 36), "Quit to Desktop")) QuitGame();
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
