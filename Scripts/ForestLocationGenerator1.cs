using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// Тестовая локация «ночной лес» из примитивов (без ассетов).
/// Задний план: стволы деревьев слоями. Передний план: трава.
/// Ещё земля, забор, туман и тёмный свет. Генерируется при старте игры.
/// Потом заменишь примитивы на свои модели/префабы.
public class ForestLocationGenerator : MonoBehaviour
{
    [Header("Общее")]
    public bool generateOnStart = true;
    public int seed = 12345;
    public float startX = -20f;
    public float length = 150f;

    [Header("Атмосфера")]
    public Color fogColor = new Color(0.03f, 0.05f, 0.10f);
    public float fogDensity = 0.035f;
    public Color ambientColor = new Color(0.04f, 0.06f, 0.10f);

    [Header("Задний план: деревья")]
    public float[] treeLayerZ = { 5f, 10f, 17f, 26f };
    public Vector2 treeSpacing = new Vector2(2f, 4.5f);
    public Vector2 treeHeight = new Vector2(8f, 14f);
    public Vector2 treeThickness = new Vector2(0.5f, 1.1f);
    public Color treeNear = new Color(0.05f, 0.05f, 0.08f);
    public Color treeFar = new Color(0.10f, 0.14f, 0.24f);

    [Header("Передний план: трава")]
    public Vector2 grassZRange = new Vector2(-4.5f, -1.5f);
    public int grassClustersPer10m = 22;
    public Vector2 grassHeight = new Vector2(0.5f, 1.4f);
    public Color grassColor = new Color(0.12f, 0.22f, 0.16f);

    [Header("Свои модели (необязательно)")]
    [Tooltip("Перетащи сюда префабы деревьев. Если пусто, будут примитивы")]
    public GameObject[] treePrefabs;
    [Tooltip("Префабы травы / кустов для переднего плана")]
    public GameObject[] grassPrefabs;
    public Vector2 prefabScale = new Vector2(0.8f, 1.3f);
    [Tooltip("Выключи для плоских картинок (спрайтов), иначе они будут разворачиваться боком")]
    public bool randomYRotation = true;

    [Header("Забор")]
    public bool buildFence = true;
    public float fenceZ = 1.5f;
    public float postSpacing = 3f;

    [Header("NavMesh для монстра")]
    [Tooltip("Построить карту проходимости, чтобы монстр обходил деревья и забор. " +
             "У своих префабов деревьев должен быть Collider (например Capsule Collider)")]
    public bool buildNavMesh = true;

    Transform root;
    NavMeshData navData;
    NavMeshDataInstance navInstance;
    System.Random rng;

    void Start()
    {
        if (generateOnStart) Generate();
    }

    [ContextMenu("Generate")]
    public void Generate()
    {
        Clear();
        rng = new System.Random(seed);
        root = new GameObject("Forest_Generated").transform;
        root.SetParent(transform, false);

        SetupAtmosphere();
        BuildGround();
        BuildTrees();
        BuildGrass();
        if (buildFence) BuildFence();
        if (buildNavMesh) BakeNavMesh();
    }

    [ContextMenu("Clear")]
    public void Clear()
    {
        if (navInstance.valid) navInstance.Remove();
        if (root != null) Destroy(root.gameObject);
    }

    // ---------- блоки ----------

    void SetupAtmosphere()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogDensity = fogDensity;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = ambientColor;

