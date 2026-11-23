using System.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

/// Собака-компаньон. Живёт рядом с игроком:
///  - идёт рядом, то чуть впереди, то чуть позади (не по рельсам);
///  - иногда убегает вперёд, разворачивается и лает на игрока;
///  - иногда бегает вокруг игрока;
///  - пока игрок стоит: сидит, ложится, виляет хвостом;
///  - когда игрок оборачивается, собака тоже оборачивается;
///  - когда рядом монстр, останавливается, смотрит на него и лает.
/// Повесь на пустой объект Dog, модель собаки положи дочерним объектом.
/// Animator Controller не нужен: клипы из FBX перетаскиваются прямо в поля.
[DefaultExecutionOrder(110)]
public class DogCompanion : MonoBehaviour
{
    enum Anim { Idle, IdleWag, Walk, Run, Bark, SitStart, SitLoop, LayStart, LayLoop }
    enum Behavior { Follow, RunAhead, Orbit }
    const int AnimCount = 9;

    [Header("Ссылки (пустые найдутся сами)")]
    public PlayerController player;
    public LookBackController look;
    [Tooltip("Модель собаки (дочерний объект Dog в сцене, не префаб из Project!)")]
    public GameObject model;
    [Tooltip("Animator на модели. Пусто = найдётся сам")]
    public Animator animator;

    [Header("Модель")]
    [Tooltip("Подогнать размер собаки под Target Height (в метрах)")]
    public bool autoFitHeight = true;
    public float targetHeight = 0.6f;
    [Tooltip("Поставить лапы на землю автоматически")]
    public bool autoGround = true;
    [Tooltip("Если собака идёт задом или лежит на боку, подбери. В Play клавиша U поворачивает по Y на 90°. " +
             "Поворот применяется ко всей собаке (корню Dog)")]
    public Vector3 modelRotationOffset = Vector3.zero;

    [Header("Где ходит")]
    [Tooltip("Минус = чуть позади игрока, плюс = чуть впереди")]
    public float followOffsetX = -1.0f;
    [Tooltip("Минус = ближе к камере (чтобы игрок не загораживал собаку)")]
    public float laneZ = -0.6f;
    public float groundY = 0f;
    [Tooltip("Как далеко собака медленно «гуляет» вперёд-назад от своей точки (м)")]
    public float followWanderX = 0.7f;
    public float followWanderZ = 0.3f;
    [Tooltip("Дальше этих границ по Z собака не заходит (камера / забор)")]
    public Vector2 zLimits = new Vector2(-1.3f, 1.0f);
    [Tooltip("Пока игрок стоит, собака не дёргается, если она ближе этого к своей точке")]
    public float deadZone = 0.5f;
    public float catchUpGain = 2.5f;
    public float maxSpeed = 4.5f;
    public float acceleration = 8f;
    public float turnSpeed = 360f;

    [Header("Игры: выбегает вперёд и бегает вокруг")]
    public bool enableRunAhead = true;
    public bool enableOrbit = true;
    [Tooltip("Раз в сколько секунд собака «решает», поиграть ли")]
    public Vector2 behaviorInterval = new Vector2(14f, 30f);
    [Range(0f, 1f)] public float behaviorChance = 0.7f;
    [Tooltip("На сколько метров впереди игрока она останавливается и лает")]
    public Vector2 aheadDistance = new Vector2(3f, 4.5f);
    public float barkAtPlayerTime = 2.5f;
    [Tooltip("Размеры круга вокруг игрока: по X и по Z (метры)")]
    public float orbitRadiusX = 1.6f;
    public float orbitRadiusZ = 0.95f;
    [Tooltip("Скорость по кругу (радиан/сек). 3 = один круг за ~2 секунды")]
    public float orbitAngularSpeed = 3f;
    public float orbitMaxSpeed = 6f;

    [Header("Анимации (клипы из FBX, не сам FBX)")]
    public AnimationClip idle;
    public AnimationClip idleTailWag;
    public AnimationClip walk;
    public AnimationClip run;
    public AnimationClip bark;
    public AnimationClip sitStart;
    public AnimationClip sitLoop;
    public AnimationClip layStart;
    public AnimationClip layLoop;
    [Tooltip("Сколько метров в секунду проходит собака в клипе ходьбы (если лапы скользят, подбирай)")]
    public float walkClipSpeed = 1.2f;
    public float runClipSpeed = 3.5f;
    [Tooltip("Быстрее этой скорости переходит на бег")]
    public float runThreshold = 3.2f;
    [Tooltip("Скорость смены анимаций")]
    public float blendSpeed = 6f;

