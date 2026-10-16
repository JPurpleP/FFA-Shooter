using System.Text.RegularExpressions;

/// <summary>Masks offensive words in player names and chat (case-insensitive, also inside longer words).</summary>
public static class NameFilter
{
    static readonly string[] Words =
    {
        "fuck", "shit", "cunt", "bitch", "nigger", "nigga", "faggot", "fag", "retard", "whore", "slut", "dick", "cock",
        "pussy", "asshole", "bastard", "kike", "spic", "chink", "tranny", "rape", "nazi", "hitler", "kkk",
    };

    static readonly Regex Pattern = new(string.Join("|", System.Array.ConvertAll(Words, Leet)), RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Catch simple letter swaps (a->@/4, i->1/!, o->0, e->3, s->$/5).
    static string Leet(string w)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in w)
            sb.Append(c switch
            {
                'a' => "[a@4]", 'i' => "[i1!l]", 'o' => "[o0]", 'e' => "[e3]", 's' => "[s$5]", 't' => "[t7]", _ => Regex.Escape(c.ToString()),
            });
        return sb.ToString();
    }

    public static string Clean(string text) =>
        string.IsNullOrEmpty(text) ? text : Pattern.Replace(text, m => new string('*', m.Length));
}
