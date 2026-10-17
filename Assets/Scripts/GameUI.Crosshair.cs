using UnityEngine;

/// <summary>The crosshair (style, color, size, thickness, outline from Options > HUD) and colorblind-safe colors.</summary>
public partial class GameUI
{
    public static readonly string[] CrosshairStyles = { "CROSS", "CROSS+DOT", "DOT", "CIRCLE" };
    public static readonly string[] CrosshairColorNames = { "WHITE", "GREEN", "YELLOW", "CYAN", "PINK", "RED" };
    static readonly Color[] CrosshairColors =
    {
        Color.white, new Color(0.3f, 1f, 0.35f), new Color(1f, 0.95f, 0.25f), new Color(0.3f, 0.95f, 1f), new Color(1f, 0.4f, 0.85f), new Color(1f, 0.25f, 0.25f),
    };
    public static readonly string[] ColorblindNames = { "OFF", "RED-GREEN", "BLUE-YELLOW" };

    static Texture2D ringTex;

    /// <summary>Crosshair at (cx, cy); gap is the weapon's current spread in pixels, alpha fades it while aiming.</summary>
    void DrawCrosshair(float cx, float cy, float gap, float alpha)
    {
        var c = CrosshairColors[Mathf.Clamp(GameSettings.CrosshairColor, 0, CrosshairColors.Length - 1)];
        c.a = 0.95f * alpha;
        var outline = new Color(0f, 0f, 0f, 0.6f * alpha);
        float size = GameSettings.CrosshairSize, th = GameSettings.CrosshairThickness;
        float len = 4.5f * size;
        gap *= Mathf.Lerp(1f, size, 0.5f);
        bool o = GameSettings.CrosshairOutline;
        int style = GameSettings.CrosshairStyle;

        void Bar(Rect r)
        {
            if (o) Fill(new Rect(r.x - 1, r.y - 1, r.width + 2, r.height + 2), outline);
            Fill(r, c);
        }
        if (style == 0 || style == 1)
        {
            Bar(new Rect(cx - gap - len, cy - th / 2, len, th));
            Bar(new Rect(cx + gap, cy - th / 2, len, th));
            Bar(new Rect(cx - th / 2, cy - gap - len, th, len));
            Bar(new Rect(cx - th / 2, cy + gap, th, len));
        }
        if (style == 1 || style == 2)
        {
            float d = Mathf.Max(1.5f, th * (style == 2 ? 1.6f : 1f));
            Bar(new Rect(cx - d / 2, cy - d / 2, d, d));
        }
        if (style == 3)
        {
            if (ringTex == null) ringTex = MakeRing();
            float rad = gap + 3f * size;
            var r = new Rect(cx - rad, cy - rad, rad * 2, rad * 2);
            var old = GUI.color;
            if (o) { GUI.color = outline; GUI.DrawTexture(new Rect(r.x - 1, r.y - 1, r.width + 2, r.height + 2), ringTex); }
            GUI.color = c;
            GUI.DrawTexture(r, ringTex);
            GUI.color = old;
            Fill(new Rect(cx - 0.75f, cy - 0.75f, 1.5f, 1.5f), c);
        }
    }

    /// <summary>A thin anti-aliased ring, white on transparent (tinted by GUI.color).</summary>
    static Texture2D MakeRing()
    {
        const int n = 128;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        float r0 = n / 2f - 6f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Abs(Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) - r0);
                byte a = (byte)(Mathf.Clamp01(3f - d) * 255f);
                px[y * n + x] = new Color32(255, 255, 255, a);
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    // ------------------------------------------------------------------ colorblind

