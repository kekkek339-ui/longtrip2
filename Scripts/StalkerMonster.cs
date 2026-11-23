using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

/// Монстр-преследователь (твоя модель). Живёт по циклу:
///  1) вдалеке в лесу изредка показывается, бродит от точки к точке (в любую сторону),
///     обходит деревья по NavMesh, издаёт далёкие звуки и пропадает.
///     Пока он просто бродит, обернувшись, игрок в безопасности;
///  2) время от времени начинает ОХОТУ: идёт по дорожке прямо позади игрока,
///     каждую секунду ближе. Звуки, дыхание, мерцание света предупреждают заранее;
///  3) если игрок успел обернуться и монстр попал в кадр: он пугается и уходит;
///     если дошёл до игрока: скример и конец.
/// Повесь на пустой объект Monster, твою модель положи дочерним объектом.
/// NavMeshAgent вручную добавлять НЕ нужно: скрипт добавит его сам.
[DefaultExecutionOrder(100)]
public class StalkerMonster : MonoBehaviour, IJumpscareTarget
{
    public enum LookResult { Nothing, TooEarly, Repelled, Caught }
    enum State { Idle, Visible, Hunting, Repelling, Scaring }

    public static StalkerMonster Instance { get; private set; }

    [Header("Ссылки (пустые найдутся сами)")]
    public PlayerController player;
    public LookBackController look;
    public CameraFollow cameraFollow;
    [Tooltip("Твоя модель монстра (дочерний объект Monster в сцене, не префаб из Project!)")]
    public GameObject model;
    [Tooltip("Если монстр смотрит боком или спиной по ходу, подбери: 0, 90, -90 или 180 (в Play клавиша Y)")]
    public float modelYawOffset = 0f;
    [Tooltip("Модель всегда видна (удобно для настройки). Выключи, чтобы монстр прятался между появлениями")]
    public bool alwaysVisible = true;
    [Tooltip("Как быстро поворачивается монстр (градусов в секунду)")]
    public float turnSpeed = 360f;

    [Header("Расписание")]
    public float firstAppearDelay = 12f;
    [Tooltip("Монстр сам начинает охоту. Выключи, если хочешь запускать охоту триггерами")]
    public bool autoHunt = true;
    public bool canHunt = true;
    public float firstHuntDelay = 45f;
    [Tooltip("Секунд между охотами")]
    public Vector2 huntInterval = new Vector2(40f, 80f);

    [Header("ИИ-навигация (NavMesh)")]
    [Tooltip("Бродить по NavMesh и обходить деревья. Нужна галочка Build Nav Mesh у ForestLocationGenerator")]
    public bool useNavMesh = true;
    [Tooltip("Во время охоты тоже идти по NavMesh. Выключено = ровно по дорожке (плавнее, на дорожке деревьев нет)")]
    public bool huntUsesNavMesh = false;
    public float agentRadius = 0.4f;
    public float agentHeight = 2f;
    public float agentAcceleration = 30f;
    [Tooltip("Как часто (сек) обновлять путь на охоте по NavMesh")]
    public float repathInterval = 0.3f;
    [Tooltip("Z за самым дальним рядом деревьев (открытое место). По ней проверяется, " +
             "что монстр не появился в «кармане» между стволами")]
    public float forestHubZ = 34f;
    [Tooltip("Если монстр не может идти дальше столько секунд, он пропадает и появится в другом месте")]
    public float stuckTimeout = 1.5f;

    [Header("Показывается вдали в лесу")]
    public Vector2 appearInterval = new Vector2(8f, 20f);
    public Vector2 visibleDuration = new Vector2(4f, 9f);
    [Tooltip("Насколько далеко от игрока по X (в любую сторону)")]
    public Vector2 lurkDistanceX = new Vector2(6f, 22f);
    [Tooltip("Глубина (Z): чем больше, тем дальше в лесу и сильнее туман")]
    public Vector2 lurkDepthZ = new Vector2(10f, 20f);
    public float wanderSpeed = 1.2f;
    [Tooltip("Через сколько секунд выбирает новую точку, куда идти")]
    public Vector2 waypointInterval = new Vector2(3f, 5f);
    [Tooltip("Шанс, что вместо ходьбы просто постоит")]
    [Range(0f, 1f)] public float standChance = 0.25f;
    public Vector2 lurkSoundInterval = new Vector2(3f, 8f);

