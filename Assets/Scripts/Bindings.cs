using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Things the player can rebind. The mouse buttons (fire, aim, hook, wheel) are fixed.</summary>
public enum GameAction
{
    Forward, Back, Left, Right, Sprint, Jump, Crouch, Prone, LeanLeft, LeanRight,
    Reload, Interact, Grapple, Grenade, Slot1, Slot2, Slot3, Scoreboard,
}

/// <summary>Keyboard bindings for every <see cref="GameAction"/>, saved between launches.</summary>
public static class Bindings
{
    public static readonly GameAction[] All = (GameAction[])System.Enum.GetValues(typeof(GameAction));

    static readonly Key[] Defaults =
    {
        Key.W, Key.S, Key.A, Key.D, Key.LeftShift, Key.Space, Key.LeftCtrl, Key.Z, Key.Q, Key.E,
        Key.R, Key.F, Key.G, Key.T, Key.Digit1, Key.Digit2, Key.Digit3, Key.Tab,
    };

    static Key[] keys;

    static Key[] Keys
    {
        get
        {
            if (keys == null) Load();
            return keys;
        }
    }

    public static string Name(GameAction a) => a switch
    {
        GameAction.Forward => "Move forward",
        GameAction.Back => "Move back",
        GameAction.Left => "Move left",
        GameAction.Right => "Move right",
        GameAction.Sprint => "Sprint",
        GameAction.Jump => "Jump / fly up",
        GameAction.Crouch => "Crouch / slide / fly down",
        GameAction.Prone => "Prone",
        GameAction.LeanLeft => "Lean left",
        GameAction.LeanRight => "Lean right",
        GameAction.Reload => "Reload",
        GameAction.Interact => "Pick up / swap",
        GameAction.Grapple => "Grappling hook",
        GameAction.Grenade => "Throw grenade (hold)",
        GameAction.Slot1 => "Weapon 1",
        GameAction.Slot2 => "Weapon 2",
        GameAction.Slot3 => "Grenades (again: next type)",
        GameAction.Scoreboard => "Scoreboard",
        _ => a.ToString(),
    };

    public static Key Get(GameAction a) => Keys[(int)a];

    public static bool Held(GameAction a)
    {
        var kb = Keyboard.current;
        var k = Get(a);
        return kb != null && k != Key.None && kb[k].isPressed;
    }

    public static bool Pressed(GameAction a)
    {
        var kb = Keyboard.current;
        var k = Get(a);
        return kb != null && k != Key.None && kb[k].wasPressedThisFrame;
    }

    /// <summary>What to show on the key's button (the keyboard layout's own name for it).</summary>
    public static string Label(GameAction a)
    {
        var k = Get(a);
        if (k == Key.None) return "—";
        var kb = Keyboard.current;
        string n = kb != null ? kb[k].displayName : k.ToString();
        return string.IsNullOrEmpty(n) ? k.ToString() : n.ToUpperInvariant();
    }

    /// <summary>Bind `a` to `key`. If another action had that key, it gets `a`'s old key (a swap).</summary>
    public static void Rebind(GameAction a, Key key)
    {
        var ks = Keys;
        Key old = ks[(int)a];
        for (int i = 0; i < ks.Length; i++)
            if (i != (int)a && ks[i] == key) ks[i] = old;
        ks[(int)a] = key;
        Save();
    }

    public static void ResetDefaults()
    {
        keys = (Key[])Defaults.Clone();
        Save();
    }

    static void Load()
    {
        keys = (Key[])Defaults.Clone();
        for (int i = 0; i < keys.Length; i++)
        {
            int saved = PlayerPrefs.GetInt("ffa.key." + All[i], -1);
            if (saved > 0 && System.Enum.IsDefined(typeof(Key), saved)) keys[i] = (Key)saved;
        }
    }

    static void Save()
    {
        for (int i = 0; i < keys.Length; i++) PlayerPrefs.SetInt("ffa.key." + All[i], (int)keys[i]);
        PlayerPrefs.Save();
    }
}