    [Header("Отдых, когда игрок стоит")]
    public float sitAfter = 8f;
    public float layAfter = 22f;
    [Tooltip("Как часто (сек) меняет idle: просто стоит или виляет хвостом")]
    public Vector2 idleSwapInterval = new Vector2(4f, 8f);

    [Header("Лай на монстра")]
    [Tooltip("Лает, если монстр бродит ближе этого (м)")]
    public float barkRadius = 18f;
    [Tooltip("Лает, если монстр охотится ближе этого (м). Так собака предупреждает заранее")]
    public float huntBarkRadius = 26f;
    [Tooltip("Минимум секунд лая после того, как монстр появился")]
    public float minBarkTime = 2.5f;
    [Tooltip("Пока лает, останавливается и смотрит на монстра")]
    public bool stopToBark = true;
    [Tooltip("Но если игрок ушёл дальше этого, собака бежит за ним")]
    public float maxStrayWhileBarking = 6f;
    public AudioClip[] barkSounds;
    public float barkSoundInterval = 0.9f;
    [Range(0f, 1f)] public float barkVolume = 1f;

    [Header("Отладка")]
    [Tooltip("U = повернуть собаку, на экране состояние. Выключи в готовой игре")]
    public bool debugKeys = true;

    // движение
    Vector3 vel;
    Vector3 faceVec = Vector3.right;
    bool movingFlag;
    float speedNow;
    float noiseSeed;
    Renderer[] renderers;

    // игры
    Behavior behavior = Behavior.Follow;
    int phase;
    float behaviorClock, behaviorTimer, aheadDist;
    float orbitTheta, orbitDir, orbitTimeLeft;

    // лай
    bool barking;
    float barkHold, barkSoundTimer;
    Vector3 monsterPos;
    AudioSource barkSource;

    // отдых
    float restTimer, idleSwapTimer;
    bool wagNow;

    // анимация
    PlayableGraph graph;
    AnimationMixerPlayable mixer;
    readonly AnimationClipPlayable[] playables = new AnimationClipPlayable[AnimCount];
    readonly AnimationClip[] clips = new AnimationClip[AnimCount];
    readonly int[] inputOf = new int[AnimCount];
    readonly float[] weights = new float[AnimCount];
    bool animReady;
    Anim current = Anim.Idle;

    bool BarkingAtPlayer { get { return behavior == Behavior.RunAhead && phase == 1; } }

    // ---------- старт ----------

    void Awake()
    {
        if (player == null) player = FindFirstObjectByType<PlayerController>();
        if (look == null && player != null) look = player.GetComponent<LookBackController>();
        noiseSeed = Random.value * 100f;

        barkSource = gameObject.AddComponent<AudioSource>();
        barkSource.playOnAwake = false;
        barkSource.spatialBlend = 0.7f;
        barkSource.minDistance = 3f;
        barkSource.maxDistance = 40f;
        barkSource.rolloffMode = AudioRolloffMode.Linear;

        if (model == null && transform.childCount > 0)
            model = transform.GetChild(0).gameObject;

        if (model == null)
        {
            Debug.LogError("DogCompanion: нет модели. Положи модель собаки дочерним объектом " +
                           "к Dog и перетащи в поле Model.");
            return;
        }

        if (!model.scene.IsValid())
            Debug.LogError("DogCompanion: в поле Model стоит префаб из папки Project, а не объект в сцене. " +
                           "Перетащи модель из Hierarchy.");
        else if (!model.transform.IsChildOf(transform))
            Debug.LogWarning("DogCompanion: модель должна быть дочерним объектом Dog, " +
                             "иначе она не будет двигаться вместе с ним.");

        for (Transform t = model.transform; t != null && t != transform; t = t.parent)
            t.gameObject.SetActive(true);

        // Модель в Model должна быть корнем FBX (где лежат и меш, и скелет metarig), а не одним мешем
        if (model.GetComponent<SkinnedMeshRenderer>() != null && model.GetComponentInChildren<Animator>(true) == null)
            Debug.LogWarning("DogCompanion: в Model стоит только меш собаки. Поставь туда корневой объект FBX " +
                             "(тот, у которого внутри есть metarig и меш): иначе поворот и размер не сработают.");

        if (animator == null) animator = model.GetComponentInChildren<Animator>(true);
        if (animator == null)
            Debug.LogWarning("DogCompanion: на модели нет Animator. Проверь Rig у FBX (Generic).");
        else
            animator.applyRootMotion = false;

        renderers = model.GetComponentsInChildren<Renderer>(true);
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            smr.updateWhenOffscreen = true;

        // До подгонки размера собаку не показываем (иначе мелькнёт огромной)
        SetRenderers(false);

        SetupAnimation();
        idleSwapTimer = Random.Range(idleSwapInterval.x, idleSwapInterval.y);
        behaviorClock = Random.Range(behaviorInterval.x, behaviorInterval.y);

        // Ставим собаку рядом с игроком сразу
        if (player != null)
        {
            Vector3 p = player.transform.position;
            transform.position = new Vector3(p.x + followOffsetX, groundY, laneZ);
        }
        transform.rotation = FaceRotation();
    }

