using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Controller support (Xbox / PlayStation / any gamepad the Input System knows). In game it feeds PlayerInput:
///   left stick move (click: sprint) · right stick look (click: melee) · RT fire · LT aim · A jump · B crouch / slide
///   X reload, or pick up when standing on a gun · Y next weapon · RB hold to throw · LB grappling hook
///   D-pad up grenades · D-pad down prone · D-pad right push to talk · View scoreboard · Menu pause.
/// In menus the left stick moves the mouse pointer, A clicks, B backs out and the right stick scrolls.
/// Aim assist slows the look speed while the crosshair is on an enemy (controller only).
/// </summary>
public static class GamepadInput
{
    /// <summary>The last input came from a controller (prompts show controller buttons).</summary>
    public static bool UsingGamepad { get; private set; }
    static Gamepad Pad => Gamepad.current;
    static bool sprintLatch;
    static float scrollAccum;

    public static bool ScoreboardHeld => Pad != null && Pad.selectButton.isPressed;
    public static bool PausePressed => Pad != null && Pad.startButton.wasPressedThisFrame;
    public static bool TalkHeld => Pad != null && Pad.dpad.right.isPressed;

    /// <summary>Once a frame (GamepadDriver): who's playing, and the menu pointer.</summary>
    public static void Tick()
    {
        var pad = Pad;
        if (pad != null)
        {
            bool used = pad.leftStick.ReadValue().sqrMagnitude > 0.09f || pad.rightStick.ReadValue().sqrMagnitude > 0.09f
                        || pad.leftTrigger.ReadValue() > 0.3f || pad.rightTrigger.ReadValue() > 0.3f;
            if (!used)
                foreach (var c in pad.allControls)
                    if (c is ButtonControl b && b.wasPressedThisFrame) { used = true; break; }
            if (used) UsingGamepad = true;
        }
        var mouse = Mouse.current;
        var kb = Keyboard.current;
        if ((mouse != null && (mouse.delta.ReadValue().sqrMagnitude > 4f || mouse.leftButton.wasPressedThisFrame)) || (kb != null && kb.anyKey.wasPressedThisFrame))
            if (!synthetic) UsingGamepad = false;
        synthetic = false;

#if !UNITY_WEBGL
        if (pad != null && !GameUI.CursorCaptured && Application.isFocused) DriveMenuPointer(pad);
#endif
    }

    // ------------------------------------------------------------------ in game

    public static void AddTo(ref PlayerInput input, FpsPlayer me)
    {
        var pad = Pad;
        if (pad == null) return;
        Vector2 move = pad.leftStick.ReadValue();
        input.Move = Vector2.ClampMagnitude(input.Move + move, 1f);
        if (pad.leftStickButton.wasPressedThisFrame) sprintLatch = !sprintLatch;
        if (move.sqrMagnitude < 0.04f || move.y < 0.2f) sprintLatch = false; // sprint lasts until you stop pushing forward
        input.Sprint |= sprintLatch;

        // Look: response curve for fine aim near the centre, degrees per second independent of mouse sensitivity.
        Vector2 look = pad.rightStick.ReadValue();
        look = look.normalized * Mathf.Pow(look.magnitude, 1.8f);
        float speed = 260f * GameSettings.PadSensitivity * Time.deltaTime / Mathf.Max(0.001f, GameSettings.Sensitivity);
        if (GameSettings.AimAssist && OnTarget(me)) speed *= 0.5f;
        input.Look += look * speed;

        input.FireHeld |= pad.rightTrigger.ReadValue() > 0.4f;
        input.FirePressed |= pad.rightTrigger.wasPressedThisFrame;
        input.Aim |= pad.leftTrigger.ReadValue() > 0.4f;
        input.Jump |= pad.buttonSouth.wasPressedThisFrame;
        input.JumpHeld |= pad.buttonSouth.isPressed;
        input.CrouchDown |= pad.buttonEast.wasPressedThisFrame;
        input.CrouchHeld |= pad.buttonEast.isPressed;
        input.ProneToggle |= pad.dpad.down.wasPressedThisFrame;
        if (pad.buttonWest.wasPressedThisFrame)
        {
            if (me.NearbyPickup != null) input.Interact = true;
            else input.Reload = true;
        }
        if (pad.buttonNorth.wasPressedThisFrame) input.SwitchTo = 3;
        if (pad.dpad.up.wasPressedThisFrame) input.SwitchTo = 2;
        input.GrenadeHeld |= pad.rightShoulder.isPressed;
        input.Grapple |= pad.leftShoulder.wasPressedThisFrame;
        input.Melee |= pad.rightStickButton.wasPressedThisFrame;
    }

