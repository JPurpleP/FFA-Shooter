using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

/// <summary>
/// Public game list (Unity Lobby). A public host advertises a lobby holding its Relay join code, map, mode and player
/// count, and keeps it alive with heartbeats; Find Game lists them and joins through the code as usual.
/// Nobody actually joins the Lobby itself: it is just the listing.
/// </summary>
public static class LobbyBrowser
{
    public struct Entry
    {
        public string Name, Code, Map, Mode, State;
        public int Players;
    }

    public const int MaxPlayers = 16;
    static string lobbyId;
    static float nextHeartbeat, nextUpdate;
    static string lastSignature = "";
    static bool busy;

    public static bool Listed => lobbyId != null;

    /// <summary>Host: start advertising this game.</summary>
    public static async void Publish(string code, string hostName)
    {
        try
        {
            await RelayConnect.EnsureSignedIn();
            var lobby = await LobbyService.Instance.CreateLobbyAsync($"{hostName}'s game", MaxPlayers,
                new CreateLobbyOptions { IsPrivate = false, Data = BuildData(code) });
            lobbyId = lobby.Id;
            nextHeartbeat = Time.unscaledTime + 15f;
            nextUpdate = Time.unscaledTime + 5f;
            Debug.Log($"[FFA] Listed publicly as \"{lobby.Name}\"");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FFA] Couldn't list the game publicly: {e.Message}");
        }
    }

    /// <summary>Host: stop advertising (game ended / quit).</summary>
    public static async void Unpublish()
    {
        if (lobbyId == null) return;
        string id = lobbyId;
        lobbyId = null;
        try { await LobbyService.Instance.DeleteLobbyAsync(id); }
        catch (Exception e) { Debug.LogWarning($"[FFA] Lobby delete: {e.Message}"); }
    }

    /// <summary>Host, every frame: heartbeat (the service drops lobbies after 30 s without one) and refresh the details.</summary>
    public static async void Tick()
    {
        if (lobbyId == null || busy) return;
        try
        {
            if (Time.unscaledTime >= nextHeartbeat)
            {
                busy = true;
                nextHeartbeat = Time.unscaledTime + 15f;
                await LobbyService.Instance.SendHeartbeatPingAsync(lobbyId);
            }
            else if (Time.unscaledTime >= nextUpdate)
            {
                nextUpdate = Time.unscaledTime + 8f;
                var data = BuildData(RelayConnect.Code);
                string sig = string.Join("|", data.Values.Select(v => v.Value));
                if (sig != lastSignature && lobbyId != null)
                {
                    busy = true;
                    lastSignature = sig;
                    await LobbyService.Instance.UpdateLobbyAsync(lobbyId, new UpdateLobbyOptions { Data = data });
                }
            }
        }
        catch (Exception e) { Debug.LogWarning($"[FFA] Lobby update: {e.Message}"); }
        finally { busy = false; }
    }

    static Dictionary<string, DataObject> BuildData(string code)
    {
        int humans = 0;
        foreach (var p in FpsPlayer.All) if (p.IsSpawned && !p.IsBot.Value) humans++;
        string state = MatchRules.LobbyOpen ? "In lobby" : "Playing";
        return new Dictionary<string, DataObject>
        {
            ["code"] = new(DataObject.VisibilityOptions.Public, code),
            ["map"] = new(DataObject.VisibilityOptions.Public, MapCatalog.NameOf(MapCatalog.Current)),
            ["mode"] = new(DataObject.VisibilityOptions.Public, MatchRules.ModeName(MatchRules.Mode)),
            ["players"] = new(DataObject.VisibilityOptions.Public, Mathf.Max(1, humans).ToString()),
            ["state"] = new(DataObject.VisibilityOptions.Public, state),
            ["ver"] = new(DataObject.VisibilityOptions.Public, Application.version),
        };
    }

    /// <summary>Public games running this same version, newest first.</summary>
    public static async Task<List<Entry>> Query()
    {
        await RelayConnect.EnsureSignedIn();
        var response = await LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions
        {
            Count = 40,
            Order = new List<QueryOrder> { new(false, QueryOrder.FieldOptions.Created) },
        });
        var list = new List<Entry>();
        foreach (var l in response.Results)
        {
            if (l.Data == null || !l.Data.TryGetValue("code", out var code) || string.IsNullOrEmpty(code.Value)) continue;
            string Get(string k) => l.Data.TryGetValue(k, out var d) ? d.Value : "";
            if (Get("ver") != Application.version) continue;
            list.Add(new Entry
            {
                Name = NameFilter.Clean(l.Name), Code = code.Value, Map = Get("map"), Mode = Get("mode"), State = Get("state"),
                Players = int.TryParse(Get("players"), out int n) ? n : 1,
            });
        }
        return list;
    }
}
