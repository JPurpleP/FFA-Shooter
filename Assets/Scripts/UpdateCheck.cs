using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Asks the public beta repo for its latest release once per launch; the main menu shows a banner when it's newer
/// than this build. Players on another version can't join a game either (see <see cref="Approve"/>).
/// </summary>
public class UpdateCheck : MonoBehaviour
{
    // The release list (newest first), not /releases/latest: that one skips pre-releases.
    const string LatestApi = "https://api.github.com/repos/JPurpleP/FFA-Shooter-Beta/releases?per_page=5";
    public const string DownloadPage = "https://github.com/JPurpleP/FFA-Shooter-Beta/releases";

    /// <summary>The newer version on GitHub ("" while unknown or when this build is current).</summary>
    public static string NewerVersion { get; private set; } = "";
    public static string NewerUrl { get; private set; } = DownloadPage;

    public static void Begin()
    {
        if (Application.isBatchMode) return;
        var go = new GameObject("Update Check");
        DontDestroyOnLoad(go);
        go.AddComponent<UpdateCheck>();
    }

    IEnumerator Start()
    {
        using var req = UnityWebRequest.Get(LatestApi);
        req.SetRequestHeader("Accept", "application/vnd.github+json");
        req.SetRequestHeader("User-Agent", "FFA-Shooter");
        req.timeout = 10;
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.Log($"[FFA] Update check failed: {req.error}");
            yield break;
        }
        var tag = Regex.Match(req.downloadHandler.text, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
        var url = Regex.Match(req.downloadHandler.text, "\"html_url\"\\s*:\\s*\"(https://github.com/[^\"]+/releases/tag/[^\"]+)\"");
        if (!tag.Success) yield break;
        string latest = tag.Groups[1].Value;
        if (Compare(latest, Application.version) > 0)
        {
            NewerVersion = Clean(latest);
            if (url.Success) NewerUrl = url.Groups[1].Value;
        }
        Debug.Log($"[FFA] Latest release {latest}, this build {Application.version}{(NewerVersion != "" ? " (update available)" : "")}");
    }

    /// <summary>"v0.9.2-beta" → "0.9.2".</summary>
    static string Clean(string v) => Regex.Match(v, @"\d+(\.\d+)*").Value;

    /// <summary>Compares dotted version numbers, ignoring any "v" prefix or "-beta" suffix.</summary>
    public static int Compare(string a, string b)
    {
        var pa = Clean(a).Split('.');
        var pb = Clean(b).Split('.');
        for (int i = 0; i < Mathf.Max(pa.Length, pb.Length); i++)
        {
            int x = i < pa.Length && int.TryParse(pa[i], out int xa) ? xa : 0;
            int y = i < pb.Length && int.TryParse(pb[i], out int yb) ? yb : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    // ------------------------------------------------------------------ joining

    /// <summary>What a client sends when connecting: its version.</summary>
    public static byte[] Payload => System.Text.Encoding.UTF8.GetBytes("ffa:" + (TestVersion() ?? Application.version));

    /// <summary>-ffaversion &lt;v&gt;: pretend to be another version (tests the refusal).</summary>
    static string TestVersion()
    {
        var args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, "-ffaversion");
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>Host: only let in players on the same version (different builds desync in odd ways).</summary>
    public static void Approve(Unity.Netcode.NetworkManager.ConnectionApprovalRequest request, Unity.Netcode.NetworkManager.ConnectionApprovalResponse response)
    {
        string theirs = request.Payload != null ? System.Text.Encoding.UTF8.GetString(request.Payload) : "";
        theirs = theirs.StartsWith("ffa:") ? theirs.Substring(4) : "an old version";
        bool same = theirs == Application.version || request.ClientNetworkId == Unity.Netcode.NetworkManager.ServerClientId;
        response.Approved = same;
        response.CreatePlayerObject = same;
        response.Pending = false;
        if (!same)
        {
            bool hostOlder = Compare(Application.version, theirs) < 0;
            response.Reason = hostOlder
                ? $"The host is on v{Application.version} and you have v{theirs}. Ask them to update."
                : $"This game is on v{Application.version} and you have v{theirs}. Download the latest version to join.";
            Debug.Log($"[FFA] Refused a player on v{theirs} (this game is v{Application.version})");
        }
    }
}
