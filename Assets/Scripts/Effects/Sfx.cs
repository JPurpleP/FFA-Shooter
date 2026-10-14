using System.Collections.Generic;
using UnityEngine;

/// <summary>Procedurally synthesized sound effects (no audio files needed).</summary>
public static class Sfx
{
    const int Rate = 44100;
    static readonly Dictionary<string, AudioClip> cache = new();

    public static AudioClip Gunshot(WeaponId id)
    {
        return id switch
        {
            WeaponId.Glock17 => Get("glock", () => Shot(0.22f, 32f, 90f, 0.55f, 0.65f, 1)),
            WeaponId.MP5 => Get("mp5", () => Shot(0.18f, 38f, 110f, 0.45f, 0.6f, 2)),
            WeaponId.M4A1 => Get("m4", () => Shot(0.3f, 24f, 75f, 0.65f, 0.7f, 3)),
            WeaponId.Remington870 => Get("870", () => Shot(0.55f, 11f, 55f, 0.9f, 0.85f, 4)),
            WeaponId.BarrettM82 => Get("m82", () => Shot(1.1f, 5f, 38f, 1f, 1f, 5)),
            WeaponId.GoldenDeagle => Get("deagle", () => Shot(0.6f, 9f, 50f, 1f, 0.95f, 6)),
            WeaponId.M32 => Get("m32", () => Shot(0.45f, 14f, 60f, 1f, 0.6f, 10)),
            WeaponId.RPG7 => Get("rpg", () => Shot(1.3f, 3.5f, 32f, 1f, 1f, 11)),
            WeaponId.AA12 => Get("aa12", () => Shot(0.45f, 13f, 60f, 0.85f, 0.8f, 13)),
            WeaponId.Mosin => Get("mosin", () => Shot(0.9f, 7f, 45f, 1f, 0.95f, 14)),
            WeaponId.PPSh41 => Get("ppsh", () => Shot(0.16f, 40f, 120f, 0.4f, 0.55f, 15)),
            WeaponId.M249 => Get("m249", () => Shot(0.28f, 26f, 80f, 0.7f, 0.72f, 16)),
            WeaponId.Cattleman => Get("saa", () => Shot(0.7f, 9f, 55f, 1f, 0.9f, 17)),
            WeaponId.Magnum44 => Get("m29", () => Shot(0.75f, 8f, 50f, 1f, 1f, 18)),
            WeaponId.LeverAction => Get("lever", () => Shot(0.8f, 8f, 48f, 1f, 0.9f, 19)),
            WeaponId.M1911 => Get("1911", () => Shot(0.3f, 22f, 80f, 0.7f, 0.75f, 20)),
            WeaponId.GlockSwitch => Get("glock", () => Shot(0.22f, 32f, 90f, 0.55f, 0.65f, 1)),
            WeaponId.KrissVector => Get("vector", () => Shot(0.2f, 34f, 95f, 0.5f, 0.6f, 22)),
            WeaponId.MP18 => Get("mp18", () => Shot(0.95f, 7f, 44f, 1f, 0.95f, 23)),
            _ => Get("glock", () => Shot(0.22f, 32f, 90f, 0.55f, 0.65f, 1)),
        };
    }

    public static AudioClip Pump => Get("pump", () => Clicks(new[] { 0f, 0.16f }, 0.3f, 1800f));
    public static AudioClip Reload => Get("reload", () => Clicks(new[] { 0f, 0.09f }, 0.2f, 2600f));
    public static AudioClip ShellInsert => Get("shell", () => Clicks(new[] { 0f }, 0.12f, 1500f));
    public static AudioClip DryFire => Get("dry", () => Clicks(new[] { 0f }, 0.08f, 3500f));
    public static AudioClip Pickup => Get("pickup", () => Clicks(new[] { 0f, 0.07f, 0.14f }, 0.25f, 2200f));
    public static AudioClip HitTick => Get("tick", () => Tone(1900f, 0.05f, 60f));
    public static AudioClip Squelch => Get("squelch", () => Squish(0.6f, 7));
    public static AudioClip GrappleFire => Get("grapple", () => Zip(0.35f));
    public static AudioClip Slide => Get("slide", () => Scrape(0.9f));
    public static AudioClip Explosion => Get("explosion", () => Shot(2.6f, 1.8f, 26f, 1f, 1f, 12));
    public static AudioClip Swing => Get("swing", () => Whoosh(0.22f));
    public static AudioClip Punch => Get("punch", () => Shot(0.2f, 28f, 70f, 1f, 0.85f, 21));
    public static AudioClip Bell => Get("bell", () => Ding(1.6f));
    public static AudioClip Splatter => Get("splatter", () => Squish(1.1f, 9));

