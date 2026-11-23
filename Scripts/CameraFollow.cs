using UnityEngine;

/// Камера сбоку: плавно едет за игроком, чуть смотрит вперёд.
/// Повесь на Main Camera. Поворот камеры настраивается в инспекторе
/// (Tilt, Yaw, Roll), а при оглядывании LookBackController добавляет к нему
/// дополнительный поворот.
[RequireComponent(typeof(Camera))]
public class CameraFollow : MonoBehaviour
{
    public Transform target;

    [Header("Позиция")]
    public Vector3 offset = new Vector3(0f, 2f, -9f);
    public float lookAhead = 1.5f;   // камера смещена вперёд по ходу
    public float smoothTime = 0.3f;

    [Header("Поворот камеры (градусы)")]
    [Tooltip("Наклон вниз (+) или вверх (-)")]
    public float tilt = 6f;
    [Tooltip("Поворот вправо (+) или влево (-). 0 = смотрит строго вдоль сцены")]
    public float yaw = 0f;
    [Tooltip("Завал горизонта, «голландский угол»")]
    public float roll = 0f;
    public float fieldOfView = 40f;

    [Header("Тряска")]
    [Tooltip("Насколько тряска ещё и качает камеру (градусов на единицу силы)")]
    public float shakeRotation = 4f;

    // Эти значения меняет LookBackController (оглядывание, скример)
    [HideInInspector] public float extraLookX;
    [HideInInspector] public float extraZ;
    [HideInInspector] public float extraFov;
    [HideInInspector] public float extraYaw;
    [HideInInspector] public float extraPitch;
    [HideInInspector] public float extraRoll;

    Camera cam;
    Vector3 velocity;
    Vector3 smoothPos;
    float shakeTime, shakeDuration, shakeAmplitude;

    void Start()
    {
        cam = GetComponent<Camera>();
        cam.fieldOfView = fieldOfView;
        ApplyRotation(0f);
        Snap();
    }

    Vector3 Desired()
    {
        return target.position + offset + new Vector3(lookAhead + extraLookX, 0f, extraZ);
    }

    void ApplyRotation(float shakeRoll)
    {
        transform.rotation = Quaternion.Euler(
            tilt + extraPitch,
            yaw + extraYaw,
            roll + extraRoll + shakeRoll);
    }

    /// Мгновенно поставить камеру (после чекпоинта, телепорта)
    public void Snap()
    {
        if (target == null) return;
        smoothPos = Desired();
        velocity = Vector3.zero;
        transform.position = smoothPos;
    }

    /// Тряска для скримеров: Shake(0.4f, 0.3f)
    public void Shake(float duration, float amplitude)
    {
        shakeDuration = Mathf.Max(0.01f, duration);
        shakeTime = duration;
        shakeAmplitude = amplitude;
    }

    void LateUpdate()
    {
        if (target == null) return;

        smoothPos = Vector3.SmoothDamp(smoothPos, Desired(), ref velocity, smoothTime);

        Vector3 shake = Vector3.zero;
        float shakeRoll = 0f;
        if (shakeTime > 0f)
        {
            shakeTime -= Time.deltaTime;
            float k = Mathf.Clamp01(shakeTime / shakeDuration);
            shake = Random.insideUnitSphere * shakeAmplitude * k;
            shakeRoll = Random.Range(-1f, 1f) * shakeAmplitude * shakeRotation * k;
        }

        transform.position = smoothPos + shake;
        ApplyRotation(shakeRoll);
        cam.fieldOfView = fieldOfView + extraFov;
    }
}
