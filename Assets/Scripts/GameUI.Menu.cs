using UnityEngine;

/// <summary>Main menu: Play (host / join), Customize (skins with a live 3D preview), Settings.</summary>
public partial class GameUI
{
    enum MenuTab { Play, Customize, Settings }

    static readonly Color Accent = new(1f, 0.55f, 0.15f);
    MenuTab tab = MenuTab.Play;
    Vector2 skinScroll;
    GUIStyle mapButtonStyle, mapButtonActiveStyle;
    CharacterPreview preview;
    Texture2D texPanel, texButton, texButtonHover, texButtonActive, texField, texFieldFocus;
    GUIStyle titleStyle, subtitleStyle, navStyle, navActiveStyle, h2Style, h3Style, smallStyle, bodyStyle,
        bigButtonStyle, fieldStyle, valueStyle, skinButtonStyle, skinButtonActiveStyle;

    static Texture2D Solid(Color c)
    {
        var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    void EnsureMenuStyles()
    {
        if (texPanel != null) return;
        texPanel = Solid(new Color(0.05f, 0.06f, 0.08f, 0.85f));
        texButton = Solid(new Color(0.12f, 0.13f, 0.17f, 0.92f));
        texButtonHover = Solid(new Color(0.2f, 0.21f, 0.26f, 0.95f));
        texButtonActive = Solid(new Color(Accent.r, Accent.g, Accent.b, 0.95f));
        texField = Solid(new Color(0.02f, 0.02f, 0.03f, 0.9f));
        texFieldFocus = Solid(new Color(0.08f, 0.08f, 0.1f, 0.95f));

        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 42, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Overflow, normal = { textColor = Color.white } };
        subtitleStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, normal = { textColor = Accent } };
        h2Style = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
        h3Style = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
        smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 1f, 1f, 0.55f) } };
        bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true, richText = true, normal = { textColor = new Color(1f, 1f, 1f, 0.85f) } };
        valueStyle = new GUIStyle(bodyStyle) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold };

        navStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(24, 10, 0, 0),
            normal = { background = texButton, textColor = new Color(1f, 1f, 1f, 0.8f) },
            hover = { background = texButtonHover, textColor = Color.white },
            active = { background = texButtonActive, textColor = Color.black },
        };
        navActiveStyle = new GUIStyle(navStyle)
        {
            normal = { background = texButtonActive, textColor = new Color(0.08f, 0.05f, 0.02f) },
            hover = { background = texButtonActive, textColor = new Color(0.08f, 0.05f, 0.02f) },
        };
        bigButtonStyle = new GUIStyle(navStyle)
        {
            alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 0), fontSize = 19,
            normal = { background = texButtonActive, textColor = new Color(0.08f, 0.05f, 0.02f) },
            hover = { background = Solid(new Color(1f, 0.68f, 0.35f, 1f)), textColor = Color.black },
        };
        fieldStyle = new GUIStyle(GUI.skin.textField)
        {
            fontSize = 17, padding = new RectOffset(12, 12, 8, 8), alignment = TextAnchor.MiddleLeft,
            normal = { background = texField, textColor = Color.white },
            hover = { background = texFieldFocus, textColor = Color.white },
            focused = { background = texFieldFocus, textColor = Color.white },
        };
        skinButtonStyle = new GUIStyle(navStyle) { fontSize = 17, padding = new RectOffset(64, 10, 0, 0) };
        skinButtonActiveStyle = new GUIStyle(navActiveStyle) { fontSize = 17, padding = new RectOffset(64, 10, 0, 0) };
        mapButtonStyle = new GUIStyle(navStyle) { fontSize = 13, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(4, 4, 0, 0) };
        mapButtonActiveStyle = new GUIStyle(navActiveStyle) { fontSize = 13, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(4, 4, 0, 0) };
    }

    void DrawMainMenu(float w, float h)
    {
        EnsureMenuStyles();
        if (preview == null && GameAssets.I != null) preview = CharacterPreview.Create();
        if (preview != null) preview.Show(GameSettings.Skin, tab == MenuTab.Customize);

        // Darken the fly-over and lay down the navigation column.
        Fill(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.25f));
        Fill(new Rect(0, 0, 400, h), new Color(0.03f, 0.04f, 0.06f, 0.88f));
        Fill(new Rect(400, 0, 2, h), new Color(Accent.r, Accent.g, Accent.b, 0.7f));

        GUI.Label(new Rect(46, 56, 350, 60), "FFA SHOOTER", titleStyle);
        GUI.Label(new Rect(50, 116, 340, 24), $"{MapCatalog.NameOf(MapCatalog.Current).ToUpper()}  ·  FREE FOR ALL", subtitleStyle);

        float y = 196;
        Nav(ref y, "PLAY", MenuTab.Play);
        Nav(ref y, "CUSTOMIZE", MenuTab.Customize);
        Nav(ref y, "SETTINGS", MenuTab.Settings);
        if (GUI.Button(new Rect(48, y, 304, 52), "QUIT", navStyle)) QuitGame();

        GUI.Label(new Rect(50, h - 96, 330, 80),
            "WASD move · Shift sprint · Space jump\nCtrl crouch / slide · Z prone · Q/E lean\nG hook · T grenade · F pick up · R reload\nTab scores · Esc menu · F1 dev panel",
            new GUIStyle(smallStyle) { fontStyle = FontStyle.Normal, wordWrap = true });

        var panel = new Rect(450, 84, Mathf.Min(w - 500, 780), h - 160);
        Fill(panel, new Color(0.04f, 0.05f, 0.07f, 0.86f));
        Fill(new Rect(panel.x, panel.y, panel.width, 3), Accent);
        var inner = new Rect(panel.x + 34, panel.y + 26, panel.width - 68, panel.height - 50);
        switch (tab)
        {
            case MenuTab.Play: DrawPlayTab(inner); break;
            case MenuTab.Customize: DrawCustomizeTab(inner); break;
            case MenuTab.Settings: DrawSettingsTab(inner); break;
        }
        GUI.Label(new Rect(w - 160, h - 30, 150, 22), "v1.0", new GUIStyle(smallStyle) { alignment = TextAnchor.MiddleRight });
    }

    void Nav(ref float y, string text, MenuTab t)
    {
        if (GUI.Button(new Rect(48, y, 304, 52), text, tab == t ? navActiveStyle : navStyle)) tab = t;
        y += 62;
    }

    // ------------------------------------------------------------------ Play

    void DrawPlayTab(Rect r)
    {
        GUI.Label(new Rect(r.x, r.y, r.width, 40), "PLAY", h2Style);
        float y = r.y + 58;

        GUI.Label(new Rect(r.x, y, 300, 20), "PLAYER NAME", smallStyle);
        string newName = GUI.TextField(new Rect(r.x, y + 22, 320, 40), GameSettings.Name, 16, fieldStyle);
        if (newName != GameSettings.Name) { GameSettings.Name = newName; GameSettings.Save(); }
        y += 86;

        // Host
        float col = (r.width - 30) / 2f;
        const float boxH = 316;
        var hostRect = new Rect(r.x, y, col, boxH);
        Fill(hostRect, new Color(1f, 1f, 1f, 0.04f));
        GUI.Label(new Rect(hostRect.x + 18, hostRect.y + 14, col - 36, 26), "HOST A GAME", h3Style);
        GUI.Label(new Rect(hostRect.x + 18, hostRect.y + 46, col - 36, 40), "Start a server on this PC. Friends join with your address below.", bodyStyle);
        // Map choice (also switches the fly-over behind the menu).
        GUI.Label(new Rect(hostRect.x + 18, hostRect.y + 96, 200, 20), "MAP", smallStyle);
        float mapW = (col - 36 - 8 * (MapCatalog.Count - 1)) / MapCatalog.Count;
        for (int i = 0; i < MapCatalog.Count; i++)
            if (GUI.Button(new Rect(hostRect.x + 18 + i * (mapW + 8), hostRect.y + 118, mapW, 40), MapCatalog.NameOf(i).ToUpper(),
                    GameSettings.Map == i ? mapButtonActiveStyle : mapButtonStyle) && GameSettings.Map != i)
            {
                GameSettings.Map = i;
                GameSettings.Save();
                MapCatalog.Apply(i);
            }
        GUI.Label(new Rect(hostRect.x + 18, hostRect.y + 160, col - 36, 22), MapCatalog.DescriptionOf(GameSettings.Map), smallStyle);
        GUI.Label(new Rect(hostRect.x + 18, hostRect.y + 188, 200, 20), "FILL WITH BOTS", smallStyle);
        int bots = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(hostRect.x + 18, hostRect.y + 216, col - 90, 20), GameSettings.HostBots, 0, 10));
        GUI.Label(new Rect(hostRect.x + col - 66, hostRect.y + 206, 48, 30), bots.ToString(), valueStyle);
        if (bots != GameSettings.HostBots) { GameSettings.HostBots = bots; GameSettings.Save(); }
        if (GUI.Button(new Rect(hostRect.x + 18, hostRect.y + boxH - 70, col - 36, 50), "HOST GAME", bigButtonStyle))
        {
            LocalPlayerName = GameSettings.Name;
            Host();
            if (InSession && GameSettings.HostBots > 0) StartCoroutine(SpawnHostBots(GameSettings.HostBots));
        }

        // Join
        var joinRect = new Rect(r.x + col + 30, y, col, boxH);
        Fill(joinRect, new Color(1f, 1f, 1f, 0.04f));
        GUI.Label(new Rect(joinRect.x + 18, joinRect.y + 14, col - 36, 26), "JOIN A GAME", h3Style);
        GUI.Label(new Rect(joinRect.x + 18, joinRect.y + 46, 200, 20), "HOST ADDRESS", smallStyle);
        address = GUI.TextField(new Rect(joinRect.x + 18, joinRect.y + 68, col - 130, 40), address, 64, fieldStyle);
        GUI.Label(new Rect(joinRect.x + col - 102, joinRect.y + 46, 80, 20), "PORT", smallStyle);
        port = GUI.TextField(new Rect(joinRect.x + col - 102, joinRect.y + 68, 84, 40), port, 5, fieldStyle);
        GUI.Label(new Rect(joinRect.x + 18, joinRect.y + 124, col - 36, 44), "You play on whichever map the host picked.", bodyStyle);
        if (GUI.Button(new Rect(joinRect.x + 18, joinRect.y + boxH - 70, col - 36, 50), "JOIN GAME", bigButtonStyle))
        {
            LocalPlayerName = GameSettings.Name;
            Join();
        }
        y += boxH + 20;

        if (!string.IsNullOrEmpty(localAddresses))
            GUI.Label(new Rect(r.x, y, r.width, 22), $"Your LAN address: <b>{localAddresses}</b>   ·   port {port}", bodyStyle);
        if (!string.IsNullOrEmpty(status))
            GUI.Label(new Rect(r.x, y + 26, r.width, 44), $"<color=#ff8a80>{status}</color>", bodyStyle);
    }

    System.Collections.IEnumerator SpawnHostBots(int count)
    {
        while (FpsPlayer.Local == null) yield return null;
        yield return new WaitForSeconds(0.5f);
        for (int i = 0; i < count; i++)
            FpsPlayer.SpawnBot(BotMode.Fighter, $"Bot {i + 1}");
    }

    // ------------------------------------------------------------------ Customize

    void DrawCustomizeTab(Rect r)
    {
        GUI.Label(new Rect(r.x, r.y, r.width, 40), "CUSTOMIZE", h2Style);
        GUI.Label(new Rect(r.x, r.y + 40, r.width, 22), "Choose how you look to other players. Your choice is saved and used next time you spawn.", bodyStyle);
        // Scrolling list of skins.
        var listRect = new Rect(r.x, r.y + 82, 296, r.height - 82);
        var content = new Rect(0, 0, 280, Skins.Count * 60);
        skinScroll = GUI.BeginScrollView(listRect, skinScroll, content, false, content.height > listRect.height);
        for (int i = 0; i < Skins.Count; i++)
        {
            var s = Skins.Get(i);
            var b = new Rect(0, i * 60, 280, 52);
            if (GUI.Button(b, s.Name.ToUpper(), GameSettings.Skin == i ? skinButtonActiveStyle : skinButtonStyle))
            {
                GameSettings.Skin = i;
                GameSettings.Save();
            }
            DrawSwatch(new Rect(b.x + 14, b.y + 9, 34, 34), s);
        }
        GUI.EndScrollView();

        // Live preview and details.
        float px = r.x + 310, pw = Mathf.Min(r.width - 310, 330, (r.height - 82 - 96) / 1.27f); // leave room for name + description
        var frame = new Rect(px, r.y + 82, pw, pw * 1.27f);
        Fill(new Rect(frame.x - 2, frame.y - 2, frame.width + 4, frame.height + 4), new Color(Accent.r, Accent.g, Accent.b, 0.6f));
        if (preview != null && preview.Texture != null)
            GUI.DrawTexture(frame, preview.Texture, ScaleMode.ScaleAndCrop);
        var sel = Skins.Get(GameSettings.Skin);
        GUI.Label(new Rect(px, frame.yMax + 12, pw, 28), sel.Name.ToUpper(), h3Style);
        GUI.Label(new Rect(px, frame.yMax + 40, pw, 56), sel.Description, bodyStyle);
    }

    void DrawSwatch(Rect r, SkinDef s)
    {
        Fill(new Rect(r.x - 1, r.y - 1, r.width + 2, r.height + 2), new Color(0f, 0f, 0f, 0.6f));
        if (s.RainbowShirt)
        {
            var bands = new[] { new Color(0.9f, 0.15f, 0.15f), new Color(1f, 0.55f, 0.1f), new Color(1f, 0.9f, 0.15f), new Color(0.2f, 0.75f, 0.25f), new Color(0.2f, 0.45f, 0.95f), new Color(0.55f, 0.25f, 0.8f) };
            for (int i = 0; i < bands.Length; i++)
                Fill(new Rect(r.x, r.y + i * r.height / bands.Length, r.width, r.height / bands.Length + 1), bands[i]);
        }
        else
            Fill(r, s.SleeveColor);
        Fill(new Rect(r.x, r.y, r.width, 7), s.Hat != HatKind.None ? s.HatColor : s.Mask ?? s.Hair); // hair / hat strip
    }

    // ------------------------------------------------------------------ Settings

    void DrawSettingsTab(Rect r)
    {
        GUI.Label(new Rect(r.x, r.y, r.width, 40), "SETTINGS", h2Style);
        float y = r.y + 70;
        bool changed = false;
        Slider(ref y, r, "MOUSE SENSITIVITY", ref GameSettings.Sensitivity, 0.02f, 0.4f, v => (v * 10f).ToString("0.00"), ref changed);
        Slider(ref y, r, "FIELD OF VIEW", ref GameSettings.Fov, 60f, 100f, v => $"{v:0}°", ref changed);
        Slider(ref y, r, "MASTER VOLUME", ref GameSettings.Volume, 0f, 1f, v => $"{v * 100f:0}%", ref changed);
        Slider(ref y, r, "GORE LEVEL", ref GameSettings.GoreLevel, 0.5f, 4f, v => $"{v:0.0}x", ref changed);
        if (changed) GameSettings.Save();
        if (GUI.Button(new Rect(r.x, y + 10, 300, 46), "RESET TO DEFAULTS", new GUIStyle(navStyle) { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 0), fontSize = 17 }))
            GameSettings.ResetToDefaults();
    }

    void Slider(ref float y, Rect r, string label, ref float value, float min, float max, System.Func<float, string> fmt, ref bool changed)
    {
        GUI.Label(new Rect(r.x, y, 300, 20), label, smallStyle);
        float v = GUI.HorizontalSlider(new Rect(r.x, y + 30, r.width - 120, 20), value, min, max);
        GUI.Label(new Rect(r.x + r.width - 110, y + 18, 110, 34), fmt(v), valueStyle);
        if (!Mathf.Approximately(v, value)) { value = v; changed = true; }
        y += 76;
    }
}
