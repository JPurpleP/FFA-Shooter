using UnityEngine;

/// <summary>Flying chunk of meat: leaves a splat where it lands.</summary>
public class Gib : MonoBehaviour
{
    int splats;

    void OnCollisionEnter(Collision c)
    {
        if (splats >= 2 || c.relativeVelocity.magnitude < 2.5f || c.contactCount == 0) return;
        var contact = c.GetContact(0);
        if (c.collider.attachedRigidbody != null) return;
        Gore.Decal(contact.point, contact.normal, Random.Range(0.15f, 0.35f), false);
        splats++;
    }
}
