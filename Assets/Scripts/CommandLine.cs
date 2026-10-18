using UnityEngine;

/// <summary>
/// Command-line arguments (the -ffa... test flags). Browsers have no command line, so in the WebGL build they come
/// from the page address instead: index.html?args=-ffapractice%20-ffaautotest (spaces between arguments).
/// </summary>
public static class CommandLine
{
    static string[] args;

    public static string[] Args
    {
        get
        {
            if (args != null) return args;
#if UNITY_WEBGL && !UNITY_EDITOR
            args = new[] { "game" };
            string url = Application.absoluteURL ?? "";
            int q = url.IndexOf("args=");
            if (q >= 0)
            {
                string raw = url.Substring(q + 5);
                int amp = raw.IndexOf('&');
                if (amp >= 0) raw = raw.Substring(0, amp);
                raw = System.Uri.UnescapeDataString(raw.Replace('+', ' '));
                var parts = raw.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
                args = new string[parts.Length + 1];
                args[0] = "game";
                System.Array.Copy(parts, 0, args, 1, parts.Length);
            }
#else
            args = System.Environment.GetCommandLineArgs();
#endif
            return args;
        }
    }
}