    IEnumerator Start()
    {
        // Ждём кадр: границы анимированной модели становятся верными
        yield return null;
        if (model == null) yield break;

        FitAndGround();
        SetRenderers(true);
    }

    void OnDestroy()
    {
        if (graph.IsValid()) graph.Destroy();
    }

    // ---------- размер и положение модели ----------

    void FitAndGround()
    {
        if (renderers == null || renderers.Length == 0)
        {
            Debug.LogWarning("DogCompanion: на модели нет рендереров (MeshRenderer / SkinnedMeshRenderer).");
            return;
        }

        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        Debug.Log("DogCompanion: размер модели до подгонки " + b.size.ToString("0.00") + " м");

        Transform m = model.transform;
        float pivotY = m.position.y;
        float minRel = b.min.y - pivotY;

        if (autoFitHeight && b.size.y > 0.01f)
        {
            float f = targetHeight / b.size.y;
            m.localScale *= f;
            minRel *= f; // низ модели смещается вместе с масштабом
        }

        if (autoGround)
        {
            float dy = groundY - (pivotY + minRel);
            m.position += Vector3.up * dy;
        }
    }

    void SetRenderers(bool visible)
    {
        if (renderers == null) return;
        foreach (var r in renderers)
            if (r != null) r.enabled = visible;
    }

    /// Куда смотрит собака. Поворот модели (Model Rotation Offset) учитывается здесь,
    /// на корне Dog, поэтому Animator модели не может его перебить.
    Quaternion FaceRotation()
    {
        Vector3 f = faceVec;
        f.y = 0f;
        if (f.sqrMagnitude < 0.0001f) f = Vector3.right;
        return Quaternion.LookRotation(f.normalized) * Quaternion.Euler(modelRotationOffset);
    }

    // ---------- каждый кадр ----------

    void Update()
    {
        if (player == null || model == null) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        var kb = Keyboard.current;
        if (debugKeys && kb != null && kb.uKey.wasPressedThisFrame)
        {
            modelRotationOffset.y = (modelRotationOffset.y + 90f) % 360f;
            Debug.Log("DogCompanion: Model Rotation Offset Y = " + modelRotationOffset.y +
                      ". Если собака теперь смотрит правильно, впиши это значение в инспектор.");
        }

        UpdateBark(dt);
        UpdateBehavior(dt);
        UpdateMovement(dt);
        UpdateAnimation(dt);
    }

    // ---------- лай ----------

    void UpdateBark(float dt)
    {
        var monster = StalkerMonster.Instance;
        bool near = false;

        if (monster != null && monster.IsPresent)
        {
            Vector3 mp = monster.transform.position;
            Vector3 dp = transform.position;
            float dx = mp.x - dp.x, dz = mp.z - dp.z;
            float d = Mathf.Sqrt(dx * dx + dz * dz);

            float r = monster.IsHunting ? huntBarkRadius : barkRadius;
            if (barking) r *= 1.15f; // чтобы лай не «мигал» на границе
            near = d <= r;
            monsterPos = mp;
        }

        if (near)
        {
            barking = true;
            barkHold = minBarkTime;
        }
        else if (barking)
        {
            barkHold -= dt;
            if (barkHold <= 0f) barking = false;
        }

        // Звук: и на монстра, и на игрока
        if (barking || BarkingAtPlayer)
        {
            barkSoundTimer -= dt;
            if (barkSoundTimer <= 0f)
            {
                barkSoundTimer = barkSoundInterval * Random.Range(0.8f, 1.2f);
                if (barkSounds != null && barkSounds.Length > 0)
                {
                    var clip = barkSounds[Random.Range(0, barkSounds.Length)];
                    if (clip != null) barkSource.PlayOneShot(clip, barkVolume);
                }
            }
        }
    }

