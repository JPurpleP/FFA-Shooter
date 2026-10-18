using UnityEngine;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
#endif

/// <summary>
/// The game was renamed (FFA Shooter → Fluketown DM). Windows keeps PlayerPrefs under the product name, so on the
/// first launch of the renamed build the old settings (name, skin, sensitivity, keys, career stats...) are copied
/// across before anything reads them.
/// </summary>
public static class PrefsMigration
{
    const string OldProduct = "FFA Shooter";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Run()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        if (Application.productName == OldProduct) return;
        string newPath = $@"Software\{Application.companyName}\{Application.productName}";
        string oldPath = $@"Software\{Application.companyName}\{OldProduct}";
        if (RegOpenKeyExW(HKCU, newPath, 0, KeyRead, out var existing) == 0)
        {
            // Already has settings of its own (not the first launch): leave them alone.
            bool hasValues = RegQueryInfoKeyW(existing, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                                              out int values, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) == 0 && values > 0;
            RegCloseKey(existing);
            if (hasValues) return;
        }
        if (RegOpenKeyExW(HKCU, oldPath, 0, KeyRead, out var from) != 0) return; // never played under the old name
        if (RegCreateKeyExW(HKCU, newPath, 0, null, 0, KeyAllAccess, IntPtr.Zero, out var to, out _) == 0)
        {
            int result = RegCopyTreeW(from, null, to);
            RegCloseKey(to);
            Debug.Log(result == 0 ? "[FFA] Settings carried over from FFA Shooter" : $"[FFA] Couldn't carry settings over ({result})");
        }
        RegCloseKey(from);
#endif
    }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    static readonly UIntPtr HKCU = (UIntPtr)0x80000001u;
    const int KeyRead = 0x20019, KeyAllAccess = 0xF003F;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    static extern int RegOpenKeyExW(UIntPtr hKey, string subKey, int options, int samDesired, out UIntPtr result);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    static extern int RegCreateKeyExW(UIntPtr hKey, string subKey, int reserved, string cls, int options, int samDesired,
                                      IntPtr security, out UIntPtr result, out int disposition);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    static extern int RegCopyTreeW(UIntPtr src, string subKey, UIntPtr dest);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    static extern int RegQueryInfoKeyW(UIntPtr hKey, IntPtr cls, IntPtr clsLen, IntPtr reserved, IntPtr subKeys, IntPtr maxSubKeyLen,
                                       IntPtr maxClassLen, out int values, IntPtr maxValueNameLen, IntPtr maxValueLen,
                                       IntPtr securityDescriptor, IntPtr lastWriteTime);

    [DllImport("advapi32.dll")]
    static extern int RegCloseKey(UIntPtr hKey);
#endif
}
