using UnityEngine;

/// <summary>Extra bot waypoint for places the automatic grid misses (stairs, doorways).</summary>
public class NavHint : MonoBehaviour
{
    void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.2f, 0.2f);
    }
}
