using UnityEngine;

/// <summary>Point light that fades out smoothly.</summary>
public class LightFade : MonoBehaviour
{
    Light lightComp;
    float start, life, t;

    public void Init(float intensity, float duration)
    {
        lightComp = GetComponent<Light>();
        start = intensity;
        life = duration;
        lightComp.intensity = intensity;
    }

    void Update()
    {
        t += Time.deltaTime;
        float k = 1f - Mathf.Clamp01(t / life);
        lightComp.intensity = start * k * k;
        if (t >= life) Destroy(gameObject);
    }
}
