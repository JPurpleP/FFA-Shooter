using System.Linq;
using UnityEngine;

/// <summary>Main menu: Play (host / join), Customize (skins with a live 3D preview), Settings.</summary>
public partial class GameUI
{
    enum MenuTab { Play, FindGame, Leaderboard, Customize, Settings }

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
        Nav(ref y, "FIND A GAME", MenuTab.FindGame);
        Nav(ref y, "LEADERBOARD", MenuTab.Leaderboard);
        Nav(ref y, "CUSTOMIZE", MenuTab.Customize);
        Nav(ref y, "SETTINGS", MenuTab.Settings);
        if (GUI.Button(new Rect(48, y, 304, 52), "QUIT", navStyle)) QuitGame();

        GUI.Label(new Rect(50, h - 96, 330, 80),
            "Settings > Keybindings (or Esc > Options in game) to change keys" + (DevPanel.Enabled ? " · F1 dev panel" : ""),
            new GUIStyle(smallStyle) { fontStyle = FontStyle.Normal, wordWrap = true });

        var panel = new Rect(450, 84, Mathf.Min(w - 500, 780), h - 160);
        Fill(panel, new Color(0.04f, 0.05f, 0.07f, 0.86f));
        Fill(new Rect(panel.x, panel.y, panel.width, 3), Accent);
        var inner = new Rect(panel.x + 34, panel.y + 26, panel.width - 68, panel.height - 50);
        switch (tab)
        {
            case MenuTab.Play: DrawPlayTab(inner); break;
            case MenuTab.FindGame: DrawFindGameTab(inner); break;
            case MenuTab.Leaderboard: DrawLeaderboardTab(inner); break;
            case MenuTab.Customize: DrawCustomizeTab(inner); break;
            case MenuTab.Settings: DrawSettingsTab(inner); break;
        }
        GUI.Label(new Rect(w - 260, h - 30, 250, 22), $"PUBLIC BETA  ·  v{Application.version}", new GUIStyle(smallStyle) { alignment = TextAnchor.MiddleRight });
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
        const float boxH = 330;
        var hostRect = new Rect(r.x, y, col, boxH);
        Fill(hostRect, new Color(1f, 1f, 1f, 0.04f));
        GUI.Label(new Rect(hostRect.x + 18, hostRect.y + 14, col - 36, 26), "HOST A GAME", h3Style);
        GUI.Label(new Rect(hostRect.x + 18, hostRect.y + 46, col - 36, 56), "Start an online game. You'll get a 6-letter code; friends join with it from anywhere.", bodyStyle);
        // Map choice (also switches the fly-over behind the menu).
        GUI.Label(new Rect(hostRect.x + 18, hostRect.y + 110, 200, 20), "MAP", smallStyle);
        float mapW = (col - 36 - 8 * (MapCatalog.Count - 1)) / MapCatalog.Count;
        for (int i = 0; i < MapCatalog.Count; i++)
            if (GUI.Button(new Rect(hostRect.x + 18 + i * (mapW + 8), hostRect.y + 132, mapW, 36), MapCatalog.NameOf(i).ToUpper(),
                    GameSettings.Map == i ? mapButtonActiveStyle : mapButtonStyle) && GameSettings.Map != i)
            {
                GameSettings.Map = i;
                GameSettings.Save();
                MapCatalog.Apply(i);
            }
        GUI.Label(new Rect(hostRect.x + 18, hostRect.y + 170, col - 36, 22), MapCatalog.DescriptionOf(GameSettings.Map), smallStyle);
        GUI.Label(new Rect(hostRect.x + 18, hostRect.y + 198, 200, 20), "VISIBILITY", smallStyle);
        float vw = (col - 36 - 8) / 2f;
        if (GUI.Button(new Rect(hostRect.x + 18, hostRect.y + 220, vw, 32), "PUBLIC", GameSettings.PublicGame ? mapButtonActiveStyle : mapButtonStyle) && !GameSettings.PublicGame)
        { GameSettings.PublicGame = true; GameSettings.Save(); }
        if (GUI.Button(new Rect(hostRect.x + 26 + vw, hostRect.y + 220, vw, 32), "CODE ONLY", GameSettings.PublicGame ? mapButtonStyle : mapButtonActiveStyle) && GameSettings.PublicGame)
        { GameSettings.PublicGame = false; GameSettings.Save(); }
        GUI.enabled = !connecting;
        if (GUI.Button(new Rect(hostRect.x + 18, hostRect.y + boxH - 70, col - 36, 50), connecting ? "CONNECTING…" : "HOST GAME", bigButtonStyle))
        {
            LocalPlayerName = GameSettings.Name;
            Host(online: true, lobby: true);
        }
        GUI.enabled = true;