    [Header("Охота: идёт позади игрока (метры)")]
    [Tooltip("С какого расстояния позади игрока начинает")]
    public float huntStartDistance = 28f;
    [Tooltip("Насколько быстро сокращается расстояние (м/с)")]
    public float approachSpeed = 1.6f;
    [Tooltip("Если игрок бежит, монстр приближается медленнее (множитель)")]
    public float runSlowFactor = 0.6f;
    [Tooltip("С этого расстояния начинают слышаться шаги и дыхание")]
    public float audibleDistance = 24f;
    [Tooltip("Ближе этого: очень громко, мерцает свет, трясётся камера")]
    public float dangerDistance = 11f;
    [Tooltip("Дошёл до игрока: скример и конец")]
    public float catchDistance = 1.6f;
    [Tooltip("Z-линия, по которой он идёт (0 = дорожка игрока)")]
    public float huntLaneZ = 0f;
    [Tooltip("Насколько монстр повёрнут лицом к камере, пока идёт (градусы, 0 = строго боком)")]
    public float huntFacingAngle = 15f;

    [Header("Когда игрок обернулся и увидел монстра")]
    [Tooltip("Пауза: монстр замирает, смотрит на игрока")]
    public float repelReactTime = 0.6f;
    [Tooltip("Сколько секунд убегает, прежде чем пропасть")]
    public float repelShowTime = 2f;
    public float retreatSpeed = 5f;
    [Tooltip("Пауза до следующего появления")]
    public Vector2 cooldown = new Vector2(25f, 50f);

    [Header("Анимации монстра (необязательно)")]
    [Tooltip("Animator на модели. Пусто = найдётся сам")]
    public Animator animator;
    [Tooltip("Если у модели уже есть свой Animator Controller, оставь клипы пустыми")]
    public AnimationClip idleClip;
    public AnimationClip walkClip;
    public AnimationClip scareClip;
    [Tooltip("Сколько метров в секунду проходит монстр в клипе ходьбы (подбирай, если ноги скользят)")]
    public float walkClipSpeed = 1.5f;
    public float animBlendSharpness = 8f;

    [Header("Звуки (необязательно, без них будет тихо)")]
    [Tooltip("Далёкие звуки: рык, треск веток (3D, из леса)")]
    public AudioClip[] lurkSounds;
    public AudioClip[] stepClips;
    public AudioClip huntStartClip;
    public AudioClip[] growlClips;
    [Tooltip("Зацикленное дыхание")]
    public AudioClip breathClip;
    public AudioClip repelClip;
    [Range(0f, 1f)] public float masterVolume = 1f;

    [Header("Отладка")]
    [Tooltip("H = охота, J = поставить рядом, Y = повернуть модель, на экране состояние. Выключи в готовой игре")]
    public bool debugKeys = true;

    const float groundY = 0f;
    const float maxChaseSpeed = 9f;
    const float maxLag = 3f;

    State state = State.Idle;
    float timer, huntClock, dist, currentDist;
    float visibleTimer, lurkSoundTimer;
    float stepTimer, growlTimer, dangerTimer;

    AudioSource world3D, stepSource, voiceSource, breathSource;
    LightFlicker[] flickers;
    Renderer[] renderers;
    Camera cam;
    readonly Plane[] planes = new Plane[6];

    // навигация
    NavMeshAgent agent;
    NavMeshPath navPath;
    bool agentReady;
    float agentInitTimer, nextRepath, stuckTimer, hunterStuckTimer;
    Vector3 lastDest;

    // блуждание
    Vector3 waypoint;
    bool hasWaypoint;
    float waypointTimer, lurkSpeed;

    // движение и модель
    Vector3 prevPos, moveVel;
    float smoothSpeed;
    bool walkingFlag;
    Quaternion baseLocalRot = Quaternion.identity;
    bool modelVisible;

    // анимация
    PlayableGraph graph;
    AnimationMixerPlayable mixer;
    AnimationClipPlayable idleP, walkP, scareP;
    bool animReady;
    float walkW, scareW;

    // отладочная строка обновляется 5 раз в секунду, а не каждый кадр
    string debugText = "";
    float debugTimer;

    bool UseAgent
    {
        get { return agentReady && agent != null && agent.enabled && agent.isOnNavMesh; }
    }

    // NavMesh ещё строится: подождём немного, чтобы монстр не ходил сквозь деревья
    bool NavPending
    {
        get { return useNavMesh && !agentReady && Time.timeSinceLevelLoad < 20f; }
    }

    /// Расстояние от игрока до монстра позади (м)
    public float CurrentDistance { get { return currentDist; } }

    /// Монстр сейчас действует в мире: бродит, охотится, пугается (не прячется). Для собаки и др.
    public bool IsPresent
    {
        get { return state == State.Visible || state == State.Hunting || state == State.Repelling || state == State.Scaring; }
    }