    /// <summary>Is the crosshair on (or just beside) an enemy in sight?</summary>
    static bool OnTarget(FpsPlayer me)
    {
        var cam = me.playerCamera;
        if (cam == null) return false;
        Vector3 eye = cam.transform.position, fwd = cam.transform.forward;
        foreach (var p in FpsPlayer.All)
        {
            if (!me.IsEnemyOf(p) || p.Head == null) continue;
            Vector3 chest = p.Head.position - Vector3.up * 0.45f;
            Vector3 to = chest - eye;
            float d = to.magnitude;
            if (d > 90f || d < 0.5f) continue;
            float allowed = Mathf.Atan2(0.9f, d) * Mathf.Rad2Deg; // about a body-width either side
            if (Vector3.Angle(fwd, to) < allowed) return true;
        }
        return false;
    }

    /// <summary>Button names for prompts while playing on a controller.</summary>
    public static string Label(GameAction a) => a switch
    {
        GameAction.Jump => "A",
        GameAction.Crouch => "B",
        GameAction.Prone => "D-PAD ↓",
        GameAction.Reload => "X",
        GameAction.Interact => "X",
        GameAction.Grapple => "LB",
        GameAction.Grenade => "RB",
        GameAction.Slot3 => "D-PAD ↑",
        GameAction.Melee => "R3",
        GameAction.Sprint => "L3",
        GameAction.Scoreboard => "VIEW",
        GameAction.PushToTalk => "D-PAD →",
        _ => Bindings.Label(a),
    };

    // ------------------------------------------------------------------ menus

    static bool synthetic; // the click we just sent shouldn't count as "using the mouse"
    static bool aHeld;

    static void DriveMenuPointer(Gamepad pad)
    {
        var mouse = Mouse.current;
        if (mouse == null) return;
        Vector2 stick = pad.leftStick.ReadValue();
        if (stick.sqrMagnitude > 0.02f)
        {
            Vector2 pos = mouse.position.ReadValue();
            float speed = 1300f * Screen.height / 1080f * Mathf.Pow(stick.magnitude, 1.6f);
            pos += stick.normalized * speed * Time.unscaledDeltaTime;
            pos.x = Mathf.Clamp(pos.x, 0f, Screen.width - 1);
            pos.y = Mathf.Clamp(pos.y, 0f, Screen.height - 1);
            mouse.WarpCursorPosition(pos);
            synthetic = true;
        }
        if (pad.buttonSouth.wasPressedThisFrame && !aHeld) { aHeld = true; Click(true); }
        if (aHeld && !pad.buttonSouth.isPressed) { aHeld = false; Click(false); }
        if (pad.buttonEast.wasPressedThisFrame) PressEscape();
        float scroll = pad.rightStick.ReadValue().y;
        scrollAccum += scroll * 12f * Time.unscaledDeltaTime;
        if (Mathf.Abs(scrollAccum) >= 1f)
        {
            int notches = (int)scrollAccum;
            scrollAccum -= notches;
            Wheel(notches);
        }
        else if (Mathf.Abs(scroll) < 0.2f) scrollAccum = 0f;
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, int data, System.UIntPtr extra);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, System.UIntPtr extra);
    static void Click(bool down) { synthetic = true; mouse_event(down ? 0x0002u : 0x0004u, 0, 0, 0, System.UIntPtr.Zero); }
    static void Wheel(int notches) { synthetic = true; mouse_event(0x0800u, 0, 0, notches * 120, System.UIntPtr.Zero); }
    static void PressEscape()
    {
        synthetic = true;
        keybd_event(0x1B, 0, 0, System.UIntPtr.Zero);
        keybd_event(0x1B, 0, 0x0002, System.UIntPtr.Zero);
    }
#else
    static void Click(bool down) { }
    static void Wheel(int notches) { }
    static void PressEscape() { }
#endif
}

/// <summary>Runs <see cref="GamepadInput.Tick"/> every frame.</summary>
public class GamepadDriver : MonoBehaviour
{
    public static void Ensure()
    {
        if (FindAnyObjectByType<GamepadDriver>() != null) return;
        var go = new GameObject("Gamepad");
        DontDestroyOnLoad(go);
        go.AddComponent<GamepadDriver>();
    }

    void Update() => GamepadInput.Tick();
}