        // Join
        var joinRect = new Rect(r.x + col + 30, y, col, boxH);
        Fill(joinRect, new Color(1f, 1f, 1f, 0.04f));
        GUI.Label(new Rect(joinRect.x + 18, joinRect.y + 14, col - 36, 26), "JOIN A GAME", h3Style);
        GUI.Label(new Rect(joinRect.x + 18, joinRect.y + 46, 200, 20), "JOIN CODE", smallStyle);
        var codeRect = new Rect(joinRect.x + 18, joinRect.y + 68, col - 36, 52);
        var codeStyle = new GUIStyle(fieldStyle) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        // Enter joins (checked before the text field eats the key).
        bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                     && GUI.GetNameOfFocusedControl() == "JoinCode";
        GUI.SetNextControlName("JoinCode");
        string typed = GUI.TextField(codeRect, address, 6, codeStyle);
        address = new string(typed.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant(); // codes are 6 letters / digits
        if (address.Length == 0 && GUI.GetNameOfFocusedControl() != "JoinCode")
            GUI.Label(codeRect, "ABC123", new GUIStyle(codeStyle) { normal = { background = null, textColor = new Color(1f, 1f, 1f, 0.2f) } });
        GUI.Label(new Rect(joinRect.x + 18, joinRect.y + 134, col - 36, 66),
            "Ask the host for their code. You play on whichever map the host picked.", bodyStyle);
        bool codeReady = RelayConnect.LooksLikeJoinCode(address);
        if (enter && codeReady && !connecting) { LocalPlayerName = GameSettings.Name; Join(); }
        GUI.enabled = !connecting && codeReady;
        if (GUI.Button(new Rect(joinRect.x + 18, joinRect.y + boxH - 70, col - 36, 50), connecting ? "CONNECTING…" : "JOIN GAME", bigButtonStyle))
        {
            LocalPlayerName = GameSettings.Name;
            Join();
        }
        GUI.enabled = true;
        y += boxH + 16;

        if (!string.IsNullOrEmpty(status))
            GUI.Label(new Rect(r.x, y, r.width, 44), connecting ? status : $"<color=#ff8a80>{status}</color>", bodyStyle);
    }

    // ------------------------------------------------------------------ Lobby

    float lobbyBusyUntil; // brief guard so a double click can't start twice / toggle ready straight back off
    float lobbyCodeCopiedTime = -10f;

    /// <summary>Waiting room after hosting / joining: player list with ready states; the host starts the match.</summary>
    void DrawLobby(float w, float h)
    {
        EnsureMenuStyles();
        if (preview != null) preview.Show(GameSettings.Skin, false);
        var me = FpsPlayer.Local;
        bool isHost = Net.IsServer;

        Fill(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.35f));
        float pw = Mathf.Min(w - 80, 640), ph = h - 120;
        float chatW = Mathf.Min(320f, w - pw - 72f);
        float left = chatW >= 220f ? (w - pw - chatW - 16f) / 2f : (w - pw) / 2f;
        var panel = new Rect(left, 60, pw, ph);
        if (chatW >= 220f) DrawLobbyChat(new Rect(panel.xMax + 16f, 60, chatW, ph));
        Fill(panel, new Color(0.04f, 0.05f, 0.07f, 0.9f));
        Fill(new Rect(panel.x, panel.y, panel.width, 3), Accent);
        float x = panel.x + 30, iw = pw - 60, y = panel.y + 22;

        GUI.Label(new Rect(x, y, iw, 40), "LOBBY", h2Style);
        GUI.Label(new Rect(x, y + 6, iw, 30), $"{MapCatalog.NameOf(MapCatalog.Current).ToUpper()}  ·  FIRST TO {MatchRules.FragLimit}",
            new GUIStyle(subtitleStyle) { alignment = TextAnchor.MiddleRight });
        y += 50;