    public bool IsHunting { get { return state == State.Hunting; } }

    // ---------- жизненный цикл ----------

    void Awake()
    {
        Instance = this;
        navPath = new NavMeshPath();

        // Если NavMeshAgent добавили вручную, выключаем: скрипт включит его, когда NavMesh будет готов
        var manual = GetComponent<NavMeshAgent>();
        if (manual != null)
        {
            manual.enabled = false;
            agent = manual;
        }

        if (player == null) player = FindFirstObjectByType<PlayerController>();
        if (look == null && player != null) look = player.GetComponent<LookBackController>();
        if (cameraFollow == null && Camera.main != null)
            cameraFollow = Camera.main.GetComponent<CameraFollow>();
        cam = cameraFollow != null ? cameraFollow.GetComponent<Camera>() : Camera.main;

        world3D = MakeSource(true, false);
        stepSource = MakeSource(false, false);
        voiceSource = MakeSource(false, false);
        breathSource = MakeSource(false, true);
        breathSource.clip = breathClip;
        breathSource.volume = 0f;

        if (model == null && transform.childCount > 0)
            model = transform.GetChild(0).gameObject;

        if (model == null)
        {
            Debug.LogError("StalkerMonster: нет модели. Положи свою модель монстра дочерним " +
                           "объектом к Monster и перетащи в поле Model.");
        }
        else
        {
            if (!model.scene.IsValid())
                Debug.LogError("StalkerMonster: в поле Model стоит префаб из папки Project, а не объект " +
                               "в сцене. Перетащи модель из Hierarchy.");
            else if (!model.transform.IsChildOf(transform))
                Debug.LogWarning("StalkerMonster: модель должна быть дочерним объектом Monster, " +
                                 "иначе она не будет двигаться вместе с ним.");

            // Сам объект модели (и его родители) должен быть включён: прячем только рендереры
            for (Transform t = model.transform; t != null && t != transform; t = t.parent)
                t.gameObject.SetActive(true);

            // Root Motion сдвигал бы саму модель отдельно от монстра (будто идёт сквозь деревья)
            foreach (var a in model.GetComponentsInChildren<Animator>(true))
                a.applyRootMotion = false;

            baseLocalRot = model.transform.localRotation;
            ApplyYawOffset();
            renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                Debug.LogWarning("StalkerMonster: на модели нет ни одного Renderer (MeshRenderer / " +
                                 "SkinnedMeshRenderer). Проверь, что в Model стоит нужный объект.");

            // Иначе анимированная модель может пропадать из-за неверных границ
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                smr.updateWhenOffscreen = true;

            if (renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                Debug.Log("StalkerMonster: модель «" + model.name + "», рендереров: " + renderers.Length +
                          ", размер: " + b.size.ToString("0.00") + " м");
                if (b.size.y < 0.3f || b.size.y > 8f)
                    Debug.LogWarning("StalkerMonster: высота модели " + b.size.y.ToString("0.00") +
                                     " м. Скорее всего не тот масштаб: подгони Scale модели " +
                                     "(обычный монстр 1.5–3 м).");
            }

            if (model.GetComponentInChildren<LODGroup>(true) != null)
                Debug.LogWarning("StalkerMonster: на модели есть LOD Group. Он сам включает и " +
                                 "выключает рендереры и может мешать. Если модель не видна, удали его.");
        }

        ShowModel(false);
        SetupAnimation();

        timer = firstAppearDelay;
        huntClock = firstHuntDelay;
    }

    void Start()
    {
        flickers = FindObjectsByType<LightFlicker>(FindObjectsSortMode.None);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (graph.IsValid()) graph.Destroy();
    }

    void Update()
    {
        if (player == null) return;

        var kb = Keyboard.current;
        if (debugKeys && kb != null)
        {
            if (kb.hKey.wasPressedThisFrame) StartHunt();
            if (kb.jKey.wasPressedThisFrame) DebugPlaceBehindPlayer();
            if (kb.yKey.wasPressedThisFrame)
            {
                modelYawOffset = (modelYawOffset + 90f) % 360f;
                ApplyYawOffset();
                Debug.Log("StalkerMonster: Model Yaw Offset = " + modelYawOffset + ". Впиши это значение в инспектор.");
            }
        }
        if (alwaysVisible && !modelVisible) ShowModel(true);

        // Ждём, пока появится NavMesh, и тогда создаём агента
        if (useNavMesh && !agentReady)
        {
            agentInitTimer -= Time.deltaTime;
            if (agentInitTimer <= 0f)
            {
                agentInitTimer = 0.5f;
                TryInitAgent();
            }
        }

        switch (state)
        {
            case State.Idle: UpdateIdle(); break;
            case State.Visible: UpdateVisible(); break;
            case State.Hunting: UpdateHunting(); break;
        }

        TrackMotion();
        UpdateAnimation();

        if (debugKeys)
        {
            debugTimer -= Time.deltaTime;
            if (debugTimer <= 0f)
            {
                debugTimer = 0.2f;
                debugText = BuildDebugText();
            }
        }
    }

