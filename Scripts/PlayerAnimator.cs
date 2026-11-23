using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// Проигрывает анимации персонажа: стоит / идёт / (бежит) в зависимости от
/// скорости PlayerController. Animator Controller НЕ нужен: достаточно
/// перетащить в инспектор клипы. Повесь на Player.
[RequireComponent(typeof(PlayerController))]
public class PlayerAnimator : MonoBehaviour
{
    [Tooltip("Пусто = найдётся у модели внутри Player")]
    public Animator animator;

    [Header("Клипы (берутся из FBX с анимацией, не сам FBX!)")]
    public AnimationClip idleClip;
    public AnimationClip walkClip;
    public AnimationClip runClip;   // необязательно

    [Header("Синхронизация шагов со скоростью")]
    [Tooltip("Сколько метров в секунду «проходит» персонаж в клипе ходьбы. " +
             "Если ноги скользят: больше число = анимация быстрее. Подбирай 0.8–1.6")]
    public float walkClipSpeed = 1.2f;
    public float runClipSpeed = 3.5f;
    [Tooltip("Как быстро плавно переходим между стоянием и ходьбой")]
    public float blendSharpness = 8f;

    PlayerController player;
    PlayableGraph graph;
    AnimationMixerPlayable mixer;
    AnimationClipPlayable idleP, walkP, runP;
    bool hasRun, ready;
    float smoothSpeed;

    void Start()
    {
        player = GetComponent<PlayerController>();

        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator == null || idleClip == null || walkClip == null)
        {
            Debug.LogWarning("PlayerAnimator: нужен Animator на модели и клипы Idle и Walk.");
            return;
        }

        animator.applyRootMotion = false;
        hasRun = runClip != null;

        graph = PlayableGraph.Create("PlayerAnimator");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        var output = AnimationPlayableOutput.Create(graph, "Animation", animator);

        mixer = AnimationMixerPlayable.Create(graph, hasRun ? 3 : 2);
        idleP = AnimationClipPlayable.Create(graph, idleClip);
        walkP = AnimationClipPlayable.Create(graph, walkClip);
        graph.Connect(idleP, 0, mixer, 0);
        graph.Connect(walkP, 0, mixer, 1);
        if (hasRun)
        {
            runP = AnimationClipPlayable.Create(graph, runClip);
            graph.Connect(runP, 0, mixer, 2);
        }

        output.SetSourcePlayable(mixer);
        graph.Play();
        ready = true;
    }

    void Update()
    {
        if (!ready) return;

        smoothSpeed = Mathf.Lerp(smoothSpeed, player.CurrentSpeed,
                                 1f - Mathf.Exp(-blendSharpness * Time.deltaTime));

        float walkMax = Mathf.Max(0.01f, player.walkSpeed);
        float idleW = 0f, walkW = 0f, runW = 0f;

        if (smoothSpeed <= walkMax || !hasRun)
        {
            walkW = Mathf.Clamp01(smoothSpeed / walkMax);
            idleW = 1f - walkW;
        }
        else
        {
            runW = Mathf.Clamp01((smoothSpeed - walkMax) / Mathf.Max(0.01f, player.runSpeed - walkMax));
            walkW = 1f - runW;
        }

        mixer.SetInputWeight(0, idleW);
        mixer.SetInputWeight(1, walkW);
        if (hasRun) mixer.SetInputWeight(2, runW);

        // Скорость шагов под реальную скорость движения (меньше скольжения ног)
        walkP.SetSpeed(Mathf.Max(0.3f, Mathf.Min(smoothSpeed, walkMax) / Mathf.Max(0.01f, walkClipSpeed)));
        if (hasRun) runP.SetSpeed(Mathf.Max(0.3f, smoothSpeed / Mathf.Max(0.01f, runClipSpeed)));
    }

    void OnDestroy()
    {
        if (graph.IsValid()) graph.Destroy();
    }
}
