using System.Collections.Generic;
using UnityEngine;

/// Монстр «за спиной». Поставь в сцену пустой объект с этим скриптом
/// и дочерним объектом-моделью (Model). Пока игрок не обернулся, модель скрыта.
/// Если игрок прошёл монстра и обернулся в пределах detectRange, будет скример.
public class BackMonster : MonoBehaviour, IJumpscareTarget
{
    public static readonly List<BackMonster> All = new List<BackMonster>();

    [Tooltip("Выключи, чтобы монстр был «безопасным» (оглядываться можно)")]
    public bool armed = true;
    [Tooltip("Как далеко позади игрока монстр ещё считается «сзади»")]
    public float detectRange = 15f;
    [Tooltip("Что показать при скримере. Если пусто, берётся первый дочерний объект")]
    public GameObject model;

    void Awake()
    {
        if (model == null && transform.childCount > 0)
            model = transform.GetChild(0).gameObject;
        Hide();
    }

    void OnEnable()  { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    public bool IsBehind(Transform player)
    {
        if (!armed) return false;
        float d = player.position.x - transform.position.x;
        return d > 0f && d <= detectRange;
    }

    public void Reveal(Transform player, float distance)
    {
        Vector3 p = transform.position;
        p.x = player.position.x - distance;
        transform.position = p;
        if (model != null) model.SetActive(true);
    }

    public void Hide()
    {
        if (model != null) model.SetActive(false);
    }

    public void SetArmed(bool value) { armed = value; }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Vector3 p = transform.position;
        Gizmos.DrawLine(p, p + Vector3.right * detectRange);
        Gizmos.DrawWireSphere(p, 0.4f);
    }
}
