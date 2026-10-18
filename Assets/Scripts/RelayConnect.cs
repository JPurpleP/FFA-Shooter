using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

/// <summary>
/// Online play through Unity Relay: the host gets a short join code, friends type it in. No port forwarding,
/// and nobody's IP address is shared. Players sign in anonymously to Unity Gaming Services on first use.
/// Needs the project linked to a UGS cloud project (Edit > Project Settings > Services) with Relay enabled.
/// </summary>
public static class RelayConnect
{
    /// <summary>Most players in one match besides the host (Relay allows up to 100).</summary>
    public const int MaxClients = 15;

    /// <summary>Join code of the online match we're in (hosting or joined), or "" for direct-IP games / the menu.</summary>
    public static string Code { get; private set; } = "";

    /// <summary>Browsers can only open WebSockets; desktop uses DTLS over UDP. Relay bridges the two.</summary>
#if UNITY_WEBGL && !UNITY_EDITOR
    const string ConnectionType = "wss";
#else
    const string ConnectionType = "dtls";
#endif

    static void Configure(UnityTransport transport)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        transport.UseWebSockets = true;
#endif
    }

    static Task signIn;

    /// <summary>Relay join codes are 6 letters/digits; anything with a dot or colon is an address.</summary>
    public static bool LooksLikeJoinCode(string text) => Regex.IsMatch((text ?? "").Trim(), "^[A-Za-z0-9]{6}$");

    /// <summary>Allocates a Relay server, points the transport at it and returns the join code.</summary>
    public static async Task<string> PrepareHost(UnityTransport transport)
    {
        await EnsureSignedIn();
        Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MaxClients);
        string code = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
        Configure(transport);
        transport.SetRelayServerData(allocation.ToRelayServerData(ConnectionType));
        Code = code.ToUpperInvariant();
        return Code;
    }

    /// <summary>Looks up a join code and points the transport at that host's Relay server.</summary>
    public static async Task PrepareJoin(UnityTransport transport, string code)
    {
        await EnsureSignedIn();
        code = code.Trim().ToUpperInvariant();
        JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(code);
        Configure(transport);
        transport.SetRelayServerData(allocation.ToRelayServerData(ConnectionType));
        Code = code;
    }

    public static void ClearCode() => Code = "";

    public static Task EnsureSignedIn() => signIn is { IsFaulted: false, IsCanceled: false } ? signIn : signIn = SignIn();

    static async Task SignIn()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            var options = new InitializationOptions();
            // Each copy of the game on one PC needs its own anonymous player (local multi-instance tests).
            string profile = CommandLineProfile() ?? (Application.isEditor ? "editor" : null);
            if (profile != null) options.SetProfile(profile);
            await UnityServices.InitializeAsync(options);
        }
        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        Debug.Log($"[FFA] Signed in to Unity services as {AuthenticationService.Instance.PlayerId}");
    }

    static string CommandLineProfile()
    {
        var args = CommandLine.Args;
        int i = Array.IndexOf(args, "-ffaprofile");
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>A short message for the menu explaining why going online failed.</summary>
    public static string Describe(Exception e)
    {
        while (e is AggregateException { InnerException: not null } agg) e = agg.InnerException;
        Debug.LogWarning($"[FFA] Relay: {e}");
        return e switch
        {
            RelayServiceException { Reason: RelayExceptionReason.JoinCodeNotFound or RelayExceptionReason.InvalidRequest }
                => "No game found with that code. Check it and try again.",
            RelayServiceException { Reason: RelayExceptionReason.AllocationNotFound }
                => "That game has ended.",
            RelayServiceException r => $"Online service error: {r.Message}",
            AuthenticationException or RequestFailedException when !IsLinked()
                => "Online play isn't set up yet: link this project to Unity Gaming Services.",
            RequestFailedException r => $"Couldn't reach Unity's online services ({r.Message}). Check your internet connection.",
            ServicesInitializationException => "Online play isn't set up yet: link this project to Unity Gaming Services.",
            _ => $"Couldn't go online: {e.Message}",
        };
    }

    static bool IsLinked() => !string.IsNullOrEmpty(Application.cloudProjectId);
}
