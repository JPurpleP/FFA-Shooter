using UnityEngine;

/// <summary>Grows a blood decal from small to full size over a few seconds.</summary>
public class BloodPool : MonoBehaviour
{
    public float targetScale = 1f;
    public float growTime = 6f;
    float t;

    void Start() => transform.localScale = Vector3.one * targetScale * 0.15f;

    void Update()
    {
        t += Time.deltaTime / growTime;
        float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 2f);
        transform.localScale = Vector3.one * targetScale * Mathf.Lerp(0.15f, 1f, k);
        if (t >= 1f) Destroy(this);
    }
}
