using UnityEngine;
using UnityEngine.InputSystem; // новый Input System (по умолчанию в Unity 6)

/// Ходьба по одной оси (X). Повесь на объект Player.
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Движение")]
    public float walkSpeed = 2.2f;
    public float runSpeed = 4.2f;
    public float gravity = -20f;
    [Tooltip("true = можно идти только вперёд (вправо)")]
    public bool forwardOnly = true;
    [Tooltip("false = управление заблокировано (катсцены, смерть и т.д.)")]
    public bool canMove = true;

    [Header("Визуал")]
    [Tooltip("Дочерний объект с моделью/спрайтом. Разворачивается по направлению")]
    public Transform visual;

    public float CurrentSpeed { get; private set; }
    public int Facing { get; private set; } = 1; // 1 = вправо, -1 = влево

    CharacterController cc;
    float verticalVelocity;

    void Reset()
    {
        // Удобные значения по умолчанию для капсулы ростом ~1.8 м
        var c = GetComponent<CharacterController>();
        c.height = 1.8f;
        c.radius = 0.3f;
        c.center = new Vector3(0f, 0.9f, 0f);
    }

    void Awake()
    {
        cc = GetComponent<CharacterController>();
    }

    void Update()
    {
        float input = 0f;
        bool run = false;

        var kb = Keyboard.current;
        if (canMove && kb != null)
        {
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) input += 1f;
            if (!forwardOnly && (kb.aKey.isPressed || kb.leftArrowKey.isPressed)) input -= 1f;
            run = kb.leftShiftKey.isPressed;
        }

        float speed = run ? runSpeed : walkSpeed;
        CurrentSpeed = Mathf.Abs(input) * speed;

        if (cc.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
        verticalVelocity += gravity * Time.deltaTime;

        Vector3 move = new Vector3(input * speed, verticalVelocity, 0f);
        cc.Move(move * Time.deltaTime);

        if (input != 0f)
        {
            Facing = input > 0f ? 1 : -1;
            if (visual != null)
            {
                Vector3 s = visual.localScale;
                s.x = Mathf.Abs(s.x) * Facing;
                visual.localScale = s;
            }
        }
    }
}
