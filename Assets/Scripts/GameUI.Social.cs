using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Chat, quick chat, kicking, the public game list (Find Game) and the leaderboard tab.</summary>
public partial class GameUI
{
    // ------------------------------------------------------------------ chat

    struct ChatLine { public string Name, Text; public Color Color; public float Time; public bool System; }
    readonly List<ChatLine> chat = new();
    bool chatOpen, quickChatOpen;
    string chatText = "";
    int chatOpenFrame = -1;

    static readonly string[] QuickPhrases = { "Nice shot!", "Good game!", "Watch out!", "Over here!", "Thanks!", "Rematch?" };

    /// <summary>Typing a chat message: every game key is ignored.</summary>
    public static bool ChatTyping => instance != null && instance.chatOpen;
    /// <summary>The quick-chat menu is up: number keys pick a phrase instead of a weapon.</summary>
    public static bool QuickChatOpen => instance != null && instance.quickChatOpen;

    public static void AddChat(string name, Color color, string text, bool system = false)
    {
        if (instance == null) return;
        instance.chat.Add(new ChatLine { Name = name, Text = text, Color = color, Time = Time.time, System = system });
        if (instance.chat.Count > 40) instance.chat.RemoveAt(0);
    }

    static void SendChat(string text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0 || FpsPlayer.Local == null) return;
        FpsPlayer.Local.SendChat(text);
    }

    /// <summary>Update: open / close chat and quick chat. Returns true if it used up Esc.</summary>
    bool UpdateChatKeys(bool inGame, bool inLobby)
    {
        var kb = Keyboard.current;
        if (kb == null || !InSession || FpsPlayer.Local == null) { chatOpen = quickChatOpen = false; return false; }
        if (chatOpen)
        {
            if (kb.escapeKey.wasPressedThisFrame) { chatOpen = false; return true; }
            return false;
        }
        if (inGame && CursorCaptured && Bindings.Pressed(GameAction.Chat))
        {
            chatOpen = true;
            quickChatOpen = false;
            chatText = "";
            chatOpenFrame = Time.frameCount;
            return false;
        }
        if (inGame && CursorCaptured && Bindings.Pressed(GameAction.QuickChat)) quickChatOpen = !quickChatOpen;
        if (quickChatOpen)
        {
            if (!inGame || kb.escapeKey.wasPressedThisFrame) { quickChatOpen = false; return kb.escapeKey.wasPressedThisFrame; }
            for (int i = 0; i < QuickPhrases.Length; i++)
                if (kb[Key.Digit1 + i].wasPressedThisFrame)
                {
                    SendChat(QuickPhrases[i]);
                    quickChatOpen = false;
                }
        }
        return false;
    }

    /// <summary>Recent chat in the corner of the HUD (everything while typing), plus the input box / quick menu.</summary>
    void DrawChat(float w, float h)
    {
        float t = Time.time;
        var style = new GUIStyle(label) { fontSize = 15, wordWrap = true };
        float x = 32, y = h * 0.42f, lineH = 21;
        int shown = 0;
        for (int i = chat.Count - 1; i >= 0 && shown < 8; i--)
        {
            var c = chat[i];
            float age = t - c.Time;
            if (!chatOpen && age > 12f) break;
            float a = chatOpen ? 1f : Mathf.Clamp01((12f - age) / 2f);
            string hex = ColorUtility.ToHtmlStringRGB(c.Color);
            string text = c.System ? $"<i>{c.Text}</i>" : $"<b><color=#{hex}>{c.Name}</color></b>: {c.Text}";
            Shadowed(new Rect(x, y + (7 - shown) * lineH, 460, lineH), text, style, new Color(1f, 1f, 1f, a));
            shown++;
        }
        float boxY = y + 8 * lineH + 6;
        if (chatOpen)
        {
            EnsureMenuStyles();
            var e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                SendChat(chatText);
                chatOpen = false;
                e.Use();
                return;
            }
            Fill(new Rect(x - 4, boxY - 2, 468, 34), new Color(0f, 0f, 0f, 0.5f));
            GUI.SetNextControlName("ChatInput");
            chatText = GUI.TextField(new Rect(x, boxY, 460, 30), chatText, 100, new GUIStyle(fieldStyle) { fontSize = 15, padding = new RectOffset(8, 8, 4, 4) });
            if (Time.frameCount <= chatOpenFrame + 1) { chatText = ""; GUI.FocusControl("ChatInput"); }
            Shadowed(new Rect(x, boxY + 34, 460, 18), "<size=12>Enter to send · Esc to cancel</size>", label, new Color(1f, 1f, 1f, 0.6f));
        }
        else if (quickChatOpen)
        {
            Fill(new Rect(x - 6, boxY - 4, 230, QuickPhrases.Length * 24 + 32), new Color(0f, 0f, 0f, 0.55f));
            Shadowed(new Rect(x, boxY, 220, 22), "<b>QUICK CHAT</b>  <size=12>(press a number)</size>", label, Accent);
            for (int i = 0; i < QuickPhrases.Length; i++)
                Shadowed(new Rect(x, boxY + 26 + i * 24, 220, 22), $"<b>{i + 1}</b>   {QuickPhrases[i]}", label, Color.white);
        }
    }

    // ------------------------------------------------------------------ lobby chat (cursor is free there)

    string lobbyChatText = "";

    void DrawLobbyChat(Rect r)
    {
        Fill(r, new Color(0.04f, 0.05f, 0.07f, 0.9f));
        Fill(new Rect(r.x, r.y, r.width, 3), Accent);
        GUI.Label(new Rect(r.x + 16, r.y + 14, r.width - 32, 24), "CHAT", h3Style);
        var style = new GUIStyle(bodyStyle) { fontSize = 14, wordWrap = true, richText = true };
        float y = r.yMax - 64;
        for (int i = chat.Count - 1; i >= 0 && y > r.y + 50; i--)
        {
            var c = chat[i];
            string hex = ColorUtility.ToHtmlStringRGB(c.Color);
            string text = c.System ? $"<i>{c.Text}</i>" : $"<b><color=#{hex}>{c.Name}</color></b>: {c.Text}";
            float lh = style.CalcHeight(new GUIContent(text), r.width - 32);
            y -= lh + 2;
            if (y < r.y + 50) break;
            GUI.Label(new Rect(r.x + 16, y, r.width - 32, lh), text, style);
        }
        var e = Event.current;
        bool send = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && GUI.GetNameOfFocusedControl() == "LobbyChat";
        GUI.SetNextControlName("LobbyChat");
        lobbyChatText = GUI.TextField(new Rect(r.x + 16, r.yMax - 52, r.width - 110, 36), lobbyChatText, 100, new GUIStyle(fieldStyle) { fontSize = 14, padding = new RectOffset(8, 8, 4, 4) });
        if (GUI.Button(new Rect(r.xMax - 88, r.yMax - 52, 72, 36), "SEND", mapButtonStyle) || send)
        {
            SendChat(lobbyChatText);
            lobbyChatText = "";
            if (send) e.Use();
        }
    }

    // ------------------------------------------------------------------ medals

    readonly List<(Medal medal, float time)> medals = new();

    public static void ShowMedal(Medal m)
    {
        if (instance == null) return;
        instance.medals.Add((m, Time.time));
        if (instance.medals.Count > 4) instance.medals.RemoveAt(0);
        var listener = FpsPlayer.Local;
        if (listener != null) AudioSource.PlayClipAtPoint(Medals.Sting(m), listener.transform.position, 0.8f);
    }

    /// <summary>Medal pop-ups under the crosshair: newest on top, each for a couple of seconds.</summary>
    void DrawMedals(float w, float h)
    {
        float t = Time.time;
        medals.RemoveAll(x => t - x.time > 2.6f);
        float y = h * 0.62f;
        var title = new GUIStyle(centerLabel) { fontSize = 26, fontStyle = FontStyle.Bold };
        var sub = new GUIStyle(centerLabel) { fontSize = 13 };
        for (int i = medals.Count - 1; i >= 0; i--)
        {
            var (m, at) = medals[i];
            float age = t - at;
            float a = Mathf.Clamp01(age / 0.12f) * Mathf.Clamp01((2.6f - age) / 0.5f);
            float pop = 1f + 0.25f * Mathf.Exp(-age * 14f); // punches in, settles
            var c = Medals.Tint(m);
            c.a = a;
            var size = new GUIStyle(title) { fontSize = Mathf.RoundToInt(26 * pop) };
            Shadowed(new Rect(0, y, w, 34), Medals.Name(m), size, c);
            Shadowed(new Rect(0, y + 30, w, 18), Medals.Detail(m), sub, new Color(1f, 1f, 1f, 0.75f * a));
            y += 56;
        }
    }

    // ------------------------------------------------------------------ voice

    void MuteButton(FpsPlayer p, Rect r)
    {
        bool muted = VoiceChat.Muted.Contains(p.OwnerClientId);
        if (VoiceChat.IsTalking(p.OwnerClientId)) Fill(new Rect(r.x - 14, r.y + r.height / 2 - 4, 8, 8), new Color(0.3f, 0.95f, 0.45f));
        if (GUI.Button(r, muted ? "UNMUTE" : "MUTE", muted ? mapButtonActiveStyle : mapButtonStyle))
        {
            if (muted) VoiceChat.Muted.Remove(p.OwnerClientId);
            else VoiceChat.Muted.Add(p.OwnerClientId);
        }
    }

    /// <summary>Who's talking (left side, above the chat) and your own mic while you transmit.</summary>
    void DrawVoice(float w, float h)
    {
        if (GameSettings.VoiceMode == 0) return;
        var style = new GUIStyle(label) { fontSize = 15 };
        float x = 32, y = h * 0.42f - 30;
        if (VoiceChat.Transmitting)
        {
            Fill(new Rect(x, y + 6, 10, 10), new Color(0.3f, 0.95f, 0.45f));
            Shadowed(new Rect(x + 18, y, 300, 22), "<b>MIC ON</b>", style, new Color(0.6f, 1f, 0.7f));
            y -= 24;
        }
        foreach (var p in FpsPlayer.All)
        {
            if (p == null || !p.IsSpawned || p == FpsPlayer.Local || p.IsBot.Value || !VoiceChat.IsTalking(p.OwnerClientId)) continue;
            Fill(new Rect(x, y + 6, 10, 10), p.PlayerColor);
            Shadowed(new Rect(x + 18, y, 300, 22), $"<b>{p.DisplayName}</b>  <size=12>speaking</size>", style, Color.white);
            y -= 24;
        }
    }

    // ------------------------------------------------------------------ kicking (host)

    bool pausePlayers; // the Esc menu is showing the player list

    void DrawPlayerList(float w, float h)
    {
        Fill(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.55f));
        float pw = Mathf.Min(w - 80, 560), ph = Mathf.Min(h - 60, 560);
        var r = new Rect((w - pw) / 2f, (h - ph) / 2f, pw, ph);
        Fill(r, new Color(0.04f, 0.05f, 0.07f, 0.96f));
        Fill(new Rect(r.x, r.y, r.width, 3), Accent);
        float x = r.x + 30, iw = pw - 60, y = r.y + 20;
        GUI.Label(new Rect(x, y, iw, 40), "PLAYERS", h2Style);
        bool host = Net.IsServer;
        y += 56;
        var row = new GUIStyle(bodyStyle) { fontSize = 16, wordWrap = false };
        foreach (var p in FpsPlayer.All.ToArray())
        {
            if (!p.IsSpawned) continue;
            if (y > r.yMax - 110) break;
            Fill(new Rect(x, y, iw, 34), new Color(1f, 1f, 1f, 0.04f));
            Fill(new Rect(x + 10, y + 12, 10, 10), p.PlayerColor);
            string tag = p.IsBot.Value ? "  <size=12>[BOT]</size>" : p.IsHostPlayer ? "  <size=12>HOST</size>" : "";
            GUI.Label(new Rect(x + 30, y + 5, iw - 160, 26), p.DisplayName + tag, row);
            if (host && !p.IsHostPlayer && GUI.Button(new Rect(x + iw - 110, y + 3, 104, 28), "KICK", mapButtonStyle))
                MatchRules.Kick(p);
            if (p != FpsPlayer.Local && !p.IsBot.Value) MuteButton(p, new Rect(x + iw - 200, y + 3, 84, 28));
            y += 40;
        }
        var back = new GUIStyle(navStyle) { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 0), fontSize = 16 };
        if (GUI.Button(new Rect(x, r.yMax - 64, 180, 44), "BACK", back)) pausePlayers = false;
    }

    // ------------------------------------------------------------------ Find Game (public list)

    List<LobbyBrowser.Entry> games = new();
    string gamesStatus = "";
    float gamesRefreshedAt = -100f;
    bool gamesLoading;
    Vector2 gamesScroll;

    async void RefreshGames()
    {
        if (gamesLoading) return;
        gamesLoading = true;
        gamesStatus = "Looking for games…";
        gamesRefreshedAt = Time.unscaledTime;
        try
        {
            games = await LobbyBrowser.Query();
            gamesStatus = games.Count == 0 ? "No public games right now. Host one and it shows up here." : "";
        }
        catch (System.Exception e) { gamesStatus = RelayConnect.Describe(e); }
        finally { gamesLoading = false; }
    }

    void DrawFindGameTab(Rect r)
    {
        GUI.Label(new Rect(r.x, r.y, r.width, 40), "FIND A GAME", h2Style);
        if (Time.unscaledTime - gamesRefreshedAt > 12f && !connecting) RefreshGames();
        GUI.enabled = !gamesLoading;
        if (GUI.Button(new Rect(r.xMax - 150, r.y + 4, 150, 34), gamesLoading ? "LOADING…" : "REFRESH", mapButtonStyle)) RefreshGames();
        GUI.enabled = true;
        float y = r.y + 56;
        var head = new GUIStyle(smallStyle);
        GUI.Label(new Rect(r.x + 12, y, 220, 20), "GAME", head);
        GUI.Label(new Rect(r.x + 250, y, 120, 20), "MAP", head);
        GUI.Label(new Rect(r.x + 370, y, 150, 20), "MODE", head);
        GUI.Label(new Rect(r.x + 520, y, 80, 20), "PLAYERS", head);
        y += 24;
        var row = new GUIStyle(bodyStyle) { fontSize = 15, wordWrap = false, clipping = TextClipping.Clip };
        var view = new Rect(r.x, y, r.width, r.yMax - y - 40);
        var content = new Rect(0, 0, r.width - 20, Mathf.Max(view.height, games.Count * 44));
        gamesScroll = GUI.BeginScrollView(view, gamesScroll, content);
        for (int i = 0; i < games.Count; i++)
        {
            var g = games[i];
            float ry = i * 44;
            Fill(new Rect(0, ry, content.width, 40), new Color(1f, 1f, 1f, i % 2 == 0 ? 0.05f : 0.03f));
            GUI.Label(new Rect(12, ry + 9, 230, 24), g.Name, row);
            GUI.Label(new Rect(250, ry + 9, 115, 24), g.Map, row);
            GUI.Label(new Rect(370, ry + 9, 145, 24), $"{g.Mode} <size=12><color=#ffffff88>{g.State}</color></size>", row);
            GUI.Label(new Rect(520, ry + 9, 80, 24), $"{g.Players}/{LobbyBrowser.MaxPlayers}", row);
            GUI.enabled = !connecting && g.Players < LobbyBrowser.MaxPlayers;
            if (GUI.Button(new Rect(content.width - 110, ry + 4, 104, 32), "JOIN", bigButtonStyle))
            {
                LocalPlayerName = GameSettings.Name;
                address = g.Code;
                Join();
            }
            GUI.enabled = true;
        }
        GUI.EndScrollView();
        string line = !string.IsNullOrEmpty(status) ? (connecting ? status : $"<color=#ff8a80>{status}</color>") : gamesStatus;
        GUI.Label(new Rect(r.x, r.yMax - 30, r.width, 26), line, bodyStyle);
    }

    // ------------------------------------------------------------------ Leaderboard

    List<Career.Row> top = new();
    string topStatus = "";
    float topRefreshedAt = -100f;
    bool topLoading;

    async void RefreshTop()
    {
        if (topLoading) return;
        topLoading = true;
        topStatus = "Loading…";
        topRefreshedAt = Time.unscaledTime;
        try
        {
            top = await Career.Top(50);
            topStatus = top.Count == 0 ? "No one on the board yet: win a match with another player to get on it." : "";
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[FFA] Leaderboard: {e}");
            topStatus = "The global leaderboard isn't available right now.";
        }
        finally { topLoading = false; }
    }

    void DrawLeaderboardTab(Rect r)
    {
        GUI.Label(new Rect(r.x, r.y, r.width, 40), "LEADERBOARD", h2Style);
        if (Time.unscaledTime - topRefreshedAt > 30f) RefreshTop();
        GUI.enabled = !topLoading;
        if (GUI.Button(new Rect(r.xMax - 150, r.y + 4, 150, 34), topLoading ? "LOADING…" : "REFRESH", mapButtonStyle)) RefreshTop();
        GUI.enabled = true;

        // Your career.
        float y = r.y + 54;
        Fill(new Rect(r.x, y, r.width, 64), new Color(1f, 1f, 1f, 0.05f));
        var big = new GUIStyle(h3Style) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
        var cap = new GUIStyle(smallStyle) { alignment = TextAnchor.MiddleCenter };
        var stats = new[] { ("WINS", Career.Wins.ToString()), ("K/D", Career.KD.ToString("0.00")), ("KILLS", Career.Kills.ToString()), ("DEATHS", Career.Deaths.ToString()), ("MATCHES", Career.Matches.ToString()) };
        float cw = r.width / stats.Length;
        for (int i = 0; i < stats.Length; i++)
        {
            GUI.Label(new Rect(r.x + i * cw, y + 6, cw, 30), stats[i].Item2, big);
            GUI.Label(new Rect(r.x + i * cw, y + 38, cw, 20), stats[i].Item1, cap);
        }
        y += 80;

        // Global top.
        var head = new GUIStyle(smallStyle);
        GUI.Label(new Rect(r.x + 12, y, 40, 20), "#", head);
        GUI.Label(new Rect(r.x + 60, y, 260, 20), "PLAYER", head);
        GUI.Label(new Rect(r.x + r.width - 260, y, 70, 20), "WINS", head);
        GUI.Label(new Rect(r.x + r.width - 180, y, 70, 20), "K/D", head);
        GUI.Label(new Rect(r.x + r.width - 100, y, 90, 20), "KILLS", head);
        y += 24;
        var row = new GUIStyle(bodyStyle) { fontSize = 15, wordWrap = false, clipping = TextClipping.Clip };
        var view = new Rect(r.x, y, r.width, r.yMax - y - 34);
        var content = new Rect(0, 0, r.width - 20, Mathf.Max(view.height, top.Count * 30));
        topScroll = GUI.BeginScrollView(view, topScroll, content);
        for (int i = 0; i < top.Count; i++)
        {
            var e = top[i];
            float ry = i * 30;
            Fill(new Rect(0, ry, content.width, 28), e.Me ? new Color(Accent.r, Accent.g, Accent.b, 0.25f) : new Color(1f, 1f, 1f, i % 2 == 0 ? 0.04f : 0.02f));
            GUI.Label(new Rect(12, ry + 4, 40, 22), e.Rank.ToString(), row);
            GUI.Label(new Rect(60, ry + 4, 300, 22), e.Name + (e.Me ? "  <size=12>(you)</size>" : ""), row);
            GUI.Label(new Rect(content.width - 240, ry + 4, 70, 22), e.Wins.ToString(), row);
            GUI.Label(new Rect(content.width - 160, ry + 4, 70, 22), e.KD.ToString("0.00"), row);
            GUI.Label(new Rect(content.width - 80, ry + 4, 80, 22), e.Kills.ToString(), row);
        }
        GUI.EndScrollView();
        GUI.Label(new Rect(r.x, r.yMax - 28, r.width, 24), topStatus, bodyStyle);
    }

    Vector2 topScroll;
}