        // Join code for inviting friends (online games).
        if (RelayConnect.Code != "")
        {
            GUI.Label(new Rect(x, y, 140, 34), "JOIN CODE", new GUIStyle(smallStyle) { alignment = TextAnchor.MiddleLeft });
            GUI.Label(new Rect(x + 96, y - 2, 160, 38), $"<b>{RelayConnect.Code}</b>", new GUIStyle(h3Style) { fontSize = 26, richText = true });
            var copyStyle = new GUIStyle(navStyle) { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 0), fontSize = 14 };
            if (GUI.Button(new Rect(x + 250, y, 110, 34), Time.unscaledTime - lobbyCodeCopiedTime < 1.5f ? "COPIED!" : "COPY", copyStyle))
            {
                GUIUtility.systemCopyBuffer = RelayConnect.Code;
                lobbyCodeCopiedTime = Time.unscaledTime;
            }
            y += 46;
        }
        if (!string.IsNullOrEmpty(winner))
        {
            GUI.Label(new Rect(x, y, iw, 24), $"Last match won by <b>{winner}</b>", bodyStyle);
            y += 30;
        }

        // Game mode: the host picks; everyone sees it.
        GUI.Label(new Rect(x, y + 10, 120, 20), "MODE", smallStyle);
        if (isHost)
        {
            var modeLabels = new[] { "FFA", "TEAM DM", "GUN GAME", "1 BULLET", "INFECTION" };
            float mbw = (iw - 130 - 4 * 6) / 5f;
            var modeStyle = new GUIStyle(mapButtonStyle) { fontSize = 11 };
            var modeActive = new GUIStyle(mapButtonActiveStyle) { fontSize = 11 };
            for (int i = 0; i < 5; i++)
            {
                var m = MatchRules.Modes[i];
                if (GUI.Button(new Rect(x + 130 + i * (mbw + 6), y + 2, mbw, 34), modeLabels[i], MatchRules.Mode == m ? modeActive : modeStyle) && MatchRules.Mode != m)
                {
                    MatchRules.SetMode(m);
                    GameSettings.Mode = i;
                    GameSettings.Save();
                }
            }
        }
        else
            GUI.Label(new Rect(x + 130, y + 4, iw - 130, 30), MatchRules.ModeName(MatchRules.Mode).ToUpper(), h3Style);
        y += 44;
        GUI.Label(new Rect(x, y, iw, 22), ModeGoal, smallStyle);
        y += 26;

        // Score to win: the host sets it (10-100, steps of 5); everyone else sees it. Only the kill-race modes use it.
        if (MatchRules.UsesScoreLimit)
        {
        GUI.Label(new Rect(x, y, 200, 20), MatchRules.Mode == MatchRules.GameMode.TeamDeathmatch ? "TEAM SCORE" : "SCORE TO WIN", smallStyle);
        if (isHost)
        {
            GUI.changed = false;
            float v = GUI.HorizontalSlider(new Rect(x + 130, y + 6, iw - 210, 20), MatchRules.FragLimit, MatchRules.MinFragLimit, MatchRules.MaxFragLimit);
            int kills = Mathf.Clamp(Mathf.RoundToInt(v / 5f) * 5, MatchRules.MinFragLimit, MatchRules.MaxFragLimit);
            if (GUI.changed && kills != MatchRules.FragLimit) // only when the host drags it (not a clamp of a test value)
            {
                MatchRules.SetFragLimit(kills);
                GameSettings.FragLimit = kills;
                GameSettings.Save();
            }
        }
        GUI.Label(new Rect(x + iw - 70, y - 4, 70, 28), $"{MatchRules.FragLimit}", valueStyle);
        y += 34;
        }

        // Map vote after each match (host toggle).
        if (isHost)
        {
            GUI.Label(new Rect(x, y + 8, 160, 20), "MAP VOTE", smallStyle);
            bool on = MatchRules.MapVoteEnabled;
            if (GUI.Button(new Rect(x + 130, y, 90, 30), "ON", on ? mapButtonActiveStyle : mapButtonStyle) && !on)
            { MatchRules.SetMapVote(true); GameSettings.MapVote = true; GameSettings.Save(); }
            if (GUI.Button(new Rect(x + 226, y, 90, 30), "OFF", on ? mapButtonStyle : mapButtonActiveStyle) && on)
            { MatchRules.SetMapVote(false); GameSettings.MapVote = false; GameSettings.Save(); }
            y += 40;
        }

        // Map: the host can switch it here (everyone in the lobby follows).
        if (isHost)
        {
            GUI.Label(new Rect(x, y + 10, 120, 20), "MAP", smallStyle);
            float mw = (iw - 130 - 8 * (MapCatalog.Count - 1)) / MapCatalog.Count;
            for (int i = 0; i < MapCatalog.Count; i++)
                if (GUI.Button(new Rect(x + 130 + i * (mw + 8), y + 2, mw, 34), MapCatalog.NameOf(i).ToUpper(),
                        MapCatalog.Current == i ? mapButtonActiveStyle : mapButtonStyle) && MapCatalog.Current != i)
                {
                    MatchRules.SetMap(i);
                    GameSettings.Map = i;
                    GameSettings.Save();
                }
            y += 46;
        }

        // Players.
        GUI.Label(new Rect(x, y, 300, 20), $"PLAYERS ({FpsPlayer.All.Count})", smallStyle);
        y += 26;
        float listBottom = panel.yMax - 110;
        var nameStyle = new GUIStyle(bodyStyle) { fontSize = 17, wordWrap = false, clipping = TextClipping.Clip };
        var stateStyle = new GUIStyle(bodyStyle) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
        foreach (var p in FpsPlayer.All)
        {
            if (y + 34 > listBottom) { GUI.Label(new Rect(x, y, iw, 24), "…", bodyStyle); break; }
            Fill(new Rect(x, y, iw, 34), new Color(1f, 1f, 1f, p == me ? 0.1f : 0.04f));
            Fill(new Rect(x + 12, y + 12, 10, 10), p.PlayerColor);
            string tag = p.IsBot.Value ? "  <size=12>[BOT]</size>" : p.IsHostPlayer ? "  <size=12>HOST</size>" : "";
            if (p == me) tag += "  <size=12>(you)</size>";
            GUI.Label(new Rect(x + 32, y + 5, iw - 230, 26), p.DisplayName + tag, nameStyle);
            bool ready = p.IsHostPlayer || p.Ready.Value;
            string state = p.IsHostPlayer ? "STARTS THE MATCH" : ready ? "READY" : "NOT READY";
            GUI.Label(new Rect(x + iw - 200, y + 4, 186, 26), state, stateStyle);
            if (isHost && !p.IsHostPlayer && GUI.Button(new Rect(x + iw - 280, y + 5, 70, 24), "KICK", mapButtonStyle))
                MatchRules.Kick(p);
            if (!p.IsHostPlayer)
                Fill(new Rect(x + iw - 6, y, 6, 34), ready ? new Color(0.3f, 0.9f, 0.4f) : new Color(0.9f, 0.3f, 0.25f));
            y += 40;
        }

        // Ready / Start, and Leave.
        float by = panel.yMax - 76, bw = (iw - 16) * 0.68f;
        bool busy = Time.unscaledTime < lobbyBusyUntil;
        if (isHost)
        {
            bool allReady = MatchRules.EveryoneReady(out int waiting);
            GUI.enabled = allReady && !busy;
            string text = allReady ? "START MATCH" : $"WAITING FOR {waiting} PLAYER{(waiting == 1 ? "" : "S")}";
            if (GUI.Button(new Rect(x, by, bw, 54), text, bigButtonStyle))
            {
                lobbyBusyUntil = Time.unscaledTime + 1f;
                MatchRules.StartMatch();
            }
            GUI.enabled = true;
            GUI.Label(new Rect(x, by - 26, iw, 22), "Start unlocks when every player is ready.", smallStyle);
        }
        else
        {
            bool ready = me.Ready.Value;
            GUI.enabled = !busy;
            if (GUI.Button(new Rect(x, by, bw, 54), ready ? "READY  (CLICK TO UNREADY)" : "READY UP", ready ? navActiveStyle : bigButtonStyle))
            {
                lobbyBusyUntil = Time.unscaledTime + 0.3f;
                me.Ready.Value = !ready;
            }
            GUI.enabled = true;
            GUI.Label(new Rect(x, by - 26, iw, 22), "The host starts the match once everyone is ready.", smallStyle);
        }
        var leaveStyle = new GUIStyle(navStyle) { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 0), fontSize = 17 };
        if (GUI.Button(new Rect(x + bw + 16, by, iw - bw - 16, 54), isHost ? "END GAME" : "LEAVE", leaveStyle))
            Disconnect();
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

    void DrawSettingsTab(Rect r) => DrawOptions(r, "SETTINGS");

    // ------------------------------------------------------------------ Options (main menu Settings and the Esc menu)

    enum OptionsTab { General, Controls, Keybindings }
    OptionsTab optionsTab;
    int rebinding = -1; // index into Bindings.All while waiting for a key
    Vector2 bindScroll;

    void DrawOptions(Rect r, string title)
    {
        GUI.Label(new Rect(r.x, r.y, 300, 40), title, h2Style);
        // Tabs.
        float tw = 150, tx = r.xMax - tw * 3 - 16;
        foreach (OptionsTab t in System.Enum.GetValues(typeof(OptionsTab)))
        {
            if (GUI.Button(new Rect(tx, r.y + 4, tw, 34), t.ToString().ToUpper(), optionsTab == t ? mapButtonActiveStyle : mapButtonStyle) && optionsTab != t)
            {
                optionsTab = t;
                rebinding = -1;
            }
            tx += tw + 8;
        }
        var body = new Rect(r.x, r.y + 60, r.width, r.height - 60);
        switch (optionsTab)
        {
            case OptionsTab.General: DrawGeneralOptions(body); break;
            case OptionsTab.Controls: DrawControlOptions(body); break;
            case OptionsTab.Keybindings: DrawKeybindings(body); break;
        }
    }

    void DrawControlOptions(Rect r)
    {
        float y = r.y + 4;
        bool changed = false;
        Slider(ref y, r, "MOUSE SENSITIVITY", ref GameSettings.Sensitivity, 0.02f, 0.4f, v => (v * 10f).ToString("0.00"), ref changed);
        Slider(ref y, r, "AIM-DOWN-SIGHTS SENSITIVITY", ref GameSettings.AdsSensitivity, 0.2f, 1.5f, v => $"{v * 100f:0}%", ref changed);
        GUI.Label(new Rect(r.x, y, 300, 20), "INVERT MOUSE Y", smallStyle);
        if (GUI.Button(new Rect(r.x, y + 24, 120, 36), "OFF", GameSettings.InvertY ? mapButtonStyle : mapButtonActiveStyle) && GameSettings.InvertY)
        { GameSettings.InvertY = false; changed = true; }
        if (GUI.Button(new Rect(r.x + 128, y + 24, 120, 36), "ON", GameSettings.InvertY ? mapButtonActiveStyle : mapButtonStyle) && !GameSettings.InvertY)
        { GameSettings.InvertY = true; changed = true; }
        y += 80;
        GUI.Label(new Rect(r.x, y, r.width, 44), "Mouse: left click fire · right click aim · middle click hook · wheel switches slots. Keys are in KEYBINDINGS.", bodyStyle);
        if (changed) GameSettings.Save();
    }

    void DrawKeybindings(Rect r)
    {
        var rowName = new GUIStyle(bodyStyle) { fontSize = 15, alignment = TextAnchor.MiddleLeft, wordWrap = false };
        var keyStyle = new GUIStyle(mapButtonStyle) { fontSize = 14 };
        float rowH = 38, listH = r.height - 60;
        var view = new Rect(r.x, r.y, r.width, listH);
        var content = new Rect(0, 0, r.width - 20, Bindings.All.Length * rowH);
        bindScroll = GUI.BeginScrollView(view, bindScroll, content, false, content.height > view.height);
        for (int i = 0; i < Bindings.All.Length; i++)
        {
            var a = Bindings.All[i];
            float y = i * rowH;
            if (i % 2 == 0) Fill(new Rect(0, y, content.width, rowH - 2), new Color(1f, 1f, 1f, 0.03f));
            GUI.Label(new Rect(12, y, content.width - 200, rowH - 2), Bindings.Name(a), rowName);
            bool waiting = rebinding == i;
            if (GUI.Button(new Rect(content.width - 180, y + 3, 170, rowH - 8), waiting ? "PRESS A KEY…" : Bindings.Label(a), waiting ? mapButtonActiveStyle : keyStyle))
                rebinding = waiting ? -1 : i;
        }
        GUI.EndScrollView();
        float by = r.yMax - 46;
        GUI.Label(new Rect(r.x, by, r.width - 230, 40), rebinding >= 0 ? "Press the new key (Esc cancels). A key already in use swaps over." : "Click a key to change it.", smallStyle);
        if (GUI.Button(new Rect(r.xMax - 210, by, 210, 40), "RESET KEYS", new GUIStyle(navStyle) { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 0), fontSize = 15 }))
        {
            Bindings.ResetDefaults();
            rebinding = -1;
        }
    }

    /// <summary>Waiting for a key to bind? Returns true if Esc was used up (cancelling).</summary>
    bool UpdateRebind()
    {
        if (rebinding < 0) return false;
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return false;
        if (kb.escapeKey.wasPressedThisFrame) { rebinding = -1; return true; }
        foreach (var k in kb.allKeys)
            if (k != null && k.wasPressedThisFrame)
            {
                Bindings.Rebind(Bindings.All[rebinding], k.keyCode);
                rebinding = -1;
                break;
            }
        return false;
    }

    void DrawGeneralOptions(Rect r)
    {
        float y = r.y + 4;
        bool changed = false;
        Slider(ref y, r, "FIELD OF VIEW", ref GameSettings.Fov, 60f, 100f, v => $"{v:0}°", ref changed);
        Slider(ref y, r, "MASTER VOLUME", ref GameSettings.Volume, 0f, 1f, v => $"{v * 100f:0}%", ref changed);
        Slider(ref y, r, "GORE LEVEL", ref GameSettings.GoreLevel, 0.5f, 4f, v => $"{v:0.0}x", ref changed);
        // Display: fullscreen / windowed (Unity remembers the mode itself) and vsync.
        float half = (r.width - 120 - 12) / 2f;
        GUI.Label(new Rect(r.x, y, 300, 20), "DISPLAY", smallStyle);
        bool full = Screen.fullScreenMode != FullScreenMode.Windowed;
        if (GUI.Button(new Rect(r.x, y + 24, half / 2f - 4, 36), "FULLSCREEN", full ? mapButtonActiveStyle : mapButtonStyle) && !full)
            Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
        if (GUI.Button(new Rect(r.x + half / 2f + 4, y + 24, half / 2f - 4, 36), "WINDOWED", full ? mapButtonStyle : mapButtonActiveStyle) && full)
            Screen.fullScreenMode = FullScreenMode.Windowed;
        GUI.Label(new Rect(r.x + half + 12, y, 300, 20), "VSYNC", smallStyle);
        if (GUI.Button(new Rect(r.x + half + 12, y + 24, half / 2f - 4, 36), "ON", GameSettings.VSync ? mapButtonActiveStyle : mapButtonStyle) && !GameSettings.VSync)
        { GameSettings.VSync = true; changed = true; }
        if (GUI.Button(new Rect(r.x + half + 12 + half / 2f + 4, y + 24, half / 2f - 4, 36), "OFF", GameSettings.VSync ? mapButtonStyle : mapButtonActiveStyle) && GameSettings.VSync)
        { GameSettings.VSync = false; changed = true; }
        y += 76;
        if (changed) GameSettings.Save();
        if (GUI.Button(new Rect(r.x, y + 4, 300, 44), "RESET TO DEFAULTS", new GUIStyle(navStyle) { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 0), fontSize = 17 }))
            GameSettings.ResetToDefaults();
    }

    void Slider(ref float y, Rect r, string label, ref float value, float min, float max, System.Func<float, string> fmt, ref bool changed)
    {
        GUI.Label(new Rect(r.x, y, 300, 20), label, smallStyle);
        float v = GUI.HorizontalSlider(new Rect(r.x, y + 28, r.width - 120, 20), value, min, max);
        GUI.Label(new Rect(r.x + r.width - 110, y + 16, 110, 34), fmt(v), valueStyle);
        if (!Mathf.Approximately(v, value)) { value = v; changed = true; }
        y += 64;
    }
}