    /// Отладка: поставить монстра в 4 м позади игрока, чтобы посмотреть на модель
    void DebugPlaceBehindPlayer()
    {
        StopMoving();
        state = State.Idle;
        timer = 30f;
        huntClock = Mathf.Max(huntClock, 30f);
        TeleportTo(new Vector3(player.transform.position.x - 4f, groundY, huntLaneZ), false);
        FaceHunter();
        ShowModel(true);
    }

    // ---------- навигация ----------

    void TryInitAgent()
    {
        NavMeshHit hit;
        if (!NavMesh.SamplePosition(transform.position, out hit, 60f, NavMesh.AllAreas)) return;

        // Ставим монстра на NavMesh до того, как включится агент
        transform.position = hit.position;
        if (agent == null) agent = gameObject.AddComponent<NavMeshAgent>();
        else agent.enabled = true;

        agent.radius = agentRadius;
        agent.height = agentHeight;
        agent.acceleration = agentAcceleration;
        agent.angularSpeed = 720f;
        agent.stoppingDistance = 0.1f;
        agent.autoBraking = true;      // плавно останавливается у точки
        agent.updateRotation = false;  // поворотом управляем сами
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
        agent.speed = wanderSpeed;
        agent.Warp(hit.position);

        agentReady = true;
    }

    /// Мгновенно поставить монстра в точку. onNavMesh = true: на NavMesh (включает агента),
    /// false: просто в точку (агент выключается, дальше монстр двигается напрямую)
    void TeleportTo(Vector3 pos, bool onNavMesh)
    {
        if (onNavMesh && agentReady)
        {
            NavMeshHit hit;
            if (NavMesh.SamplePosition(pos, out hit, 20f, NavMesh.AllAreas))
            {
                transform.position = hit.position;
                if (!agent.enabled) agent.enabled = true;
                agent.Warp(hit.position);
                agent.ResetPath();
                ResetMotion();
                return;
            }
        }

        if (agent != null && agent.enabled) agent.enabled = false;
        transform.position = pos;
        ResetMotion();
    }

