using UnityEngine;

/// Всё, что может показаться за спиной игрока в момент скримера.
/// Его реализуют BackMonster (монстр на месте) и StalkerMonster (охотник).
public interface IJumpscareTarget
{
    void Reveal(Transform player, float distance);
    void Hide();
}
