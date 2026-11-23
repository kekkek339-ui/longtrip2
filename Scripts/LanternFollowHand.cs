using UnityEngine;

/// Фонарь следует за кистью руки, но не поворачивается вместе с ней.
/// Один скрипт вместо AttachToBone + LockWorldRotation (их с Lantern убери).
/// Фонарь НЕ становится дочерним к кости: каждый кадр его позиция
/// просто = позиция кисти + Offset (в метрах, по осям мира).
/// Размер и иерархия фонаря не меняются.
public class LanternFollowHand : MonoBehaviour
{
    [Tooltip("Кисть руки. Если пусто, найдётся по имени внутри Player")]
    public Transform targetBone;
    public string boneName = "RightHand";

    [Header("Положение относительно кисти (метры, оси мира)")]
    [Tooltip("Y = -0.15 означает: фонарь висит на 15 см ниже кисти. " +
             "X = вперёд/назад по ходу, Z = к камере/от камеры")]
    public Vector3 worldOffset = new Vector3(0f, -0.15f, 0f);

    [Header("Поворот")]
    [Tooltip("Включено: фонарь держит тот поворот, с каким стоит в сцене при старте")]
    public bool keepStartRotation = true;
    public Vector3 fixedEuler = Vector3.zero;

    [Header("Покачивание при ходьбе (необязательно)")]
    [Tooltip("0 = нет. Попробуй 3–8. Минус = качается в другую сторону")]
    public float swayAmount = 0f;
    public float maxSwayAngle = 25f;
    public float swaySpeed = 6f;

    Quaternion rot;
    Vector3 lastPos;
    float sway;

    void Start()
    {
        if (targetBone == null) targetBone = FindBone();

        if (targetBone == null)
        {
            Debug.LogWarning("LanternFollowHand: кость «" + boneName + "» не найдена. " +
                             "Перетащи кисть из Hierarchy в поле Target Bone.");
            enabled = false;
            return;
        }

        rot = keepStartRotation ? transform.rotation : Quaternion.Euler(fixedEuler);
        lastPos = targetBone.position + worldOffset;

        Debug.Log("LanternFollowHand: слежу за костью " + PathOf(targetBone));
    }

    void LateUpdate()
    {
        // LateUpdate идёт после анимации, поэтому кисть уже стоит на месте
        Vector3 pos = targetBone.position + worldOffset;
        transform.position = pos;

        float swayTarget = 0f;
        if (swayAmount != 0f && Time.deltaTime > 0f)
        {
            float vx = (pos.x - lastPos.x) / Time.deltaTime;
            swayTarget = Mathf.Clamp(-vx * swayAmount, -maxSwayAngle, maxSwayAngle);
        }
        lastPos = pos;

        sway = Mathf.Lerp(sway, swayTarget, 1f - Mathf.Exp(-swaySpeed * Time.deltaTime));
        transform.rotation = Quaternion.Euler(0f, 0f, sway) * rot;
    }

    Transform FindBone()
    {
        var player = GetComponentInParent<PlayerController>();
        if (player == null) player = FindFirstObjectByType<PlayerController>();
        if (player == null) return null;

        Transform endsWith = null;
        foreach (var t in player.GetComponentsInChildren<Transform>(true))
        {
            if (t == transform || t.IsChildOf(transform)) continue;
            if (t.name == boneName) return t;
            if (endsWith == null && t.name.EndsWith(boneName)) endsWith = t;
        }
        return endsWith;
    }

    static string PathOf(Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    void OnDrawGizmosSelected()
    {
        if (targetBone == null) return;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(targetBone.position, 0.05f);
        Gizmos.DrawLine(targetBone.position, targetBone.position + worldOffset);
    }
}
