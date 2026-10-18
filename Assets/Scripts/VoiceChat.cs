using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Proximity voice chat. The local microphone is captured at 16 kHz, packed 4:1 with IMA ADPCM (64 kbit/s while talking,
/// nothing when silent) and sent to the host, which forwards it only to players within <see cref="Range"/> (everyone, while
/// in the lobby). Listeners hear it from the speaker's head through a 3D AudioSource, so it fades with distance.
/// </summary>
public class VoiceChat : MonoBehaviour
{
    public const int Rate = 16000;
    const int Frame = 960;                       // 60 ms per packet
    const int Payload = 4 + Frame / 2;           // predictor, step index, 4-bit samples
    public const float Range = 40f, FullVolume = 4f;
    const string Msg = "ffa.voice";

    public static VoiceChat I { get; private set; }
    public static bool Transmitting { get; private set; }
    public static float MicLevel { get; private set; }
    public static string MicError { get; private set; }
    /// <summary>Players muted this session (by client id).</summary>
    public static readonly HashSet<ulong> Muted = new();
    static readonly Dictionary<ulong, float> lastHeard = new();
    public static bool IsTalking(ulong clientId) => lastHeard.TryGetValue(clientId, out float t) && Time.unscaledTime - t < 0.35f;

    // Test hook: -ffavoicetest sends a tone instead of the microphone (headless machines have none).
    static bool testTone;

    string device;
    AudioClip mic;
    int readPos;
    float[] chunk = new float[Rate];
    readonly List<float> pending = new();
    readonly float[] frame = new float[Frame];
    readonly byte[] packet = new byte[Payload];
    float gateUntil, tonePhase, toneAccum;
    CustomMessagingManager registered;
    readonly Dictionary<ulong, VoiceSpeaker> speakers = new();
    readonly Dictionary<ulong, (float windowStart, int count)> serverRate = new();

    public static void Ensure()
    {
        if (I != null) return;
        var go = new GameObject("Voice Chat");
        DontDestroyOnLoad(go);
        I = go.AddComponent<VoiceChat>();
        testTone = System.Array.IndexOf(CommandLine.Args, "-ffavoicetest") >= 0;
    }

    static NetworkManager Net => NetworkManager.Singleton;

    static string[] devices = new string[0];
    static float devicesAt = -100f;
    /// <summary>Microphone names, cached (enumerating them takes milliseconds on Windows).</summary>
    public static string[] Devices
    {
        get
        {
            if (Time.unscaledTime - devicesAt > 3f)
            {
                devicesAt = Time.unscaledTime;
#if UNITY_WEBGL
                devices = new string[0];
#else
                devices = Microphone.devices;
#endif
            }
            return devices;
        }
    }

    void Update()
    {
        var net = Net;
        bool online = net != null && net.IsListening && net.CustomMessagingManager != null;
        if (online && registered != net.CustomMessagingManager)
        {
            registered = net.CustomMessagingManager;
            registered.RegisterNamedMessageHandler(Msg, OnMessage);
        }
        if (!online) registered = null;

        bool wantMic = online && GameSettings.VoiceMode != 0 && !testTone && !Application.isBatchMode;
        if (wantMic) StartMic();
        else if (openedFor != null) { StopMic(); openedFor = null; }

        Capture(online);
    }

    void OnDisable() => StopMic();

    // ------------------------------------------------------------------ capture

    /// <summary>The device setting the running mic was opened for ("" = system default).</summary>
    string openedFor;
    float nextMicCheck;

    void StartMic()
    {
        // The Microphone API is slow on Windows (it enumerates devices), so only look at it now and then.
        string want = GameSettings.MicDevice ?? "";
        if (mic != null && openedFor == want && Time.unscaledTime < nextMicCheck) return;
        if (mic == null && openedFor == want && Time.unscaledTime < nextMicCheck) return; // failed recently: retry later
        nextMicCheck = Time.unscaledTime + 3f;
#if UNITY_WEBGL
        MicError = "Voice chat isn't available in the browser version";
        return;
#else
        if (mic != null && openedFor == want && Microphone.IsRecording(device)) return;
        StopMic();
        openedFor = want;
        var devices = Devices;
        if (devices.Length == 0) { MicError = "No microphone found"; return; }
        if (want.Length > 0 && System.Array.IndexOf(devices, want) < 0) want = "";
        device = want.Length == 0 ? null : want;
        mic = Microphone.Start(device, true, 1, Rate);
#endif
        readPos = 0;
        pending.Clear();
        MicError = mic == null ? "Couldn't open the microphone" : null;
    }

    void StopMic()
    {
        if (mic == null) return;
#if !UNITY_WEBGL
        Microphone.End(device);
#endif
        Destroy(mic);
        mic = null;
        pending.Clear();
        MicLevel = 0f;
    }

    void Capture(bool online)
    {
        if (testTone && online)
        {
            // 440 Hz tone, half a second on / half off.
            toneAccum += Time.unscaledDeltaTime * Rate;
            int n = Mathf.Min((int)toneAccum, Rate);
            toneAccum -= n;
            for (int i = 0; i < n; i++)
            {
                tonePhase += 1f / Rate;
                pending.Add((tonePhase % 1f) < 0.5f ? 0.3f * Mathf.Sin(tonePhase * 440f * 2f * Mathf.PI) : 0f);
            }
        }
        else if (mic != null)
        {
#if UNITY_WEBGL
            int pos = 0;
#else
            int pos = Microphone.GetPosition(device);
#endif
            int len = mic.samples;
            int count = (pos - readPos + len) % len;
            if (count > 0)
            {
                int first = Mathf.Min(count, len - readPos);
                ReadMic(readPos, first);
                if (count > first) ReadMic(0, count - first);
                readPos = pos;
            }
        }
        else
        {
            Transmitting = false;
            return;
        }

        bool ptt = GameSettings.VoiceMode == 1 && (Bindings.Held(GameAction.PushToTalk) || GamepadInput.TalkHeld) && !GameUI.ChatTyping;
        while (pending.Count >= Frame)
        {
            pending.CopyTo(0, frame, 0, Frame);
            pending.RemoveRange(0, Frame);
            float sum = 0f;
            for (int i = 0; i < Frame; i++) sum += frame[i] * frame[i];
            float rms = Mathf.Sqrt(sum / Frame);
            MicLevel = Mathf.Max(rms * 6f, MicLevel * 0.6f);
            bool open = GameSettings.VoiceMode == 2 || testTone;
            if (open && rms > GameSettings.VoiceGate) gateUntil = Time.unscaledTime + 0.4f;
            bool send = testTone ? rms > 0.01f || Time.unscaledTime < gateUntil : open ? Time.unscaledTime < gateUntil : ptt;
            Transmitting = send && online && FpsPlayer.Local != null;
            if (Transmitting) Send();
        }
        if (pending.Count > Rate) pending.RemoveRange(0, pending.Count - Frame); // fell behind (hitch): drop old audio
        MicLevel *= 0.97f;
    }

    void ReadMic(int offset, int count)
    {
        if (chunk.Length < count) chunk = new float[count];
        if (!mic.GetData(chunk, offset)) return;
        for (int i = 0; i < count; i++) pending.Add(Mathf.Clamp(chunk[i] * GameSettings.MicGain, -1f, 1f));
    }

    void Send()
    {
        Adpcm.Encode(frame, packet);
        var net = Net;
        if (net.IsServer)
        {
            ServerRelay(net.LocalClientId, packet);
            return;
        }
        var w = new FastBufferWriter(Payload + 8, Allocator.Temp);
        using (w)
        {
            w.WriteBytesSafe(packet, Payload);
            net.CustomMessagingManager.SendNamedMessage(Msg, NetworkManager.ServerClientId, w, NetworkDelivery.UnreliableSequenced);
        }
    }

    // ------------------------------------------------------------------ network

    void OnMessage(ulong sender, FastBufferReader reader)
    {
        var net = Net;
        if (net.IsServer)
        {
            if (reader.Length - reader.Position < Payload) return;
            var data = new byte[Payload];
            reader.ReadBytesSafe(ref data, Payload);
            // Rate limit: a talking client sends ~17 packets a second.
            float now = Time.unscaledTime;
            serverRate.TryGetValue(sender, out var r);
            if (now - r.windowStart > 1f) r = (now, 0);
            r.count++;
            serverRate[sender] = r;
            if (r.count > 30) return;
            ServerRelay(sender, data);
        }
        else
        {
            if (reader.Length - reader.Position < 8 + Payload) return;
            reader.ReadValueSafe(out ulong speaker);
            var data = new byte[Payload];
            reader.ReadBytesSafe(ref data, Payload);
            Play(speaker, data);
        }
    }

    /// <summary>Host: pass a speaker's packet on to everyone close enough to hear it.</summary>
    void ServerRelay(ulong speakerId, byte[] data)
    {
        var net = Net;
        var speaker = Find(speakerId);
        if (speaker == null) return;
        foreach (var listener in FpsPlayer.All)
        {
            if (listener == null || !listener.IsSpawned || listener.IsBot.Value || listener.OwnerClientId == speakerId) continue;
            bool lobby = speaker.InLobby.Value || listener.InLobby.Value;
            if (!lobby && (listener.transform.position - speaker.transform.position).sqrMagnitude > (Range + 5f) * (Range + 5f)) continue;
            if (listener.OwnerClientId == net.LocalClientId)
            {
                Play(speakerId, data);
                continue;
            }
            var w = new FastBufferWriter(Payload + 16, Allocator.Temp);
            using (w)
            {
                w.WriteValueSafe(speakerId);
                w.WriteBytesSafe(data, Payload);
                net.CustomMessagingManager.SendNamedMessage(Msg, listener.OwnerClientId, w, NetworkDelivery.UnreliableSequenced);
            }
        }
    }

    static FpsPlayer Find(ulong clientId)
    {
        foreach (var p in FpsPlayer.All)
            if (p != null && p.IsSpawned && !p.IsBot.Value && p.OwnerClientId == clientId) return p;
        return null;
    }

    void Play(ulong speakerId, byte[] data)
    {
        if (GameSettings.VoiceMode == 0 && !testTone || Muted.Contains(speakerId)) return;
        var player = Find(speakerId);
        if (player == null) return;
        lastHeard[speakerId] = Time.unscaledTime;
        if (!speakers.TryGetValue(speakerId, out var s) || s == null)
        {
            var go = new GameObject("Voice");
            go.transform.SetParent(player.head != null ? player.head : player.transform, false);
            s = go.AddComponent<VoiceSpeaker>();
            speakers[speakerId] = s;
            Debug.Log($"[FFA] Voice from {player.DisplayName}");
        }
        s.Push(data, player.InLobby.Value || FpsPlayer.Local == null || FpsPlayer.Local.InLobby.Value);
        if (testTone && s.Received % 50 == 0) Debug.Log($"[FFA] Voice packets from {player.DisplayName}: {s.Received}");
    }
}

/// <summary>Plays one player's incoming voice through a streamed 3D AudioSource, with a small jitter buffer.</summary>
public class VoiceSpeaker : MonoBehaviour
{
    AudioSource source;
    readonly float[] ring = new float[VoiceChat.Rate * 2];
    int head, tail, buffered;
    bool primed;
    readonly object gate = new();
    readonly float[] decoded = new float[960];
    public int Received { get; private set; }

    void Awake()
    {
        source = gameObject.AddComponent<AudioSource>();
        source.clip = AudioClip.Create("voice", VoiceChat.Rate * 2, 1, VoiceChat.Rate, true, OnRead);
        source.loop = true;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = VoiceChat.FullVolume;
        source.maxDistance = VoiceChat.Range;
        source.dopplerLevel = 0f;
        source.Play();
    }

    void Update() => source.volume = GameSettings.VoiceVolume;

    public void Push(byte[] packet, bool flat)
    {
        source.spatialBlend = flat ? 0f : 1f;
        Adpcm.Decode(packet, decoded);
        Received++;
        lock (gate)
        {
            foreach (float v in decoded)
            {
                ring[tail] = v;
                tail = (tail + 1) % ring.Length;
                if (buffered < ring.Length) buffered++;
                else head = (head + 1) % ring.Length;
            }
            // Too far behind (stalls): skip ahead so the voice stays live.
            while (buffered > VoiceChat.Rate / 2) { head = (head + 1) % ring.Length; buffered--; }
            if (buffered >= VoiceChat.Rate / 8) primed = true; // 125 ms of headroom before playing
        }
    }

    void OnRead(float[] data)
    {
        lock (gate)
        {
            for (int i = 0; i < data.Length; i++)
            {
                if (primed && buffered > 0)
                {
                    data[i] = ring[head];
                    head = (head + 1) % ring.Length;
                    buffered--;
                }
                else
                {
                    data[i] = 0f;
                    primed = false;
                }
            }
        }
    }
}

/// <summary>IMA ADPCM, 4 bits a sample. Each packet carries its own starting state so a lost packet doesn't break the next.</summary>
static class Adpcm
{
    static readonly int[] Steps =
    {
        7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118,
        130, 143, 157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876, 963, 1060,
        1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484,
        7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767,
    };
    static readonly int[] IndexTable = { -1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8 };

    static int encPredictor, encIndex;

    public static void Encode(float[] input, byte[] output)
    {
        int predictor = encPredictor, index = encIndex;
        output[0] = (byte)(predictor & 0xFF);
        output[1] = (byte)((predictor >> 8) & 0xFF);
        output[2] = (byte)index;
        output[3] = 0;
        for (int i = 0; i < input.Length; i++)
        {
            int sample = Mathf.Clamp((int)(input[i] * 32767f), -32768, 32767);
            int step = Steps[index], diff = sample - predictor, code = 0;
            if (diff < 0) { code = 8; diff = -diff; }
            int delta = step >> 3;
            if (diff >= step) { code |= 4; diff -= step; delta += step; }
            step >>= 1;
            if (diff >= step) { code |= 2; diff -= step; delta += step; }
            step >>= 1;
            if (diff >= step) { code |= 1; delta += step; }
            predictor = Mathf.Clamp((code & 8) != 0 ? predictor - delta : predictor + delta, -32768, 32767);
            index = Mathf.Clamp(index + IndexTable[code], 0, 88);
            int b = 4 + i / 2;
            if ((i & 1) == 0) output[b] = (byte)code;
            else output[b] |= (byte)(code << 4);
        }
        encPredictor = predictor;
        encIndex = index;
    }

    public static void Decode(byte[] input, float[] output)
    {
        int predictor = (short)(input[0] | (input[1] << 8));
        int index = Mathf.Clamp(input[2], 0, 88);
        for (int i = 0; i < output.Length; i++)
        {
            int code = (input[4 + i / 2] >> ((i & 1) * 4)) & 0xF;
            int step = Steps[index];
            int delta = step >> 3;
            if ((code & 4) != 0) delta += step;
            if ((code & 2) != 0) delta += step >> 1;
            if ((code & 1) != 0) delta += step >> 2;
            predictor = Mathf.Clamp((code & 8) != 0 ? predictor - delta : predictor + delta, -32768, 32767);
            index = Mathf.Clamp(index + IndexTable[code], 0, 88);
            output[i] = predictor / 32768f;
        }
    }
}
