using UnityEngine;

/// <summary>Muzzle flame: scales up a touch and vanishes after a couple of frames.</summary>
public class FlashFade : MonoBehaviour
{
    public float life = 0.04f;
    float t;
    Vector3 baseScale;

    void Start() => baseScale = transform.localScale;

    void Update()
    {
        t += Time.deltaTime;
        transform.localScale = baseScale * (1f + t / life * 0.35f);
        if (t >= life) Destroy(gameObject);
    }
}
