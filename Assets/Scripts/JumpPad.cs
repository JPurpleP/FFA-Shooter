using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Launch pad placed by the map builder: step on it and you're thrown along `launch` (m/s). The owner's movement code
/// checks <see cref="All"/> each frame; flight keeps its horizontal speed until you land.
/// </summary>
public class JumpPad : MonoBehaviour
{
    public static readonly List<JumpPad> All = new();

    public Vector3 launch = new(0f, 16f, 0f);
    public float radius = 1.3f;
    public Light glow;

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    public bool Touches(Vector3 feet)
    {
        Vector3 d = feet - transform.position;
        return d.y > -0.4f && d.y < 0.9f && d.x * d.x + d.z * d.z < radius * radius;
    }

    void Update()
    {
        if (glow != null) glow.intensity = 1.6f + Mathf.Sin(Time.time * 4f + transform.position.x) * 0.6f;
    }
}
