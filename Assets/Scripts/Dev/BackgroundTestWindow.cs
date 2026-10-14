using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// -ffabackground: keeps an automated test window out of the way of whoever is using the PC. The window never takes
/// keyboard/mouse focus (WS_EX_NOACTIVATE), and if Windows activated it on launch, focus is handed straight back to
/// the window given by -ffareturnfocus &lt;hwnd&gt; (the one that was in front before the test started). The game also
/// never locks or reads the mouse in this mode (see GameUI.SetCursorCaptured / ReadHumanInput).
/// </summary>
public class BackgroundTestWindow : MonoBehaviour
{
    public static bool Active { get; private set; }

    void Awake()
    {
        var args = Environment.GetCommandLineArgs();
        Active = Array.IndexOf(args, "-ffabackground") >= 0;
        if (!Active) return;
        Application.runInBackground = true;
        long ret = 0;
        int i = Array.IndexOf(args, "-ffareturnfocus");
        if (i >= 0 && i + 1 < args.Length) long.TryParse(args[i + 1], out ret);
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        StartCoroutine(StayInBackground(new IntPtr(ret)));
#endif
    }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    const int GWL_EXSTYLE = -20;
    const long WS_EX_NOACTIVATE = 0x08000000L;

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    static IntPtr OwnWindow()
    {
        uint me = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == me && IsWindowVisible(h)) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    IEnumerator StayInBackground(IntPtr returnTo)
    {
        // Watch the first few seconds after launch (window creation, first frames, going windowed on monitor 2).
        float until = Time.realtimeSinceStartup + 6f;
        bool styled = false;
        while (Time.realtimeSinceStartup < until)
        {
            var own = OwnWindow();
            if (own != IntPtr.Zero)
            {
                if (!styled)
                {
                    long ex = GetWindowLongPtr(own, GWL_EXSTYLE).ToInt64();
                    SetWindowLongPtr(own, GWL_EXSTYLE, new IntPtr(ex | WS_EX_NOACTIVATE));
                    styled = true;
                }
                // We're in front only because Windows put us there; we may hand focus back (allowed while foreground).
                if (GetForegroundWindow() == own && returnTo != IntPtr.Zero && IsWindow(returnTo))
                    SetForegroundWindow(returnTo);
            }
            yield return null;
        }
    }
#endif
}