    // ---------- игры (выбегает вперёд, бегает вокруг) ----------

    void UpdateBehavior(float dt)
    {
        bool lookHold = look != null && look.IsBusy;

        // Лает на монстра или игрок оборачивается: никаких игр
        if (barking || lookHold)
        {
            if (behavior != Behavior.Follow) EndBehavior();
            behaviorClock = Mathf.Max(behaviorClock, 3f);
            return;
        }

        Vector3 pos = transform.position;
        float px = player.transform.position.x;

        switch (behavior)
        {
            case Behavior.Follow:
                if (restTimer >= sitAfter) return; // отдыхает: пока не играет
                behaviorClock -= dt;
                if (behaviorClock <= 0f)
                {
                    behaviorClock = Random.Range(behaviorInterval.x, behaviorInterval.y);
                    if (Random.value < behaviorChance) StartRandomBehavior();
                }
                break;

            case Behavior.RunAhead:
                behaviorTimer += dt;
                if (phase == 0)
                {
                    bool reached = Mathf.Abs(pos.x - (px + aheadDist)) < 0.6f;
                    if (reached || behaviorTimer > 6f)
                    {
                        phase = 1;
                        behaviorTimer = 0f;
                        barkSoundTimer = 0f;
                    }
                }
                else
                {
                    // Лает на игрока, пока он не подошёл
                    if (behaviorTimer > barkAtPlayerTime || px >= pos.x - 0.8f) EndBehavior();
                }
                break;

            case Behavior.Orbit:
                orbitTheta += orbitDir * orbitAngularSpeed * dt;
                orbitTimeLeft -= dt;
                if (orbitTimeLeft <= 0f) EndBehavior();
                break;
        }
    }

    void StartRandomBehavior()
    {
        if (!enableRunAhead && !enableOrbit) return;
        bool pickRun = enableRunAhead && (!enableOrbit || Random.value < 0.5f);

        if (pickRun)
        {
            behavior = Behavior.RunAhead;
            phase = 0;
            behaviorTimer = 0f;
            aheadDist = Random.Range(aheadDistance.x, aheadDistance.y);
        }
        else
        {
            behavior = Behavior.Orbit;
            orbitDir = Random.value < 0.5f ? -1f : 1f;
            int laps = Random.Range(1, 3); // 1 или 2 круга
            orbitTimeLeft = laps * Mathf.PI * 2f / Mathf.Max(0.1f, orbitAngularSpeed);

            // Начинаем с того места круга, где собака уже находится
            Vector3 d = transform.position - player.transform.position;
            orbitTheta = Mathf.Atan2(d.z / Mathf.Max(0.1f, orbitRadiusZ), d.x / Mathf.Max(0.1f, orbitRadiusX));
        }
    }

    void EndBehavior()
    {
        behavior = Behavior.Follow;
        phase = 0;
        behaviorClock = Random.Range(behaviorInterval.x, behaviorInterval.y);
    }

    // ---------- движение ----------

