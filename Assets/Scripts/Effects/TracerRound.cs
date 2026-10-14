using System;
using UnityEngine;

/// <summary>Moves a short glowing streak from start to end, then runs the arrival callback.</summary>
public class TracerRound : MonoBehaviour
{
    LineRenderer line;
    Vector3 from, to, dir;
    float dist, speed, length, travelled;
    Action onArrive;
    bool arrived;
    float fade = 1f;

    public void Init(LineRenderer l, Vector3 start, Vector3 end, float metersPerSecond, float streakLength, Action arrive)
    {
        line = l;
        from = start;
        to = end;
        dist = Vector3.Distance(start, end);
        dir = (end - start) / dist;
        speed = metersPerSecond;
        length = streakLength;
        onArrive = arrive;
        travelled = Mathf.Min(dist, length * 0.35f); // appear already a little way out of the barrel
        UpdateLine();
    }

    void Update()
    {
        if (!arrived)
        {
            travelled += speed * Time.deltaTime;
            if (travelled >= dist)
            {
                travelled = dist;
                arrived = true;
                onArrive?.Invoke();
            }
        }
        else
        {
            // Let the tail catch up to the impact point, then go.
            fade -= Time.deltaTime * 14f;
            if (fade <= 0f)
            {
                Destroy(gameObject);
                return;
            }
        }
        UpdateLine();
    }

    void UpdateLine()
    {
        float head = travelled;
        float tail = Mathf.Max(0f, head - length * (arrived ? fade : 1f));
        line.SetPosition(0, from + dir * tail);
        line.SetPosition(1, from + dir * head);
    }
}
