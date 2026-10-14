using UnityEngine;

/// <summary>Pulsing arterial spray that weakens and stops after a while.</summary>
public class BloodFountain : MonoBehaviour
{
    public float duration = 5f;
    public float baseRate = 60f;
    ParticleSystem ps;
    float t;

    void Awake() => ps = GetComponent<ParticleSystem>();

    void Update()
    {
        t += Time.deltaTime;
        float fade = 1f - Mathf.Clamp01(t / duration);
        float pulse = 0.35f + 0.65f * Mathf.Max(0f, Mathf.Sin(t * 9f)); // heartbeat
        var emission = ps.emission;
        emission.rateOverTime = baseRate * pulse * fade;
        var main = ps.main;
        main.startSpeedMultiplier = Mathf.Lerp(0.3f, 1f, fade) * (0.6f + 0.4f * pulse);
        if (t > duration)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            transform.SetParent(null, true);
            Destroy(gameObject, 2f);
            enabled = false;
        }
    }
}