    /// Идти к точке с заданной скоростью (по NavMesh, а без него напрямую)
    void MoveTo(Vector3 target, float speed)
    {
        if (UseAgent)
        {
            agent.isStopped = false;
            agent.speed = Mathf.MoveTowards(agent.speed, speed, 10f * Time.deltaTime);
            // Путь пересчитываем не каждый кадр: иначе монстр «дёргается»
            if (Time.time >= nextRepath && (target - lastDest).sqrMagnitude > 0.5f)
            {
                nextRepath = Time.time + repathInterval;
                lastDest = target;
                NavMeshHit hit;
                if (NavMesh.SamplePosition(target, out hit, 4f, NavMesh.AllAreas)) target = hit.position;
                agent.SetDestination(target);
            }
        }
        else
        {
            transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);
        }
    }

    void StopMoving()
    {
        if (!UseAgent) return;
        agent.ResetPath();
        agent.isStopped = true;
    }

    /// Монстр не может пройти дальше (нет пути или стоит на месте)
    bool IsAgentStuck()
    {
        if (!UseAgent || agent.pathPending) return false;
        bool badPath = agent.hasPath && agent.pathStatus != NavMeshPathStatus.PathComplete;
        bool standing = agent.hasPath && agent.remainingDistance > 1.5f && agent.velocity.sqrMagnitude < 0.02f;
        return badPath || standing;
    }

    /// Точка связана с открытым местом за лесом (не «карман» между стволами)
    bool IsConnectedToOpenArea(Vector3 p)
    {
        NavMeshHit hub;
        if (!NavMesh.SamplePosition(new Vector3(p.x, groundY, forestHubZ), out hub, 4f, NavMesh.AllAreas))
            return true; // хаба нет: не проверяем
        return NavMesh.CalculatePath(p, hub.position, NavMesh.AllAreas, navPath)
               && navPath.status == NavMeshPathStatus.PathComplete;
    }

    static float PathLength(NavMeshPath path)
    {
        var c = path.corners;
        float len = 0f;
        for (int i = 1; i < c.Length; i++) len += Vector3.Distance(c[i - 1], c[i]);
        return len;
    }

    // ---------- повороты и движение ----------

    void TurnTo(Quaternion target)
    {
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
    }

    Quaternion RotationToward(Vector3 point)
    {
        Vector3 d = point - transform.position;
        d.y = 0f;
        return d.sqrMagnitude > 0.01f ? Quaternion.LookRotation(d.normalized) : transform.rotation;
    }

    Quaternion HunterRotation()
    {
        // Идёт к игроку (вправо), лицом чуть к камере
        return Quaternion.LookRotation(Quaternion.Euler(0f, huntFacingAngle, 0f) * Vector3.right);
    }

    void FaceHunter()
    {
        transform.rotation = HunterRotation();
    }

    void ResetMotion()
    {
        prevPos = transform.position;
        moveVel = Vector3.zero;
        smoothSpeed = 0f;
    }

    /// Реальная скорость монстра в мире (для поворота и темпа шагов)
    void TrackMotion()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 d = transform.position - prevPos;
        d.y = 0f;
        prevPos = transform.position;

        Vector3 vel = d / dt;
        float k = 1f - Mathf.Exp(-10f * dt);
        moveVel = Vector3.Lerp(moveVel, vel, k);
        smoothSpeed = moveVel.magnitude;

        // Гистерезис: чтобы не дёргаться между «стоит» и «идёт»
        if (!walkingFlag && smoothSpeed > 0.4f) walkingFlag = true;
        else if (walkingFlag && smoothSpeed < 0.15f) walkingFlag = false;
    }

    // ---------- ожидание ----------

    void UpdateIdle()
    {
        timer -= Time.deltaTime;
        huntClock -= Time.deltaTime;
        if (timer > 0f) return;

        if (autoHunt && canHunt && huntClock <= 0f) StartHunt();
        else StartVisible();
    }

    // ---------- появление вдали ----------

    void StartVisible()
    {
        // Без готового NavMesh не появляемся: иначе пойдёт сквозь деревья
        if (NavPending) { timer = 1f; return; }

        float px = player.transform.position.x;
        bool found = false;
        Vector3 spawn = Vector3.zero;

        // Ищем место, где монстр точно не застрянет между стволами
        for (int i = 0; i < 8 && !found; i++)
        {
            float side = Random.value < 0.5f ? -1f : 1f;
            float ox = side * Random.Range(lurkDistanceX.x, lurkDistanceX.y);
            float dz = Random.Range(lurkDepthZ.x, lurkDepthZ.y);
            Vector3 desired = new Vector3(px + ox, groundY, dz);

            if (agentReady)
            {
                NavMeshHit hit;
                if (!NavMesh.SamplePosition(desired, out hit, 3f, NavMesh.AllAreas)) continue;
                if (!IsConnectedToOpenArea(hit.position)) continue;
                spawn = hit.position;
            }
            else
            {
                spawn = desired;
            }
            found = true;
        }

        if (!found) { timer = 3f; return; } // не нашли: попробуем позже

        visibleTimer = Random.Range(visibleDuration.x, visibleDuration.y);
        lurkSoundTimer = Random.Range(0.5f, 2f);
        stuckTimer = 0f;
        hasWaypoint = false;
        waypointTimer = Random.Range(0.5f, 1.5f); // сначала чуть постоит, потом пойдёт

        state = State.Visible;
        TeleportTo(spawn, true);
        transform.rotation = RotationToward(player.transform.position);
        ShowModel(true);
    }

    void EndAppearance(float nextDelay)
    {
        ShowModel(false);
        StopMoving();
        hasWaypoint = false;
        state = State.Idle;
        timer = nextDelay;
    }

    /// Выбирает следующую точку, куда пойдёт монстр (с проверкой пути)
    void PickWaypoint()
    {
        float interval = Random.Range(waypointInterval.x, waypointInterval.y);
        waypointTimer = interval;
        hasWaypoint = false;

        if (Random.value < standChance)
        {
            StopMoving();
            return;
        }

        float px = player.transform.position.x;
        float leadX = px + player.CurrentSpeed * interval; // где будет игрок к моменту прихода

        for (int i = 0; i < 6; i++)
        {
            float side = Random.value < 0.5f ? -1f : 1f;
            float ox = side * Random.Range(lurkDistanceX.x, lurkDistanceX.y);
            float dz = Random.Range(lurkDepthZ.x, lurkDepthZ.y);
            Vector3 desired = new Vector3(leadX + ox, groundY, dz);

            if (UseAgent)
            {
                NavMeshHit hit;
                if (!NavMesh.SamplePosition(desired, out hit, 2f, NavMesh.AllAreas)) continue;
                if (!NavMesh.CalculatePath(transform.position, hit.position, NavMesh.AllAreas, navPath)) continue;
                if (navPath.status != NavMeshPathStatus.PathComplete) continue;

                float len = PathLength(navPath);
                float straight = Vector3.Distance(transform.position, hit.position);
                if (len > straight * 2.2f + 3f) continue; // слишком извилистый путь

                waypoint = hit.position;
                lurkSpeed = Mathf.Clamp(len / interval, wanderSpeed, maxChaseSpeed);
                agent.isStopped = false;
                agent.SetDestination(waypoint);
                hasWaypoint = true;
                return;
            }

            waypoint = desired;
            lurkSpeed = Mathf.Clamp(Vector3.Distance(transform.position, desired) / interval,
                                    wanderSpeed, maxChaseSpeed);
            hasWaypoint = true;
            return;
        }
        // Подходящей точки нет: постоит на месте до следующего выбора
    }

    void UpdateVisible()
    {
        float dt = Time.deltaTime;
        huntClock -= dt;
        visibleTimer -= dt;

        waypointTimer -= dt;
        if (waypointTimer <= 0f) PickWaypoint();

        if (hasWaypoint)
        {
            if (UseAgent)
                agent.speed = Mathf.MoveTowards(agent.speed, lurkSpeed, 6f * dt);
            else
                transform.position = Vector3.MoveTowards(transform.position, waypoint, lurkSpeed * dt);
        }

        // Смотрит по ходу движения, а когда стоит: на игрока
        if (walkingFlag && moveVel.sqrMagnitude > 0.01f)
            TurnTo(Quaternion.LookRotation(moveVel.normalized));
        else
            TurnTo(RotationToward(player.transform.position));

        // Застрял: тихо пропадаем и появимся в другом месте
        stuckTimer = IsAgentStuck() ? stuckTimer + dt : 0f;
        if (stuckTimer > stuckTimeout)
        {
            EndAppearance(2f);
            return;
        }

        lurkSoundTimer -= dt;
        if (lurkSoundTimer <= 0f)
        {
            lurkSoundTimer = Random.Range(lurkSoundInterval.x, lurkSoundInterval.y);
            PlayRandom(world3D, lurkSounds, 0.6f * masterVolume, 0f);
        }

        if (visibleTimer <= 0f)
            EndAppearance(Random.Range(appearInterval.x, appearInterval.y));
    }

    // ---------- охота ----------

    public void StartHunt()
    {
        if (player == null) return;
        if (state == State.Hunting || state == State.Repelling || state == State.Scaring) return;

        state = State.Hunting;
        dist = huntStartDistance;
        currentDist = dist;
        stepTimer = 0.5f;
        growlTimer = Random.Range(3f, 6f);
        dangerTimer = 1f;
        hunterStuckTimer = 0f;
        huntClock = Random.Range(huntInterval.x, huntInterval.y);

        TeleportTo(new Vector3(player.transform.position.x - dist, groundY, huntLaneZ), huntUsesNavMesh);
        FaceHunter();
        ShowModel(true);

        PlayClip(voiceSource, huntStartClip, 0.8f * masterVolume, -0.7f);
        if (breathClip != null)
        {
            breathSource.volume = 0f;
            breathSource.Play();
        }
    }

    void UpdateHunting()
    {
        float dt = Time.deltaTime;

        bool running = player.CurrentSpeed > player.walkSpeed + 0.1f;
        float rate = approachSpeed * (running ? runSlowFactor : 1f);
        float px = player.transform.position.x;

        dist -= rate * dt;
        dist = Mathf.Max(dist, 0.5f);

        if (huntUsesNavMesh && UseAgent)
        {
            // По NavMesh: цель впереди монстра, он догоняет её
            float real = px - transform.position.x;
            dist = Mathf.Max(dist, real - maxLag); // цель не убегает вперёд застрявшего монстра

            Vector3 target = new Vector3(px - dist, groundY, huntLaneZ);
            float lag = Mathf.Max(0f, real - dist);
            float speed = player.CurrentSpeed + rate + lag * 2f + 0.3f;
            MoveTo(target, Mathf.Clamp(speed, 0.5f, maxChaseSpeed));

            // Сильно отстал: догоняем телепортом, но только пока его не видно в кадре
            hunterStuckTimer = lag > 5f ? hunterStuckTimer + dt : 0f;
            if (hunterStuckTimer > stuckTimeout && !IsInCameraView())
            {
                TeleportTo(target, true);
                hunterStuckTimer = 0f;
            }
        }
        else
        {
            // Ровно по дорожке, на фиксированном расстоянии позади игрока: плавно и без рывков
            transform.position = new Vector3(px - dist, groundY, huntLaneZ);
        }

        TurnTo(HunterRotation());
        currentDist = Mathf.Max(0.5f, px - transform.position.x);

        float t = Mathf.Clamp01(1f - Mathf.InverseLerp(catchDistance, audibleDistance, currentDist)); // 0 далеко, 1 рядом

        // Шаги: чаще и громче
        stepTimer -= dt;
        if (stepTimer <= 0f)
        {
            stepTimer = Mathf.Lerp(0.95f, 0.35f, t);
            PlayRandom(stepSource, stepClips, Mathf.Lerp(0.05f, 1f, t * t) * masterVolume, -0.6f);
        }

        // Дыхание
        if (breathClip != null)
        {
            breathSource.volume = Mathf.Clamp01((t - 0.25f) / 0.75f) * masterVolume;
            breathSource.pitch = Mathf.Lerp(0.9f, 1.15f, t);
            breathSource.panStereo = -0.5f;
        }

        // Рычание
        growlTimer -= dt;
        if (growlTimer <= 0f)
        {
            growlTimer = Mathf.Lerp(7f, 2.5f, t) * Random.Range(0.8f, 1.2f);
            PlayRandom(voiceSource, growlClips, Mathf.Lerp(0.2f, 1f, t) * masterVolume, -0.5f);
        }

        // Совсем близко: мерцает свет и дрожит камера
        if (currentDist <= dangerDistance)
        {
            dangerTimer -= dt;
            if (dangerTimer <= 0f)
            {
                dangerTimer = Random.Range(0.8f, 1.6f);
                if (cameraFollow != null) cameraFollow.Shake(0.25f, 0.03f + 0.05f * t);
                if (flickers != null && flickers.Length > 0 && Random.value < 0.4f)
                    flickers[Random.Range(0, flickers.Length)].BlackoutNow(Random.Range(0.1f, 0.35f));
            }
        }

        // Дошёл, а игрок не обернулся: скример
        if (currentDist <= catchDistance && look != null && !look.IsBusy)
            look.ForceCaught(this);
    }

    // ---------- результат «обернуться» ----------

    /// Вызывается из LookBackController в момент проверки.
    /// Опасен ТОЛЬКО монстр на охоте. Если он просто бродит или прячется: Nothing.
    public LookResult QueryLookBack(Transform playerTransform)
    {
        if (state != State.Hunting) return LookResult.Nothing;
        if (currentDist <= catchDistance) return LookResult.Caught;
        if (IsInCameraView()) return LookResult.Repelled;
        return LookResult.TooEarly;
    }

    /// Монстр замирает, пугается и убегает. Вызывается из LookBackController.
    public IEnumerator RepelSequence(Transform playerTransform)
    {
        state = State.Repelling;
        breathSource.Stop();
        StopMoving();

        // Замирает и смотрит на игрока (на камеру)
        transform.rotation = Quaternion.LookRotation(Vector3.back);
        PlayClip(voiceSource, repelClip, masterVolume, -0.3f);
        if (cameraFollow != null) cameraFollow.Shake(0.4f, 0.12f);
        yield return new WaitForSeconds(repelReactTime);

        // Разворачивается и убегает назад по дорожке
        transform.rotation = Quaternion.LookRotation(Vector3.left);
        float t = 0f;
        while (t < repelShowTime)
        {
            t += Time.deltaTime;
            float sp = retreatSpeed * Mathf.Clamp01(t / (repelShowTime * 0.4f));
            MoveTo(transform.position + Vector3.left * 10f, Mathf.Max(0.1f, sp));
            yield return null;
        }

        EndAppearance(Random.Range(cooldown.x, cooldown.y));
        huntClock = Mathf.Max(huntClock, timer + 5f);
    }

    // ---------- IJumpscareTarget ----------

    public void Reveal(Transform playerTransform, float distance)
    {
        // Если монстр уже идёт позади, не отодвигаем его назад
        float d = state == State.Hunting ? Mathf.Min(currentDist, distance) : distance;
        state = State.Scaring;
        breathSource.Stop();

        StopMoving();
        TeleportTo(new Vector3(playerTransform.position.x - d, groundY, huntLaneZ), false);
        transform.rotation = Quaternion.LookRotation(Vector3.back); // лицом к камере
        ShowModel(true);
    }

    public void Hide()
    {
        EndAppearance(Random.Range(cooldown.x, cooldown.y));
    }

    // ---------- внутреннее ----------

    bool IsInCameraView()
    {
        if (cam == null || renderers == null) return false;
        GeometryUtility.CalculateFrustumPlanes(cam, planes);
        foreach (var r in renderers)
            if (r != null && r.enabled && GeometryUtility.TestPlanesAABB(planes, r.bounds))
                return true;
        return false;
    }

    void ShowModel(bool visible)
    {
        if (alwaysVisible) visible = true;
        modelVisible = visible;
        if (renderers == null) return;
        foreach (var r in renderers)
            if (r != null) r.enabled = visible;
    }

    void PlayClip(AudioSource source, AudioClip clip, float volume, float pan)
    {
        if (source == null || clip == null) return;
        source.panStereo = pan;
        source.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    void PlayRandom(AudioSource source, AudioClip[] clips, float volume, float pan)
    {
        if (clips == null || clips.Length == 0) return;
        PlayClip(source, clips[Random.Range(0, clips.Length)], volume, pan);
    }

    AudioSource MakeSource(bool spatial, bool loop)
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.loop = loop;
        s.spatialBlend = spatial ? 1f : 0f;
        if (spatial)
        {
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 6f;
            s.maxDistance = 45f;
        }
        return s;
    }

    void ApplyYawOffset()
    {
        if (model != null)
            model.transform.localRotation = baseLocalRot * Quaternion.Euler(0f, modelYawOffset, 0f);
    }

    // ---------- анимация ----------

    void SetupAnimation()
    {
        if (walkClip == null || model == null) return;

        if (animator == null) animator = model.GetComponentInChildren<Animator>(true);
        if (animator == null)
        {
            Debug.LogWarning("StalkerMonster: клипы заданы, но на модели нет Animator.");
            return;
        }

        animator.applyRootMotion = false;

        graph = PlayableGraph.Create("MonsterAnimator");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        var output = AnimationPlayableOutput.Create(graph, "Animation", animator);

        mixer = AnimationMixerPlayable.Create(graph, 3);
        idleP = AnimationClipPlayable.Create(graph, idleClip != null ? idleClip : walkClip);
        walkP = AnimationClipPlayable.Create(graph, walkClip);
        scareP = AnimationClipPlayable.Create(graph,
            scareClip != null ? scareClip : (idleClip != null ? idleClip : walkClip));

        // Без отдельного idle: берём ходьбу и «замораживаем» её на месте
        if (idleClip == null) idleP.SetSpeed(0f);

        graph.Connect(idleP, 0, mixer, 0);
        graph.Connect(walkP, 0, mixer, 1);
        graph.Connect(scareP, 0, mixer, 2);
        mixer.SetInputWeight(0, 1f);

        output.SetSourcePlayable(mixer);
        graph.Play();
        animReady = true;
    }

    void UpdateAnimation()
    {
        if (!animReady) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        bool scaring = state == State.Scaring;
        bool walking = !scaring && walkingFlag;

        float k = 1f - Mathf.Exp(-animBlendSharpness * dt);
        walkW = Mathf.Lerp(walkW, walking ? 1f : 0f, k);
        scareW = Mathf.Lerp(scareW, scaring ? 1f : 0f, k);
        float idleW = Mathf.Max(0f, 1f - walkW - scareW);

        float sum = idleW + walkW + scareW;
        if (sum < 0.0001f) sum = 1f;
        mixer.SetInputWeight(0, idleW / sum);
        mixer.SetInputWeight(1, walkW / sum);
        mixer.SetInputWeight(2, scareW / sum);

        walkP.SetSpeed(Mathf.Clamp(smoothSpeed / Mathf.Max(0.01f, walkClipSpeed), 0.4f, 3f));
    }

    // ---------- отладка ----------

    string BuildDebugText()
    {
        string extra = "";
        if (state == State.Hunting)
            extra = "  позади: " + currentDist.ToString("0.0") + " м, в кадре: " + (IsInCameraView() ? "да" : "нет");
        string nav = UseAgent ? "NavMesh" : (NavPending ? "NavMesh строится" : "напрямую");
        int rc = renderers != null ? renderers.Length : 0;
        return "Монстр: " + state + extra + "  [" + nav + ", рендереров: " + rc +
               ", всегда виден: " + (alwaysVisible ? "да" : "нет") +
               "]   (H = охота, J = поставить рядом, Y = повернуть модель, сейчас " + modelYawOffset + "°)";
    }

    void OnGUI()
    {
        if (debugKeys) GUI.Label(new Rect(10f, 10f, 1100f, 30f), debugText);
    }
}