using UnityEngine;

/// <summary>Billboard: keeps a quad turned toward whichever camera is rendering the local player's view.</summary>
public class FaceCamera : MonoBehaviour
{
    void LateUpdate()
    {
        var local = FpsPlayer.Local;
        Camera cam = local != null && local.playerCamera != null && local.playerCamera.enabled ? local.playerCamera : Camera.main;
        if (cam == null) return;
        transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
    }
}
