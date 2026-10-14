using UnityEngine;

/// <summary>Slowly circles the arena behind the main menu.</summary>
public class MenuCameraOrbit : MonoBehaviour
{
    public float radius = 40f;
    public float height = 26f;
    public float degreesPerSecond = 6f;
    public Vector3 center = new(0f, 2f, 10f);

    float angle = 180f;

    void Update()
    {
        angle += degreesPerSecond * Time.deltaTime;
        float rad = angle * Mathf.Deg2Rad;
        transform.position = new Vector3(center.x + Mathf.Sin(rad) * radius, height, center.z + Mathf.Cos(rad) * radius);
        transform.LookAt(center);
    }
}
