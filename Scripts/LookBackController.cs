using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// Кнопка «обернуться». Повесь на Player (рядом с PlayerController).
/// Ход: игрок останавливается, разворачивается, камера смещается назад,
/// пауза для напряжения, затем либо скример (если сзади BackMonster), либо всё ок.
[RequireComponent(typeof(PlayerController))]
public class LookBackController : MonoBehaviour
{
    [Header("Ссылки (пустые найдутся сами)")]
    public CameraFollow cameraFollow;
    public Transform visual;
    public AudioSource audioSource;

    [Header("Управление")]
    public Key lookBackKey = Key.Q;
    public float cooldown = 1f;

    [Header("Тайминги")]
    public float turnTime = 0.4f;       // поворот туда и обратно
    public float suspenseTime = 0.9f;   // тишина перед проверкой
    public float safeHoldTime = 0.6f;   // пауза, если сзади пусто

    [Header("Камера при оглядывании")]
    public float cameraLookX = -2.5f;   // сдвиг камеры назад
    public float cameraZoom = 2.5f;     // ближе к игроку
    public float cameraFov = -6f;
    [Tooltip("Поворот камеры назад. Минус = влево. Основная фишка оглядывания")]
    public float cameraYaw = -14f;
    [Tooltip("Дополнительный наклон вниз (+) или вверх (-)")]
    public float cameraPitch = 0f;
    [Tooltip("Лёгкий завал горизонта при оглядывании")]
    public float cameraRoll = 3f;

    [Header("Скример")]
    public float revealDistance = 2.2f; // где появляется монстр
    public float scareZoom = 3f;
    public float scareFov = -14f;
    [Tooltip("Дополнительный завал горизонта в момент скримера")]
    public float scareRoll = -5f;
    public float shakeTime = 0.7f;
    public float shakeAmount = 0.5f;
    public float restartDelay = 2f;
    public bool reloadSceneOnCaught = true;

    [Header("Монстры")]
    [Tooltip("Учитывать старые заскриптованные BackMonster. Если скример срабатывает " +
             "от монстра, который не охотится, выключи или удали BackMonster в сцене")]
    public bool useBackMonsters = false;

    [Header("Звуки (необязательно)")]
    public AudioClip turnSound;
    public AudioClip safeSound;
    public AudioClip scareSound;

    [Header("События (для UI, музыки, света)")]
    public UnityEvent onLookStart;
    public UnityEvent onSafe;
    public UnityEvent onCaught;

    public bool IsBusy { get { return busy; } }

    PlayerController player;
    Quaternion baseRotation = Quaternion.identity;
    bool busy;
    float nextAllowed;
    float blend, blendTarget;   // 0 = смотрит вперёд, 1 = смотрит назад
    float scare, scareTarget;   // усиление камеры при скримере

    void Awake()
    {
        player = GetComponent<PlayerController>();
        if (visual == null) visual = player.visual;
        if (visual != null) baseRotation = visual.localRotation;

        if (cameraFollow == null && Camera.main != null)
            cameraFollow = Camera.main.GetComponent<CameraFollow>();

        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 0f;
            audioSource.playOnAwake = false;
        }
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (!busy && player.canMove && Time.time >= nextAllowed
            && kb != null && kb[lookBackKey].wasPressedThisFrame)
        {
            StartCoroutine(LookBack());
        }

        blend = Mathf.MoveTowards(blend, blendTarget, Time.deltaTime / Mathf.Max(0.01f, turnTime));
        scare = Mathf.MoveTowards(scare, scareTarget, Time.deltaTime / 0.12f);
        float e = Mathf.SmoothStep(0f, 1f, blend);

        if (visual != null)
            visual.localRotation = baseRotation * Quaternion.Euler(0f, 180f * e, 0f);

        if (cameraFollow != null)
        {
            cameraFollow.extraLookX = cameraLookX * e;
            cameraFollow.extraZ = cameraZoom * e + scareZoom * scare;
            cameraFollow.extraFov = cameraFov * e + scareFov * scare;
            cameraFollow.extraYaw = cameraYaw * e;
            cameraFollow.extraPitch = cameraPitch * e;
            cameraFollow.extraRoll = cameraRoll * e + scareRoll * scare;
        }
    }

    IEnumerator LookBack()
    {
        busy = true;
        player.canMove = false;
        onLookStart.Invoke();
        Play(turnSound);

        // 1. Поворачиваемся
        blendTarget = 1f;
        yield return new WaitUntil(() => blend >= 1f);

        // 2. Напряжённая пауза
        yield return new WaitForSeconds(suspenseTime);

        // 3. Что сзади?
        bool repelled = false;
        var stalker = StalkerMonster.Instance;
        if (stalker != null)
        {
            var result = stalker.QueryLookBack(transform);
            if (result == StalkerMonster.LookResult.Caught)
            {
                Debug.Log("LookBackController: скример от StalkerMonster (охота, он рядом).");
                yield return Jumpscare(stalker);
                yield break;
            }
            if (result == StalkerMonster.LookResult.Repelled)
            {
                // Успел обернуться: монстр показывается и отступает
                repelled = true;
                onSafe.Invoke();
                yield return stalker.RepelSequence(transform);
            }
        }

        BackMonster monster = useBackMonsters ? FindMonster() : null;
        if (monster != null)
        {
            Debug.Log("LookBackController: скример от BackMonster на объекте «" + monster.name +
                      "». Это заскриптованный монстр: удали его из сцены или сними галочку Armed.");
            yield return Jumpscare(monster);
            yield break;
        }

        if (!repelled)
        {
            onSafe.Invoke();
            Play(safeSound);
        }
        yield return new WaitForSeconds(safeHoldTime);

        // 4. Поворачиваемся обратно
        blendTarget = 0f;
        yield return new WaitUntil(() => blend <= 0f);
        Finish();
    }

    IEnumerator Jumpscare(IJumpscareTarget monster)
    {
        monster.Reveal(transform, revealDistance);
        scareTarget = 1f;
        if (cameraFollow != null) cameraFollow.Shake(shakeTime, shakeAmount);
        Play(scareSound);
        onCaught.Invoke();

        yield return new WaitForSeconds(restartDelay);

        if (reloadSceneOnCaught)
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            yield break;
        }

        // Без перезапуска: спрятать монстра и отпустить игрока
        monster.Hide();
        scareTarget = 0f;
        blendTarget = 0f;
        yield return new WaitUntil(() => blend <= 0f);
        Finish();
    }

    /// Вызывается монстром-охотником, когда он добрался до игрока,
    /// а игрок так и не обернулся.
    public void ForceCaught(IJumpscareTarget target)
    {
        if (busy) return; // игрок уже оглядывается, исход решится в проверке
        Debug.Log("LookBackController: монстр добрался до игрока, скример.");
        StartCoroutine(ForcedCatch(target));
    }

    IEnumerator ForcedCatch(IJumpscareTarget target)
    {
        busy = true;
        player.canMove = false;
        onLookStart.Invoke();
        blendTarget = 1f;
        yield return new WaitUntil(() => blend >= 1f);
        yield return Jumpscare(target);
    }

    void Finish()
    {
        player.canMove = true;
        busy = false;
        nextAllowed = Time.time + cooldown;
    }

    BackMonster FindMonster()
    {
        foreach (var m in BackMonster.All)
            if (m.IsBehind(transform)) return m;
        return null;
    }

    void Play(AudioClip clip)
    {
        if (clip != null && audioSource != null) audioSource.PlayOneShot(clip);
    }
}