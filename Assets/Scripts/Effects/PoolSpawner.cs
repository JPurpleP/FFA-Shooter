using UnityEngine;

/// <summary>Waits for the ragdoll to slow down, then spawns a growing blood pool under it.</summary>
public class PoolSpawner : MonoBehaviour
{
    public float size = 1f;
    float timer;

    void Update()
    {
        timer += Time.deltaTime;
        var rb = GetComponent<Rigidbody>();
        bool settled = rb == null || rb.linearVelocity.sqrMagnitude < 0.3f;
        if ((timer > 1.2f && settled) || timer > 6f)
        {
            if (Physics.Raycast(transform.position + Vector3.up * 0.3f, Vector3.down, out var hit, 3f, 1, QueryTriggerInteraction.Ignore))
                Gore.Decal(hit.point, hit.normal, size * Gore.Multiplier, true);
            Destroy(this);
        }
    }
}
