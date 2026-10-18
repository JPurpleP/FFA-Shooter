using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Derived GUI styles, cached. The HUD and menus used to build `new GUIStyle(base) { fontSize = ... }` every frame,
/// which made garbage every frame; in browsers the garbage collector can't run incrementally, so that showed up as
/// regular hitches. St(...) hands back the same style object for the same base + settings.
/// </summary>
public partial class GameUI
{
    readonly struct StyleKey : System.IEquatable<StyleKey>
    {
        public readonly GUIStyle Base;
        public readonly int Size, Align, Font, Wrap, Rich, Clip, PadL, PadR, PadT, PadB;

        public StyleKey(GUIStyle b, int size, int align, int font, int wrap, int rich, int clip, RectOffset pad)
        {
            Base = b; Size = size; Align = align; Font = font; Wrap = wrap; Rich = rich; Clip = clip;
            PadL = pad != null ? pad.left : int.MinValue; PadR = pad != null ? pad.right : 0;
            PadT = pad != null ? pad.top : 0; PadB = pad != null ? pad.bottom : 0;
        }

        public bool Equals(StyleKey o) => ReferenceEquals(Base, o.Base) && Size == o.Size && Align == o.Align && Font == o.Font
            && Wrap == o.Wrap && Rich == o.Rich && Clip == o.Clip && PadL == o.PadL && PadR == o.PadR && PadT == o.PadT && PadB == o.PadB;

        public override bool Equals(object obj) => obj is StyleKey k && Equals(k);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = Base != null ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Base) : 0;
                h = h * 31 + Size; h = h * 31 + Align; h = h * 31 + Font; h = h * 31 + Wrap; h = h * 31 + Rich; h = h * 31 + Clip;
                h = h * 31 + PadL; h = h * 31 + PadR; h = h * 31 + PadT; h = h * 31 + PadB;
                return h;
            }
        }
    }

    static readonly Dictionary<StyleKey, GUIStyle> styleCache = new();

    /// <summary>A style based on `b` with these settings changed (unset ones keep b's), cached.</summary>
    static GUIStyle St(GUIStyle b, int fontSize = -1, TextAnchor? alignment = null, FontStyle? fontStyle = null,
                       bool? wordWrap = null, bool? richText = null, TextClipping? clipping = null, RectOffset padding = null)
    {
        var key = new StyleKey(b, fontSize, alignment.HasValue ? (int)alignment.Value : -1, fontStyle.HasValue ? (int)fontStyle.Value : -1,
            wordWrap.HasValue ? (wordWrap.Value ? 1 : 0) : -1, richText.HasValue ? (richText.Value ? 1 : 0) : -1,
            clipping.HasValue ? (int)clipping.Value : -1, padding);
        if (styleCache.TryGetValue(key, out var s)) return s;
        if (styleCache.Count > 600) styleCache.Clear(); // a base style made on the fly: keep the cache bounded
        s = new GUIStyle(b);
        if (fontSize >= 0) s.fontSize = fontSize;
        if (alignment.HasValue) s.alignment = alignment.Value;
        if (fontStyle.HasValue) s.fontStyle = fontStyle.Value;
        if (wordWrap.HasValue) s.wordWrap = wordWrap.Value;
        if (richText.HasValue) s.richText = richText.Value;
        if (clipping.HasValue) s.clipping = clipping.Value;
        if (padding != null) s.padding = padding;
        styleCache[key] = s;
        return s;
    }
}
