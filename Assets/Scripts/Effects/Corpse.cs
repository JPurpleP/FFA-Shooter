using System.Collections;
using UnityEngine;

/// <summary>Ragdoll lifetime: lies around for a while, then sinks into the floor and disappears.</summary>
public class Corpse : MonoBehaviour
{
    public Rigidbody[] bodies;
    public Transform[] parts;
    public float born;

    void Awake() => born = Time.time;
    public float lifetime = 45f;

    IEnumerator Start()
    {
        yield return new WaitForSeconds(lifetime);
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        foreach (var rb in bodies) if (rb != null) { rb.isKinematic = true; }
        float t = 0f;
        while (t < 3f)
        {
            t += Time.deltaTime;
            transform.position += Vector3.down * Time.deltaTime * 0.4f;
            yield return null;
        }
        Destroy(gameObject);
    }

    void Update()
    {
        // Something flew out of the world: clean up.
        if (bodies != null && bodies.Length > 0 && bodies[0] != null && bodies[0].position.y < -60f)
            Destroy(gameObject);
    }

    /// <summary>After the body settles, spread a pool of blood beneath it.</summary>
    public static void SchedulePool(Transform chest, float size)
    {
        if (chest == null) return;
        chest.gameObject.AddComponent<PoolSpawner>().size = size;
    }
}