    void UpdateMovement(float dt)
    {
        Vector3 pos = transform.position;
        Vector3 pp = player.transform.position;
        float px = pp.x;
        float pv = player.CurrentSpeed; // игрок идёт вправо

        bool lookHold = look != null && look.IsBusy && !barking;
        bool stayOnBark = barking && stopToBark && Mathf.Abs(px - pos.x) < maxStrayWhileBarking;
        bool stayForGame = BarkingAtPlayer;

        Vector3 desired;
        float vmax = maxSpeed;
        float acc = acceleration;

        if (stayOnBark || lookHold || stayForGame)
        {
            desired = Vector3.zero; // стоит
        }
        else
        {
            Vector3 goal;
            Vector3 feed;
            float gain = catchUpGain;
            bool allowRest = false;

            switch (behavior)
            {
                case Behavior.RunAhead: // phase 0: бежит вперёд игрока
                    goal = new Vector3(px + aheadDist, groundY, laneZ);
                    feed = Vector3.zero;
                    gain = 3f;
                    vmax = maxSpeed * 1.15f;
                    break;

                case Behavior.Orbit:
                {
                    float c = Mathf.Cos(orbitTheta), s = Mathf.Sin(orbitTheta);
                    float dth = orbitDir * orbitAngularSpeed;
                    goal = new Vector3(px + orbitRadiusX * c, groundY, pp.z + orbitRadiusZ * s);
                    feed = new Vector3(pv - orbitRadiusX * s * dth, 0f, orbitRadiusZ * c * dth);
                    gain = 5f;
                    vmax = orbitMaxSpeed;
                    acc = acceleration * 1.8f;
                    break;
                }

                default: // Follow: идёт рядом, медленно «гуляя» вперёд-назад
                {
                    float wx = (Mathf.PerlinNoise(Time.time * 0.12f, noiseSeed) - 0.5f) * 2f * followWanderX;
                    float wz = (Mathf.PerlinNoise(Time.time * 0.2f, noiseSeed + 7.7f) - 0.5f) * 2f * followWanderZ;
                    goal = new Vector3(px + followOffsetX + wx, groundY, laneZ + wz);
                    feed = new Vector3(pv, 0f, 0f);
                    allowRest = true;
                    break;
                }
            }

            Vector3 to = goal - pos;
            to.y = 0f;

            // Игрок стоит, собака уже рядом со своей точкой: не дёргается
            if (allowRest && to.magnitude < deadZone && pv < 0.1f)
                desired = Vector3.zero;
            else
                desired = Vector3.ClampMagnitude(feed + to * gain, vmax);
        }

        vel = Vector3.MoveTowards(vel, desired, acc * dt);
        pos += vel * dt;
        pos.y = groundY;
        pos.z = Mathf.Clamp(pos.z, zLimits.x, zLimits.y);
        transform.position = pos;

        Vector3 flat = new Vector3(vel.x, 0f, vel.z);
        speedNow = flat.magnitude;
        if (!movingFlag && speedNow > 0.35f) movingFlag = true;
        else if (movingFlag && speedNow < 0.15f) movingFlag = false;

        // Куда смотрит
        if (barking && stopToBark && !movingFlag)
            faceVec = new Vector3(monsterPos.x - pos.x, 0f, monsterPos.z - pos.z); // на монстра
        else if (BarkingAtPlayer && !movingFlag)
            faceVec = new Vector3(px - pos.x, 0f, pp.z - pos.z);                   // на игрока
        else if (lookHold && !movingFlag)
            faceVec = Vector3.left;                                                // тоже оборачивается
        else if (speedNow > 0.3f)
            faceVec = flat;                                                        // по ходу движения

        transform.rotation = Quaternion.RotateTowards(transform.rotation, FaceRotation(), turnSpeed * dt);
    }

    // ---------- анимация ----------

    void SetupAnimation()
    {
        clips[(int)Anim.Idle] = idle;
        clips[(int)Anim.IdleWag] = idleTailWag;
        clips[(int)Anim.Walk] = walk;
        clips[(int)Anim.Run] = run;
        clips[(int)Anim.Bark] = bark;
        clips[(int)Anim.SitStart] = sitStart;
        clips[(int)Anim.SitLoop] = sitLoop;
        clips[(int)Anim.LayStart] = layStart;
        clips[(int)Anim.LayLoop] = layLoop;

        int count = 0;
        for (int i = 0; i < AnimCount; i++)
            inputOf[i] = clips[i] != null ? count++ : -1;

        if (animator == null || count == 0)
        {
            if (count == 0)
                Debug.LogWarning("DogCompanion: не заданы клипы анимаций. Перетащи клипы из FBX в поля Idle, Walk и т.д.");
            return;
        }

        graph = PlayableGraph.Create("DogAnimator");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        var output = AnimationPlayableOutput.Create(graph, "Animation", animator);
        mixer = AnimationMixerPlayable.Create(graph, count);

        for (int i = 0; i < AnimCount; i++)
        {
            if (inputOf[i] < 0) continue;
            playables[i] = AnimationClipPlayable.Create(graph, clips[i]);
            graph.Connect(playables[i], 0, mixer, inputOf[i]);
            mixer.SetInputWeight(inputOf[i], 0f);
        }

        output.SetSourcePlayable(mixer);
        graph.Play();
        animReady = true;

        current = Resolve(Anim.Idle);
        weights[(int)current] = 1f;
    }