        var cam = Camera.main;
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = fogColor;
        }
    }

    void BuildGround()
    {
        var mat = MakeMat(new Color(0.07f, 0.10f, 0.09f));
        float cx = startX + length * 0.5f;
        Prim(PrimitiveType.Cube, "Ground",
            new Vector3(cx, -0.5f, 10f),
            new Vector3(length + 80f, 1f, 80f), mat, true);
    }

    void BuildTrees()
    {
        int n = treeLayerZ.Length;
        for (int layer = 0; layer < n; layer++)
        {
            float t = n > 1 ? (float)layer / (n - 1) : 0f;
            var mat = MakeMat(Color.Lerp(treeNear, treeFar, t));

            float x = startX - 10f;
            while (x < startX + length + 10f)
            {
                float h = Rand(treeHeight.x, treeHeight.y);
                float th = Rand(treeThickness.x, treeThickness.y);
                float z = treeLayerZ[layer] + Rand(-1.5f, 1.5f);

                GameObject customTree = PickPrefab(treePrefabs);
                if (customTree != null)
                {
                    Spawn(customTree, new Vector3(x, 0f, z));
                    x += Rand(treeSpacing.x, treeSpacing.y);
                    continue;
                }

                // цилиндр высотой 2 по умолчанию: scale.y = h/2
                var tree = Prim(PrimitiveType.Cylinder, "Tree_L" + layer,
                    new Vector3(x, h * 0.5f, z),
                    new Vector3(th, h * 0.5f, th), mat, buildNavMesh);
                tree.transform.rotation = Quaternion.Euler(0f, 0f, Rand(-2f, 2f));

                x += Rand(treeSpacing.x, treeSpacing.y);
            }
        }
    }

    void BuildGrass()
    {
        var mat = MakeMat(grassColor);
        int clusters = Mathf.RoundToInt(length / 10f * grassClustersPer10m);

        for (int i = 0; i < clusters; i++)
        {
            float cx = startX + Rand(0f, length);
            float cz = Rand(grassZRange.x, grassZRange.y);

            GameObject customGrass = PickPrefab(grassPrefabs);
            if (customGrass != null)
            {
                Spawn(customGrass, new Vector3(cx, 0f, cz));
                continue;
            }

            int blades = rng.Next(3, 7);

            for (int b = 0; b < blades; b++)
            {
                float h = Rand(grassHeight.x, grassHeight.y);
                float x = cx + Rand(-0.25f, 0.25f);
                var blade = Prim(PrimitiveType.Cube, "Grass",
                    new Vector3(x, h * 0.5f, cz + Rand(-0.2f, 0.2f)),
                    new Vector3(0.07f, h, 0.02f), mat, false);
                blade.transform.rotation = Quaternion.Euler(0f, 0f, Rand(-18f, 18f));
            }
        }
    }

    void BuildFence()
    {
        var mat = MakeMat(new Color(0.12f, 0.08f, 0.05f));
        for (float x = startX; x < startX + length; x += postSpacing)
            Prim(PrimitiveType.Cube, "FencePost",
                new Vector3(x, 0.6f, fenceZ), new Vector3(0.12f, 1.2f, 0.12f), mat, buildNavMesh);

        float cx = startX + length * 0.5f;
        Prim(PrimitiveType.Cube, "FenceRail",
            new Vector3(cx, 0.95f, fenceZ), new Vector3(length, 0.07f, 0.05f), mat, buildNavMesh);
        Prim(PrimitiveType.Cube, "FenceRail",
            new Vector3(cx, 0.55f, fenceZ), new Vector3(length, 0.07f, 0.05f), mat, buildNavMesh);
    }

    // ---------- NavMesh ----------

    /// Строит NavMesh по коллайдерам леса (земля, стволы, забор). Работает в фоне.
    void BakeNavMesh()
    {
        Physics.SyncTransforms(); // коллайдеры только что созданы: обновляем их позиции

        var sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(root, ~0, NavMeshCollectGeometry.PhysicsColliders, 0,
                                      new List<NavMeshBuildMarkup>(), sources);

        var settings = NavMesh.GetSettingsByID(0); // стандартный агент Humanoid
        float cx = startX + length * 0.5f;
        var bounds = new Bounds(new Vector3(cx, 8f, 10f), new Vector3(length + 60f, 30f, 70f));

        navData = new NavMeshData(settings.agentTypeID);
        navInstance = NavMesh.AddNavMeshData(navData);
        NavMeshBuilder.UpdateNavMeshDataAsync(navData, settings, sources, bounds);
    }

    void OnDestroy()
    {
        if (navInstance.valid) navInstance.Remove();
    }

    // ---------- хелперы ----------

    GameObject PickPrefab(GameObject[] arr)
    {
        if (arr == null || arr.Length == 0) return null;
        return arr[rng.Next(arr.Length)];
    }

    void Spawn(GameObject prefab, Vector3 pos)
    {
        var go = Instantiate(prefab, root);
        go.transform.localPosition = pos;
        float yaw = randomYRotation ? Rand(0f, 360f) : 0f;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = Vector3.one * Rand(prefabScale.x, prefabScale.y);
    }

    float Rand(float a, float b)
    {
        return a + (float)rng.NextDouble() * (b - a);
    }

    Material MakeMat(Color c)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var m = new Material(shader);
        m.SetColor("_BaseColor", c);
        m.SetColor("_Color", c);
        m.SetFloat("_Smoothness", 0f);
        m.enableInstancing = true;
        return m;
    }

    GameObject Prim(PrimitiveType type, string name, Vector3 pos, Vector3 scale,
                    Material mat, bool keepCollider)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(root, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (!keepCollider) Destroy(go.GetComponent<Collider>());
        return go;
    }
}