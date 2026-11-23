using System.Collections;
using UnityEngine;

/// Мерцание света + случайные отключения. Повесь на Light (фонарь у игрока).
[RequireComponent(typeof(Light))]
public class LightFlicker : MonoBehaviour
{
    public float baseIntensity = 2.5f;
    [Range(0f, 1f)] public float flickerAmount = 0.25f;
    public float flickerSpeed = 6f;

    [Header("Случайные отключения")]
    public bool randomBlackouts = true;
    public Vector2 timeBetweenBlackouts = new Vector2(8f, 20f);
    public Vector2 blackoutDuration = new Vector2(0.2f, 1.5f);

    Light lamp;
    bool forcedOff;
    float seedOffset;

    void Awake()
    {
        lamp = GetComponent<Light>();
        seedOffset = Random.value * 100f;
    }

    void OnEnable()
    {
        if (randomBlackouts) StartCoroutine(BlackoutRoutine());
    }

    void Update()
    {
        if (forcedOff) { lamp.intensity = 0f; return; }
        float n = Mathf.PerlinNoise(Time.time * flickerSpeed, seedOffset);
        lamp.intensity = baseIntensity * (1f - flickerAmount + n * flickerAmount);
    }

    IEnumerator BlackoutRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(timeBetweenBlackouts.x, timeBetweenBlackouts.y));
            yield return Blackout(Random.Range(blackoutDuration.x, blackoutDuration.y));
        }
    }

    IEnumerator Blackout(float seconds)
    {
        forcedOff = true;
        yield return new WaitForSeconds(seconds);
        forcedOff = false;
    }

    /// Вызвать из события: GetComponent<LightFlicker>().BlackoutNow(2f)
    public void BlackoutNow(float seconds)
    {
        StartCoroutine(Blackout(seconds));
    }
}