    /// Если нужного клипа нет, берём запасной
    Anim Resolve(Anim a)
    {
        if (clips[(int)a] != null) return a;
        switch (a)
        {
            case Anim.IdleWag: return Resolve(Anim.Idle);
            case Anim.Run: return Resolve(Anim.Walk);
            case Anim.Walk: return Resolve(Anim.Idle);
            case Anim.Bark: return Resolve(Anim.Idle);
            case Anim.SitStart: return clips[(int)Anim.SitLoop] != null ? Anim.SitLoop : Resolve(Anim.Idle);
            case Anim.SitLoop: return clips[(int)Anim.SitStart] != null ? Anim.SitStart : Resolve(Anim.Idle);
            case Anim.LayStart: return clips[(int)Anim.LayLoop] != null ? Anim.LayLoop : Resolve(Anim.Idle);
            case Anim.LayLoop: return clips[(int)Anim.LayStart] != null ? Anim.LayStart : Resolve(Anim.Idle);
            default:
                // idle нет совсем: берём любую
                for (int i = 0; i < AnimCount; i++) if (clips[i] != null) return (Anim)i;
                return Anim.Idle;
        }
    }

    Anim ChooseAnim(float dt)
    {
        bool barkingNow = barking || BarkingAtPlayer;

        // Двигается или лает: отдых сбрасывается
        if (movingFlag || barkingNow)
        {
            restTimer = 0f;
            if (barkingNow && !movingFlag) return Anim.Bark;
            if (movingFlag) return speedNow > runThreshold ? Anim.Run : Anim.Walk;
        }

        restTimer += dt;

        if (restTimer >= layAfter && (layStart != null || layLoop != null))
        {
            float t = restTimer - layAfter;
            return (layStart != null && t < layStart.length) ? Anim.LayStart : Anim.LayLoop;
        }

        if (restTimer >= sitAfter && (sitStart != null || sitLoop != null))
        {
            float t = restTimer - sitAfter;
            return (sitStart != null && t < sitStart.length) ? Anim.SitStart : Anim.SitLoop;
        }

        // Просто стоит: то обычный idle, то виляет хвостом
        idleSwapTimer -= dt;
        if (idleSwapTimer <= 0f)
        {
            idleSwapTimer = Random.Range(idleSwapInterval.x, idleSwapInterval.y);
            wagNow = !wagNow && Random.value < 0.6f;
        }
        return wagNow ? Anim.IdleWag : Anim.Idle;
    }

    static bool IsLooping(Anim a)
    {
        return a == Anim.Idle || a == Anim.IdleWag || a == Anim.Walk || a == Anim.Run ||
               a == Anim.Bark || a == Anim.SitLoop || a == Anim.LayLoop;
    }

    void UpdateAnimation(float dt)
    {
        Anim want = Resolve(ChooseAnim(dt));

        if (!animReady)
        {
            current = want;
            return;
        }

        if (want != current)
        {
            current = want;
            // Заново с начала: Start-клипы, лай и «лежит/сидит»
            if (!(current == Anim.Idle || current == Anim.IdleWag || current == Anim.Walk || current == Anim.Run))
                playables[(int)current].SetTime(0.0);
        }

        // Плавно перетекаем к нужной анимации
        float sum = 0f;
        for (int i = 0; i < AnimCount; i++)
        {
            if (inputOf[i] < 0) continue;
            float target = i == (int)current ? 1f : 0f;
            weights[i] = Mathf.MoveTowards(weights[i], target, blendSpeed * dt);
            sum += weights[i];
        }
        if (sum < 0.0001f) sum = 1f;

        for (int i = 0; i < AnimCount; i++)
        {
            if (inputOf[i] < 0) continue;
            mixer.SetInputWeight(inputOf[i], weights[i] / sum);

            // Зацикливаем вручную, даже если у клипа не включён Loop Time
            if (IsLooping((Anim)i))
            {
                double len = clips[i].length;
                double t = playables[i].GetTime();
                if (len > 0.01 && t >= len) playables[i].SetTime(t % len);
            }
        }

        // Темп шагов под реальную скорость
        if (inputOf[(int)Anim.Walk] >= 0)
            playables[(int)Anim.Walk].SetSpeed(Mathf.Clamp(speedNow / Mathf.Max(0.01f, walkClipSpeed), 0.5f, 2.5f));
        if (inputOf[(int)Anim.Run] >= 0)
            playables[(int)Anim.Run].SetSpeed(Mathf.Clamp(speedNow / Mathf.Max(0.01f, runClipSpeed), 0.6f, 2f));
    }

    // ---------- отладка ----------

    void OnGUI()
    {
        if (!debugKeys) return;
        GUI.Label(new Rect(10f, 32f, 1000f, 30f),
                  "Собака: " + current + ", игра: " + behavior + (barking ? ", лает на монстра" : "") +
                  "   (U = повернуть собаку, сейчас Y " + modelRotationOffset.y + "°)");
    }
}