    /// <summary>Swap a color the colorblind setting can't tell apart for one it can.</summary>
    public static Color Accessible(Color c)
    {
        int mode = GameSettings.Colorblind;
        if (mode == 0) return c;
        bool reddish = c.r > 0.8f && c.g < 0.45f && c.b < 0.45f;
        bool greenish = c.g > 0.8f && c.r < 0.6f && c.b < 0.6f;
        bool blueish = c.b > 0.8f && c.r < 0.45f;
        if (mode == 1) // red-green: red becomes orange, green becomes sky blue
        {
            if (reddish) return new Color(1f, 0.6f, 0.1f, c.a);
            if (greenish) return new Color(0.35f, 0.75f, 1f, c.a);
        }
        else // blue-yellow: blue becomes cyan, red becomes magenta
        {
            if (blueish) return new Color(0.2f, 0.9f, 0.9f, c.a);
            if (reddish) return new Color(1f, 0.25f, 0.6f, c.a);
        }
        return c;
    }

    /// <summary>Health bar color from empty to full, colorblind-aware.</summary>
    public static Color HealthColor(float hp) => GameSettings.Colorblind == 1
        ? Color.Lerp(new Color(1f, 0.6f, 0.1f), new Color(0.35f, 0.75f, 1f), hp)
        : Color.Lerp(new Color(1f, 0.2f, 0.2f), new Color(0.3f, 1f, 0.4f), hp);

    // ------------------------------------------------------------------ Options > HUD

    void DrawHudOptions(Rect r)
    {
        float y = r.y + 4;
        bool changed = false;
        float cw = r.width - 120 - 170; // controls on the left, live preview on the right
        void Row(string label, string[] names, ref int value)
        {
            GUI.Label(new Rect(r.x, y, 300, 20), label, smallStyle);
            float bw = (cw - (names.Length - 1) * 6) / names.Length;
            for (int i = 0; i < names.Length; i++)
                if (GUI.Button(new Rect(r.x + i * (bw + 6), y + 22, bw, 32), names[i], value == i ? mapButtonActiveStyle : mapButtonStyle) && value != i)
                { value = i; changed = true; }
            y += 66;
        }
        Row("CROSSHAIR", CrosshairStyles, ref GameSettings.CrosshairStyle);
        Row("COLOR", CrosshairColorNames, ref GameSettings.CrosshairColor);
        var sr = new Rect(r.x, r.y, cw + 120, r.height);
        Slider(ref y, sr, "SIZE", ref GameSettings.CrosshairSize, 0.5f, 2.5f, v => $"{v * 100f:0}%", ref changed);
        Slider(ref y, sr, "THICKNESS", ref GameSettings.CrosshairThickness, 1f, 4f, v => $"{v:0.0}", ref changed);
        int outline = GameSettings.CrosshairOutline ? 1 : 0;
        Row("OUTLINE", new[] { "OFF", "ON" }, ref outline);
        GameSettings.CrosshairOutline = outline == 1;
        Row("COLORBLIND MODE", ColorblindNames, ref GameSettings.Colorblind);

        // Live preview over a busy background.
        var pv = new Rect(r.xMax - 150, r.y + 22, 150, 150);
        Fill(pv, new Color(0.35f, 0.45f, 0.55f, 1f));
        Fill(new Rect(pv.x, pv.y + 90, pv.width, 60), new Color(0.55f, 0.5f, 0.42f, 1f));
        Fill(new Rect(pv.x + 20, pv.y + 40, 40, 70), new Color(0.75f, 0.73f, 0.7f, 1f));
        DrawCrosshair(pv.center.x, pv.center.y, 6f, 1f);
        GUI.Label(new Rect(pv.x, pv.yMax + 4, pv.width, 20), "PREVIEW", new GUIStyle(smallStyle) { alignment = TextAnchor.MiddleCenter });
        // Team color swatches for the colorblind setting.
        float sy = pv.yMax + 34;
        foreach (var (name, col) in new[] { ("RED TEAM", MatchRules.TeamColorRaw(MatchRules.TeamRed)), ("BLUE TEAM", MatchRules.TeamColorRaw(MatchRules.TeamBlue)), ("INFECTED", MatchRules.InfectedColorRaw) })
        {
            Fill(new Rect(pv.x, sy, 18, 18), Accessible(col));
            GUI.Label(new Rect(pv.x + 26, sy, 130, 20), name, smallStyle);
            sy += 26;
        }
        if (changed) GameSettings.Save();
    }
}