    static AudioClip Get(string key, System.Func<float[]> make)
    {
        if (cache.TryGetValue(key, out var clip) && clip != null)
            return clip;
        var data = make();
        clip = AudioClip.Create(key, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        cache[key] = clip;
        return clip;
    }

    /// <summary>Noise burst + low thump + optional tail; parameters shape each gun's character.</summary>
    static float[] Shot(float length, float decay, float thumpHz, float thumpAmount, float gain, int seed)
    {
        int n = (int)(Rate * length);
        var d = new float[n];
        var rng = new System.Random(seed);
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += (white - lp) * Mathf.Lerp(0.9f, 0.08f, Mathf.Clamp01(t * 6f)); // darker as it decays
            float crack = white * Mathf.Exp(-t * decay * 3f);
            float body = lp * Mathf.Exp(-t * decay);
            float thump = Mathf.Sin(2f * Mathf.PI * thumpHz * t * (1f - t * 0.6f)) * Mathf.Exp(-t * decay * 1.3f) * thumpAmount;
            d[i] = Mathf.Clamp((crack * 0.5f + body * 0.9f + thump) * gain, -1f, 1f);
        }
        return d;
    }

    static float[] Clicks(float[] times, float length, float hz)
    {
        int n = (int)(Rate * length);
        var d = new float[n];
        var rng = new System.Random(11);
        foreach (float start in times)
        {
            int s = (int)(start * Rate);
            for (int i = s; i < n; i++)
            {
                float t = (float)(i - s) / Rate;
                float env = Mathf.Exp(-t * 90f);
                d[i] += (Mathf.Sin(2f * Mathf.PI * hz * t) * 0.6f + (float)(rng.NextDouble() * 2 - 1) * 0.4f) * env * 0.5f;
            }
        }
        return d;
    }

    static float[] Tone(float hz, float length, float decay)
    {
        int n = (int)(Rate * length);
        var d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            d[i] = Mathf.Sin(2f * Mathf.PI * hz * t) * Mathf.Exp(-t * decay) * 0.4f;
        }
        return d;
    }

    /// <summary>Gritty scrape that fades out (sliding on pavement / grass).</summary>
    static float[] Scrape(float length)
    {
        int n = (int)(Rate * length);
        var d = new float[n];
        var rng = new System.Random(31);
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += (white - lp) * 0.35f;
            float grit = rng.NextDouble() < 0.02 ? (float)(rng.NextDouble() - 0.5) * 1.5f : 0f;
            float env = Mathf.Min(1f, t * 30f) * Mathf.Exp(-t * 2.5f);
            d[i] = Mathf.Clamp((lp * 0.6f + grit * 0.3f) * env, -1f, 1f);
        }
        return d;
    }

    /// <summary>Rising whoosh followed by a metallic clank (hook launch and bite).</summary>
    static float[] Zip(float length)
    {
        int n = (int)(Rate * length);
        var d = new float[n];
        var rng = new System.Random(23);
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += (white - lp) * Mathf.Lerp(0.05f, 0.6f, t / length);
            float whoosh = lp * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.2f)) * 0.8f;
            float ct = t - 0.13f;
            float clank = ct > 0f ? (Mathf.Sin(2f * Mathf.PI * 1250f * ct) + Mathf.Sin(2f * Mathf.PI * 1870f * ct) * 0.6f) * Mathf.Exp(-ct * 35f) * 0.35f : 0f;
            d[i] = Mathf.Clamp(whoosh + clank, -1f, 1f);
        }
        return d;
    }

    /// <summary>Short band-passed air whoosh (a swung fist).</summary>
    static float[] Whoosh(float length)
    {
        int n = (int)(Rate * length);
        var d = new float[n];
        var rng = new System.Random(41);
        float lp = 0f, lp2 = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += (white - lp) * Mathf.Lerp(0.08f, 0.4f, t / length);
            lp2 += (lp - lp2) * 0.5f;
            d[i] = Mathf.Clamp((lp - lp2) * 3f * Mathf.Sin(Mathf.PI * t / length), -1f, 1f);
        }
        return d;
    }

    /// <summary>Boxing-ring bell: a few inharmonic partials ringing out.</summary>
    static float[] Ding(float length)
    {
        int n = (int)(Rate * length);
        var d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float s = Mathf.Sin(2f * Mathf.PI * 880f * t) + 0.6f * Mathf.Sin(2f * Mathf.PI * 2390f * t) * Mathf.Exp(-t * 3f) + 0.4f * Mathf.Sin(2f * Mathf.PI * 1440f * t);
            d[i] = s * Mathf.Exp(-t * 2.2f) * 0.3f;
        }
        return d;
    }

    /// <summary>Wet, gloopy noise: low-passed noise with a wobbling cutoff.</summary>
    static float[] Squish(float length, int seed)
    {
        int n = (int)(Rate * length);
        var d = new float[n];
        var rng = new System.Random(seed);
        float lp = 0f, lp2 = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            float wobble = 0.05f + 0.25f * Mathf.Abs(Mathf.Sin(t * 38f + Mathf.Sin(t * 9f) * 3f));
            lp += (white - lp) * wobble;
            lp2 += (lp - lp2) * 0.3f;
            float env = Mathf.Exp(-t * 4f) * (1f - Mathf.Exp(-t * 200f));
            d[i] = Mathf.Clamp(lp2 * env * 2.2f, -1f, 1f);
        }
        return d;
    }
}
