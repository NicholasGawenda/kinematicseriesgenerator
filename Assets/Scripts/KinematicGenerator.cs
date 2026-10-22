using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Random = UnityEngine.Random;

/// <summary>
/// Monolithic Synthetic Dataset Generator for Unity URP.
/// Unifies dynamic asset loading from designated directories and Inspector fields,
/// procedural physics, deterministic sub-stepping, flat ambient shading, frame capture via WaitForEndOfFrame,
/// concurrent metadata.csv serialization, selective material color randomization (preserving textured objects),
/// adaptive object scaling with simulated distance sensor calculation,
/// structured filename encoding c[{count}]_v[{velocity}]_d[{directionX}]x[{directionY}]y[{directionZ}]z_k[{kind}]_p[{posX}]x[{posY}]y[{posZ}]z.png,
/// and automated URP CameraAndObjects Motion Blur configuration for ground-truth velocity point spread.
/// </summary>
public class KinematicGenerator : MonoBehaviour
{
    [Header("Environment Geometry (Three Walls & Ground)")]
    [Tooltip("Ground / floor object in the scene.")]
    [SerializeField] private GameObject ground;

    [Tooltip("Middle / back wall. The camera positions itself opposite of this wall, facing towards the spawn container.")]
    [SerializeField] private GameObject middleWall;

    [Tooltip("Left bounding wall.")]
    [SerializeField] private GameObject leftWall;

    [Tooltip("Right bounding wall.")]
    [SerializeField] private GameObject rightWall;

    [Header("Environment Materials (Optional)")]
    [Tooltip("Optional material for ground.")]
    [SerializeField] private Material groundMaterial;

    [Tooltip("Optional material for middle wall.")]
    [SerializeField] private Material middleWallMaterial;

    [Tooltip("Optional material for left wall.")]
    [SerializeField] private Material leftWallMaterial;

    [Tooltip("Optional material for right wall.")]
    [SerializeField] private Material rightWallMaterial;

    [Header("Camera & Visual Configuration")]
    [Tooltip("Camera used to render captures and compute proximity ground truth.")]
    [SerializeField] private Camera screenshotCamera;

    [Tooltip("Optional transform focal point for camera to look at (if autoPositionCamera is disabled).")]
    [SerializeField] private Transform cameraLookAtTarget;

    [Header("Camera Automatic Alignment & Pose Jitter")]
    [Tooltip("Automatically position the camera opposite of the middle wall, facing towards the spawn container.")]
    [SerializeField] private bool autoPositionCamera = true;

    [Tooltip("Distance from spawn container to camera along the middle wall opposing axis.")]
    [SerializeField] private float cameraDistance = 10.0f;

    [Tooltip("Height offset of the camera relative to the spawn container.")]
    [SerializeField] private float cameraHeightOffset = 1.0f;

    [Tooltip("Toggle slight random perturbations in camera position and rotation per simulation run.")]
    [SerializeField] private bool randomizeCameraPose = true;

    [Tooltip("Max random camera position offset (in meters) around nominal camera position.")]
    [SerializeField] private Vector3 cameraPositionJitter = new Vector3(0.35f, 0.20f, 0.35f);

    [Tooltip("Max random camera rotation offset (in degrees) around nominal camera orientation.")]
    [SerializeField] private Vector3 cameraRotationJitter = new Vector3(1.5f, 2.0f, 1.0f);

    [Header("Spawn Position & Range Settings")]
    [Tooltip("Reference to the empty GameObject or Transform in the scene defining the spawn anchor / container. Drag your empty GameObject here!")]
    [SerializeField] private Transform spawnContainer;

    [Tooltip("Fallback spawn center in world coordinates if spawnContainer is not assigned.")]
    [SerializeField] private Vector3 spawnCenter = new Vector3(0f, 0f, 0f);

    [Tooltip("Elevation height offset applied above the spawn container/center to keep objects well above the floor.")]
    [SerializeField] private float spawnElevationOffset = 5.0f;

    [Tooltip("Radius of the potential spawn sphere.")]
    [SerializeField] private float spawnRadius = 2.0f;

    [Tooltip("Minimum radius (inner deadzone) if you want a hollow spherical shell instead of solid sphere (0 = solid sphere).")]
    [SerializeField] private float minSpawnRadius = 0.0f;

    [Tooltip("Strict minimum ground clearance (Y elevation) to ensure objects never spawn on or intersecting the floor.")]
    [SerializeField] private float minimumGroundClearance = 3.5f;

    [Header("Spawnable Objects Configuration")]
    [Tooltip("Manual list of prefabs to spawn. Drag custom prefabs (e.g. Tape Dispenser, Calculator, etc.) here or leave empty to auto-load from directories.")]
    [SerializeField] private GameObject[] spawnablePrefabs;

    [Tooltip("Designated project directory path containing prefabs to spawn.")]
    [SerializeField] private string spawnablesDirectory = "Assets/Spawnables";

    [Header("Object Scale & Simulated Distance Sensor")]
    [Tooltip("Minimum scale for standard primitive objects (Cube, Sphere, Cylinder, Capsule).")]
    [SerializeField] private float minSize = 0.8f;

    [Tooltip("Maximum scale for standard primitive objects.")]
    [SerializeField] private float maxSize = 1.6f;

    [Tooltip("Minimum scale for custom imported objects (Tape dispenser, Calculator, etc.) which have smaller real-world dimensions.")]
    [SerializeField] private float customModelMinSize = 3.5f;

    [Tooltip("Maximum scale for custom imported objects.")]
    [SerializeField] private float customModelMaxSize = 6.0f;

    [Tooltip("Base reference scale corresponding to 1:1 true physical distance.")]
    [SerializeField] private float referenceSize = 1.0f;

    [Tooltip("Optionally append _dist[{distance}] to the image filename.")]
    [SerializeField] private bool includeDistanceInFilename = false;

    [Tooltip("Decimal places for floating-point numbers in generated filenames (e.g. 2 for 0.00).")]
    [Range(1, 4)]
    [SerializeField] private int filenameFloatPrecision = 2;

    [Header("Spawn Sphere Visualization (Gizmos)")]
    [Tooltip("Show the spawn radius sphere visualization in the Scene view while adjusting ranges.")]
    [SerializeField] private bool showSpawnGizmos = true;

    [Tooltip("Color of the visible spawn sphere.")]
    [SerializeField] private Color spawnSphereColor = new Color(0.2f, 0.75f, 1.0f, 0.35f);

    [Header("Selective Material & Color Randomization")]
    [Tooltip("Target material for selective color randomization. Only objects or submeshes utilizing this material will have randomized colors applied. Textured objects will preserve their original textures!")]
    [SerializeField] private Material spawnableMaterial;

    [Tooltip("Enable randomized color assignment on objects containing spawnableMaterial.")]
    [SerializeField] private bool randomizeSpawnableColor = true;

    [Tooltip("Optional color palette. If empty, generates vibrant random HSV colors.")]
    [SerializeField] private Color[] colorPalette;

    [Header("URP Motion Blur Configuration")]
    [Tooltip("Enable programmatic URP Motion Blur volume override.")]
    [SerializeField] private bool enableMotionBlur = true;

    [Tooltip("Motion blur intensity (0 to 1). Higher values produce longer blur streaks.")]
    [Range(0f, 1f)]
    [SerializeField] private float motionBlurIntensity = 1.0f;

    [Tooltip("Motion blur quality setting.")]
    [SerializeField] private MotionBlurQuality motionBlurQuality = MotionBlurQuality.High;

    [Tooltip("Motion blur clamp limit (maximum blur radius as fraction of screen).")]
    [Range(0.01f, 1f)]
    [SerializeField] private float motionBlurClamp = 0.25f;

    [Header("Simulation & Framerate Parameters")]
    [Tooltip("Framerate lock for synchronous capture (capturesPerSecond).")]
    [Range(10, 120)]
    [SerializeField] private int capturesPerSecond = 30;

    [Tooltip("Physics oversample multiplier (fixedDeltaTime = 1 / (fps * multiplier)).")]
    [SerializeField] private int oversampleMultiplier = 4;

    [Tooltip("Solver iteration count for extreme collision and trajectory fidelity.")]
    [SerializeField] private int solverIterations = 50;

    [Tooltip("Total number of distinct simulation runs to generate before stopping.")]
    [SerializeField] private int targetRuns = 10;

    [Tooltip("Maximum number of frames captured per simulation run.")]
    [SerializeField] private int framesPerRun = 35;

    [Header("Run Optimization & Pause Reduction")]
    [Tooltip("End run early when object leaves camera view frustum or falls below minimum Y coordinate.")]
    [SerializeField] private bool terminateWhenOutOfBounds = true;
    [SerializeField] private float minimumYBound = -4.0f;

    [Tooltip("End run early if the object collides and settles/rests to avoid recording static pause frames.")]
    [SerializeField] private bool terminateOnRest = true;
    [SerializeField] private float restVelocityThreshold = 0.25f;
    [SerializeField] private int maxRestFrames = 5;
    [SerializeField] private int minFramesBeforeEarlyExit = 15;

    [Tooltip("Cooldown delay in seconds between runs (0 = immediate next run).")]
    [SerializeField] private float interRunCooldownSeconds = 0.0f;

    [Header("Kinematic Impulse & Trajectory")]
    [Tooltip("Min initial speed in m/s (transverse to camera for strong motion blur streaks).")]
    [SerializeField] private float minInitialSpeed = 6.0f;
    [Tooltip("Max initial speed in m/s.")]
    [SerializeField] private float maxInitialSpeed = 12.0f;
    [SerializeField] private Vector3 velocityJitter = new Vector3(1.5f, 1.0f, 1.5f);
    [Tooltip("Loft impulse applied upward to produce a parabolic ballistic arc in mid-air.")]
    [SerializeField] private Vector2 verticalLoftRange = new Vector2(1.5f, 4.0f);
    [SerializeField] private float angularImpulseMax = 6.0f;
    [SerializeField] private bool enableGravity = true;

    [Header("Storage Configuration")]
    [Tooltip("Output directory path relative to project root or absolute.")]
    [SerializeField] private string outputDirectory = "Dataset";

    [Header("Time of Day Randomization")]
    [Tooltip("Toggle randomized time of day per simulation run, driving directional light angle, sunlight color, and skybox lighting.")]
    [SerializeField] private bool randomizeTimeOfDay = true;

    [Tooltip("Optional reference to the scene Directional Light representing the Sun. If null, auto-discovered in scene.")]
    [SerializeField] private Light sceneDirectionalLight;

    [Tooltip("Sun elevation/pitch angle range in degrees (e.g. 10 deg = dawn/dusk, 75 deg = high noon).")]
    [SerializeField] private Vector2 sunPitchRange = new Vector2(10.0f, 75.0f);

    [Tooltip("Sun azimuth/yaw rotation range in degrees.")]
    [SerializeField] private Vector2 sunYawRange = new Vector2(-160.0f, 160.0f);

    [Tooltip("Sunlight intensity range from dawn/dusk (min) to noon (max).")]
    [SerializeField] private Vector2 sunIntensityRange = new Vector2(0.8f, 2.2f);

    [Header("Environment Colors Randomization")]
    [Tooltip("Toggle random color changes for all walls and the ground per simulation run.")]
    [SerializeField] private bool randomizeEnvironmentColors = true;

    [Tooltip("Optional custom palette for wall colors. If empty, generates harmonious architectural tones.")]
    [SerializeField] private Color[] wallColorPalette;

    [Tooltip("Optional custom palette for ground colors. If empty, generates harmonious floor tones.")]
    [SerializeField] private Color[] groundColorPalette;

    [Header("Lighting & Material Settings")]
    [Tooltip("Enforce flat ambient lighting and disable directional shadows.")]
    [SerializeField] private bool enforceFlatLighting = true;
    [SerializeField] private Color flatAmbientColor = new Color(0.9f, 0.9f, 0.9f, 1f);

    // Public properties for external query
    public GameObject Ground => ground;
    public GameObject MiddleWall => middleWall;
    public GameObject LeftWall => leftWall;
    public GameObject RightWall => rightWall;
    public float SpawnRadius => spawnRadius;
    public float SpawnElevationOffset => spawnElevationOffset;
    public GameObject[] SpawnablePrefabs => _spawnables;
    public float MinSize => minSize;
    public float MaxSize => maxSize;
    public float CustomModelMinSize => customModelMinSize;
    public float CustomModelMaxSize => customModelMaxSize;
    public float CurrentScale => _currentScale;
    public float DistanceSensor => _currentDistanceSensor;
    public bool RandomizeTimeOfDay { get => randomizeTimeOfDay; set => randomizeTimeOfDay = value; }
    public bool RandomizeEnvironmentColors { get => randomizeEnvironmentColors; set => randomizeEnvironmentColors = value; }
    public bool RandomizeCameraPose { get => randomizeCameraPose; set => randomizeCameraPose = value; }

    // Runtime state
    private GameObject[] _spawnables;
    private StreamWriter _csvWriter;
    private GameObject _currentSimObject;
    private Rigidbody _currentRb;
    private PhysicsMaterial _proceduralMaterial;
    private Volume _runtimeVolume;
    private MotionBlur _motionBlur;
    private string _sessionTimestamp;
    private string _currentRunDirectory;
    private float _currentBlurIntensity = 0f;
    private float _currentScale = 1.0f;
    private float _currentDistanceSensor = 0f;
    private int _currentKindIndex;
    private bool _isGenerating;

    void Awake()
    {
        ParseCommandLineOverrides();
        InitializeCamera();
        InitializeSpawnContainer();
        InitializeSpawnableMaterial();
        InitializeEnvironment();
        UpdateCameraPosition();
        InitializePhysicsMaterial();
        InitializeMotionBlurVolume();
        ApplyLightingEnvironment();
        LoadSpawnables();
    }

    void Start()
    {
        ConfigureDeterministicPhysics();
        UpdateCameraPosition();
        _sessionTimestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        StartCoroutine(ExecuteDatasetGeneration());
    }

    private void OnValidate()
    {
        spawnRadius = Mathf.Max(0.1f, spawnRadius);
        minSpawnRadius = Mathf.Clamp(minSpawnRadius, 0f, spawnRadius);
        spawnElevationOffset = Mathf.Max(1.0f, spawnElevationOffset);
        minimumGroundClearance = Mathf.Max(1.0f, minimumGroundClearance);
        minSize = Mathf.Max(0.05f, minSize);
        maxSize = Mathf.Max(minSize, maxSize);
        customModelMinSize = Mathf.Max(0.05f, customModelMinSize);
        customModelMaxSize = Mathf.Max(customModelMinSize, customModelMaxSize);
        referenceSize = Mathf.Max(0.05f, referenceSize);
        cameraDistance = Mathf.Max(1.0f, cameraDistance);
        capturesPerSecond = Mathf.Clamp(capturesPerSecond, 10, 120);

        if (autoPositionCamera)
        {
            UpdateCameraPosition();
        }
    }

    private void ParseCommandLineOverrides()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-targetRuns" && i + 1 < args.Length && int.TryParse(args[i + 1], out int runs))
            {
                targetRuns = runs;
            }
            else if (args[i] == "-framesPerRun" && i + 1 < args.Length && int.TryParse(args[i + 1], out int frames))
            {
                framesPerRun = frames;
            }
            else if (args[i] == "-capturesPerSecond" && i + 1 < args.Length && int.TryParse(args[i + 1], out int fps))
            {
                capturesPerSecond = Mathf.Clamp(fps, 10, 120);
            }
            else if (args[i] == "-minSize" && i + 1 < args.Length && float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float minS))
            {
                minSize = Mathf.Max(0.05f, minS);
            }
            else if (args[i] == "-maxSize" && i + 1 < args.Length && float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float maxS))
            {
                maxSize = Mathf.Max(minSize, maxS);
            }
            else if (args[i] == "-customModelMinSize" && i + 1 < args.Length && float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float cMinS))
            {
                customModelMinSize = Mathf.Max(0.05f, cMinS);
            }
            else if (args[i] == "-customModelMaxSize" && i + 1 < args.Length && float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float cMaxS))
            {
                customModelMaxSize = Mathf.Max(customModelMinSize, cMaxS);
            }
            else if (args[i] == "-randomizeTimeOfDay" && i + 1 < args.Length && bool.TryParse(args[i + 1], out bool rTod))
            {
                randomizeTimeOfDay = rTod;
            }
            else if (args[i] == "-randomizeEnvironmentColors" && i + 1 < args.Length && bool.TryParse(args[i + 1], out bool rEnv))
            {
                randomizeEnvironmentColors = rEnv;
            }
            else if (args[i] == "-randomizeCameraPose" && i + 1 < args.Length && bool.TryParse(args[i + 1], out bool rCam))
            {
                randomizeCameraPose = rCam;
            }
            else if (args[i] == "-outputDirectory" && i + 1 < args.Length)
            {
                outputDirectory = args[i + 1];
            }
            else if (args[i] == "-spawnablesDirectory" && i + 1 < args.Length)
            {
                spawnablesDirectory = args[i + 1];
            }
        }
    }

    private void InitializeCamera()
    {
        if (screenshotCamera == null)
        {
            screenshotCamera = GetComponent<Camera>();
            if (screenshotCamera == null)
            {
                screenshotCamera = Camera.main;
            }
            if (screenshotCamera == null)
            {
                screenshotCamera = FindFirstObjectByType<Camera>();
            }
        }

        if (screenshotCamera != null)
        {
            var camData = screenshotCamera.GetUniversalAdditionalCameraData();
            if (camData != null)
            {
                camData.renderPostProcessing = true;
            }
        }
    }

    private void InitializeMotionBlurVolume()
    {
        if (!enableMotionBlur) return;

        Volume[] volumes = FindObjectsByType<Volume>(FindObjectsSortMode.None);
        foreach (var vol in volumes)
        {
            VolumeProfile prof = vol.profile != null ? vol.profile : vol.sharedProfile;
            if (prof != null && prof.TryGet<MotionBlur>(out var mb))
            {
                _runtimeVolume = vol;
                _motionBlur = mb;
                break;
            }
        }

        if (_motionBlur == null)
        {
            GameObject volObj = new GameObject("_RuntimeMotionBlurVolume");
            _runtimeVolume = volObj.AddComponent<Volume>();
            _runtimeVolume.isGlobal = true;
            _runtimeVolume.priority = 100f;

            VolumeProfile runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            runtimeProfile.name = "RuntimeMotionBlurProfile";
            _motionBlur = runtimeProfile.Add<MotionBlur>(true);
            _runtimeVolume.profile = runtimeProfile;
        }

        if (_motionBlur != null)
        {
            _motionBlur.active = true;
            _motionBlur.mode.overrideState = true;
            _motionBlur.mode.value = MotionBlurMode.CameraAndObjects;

            _motionBlur.quality.overrideState = true;
            _motionBlur.quality.value = motionBlurQuality;

            _motionBlur.intensity.overrideState = true;
            _motionBlur.intensity.value = motionBlurIntensity;

            _motionBlur.clamp.overrideState = true;
            _motionBlur.clamp.value = motionBlurClamp;

            _currentBlurIntensity = _motionBlur.intensity.value;
            Debug.Log($"[KinematicGenerator] MotionBlur configured: Mode={_motionBlur.mode.value}, Quality={_motionBlur.quality.value}, Intensity={_currentBlurIntensity:F2}");
        }
    }

    private void InitializeSpawnContainer()
    {
        if (spawnContainer == null)
        {
            GameObject found = GameObject.Find("SpawnContainer") ?? GameObject.Find("SpawnPoint");
            if (found != null)
            {
                spawnContainer = found.transform;
                Debug.Log($"[KinematicGenerator] Auto-bound spawnContainer to: '{found.name}' at position {spawnContainer.position}");
            }
        }

        GameObject residual = GameObject.Find("FallingObject");
        if (residual != null && (spawnContainer == null || residual.transform != spawnContainer))
        {
            var rend = residual.GetComponent<Renderer>();
            if (rend != null) rend.enabled = false;
            var col = residual.GetComponent<Collider>();
            if (col != null) col.enabled = false;
            Debug.Log("[KinematicGenerator] Deactivated residual scene object 'FallingObject' to ensure clean simulation environment.");
        }
    }

    /// <summary>
    /// Auto-discovers spawnableMaterial if unassigned in Inspector by searching Assets and Resources.
    /// </summary>
    private void InitializeSpawnableMaterial()
    {
        if (spawnableMaterial == null)
        {
            spawnableMaterial = Resources.Load<Material>("spawnableMaterial") ??
                                Resources.Load<Material>("FallingObject");
#if UNITY_EDITOR
            if (spawnableMaterial == null)
            {
                string[] guids = UnityEditor.AssetDatabase.FindAssets("spawnableMaterial t:Material");
                if (guids.Length > 0)
                {
                    spawnableMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
                }
            }
            if (spawnableMaterial == null)
            {
                string[] guids = UnityEditor.AssetDatabase.FindAssets("FallingObject t:Material");
                if (guids.Length > 0)
                {
                    spawnableMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
                }
            }
#endif
            if (spawnableMaterial != null)
            {
                Debug.Log($"[KinematicGenerator] Auto-bound spawnableMaterial to: '{spawnableMaterial.name}'");
            }
        }
    }

    private void InitializeEnvironment()
    {
        if (ground == null) ground = GameObject.Find("Ground") ?? GameObject.Find("Floor");
        if (middleWall == null) middleWall = GameObject.Find("CenterWall") ?? GameObject.Find("MiddleWall") ?? GameObject.Find("BackWall");
        if (leftWall == null) leftWall = GameObject.Find("LeftWall");
        if (rightWall == null) rightWall = GameObject.Find("RightWall");

        ApplyMaterialIfAssigned(ground, groundMaterial);
        ApplyMaterialIfAssigned(middleWall, middleWallMaterial);
        ApplyMaterialIfAssigned(leftWall, leftWallMaterial);
        ApplyMaterialIfAssigned(rightWall, rightWallMaterial);
    }

    private void ApplyMaterialIfAssigned(GameObject targetObj, Material mat)
    {
        if (targetObj == null || mat == null) return;
        var renderers = targetObj.GetComponentsInChildren<Renderer>();
        foreach (var rend in renderers)
        {
            rend.material = mat;
        }
    }

    public void UpdateCameraPosition()
    {
        if (screenshotCamera == null)
        {
            screenshotCamera = GetComponent<Camera>();
            if (screenshotCamera == null) screenshotCamera = Camera.main;
            if (screenshotCamera == null) screenshotCamera = FindFirstObjectByType<Camera>();
            if (screenshotCamera == null) return;
        }

        Vector3 spawnTarget = spawnContainer != null ? spawnContainer.position : spawnCenter;
        Vector3 midAirTarget = new Vector3(spawnTarget.x, Mathf.Max(2.5f, spawnTarget.y + (spawnElevationOffset * 0.45f)), spawnTarget.z);

        Vector3 camPos;
        Quaternion nominalRot;

        if (middleWall != null)
        {
            Vector3 wallPos = middleWall.transform.position;
            Vector3 wallToSpawn = spawnTarget - wallPos;

            Vector3 dirFromWall = new Vector3(wallToSpawn.x, 0f, wallToSpawn.z);
            if (dirFromWall.sqrMagnitude < 0.001f)
            {
                dirFromWall = -middleWall.transform.forward;
                dirFromWall.y = 0f;
                if (dirFromWall.sqrMagnitude < 0.001f) dirFromWall = Vector3.back;
            }
            dirFromWall.Normalize();

            camPos = spawnTarget + dirFromWall * cameraDistance;
            camPos.y = midAirTarget.y + cameraHeightOffset;
            nominalRot = Quaternion.LookRotation(midAirTarget - camPos);
        }
        else if (cameraLookAtTarget != null)
        {
            camPos = screenshotCamera.transform.position;
            nominalRot = Quaternion.LookRotation(cameraLookAtTarget.position - camPos);
        }
        else
        {
            camPos = screenshotCamera.transform.position;
            nominalRot = Quaternion.LookRotation(midAirTarget - camPos);
        }

        // Apply slight random camera position and rotation perturbations if enabled
        if (randomizeCameraPose)
        {
            Vector3 posJitter = new Vector3(
                Random.Range(-cameraPositionJitter.x, cameraPositionJitter.x),
                Random.Range(-cameraPositionJitter.y, cameraPositionJitter.y),
                Random.Range(-cameraPositionJitter.z, cameraPositionJitter.z)
            );
            camPos += posJitter;

            Vector3 rotJitterEuler = new Vector3(
                Random.Range(-cameraRotationJitter.x, cameraRotationJitter.x),
                Random.Range(-cameraRotationJitter.y, cameraRotationJitter.y),
                Random.Range(-cameraRotationJitter.z, cameraRotationJitter.z)
            );
            screenshotCamera.transform.position = camPos;
            screenshotCamera.transform.rotation = nominalRot * Quaternion.Euler(rotJitterEuler);
        }
        else
        {
            screenshotCamera.transform.position = camPos;
            screenshotCamera.transform.rotation = nominalRot;
        }
    }

    public Vector3 GetSpawnOrigin()
    {
        Vector3 basePos = spawnContainer != null ? spawnContainer.position : spawnCenter;
        basePos.y += spawnElevationOffset;
        if (basePos.y < minimumGroundClearance)
        {
            basePos.y = minimumGroundClearance;
        }
        return basePos;
    }

    private void OnDrawGizmos()
    {
        if (!showSpawnGizmos) return;

        Vector3 origin = GetSpawnOrigin();

        Gizmos.color = spawnSphereColor;
        Gizmos.DrawSphere(origin, spawnRadius);

        Gizmos.color = new Color(spawnSphereColor.r, spawnSphereColor.g, spawnSphereColor.b, 0.9f);
        Gizmos.DrawWireSphere(origin, spawnRadius);

        if (minSpawnRadius > 0f)
        {
            Gizmos.color = new Color(1.0f, 0.55f, 0.1f, 0.85f);
            Gizmos.DrawWireSphere(origin, minSpawnRadius);
        }

        Gizmos.color = new Color(0.1f, 1.0f, 0.4f, 0.5f);
        Gizmos.DrawWireCube(new Vector3(origin.x, minimumGroundClearance, origin.z), new Vector3(spawnRadius * 2.5f, 0.02f, spawnRadius * 2.5f));

        if (middleWall != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(middleWall.transform.position, origin);
        }
        if (screenshotCamera != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(screenshotCamera.transform.position, origin);
        }
    }

    private void InitializePhysicsMaterial()
    {
        _proceduralMaterial = new PhysicsMaterial("ProceduralKinematicMaterial")
        {
            dynamicFriction = 0.6f,
            staticFriction = 0.6f,
            bounciness = 0.3f,
            frictionCombine = PhysicsMaterialCombine.Average,
            bounceCombine = PhysicsMaterialCombine.Average
        };
    }

    private void ApplyLightingEnvironment()
    {
        if (sceneDirectionalLight == null)
        {
            Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (var l in lights)
            {
                if (l.type == LightType.Directional)
                {
                    sceneDirectionalLight = l;
                    break;
                }
            }
        }

        if (sceneDirectionalLight != null && RenderSettings.sun == null)
        {
            RenderSettings.sun = sceneDirectionalLight;
        }

        if (!enforceFlatLighting) return;

        if (sceneDirectionalLight != null)
        {
            sceneDirectionalLight.shadows = LightShadows.None;
        }

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = flatAmbientColor;
    }

    /// <summary>
    /// Randomizes Directional Light orientation, solar color, intensity, and skybox lighting
    /// to simulate dynamic times of day (dawn, morning, midday, afternoon, sunset).
    /// </summary>
    private void ApplyRandomTimeOfDay()
    {
        if (sceneDirectionalLight == null)
        {
            Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (var l in lights)
            {
                if (l.type == LightType.Directional)
                {
                    sceneDirectionalLight = l;
                    break;
                }
            }
            if (sceneDirectionalLight == null) return;
        }

        if (RenderSettings.sun == null)
        {
            RenderSettings.sun = sceneDirectionalLight;
        }

        // 1. Randomize Sun orientation (pitch: elevation above horizon, yaw: compass azimuth)
        float pitch = Random.Range(sunPitchRange.x, sunPitchRange.y);
        float yaw = Random.Range(sunYawRange.x, sunYawRange.y);
        sceneDirectionalLight.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

        // 2. Factor t: 0.0 = horizon dawn/dusk, 1.0 = zenith midday
        float t = Mathf.InverseLerp(sunPitchRange.x, sunPitchRange.y, pitch);

        // 3. Solar color gradient: warm dawn/dusk golden-orange -> bright midday white-gold
        Color dawnDuskColor = new Color(1.0f, 0.62f, 0.40f);
        Color middayColor = new Color(1.0f, 0.98f, 0.94f);
        Color sunColor = Color.Lerp(dawnDuskColor, middayColor, t);

        sceneDirectionalLight.color = sunColor;
        sceneDirectionalLight.intensity = Mathf.Lerp(sunIntensityRange.x, sunIntensityRange.y, t);
        sceneDirectionalLight.shadows = LightShadows.None;

        // 4. Update ambient light to harmonize with the time of day
        if (enforceFlatLighting)
        {
            Color ambientDawn = new Color(0.72f, 0.62f, 0.58f);
            Color ambientMidday = flatAmbientColor;
            RenderSettings.ambientLight = Color.Lerp(ambientDawn, ambientMidday, t);
        }

        DynamicGI.UpdateEnvironment();
        Debug.Log($"[KinematicGenerator] Time of Day updated: Pitch={pitch:F1} deg, Yaw={yaw:F1} deg, SunColor=#{ColorUtility.ToHtmlStringRGBA(sunColor)}, Intensity={sceneDirectionalLight.intensity:F2}");
    }

    /// <summary>
    /// Randomizes colors of all three walls and the ground floor per simulation run.
    /// </summary>
    private void ApplyRandomEnvironmentColors()
    {
        Color wallColor = GetNextEnvironmentColor(wallColorPalette, false);
        Color floorColor = GetNextEnvironmentColor(groundColorPalette, true);

        ApplyObjectColor(middleWall, wallColor);
        ApplyObjectColor(leftWall, wallColor);
        ApplyObjectColor(rightWall, wallColor);
        ApplyObjectColor(ground, floorColor);

        Debug.Log($"[KinematicGenerator] Environment colors updated: Walls=#{ColorUtility.ToHtmlStringRGBA(wallColor)}, Floor=#{ColorUtility.ToHtmlStringRGBA(floorColor)}");
    }

    private Color GetNextEnvironmentColor(Color[] palette, bool isFloor)
    {
        if (palette != null && palette.Length > 0)
        {
            Color c = palette[Random.Range(0, palette.Length)];
            if (c.a > 0.01f || c.r > 0.01f || c.g > 0.01f || c.b > 0.01f)
            {
                return c;
            }
        }

        if (isFloor)
        {
            // Harmonious studio floor tones (concrete, warm gray, terracotta, neutral slate)
            float val = Random.Range(0.60f, 0.92f);
            float sat = Random.Range(0.05f, 0.35f);
            float hue = Random.value;
            return Color.HSVToRGB(hue, sat, val);
        }
        else
        {
            // Harmonious studio wall tones (soft off-white, light warm gray, soft sage, muted blue)
            float val = Random.Range(0.75f, 0.98f);
            float sat = Random.Range(0.04f, 0.28f);
            float hue = Random.value;
            return Color.HSVToRGB(hue, sat, val);
        }
    }

    private void ApplyObjectColor(GameObject obj, Color color)
    {
        if (obj == null) return;
        var renderers = obj.GetComponentsInChildren<Renderer>();
        foreach (var rend in renderers)
        {
            Material[] mats = rend.materials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] != null)
                {
                    if (mats[i].HasProperty("_BaseColor")) mats[i].SetColor("_BaseColor", color);
                    if (mats[i].HasProperty("_Color")) mats[i].SetColor("_Color", color);
                }
            }
            rend.materials = mats;

            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            rend.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", color);
            mpb.SetColor("_Color", color);
            rend.SetPropertyBlock(mpb);
        }
    }

    /// <summary>
    /// Loads all spawnable prefabs across manual inspector assignments, designated project directory,
    /// and Resources folder. Merges and deduplicates all prefabs by name.
    /// </summary>
    private void LoadSpawnables()
    {
        var loadedList = new List<GameObject>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Load any manually assigned prefabs in spawnablePrefabs inspector array
        if (spawnablePrefabs != null)
        {
            foreach (var prefab in spawnablePrefabs)
            {
                if (prefab != null && seenNames.Add(prefab.name))
                {
                    loadedList.Add(prefab);
                }
            }
        }

#if UNITY_EDITOR
        // 2. Load from designated project directory (Assets/Spawnables)
        if (!string.IsNullOrEmpty(spawnablesDirectory) && Directory.Exists(spawnablesDirectory))
        {
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { spawnablesDirectory });
            foreach (string guid in guids)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null && seenNames.Add(prefab.name))
                {
                    loadedList.Add(prefab);
                }
            }
        }

        // 3. Load from Assets/Resources/Spawnables directory in Editor
        if (Directory.Exists("Assets/Resources/Spawnables"))
        {
            string[] rGuids = UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Resources/Spawnables" });
            foreach (string guid in rGuids)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null && seenNames.Add(prefab.name))
                {
                    loadedList.Add(prefab);
                }
            }
        }
#endif

        // 4. Load from Unity Resources runtime system
        GameObject[] fromResources = Resources.LoadAll<GameObject>("Spawnables");
        if (fromResources != null)
        {
            foreach (var prefab in fromResources)
            {
                if (prefab != null && seenNames.Add(prefab.name))
                {
                    loadedList.Add(prefab);
                }
            }
        }

        if (loadedList.Count > 0)
        {
            _spawnables = loadedList.ToArray();
            var names = new List<string>();
            foreach (var p in _spawnables) names.Add(p.name);
            Debug.Log($"[KinematicGenerator] Successfully loaded {_spawnables.Length} spawnable prefabs: [{string.Join(", ", names)}]");
            return;
        }

        // 5. Fallback procedural primitives
        Debug.LogWarning("[KinematicGenerator] No prefabs found. Generating procedural fallback primitives.");
        _spawnables = CreateFallbackPrimitives();
    }

    private GameObject[] CreateFallbackPrimitives()
    {
        PrimitiveType[] types = { PrimitiveType.Cube, PrimitiveType.Sphere, PrimitiveType.Cylinder, PrimitiveType.Capsule };
        GameObject[] fallbacks = new GameObject[types.Length];
        GameObject container = new GameObject("_ProceduralPrimitiveCache");
        container.SetActive(false);

        for (int i = 0; i < types.Length; i++)
        {
            GameObject prim = GameObject.CreatePrimitive(types[i]);
            prim.name = types[i].ToString();
            prim.transform.SetParent(container.transform, false);
            fallbacks[i] = prim;
        }

        return fallbacks;
    }

    /// <summary>
    /// Checks whether a prefab is a standard primitive shape vs a custom imported model (calculator, tape dispenser, etc.)
    /// </summary>
    public bool IsPrimitiveShape(GameObject prefab)
    {
        if (prefab == null) return false;
        string name = prefab.name.ToLowerInvariant();
        return name.StartsWith("cube") || name.StartsWith("sphere") ||
               name.StartsWith("cylinder") || name.StartsWith("capsule") ||
               name.Contains("primitive");
    }

    private void ConfigureDeterministicPhysics()
    {
        capturesPerSecond = Mathf.Clamp(capturesPerSecond, 10, 120);
        Time.captureFramerate = capturesPerSecond;
        if (capturesPerSecond > 0 && oversampleMultiplier > 0)
        {
            Time.fixedDeltaTime = 1.0f / (capturesPerSecond * oversampleMultiplier);
            Time.maximumDeltaTime = Time.fixedDeltaTime;
        }

        Physics.defaultSolverIterations = solverIterations;
        Physics.defaultSolverVelocityIterations = solverIterations;
    }

    private IEnumerator ExecuteDatasetGeneration()
    {
        _isGenerating = true;

        for (int runIndex = 1; runIndex <= targetRuns; runIndex++)
        {
            Debug.Log($"[KinematicGenerator] Beginning Simulation Run {runIndex} of {targetRuns}...");

            InitializeRunStorage(runIndex);

            // 1. Time of Day randomization (Sun orientation, solar color, skybox)
            if (randomizeTimeOfDay)
            {
                ApplyRandomTimeOfDay();
            }

            // 2. Wall & ground color randomization
            if (randomizeEnvironmentColors)
            {
                ApplyRandomEnvironmentColors();
            }

            // 3. Camera alignment with optional slight pose jitter
            if (autoPositionCamera)
            {
                UpdateCameraPosition();
            }

            if (_motionBlur != null)
            {
                _currentBlurIntensity = _motionBlur.intensity.value;
            }

            SpawnSimObject();

            ApplyKinematicImpulse();

            yield return StartCoroutine(RunCaptureLoop(runIndex));

            TeardownCurrentRun();

            if (interRunCooldownSeconds > 0f)
            {
                yield return new WaitForSeconds(interRunCooldownSeconds);
            }
        }

        _isGenerating = false;
        Debug.Log("[KinematicGenerator] All target runs completed successfully. Terminating engine.");
        TerminateEngine();
    }

    private void InitializeRunStorage(int runIndex)
    {
        CloseCsvWriter();

        string targetBase = Path.IsPathRooted(outputDirectory)
            ? outputDirectory
            : Path.Combine(Application.dataPath, "..", outputDirectory);

        string runFolderName = $"{_sessionTimestamp}_Run_{runIndex:D3}";
        _currentRunDirectory = Path.Combine(targetBase, runFolderName);

        if (!Directory.Exists(_currentRunDirectory))
        {
            Directory.CreateDirectory(_currentRunDirectory);
        }

        string csvPath = Path.Combine(_currentRunDirectory, "metadata.csv");
        _csvWriter = new StreamWriter(csvPath, false, Encoding.UTF8);
        _csvWriter.WriteLine("frame_id,timestamp,kind,pos_x,pos_y,pos_z,vel_mag,vel_dir_x,vel_dir_y,vel_dir_z,cam_dist,distance_sensor,blur_intensity");
        _csvWriter.Flush();
    }

    /// <summary>
    /// Instantiates a random prefab with adaptive scaling (primitives vs custom models),
    /// generates fitted bounding colliders covering complex child hierarchies,
    /// sets up ContinuousDynamic physics, and preserves custom texture materials.
    /// </summary>
    private void SpawnSimObject()
    {
        if (_currentSimObject != null)
        {
            TeardownCurrentRun();
        }

        _currentKindIndex = Random.Range(0, _spawnables.Length);
        GameObject prefab = _spawnables[_currentKindIndex];

        Vector3 origin = GetSpawnOrigin();

        Vector3 randomDir = Random.insideUnitSphere;
        if (minSpawnRadius > 0f)
        {
            float r = Random.Range(minSpawnRadius, spawnRadius);
            randomDir = randomDir.normalized * r;
        }
        else
        {
            randomDir *= spawnRadius;
        }

        Vector3 spawnPos = origin + randomDir;
        spawnPos.y = Mathf.Max(spawnPos.y, minimumGroundClearance);

        Quaternion spawnRot = Random.rotation;

        // Custom models (Tape dispenser, Calculator, etc.) are small in real-world units,
        // so they use customModelMinSize/customModelMaxSize to match the visual footprint of the primitives!
        bool isPrimitive = IsPrimitiveShape(prefab);
        if (isPrimitive)
        {
            _currentScale = Random.Range(minSize, maxSize);
        }
        else
        {
            _currentScale = Random.Range(customModelMinSize, customModelMaxSize);
        }

        _currentSimObject = Instantiate(prefab, spawnPos, spawnRot, null);
        _currentSimObject.name = $"SimObject_{prefab.name}_Kind{_currentKindIndex}";
        _currentSimObject.transform.localScale = Vector3.one * _currentScale;

        // Ensure collider exists and is properly fitted to all sub-meshes / children
        var colliders = _currentSimObject.GetComponentsInChildren<Collider>();
        if (colliders.Length == 0)
        {
            var childRenderers = _currentSimObject.GetComponentsInChildren<Renderer>();
            if (childRenderers.Length > 0)
            {
                Bounds combinedBounds = childRenderers[0].bounds;
                for (int i = 1; i < childRenderers.Length; i++)
                {
                    combinedBounds.Encapsulate(childRenderers[i].bounds);
                }

                var box = _currentSimObject.AddComponent<BoxCollider>();
                box.center = _currentSimObject.transform.InverseTransformPoint(combinedBounds.center);
                Vector3 worldSize = combinedBounds.size;
                Vector3 localScale = _currentSimObject.transform.lossyScale;
                box.size = new Vector3(
                    localScale.x > 0.0001f ? worldSize.x / localScale.x : worldSize.x,
                    localScale.y > 0.0001f ? worldSize.y / localScale.y : worldSize.y,
                    localScale.z > 0.0001f ? worldSize.z / localScale.z : worldSize.z
                );
                box.material = _proceduralMaterial;
            }
            else
            {
                var box = _currentSimObject.AddComponent<BoxCollider>();
                box.material = _proceduralMaterial;
            }
        }
        else
        {
            foreach (var col in colliders)
            {
                col.material = _proceduralMaterial;
            }
        }

        float bottomY = GetObjectBottomY(_currentSimObject);
        if (bottomY < minimumGroundClearance)
        {
            float elevationOffset = minimumGroundClearance - bottomY;
            _currentSimObject.transform.position += Vector3.up * elevationOffset;
        }

        _currentSimObject.SetActive(true);

        var renderers = _currentSimObject.GetComponentsInChildren<Renderer>();
        foreach (var rend in renderers)
        {
            rend.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            if (enforceFlatLighting)
            {
                rend.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        // Apply selective color randomization (strictly on spawnableMaterial, preserving custom textures!)
        ApplySelectiveMaterialColors(_currentSimObject);

        _currentRb = _currentSimObject.GetComponent<Rigidbody>();
        if (_currentRb == null)
        {
            _currentRb = _currentSimObject.AddComponent<Rigidbody>();
        }

        _currentRb.useGravity = false;
        _currentRb.linearVelocity = Vector3.zero;
        _currentRb.angularVelocity = Vector3.zero;
        _currentRb.interpolation = RigidbodyInterpolation.Interpolate;
        _currentRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _currentRb.WakeUp();
    }

    private float GetObjectBottomY(GameObject obj)
    {
        float minY = float.MaxValue;

        var colliders = obj.GetComponentsInChildren<Collider>();
        foreach (var col in colliders)
        {
            if (col.bounds.min.y < minY)
            {
                minY = col.bounds.min.y;
            }
        }
        if (minY != float.MaxValue) return minY;

        var renderers = obj.GetComponentsInChildren<Renderer>();
        foreach (var rend in renderers)
        {
            if (rend.bounds.min.y < minY)
            {
                minY = rend.bounds.min.y;
            }
        }

        return minY != float.MaxValue ? minY : obj.transform.position.y;
    }

    /// <summary>
    /// Selectively applies randomized colors to spawnableMaterial and flat-shaded primitives,
    /// while preserving custom textured materials (e.g. Office Supplies Low Poly) with 100% fidelity.
    /// </summary>
    private void ApplySelectiveMaterialColors(GameObject obj)
    {
        if (!randomizeSpawnableColor) return;

        Color chosenColor = GetNextRandomColor();
        var renderers = obj.GetComponentsInChildren<Renderer>();

        foreach (var rend in renderers)
        {
            Material[] sharedMats = rend.sharedMaterials;
            bool modified = false;
            Material[] newMats = new Material[sharedMats.Length];

            for (int i = 0; i < sharedMats.Length; i++)
            {
                Material currentMat = sharedMats[i];
                if (currentMat == null)
                {
                    newMats[i] = null;
                    continue;
                }

                // Check if the material is a textured object (e.g. Office Supplies Shared Texture)
                bool isTextured = (currentMat.HasProperty("_BaseMap") && currentMat.GetTexture("_BaseMap") != null) ||
                                  (currentMat.HasProperty("_MainTex") && currentMat.GetTexture("_MainTex") != null);

                // If it is textured, preserve original material and textures!
                if (isTextured)
                {
                    currentMat.SetShaderPassEnabled("MotionVectors", true);
                    currentMat.SetShaderPassEnabled("MOTIONVECTORS", true);
                    newMats[i] = currentMat;
                    continue;
                }

                // Untextured / flat material: Target for color randomization!
                Material baseMat = spawnableMaterial != null ? spawnableMaterial : currentMat;
                Material coloredMat = new Material(baseMat);
                coloredMat.name = $"{baseMat.name}_RandomColor";

                coloredMat.SetShaderPassEnabled("MotionVectors", true);
                coloredMat.SetShaderPassEnabled("MOTIONVECTORS", true);

                if (coloredMat.HasProperty("_BaseColor"))
                {
                    coloredMat.SetColor("_BaseColor", chosenColor);
                }
                if (coloredMat.HasProperty("_Color"))
                {
                    coloredMat.SetColor("_Color", chosenColor);
                }

                newMats[i] = coloredMat;
                modified = true;
            }

            if (modified)
            {
                rend.materials = newMats;

                // Also apply MaterialPropertyBlock to guarantee instantaneous URP rendering update across all render passes
                MaterialPropertyBlock mpb = new MaterialPropertyBlock();
                rend.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", chosenColor);
                mpb.SetColor("_Color", chosenColor);
                rend.SetPropertyBlock(mpb);

                Debug.Log($"[KinematicGenerator] Applied randomized color #{ColorUtility.ToHtmlStringRGBA(chosenColor)} to '{rend.gameObject.name}'");
            }
        }
    }

    /// <summary>
    /// Generates vibrant saturated random colors across the full color spectrum,
    /// or samples from colorPalette if non-empty and non-zero.
    /// </summary>
    private Color GetNextRandomColor()
    {
        if (colorPalette != null && colorPalette.Length > 0)
        {
            Color c = colorPalette[Random.Range(0, colorPalette.Length)];
            if (c.a > 0.01f || c.r > 0.01f || c.g > 0.01f || c.b > 0.01f)
            {
                return c;
            }
        }

        // Generate vibrant random HSV colors across the entire 360-degree color wheel
        float hue = Random.value;
        float saturation = Random.Range(0.65f, 1.0f);
        float value = Random.Range(0.75f, 1.0f);
        return Color.HSVToRGB(hue, saturation, value);
    }

    private void ApplyKinematicImpulse()
    {
        if (_currentRb == null) return;

        _currentRb.useGravity = enableGravity;
        _currentRb.linearVelocity = Vector3.zero;
        _currentRb.angularVelocity = Vector3.zero;

        Vector3 camRight = screenshotCamera != null ? screenshotCamera.transform.right : Vector3.right;
        camRight.y = 0f;
        if (camRight.sqrMagnitude < 0.001f) camRight = Vector3.right;
        camRight.Normalize();

        Vector3 camForward = screenshotCamera != null ? screenshotCamera.transform.forward : Vector3.forward;
        camForward.y = 0f;
        if (camForward.sqrMagnitude < 0.001f) camForward = Vector3.forward;
        camForward.Normalize();

        float horizontalSign = Random.value > 0.5f ? 1.0f : -1.0f;
        float speed = Random.Range(minInitialSpeed, maxInitialSpeed);
        float verticalLoft = Random.Range(verticalLoftRange.x, verticalLoftRange.y);

        Vector3 impulse = (camRight * (speed * horizontalSign))
            + (camForward * Random.Range(-velocityJitter.z, velocityJitter.z))
            + (Vector3.up * verticalLoft);

        Vector3 torque = Random.insideUnitSphere * Random.Range(1.0f, angularImpulseMax);

        _currentRb.AddForce(impulse, ForceMode.VelocityChange);
        _currentRb.AddTorque(torque, ForceMode.VelocityChange);
    }

    private IEnumerator RunCaptureLoop(int runIndex)
    {
        float runStartTime = Time.time;
        int restFrameCount = 0;
        int outOfBoundsFrameCount = 0;
        string floatFmt = $"F{filenameFloatPrecision}";

        for (int frameCount = 0; frameCount < framesPerRun; frameCount++)
        {
            yield return new WaitForEndOfFrame();

            if (_currentSimObject == null || _currentRb == null) break;

            float elapsed = Time.time - runStartTime;
            Vector3 pos = _currentSimObject.transform.position;
            Vector3 vel = _currentRb.linearVelocity;
            float velMag = vel.magnitude;
            Vector3 velDir = velMag > 1e-6f ? vel / velMag : Vector3.zero;

            float camDist = 0f;
            if (screenshotCamera != null)
            {
                camDist = Vector3.Distance(screenshotCamera.transform.position, pos);
            }

            // Simulated distance sensor reading:
            // Larger object scale simulates a camera view closer to the object
            float scaleFactor = _currentScale > 0.001f ? (_currentScale / referenceSize) : 1.0f;
            _currentDistanceSensor = camDist / scaleFactor;

            // Filename following pattern: c[{count}]_v[{velocity}]_d[{directionX}]x[{directionY}]y[{directionZ}]z_k[{kind}]_p[{posX}]x[{posY}]y[{posZ}]z.png
            string frameId;
            if (includeDistanceInFilename)
            {
                frameId = string.Format(
                    CultureInfo.InvariantCulture,
                    "c[{0}]_v[{1:" + floatFmt + "}]_d[{2:" + floatFmt + "}]x[{3:" + floatFmt + "}]y[{4:" + floatFmt + "}]z_k[{5}]_p[{6:" + floatFmt + "}]x[{7:" + floatFmt + "}]y[{8:" + floatFmt + "}]z_dist[{9:" + floatFmt + "}].png",
                    frameCount,
                    velMag,
                    velDir.x,
                    velDir.y,
                    velDir.z,
                    _currentKindIndex,
                    pos.x,
                    pos.y,
                    pos.z,
                    _currentDistanceSensor
                );
            }
            else
            {
                frameId = string.Format(
                    CultureInfo.InvariantCulture,
                    "c[{0}]_v[{1:" + floatFmt + "}]_d[{2:" + floatFmt + "}]x[{3:" + floatFmt + "}]y[{4:" + floatFmt + "}]z_k[{5}]_p[{6:" + floatFmt + "}]x[{7:" + floatFmt + "}]y[{8:" + floatFmt + "}]z.png",
                    frameCount,
                    velMag,
                    velDir.x,
                    velDir.y,
                    velDir.z,
                    _currentKindIndex,
                    pos.x,
                    pos.y,
                    pos.z
                );
            }

            string fullImagePath = Path.Combine(_currentRunDirectory, frameId);

            if (_csvWriter != null)
            {
                string row = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0},{1:F4},{2},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F4},{10:F4},{11:F4},{12:F4}",
                    frameId,
                    elapsed,
                    _currentKindIndex,
                    pos.x,
                    pos.y,
                    pos.z,
                    velMag,
                    velDir.x,
                    velDir.y,
                    velDir.z,
                    camDist,
                    _currentDistanceSensor,
                    _currentBlurIntensity
                );

                _csvWriter.WriteLine(row);
                _csvWriter.Flush();
            }

            ScreenCapture.CaptureScreenshot(fullImagePath);

            if ((frameCount + 1) % 10 == 0 || frameCount == 0 || frameCount == framesPerRun - 1)
            {
                Debug.Log($"[KinematicGenerator] Captured frame {frameCount + 1} of {framesPerRun} for Run {runIndex} -> {frameId}");
            }

            if (frameCount >= minFramesBeforeEarlyExit)
            {
                if (terminateWhenOutOfBounds)
                {
                    bool isOutOfBounds = pos.y < minimumYBound || !IsObjectVisibleToCamera(screenshotCamera, _currentSimObject);
                    if (isOutOfBounds)
                    {
                        outOfBoundsFrameCount++;
                        if (outOfBoundsFrameCount >= 2)
                        {
                            Debug.Log($"[KinematicGenerator] Run {runIndex}: Object exited view bounds at frame {frameCount + 1}. Transitioning immediately to next run.");
                            break;
                        }
                    }
                    else
                    {
                        outOfBoundsFrameCount = 0;
                    }
                }

                if (terminateOnRest)
                {
                    if (velMag < restVelocityThreshold)
                    {
                        restFrameCount++;
                        if (restFrameCount >= maxRestFrames)
                        {
                            Debug.Log($"[KinematicGenerator] Run {runIndex}: Object settled at frame {frameCount + 1} (speed {velMag:F2} m/s). Transitioning immediately to next run.");
                            break;
                        }
                    }
                    else
                    {
                        restFrameCount = 0;
                    }
                }
            }
        }
    }

    private bool IsObjectVisibleToCamera(Camera cam, GameObject obj)
    {
        if (cam == null || obj == null) return true;
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return true;

        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(cam);
        foreach (var r in renderers)
        {
            if (r != null && r.enabled && GeometryUtility.TestPlanesAABB(planes, r.bounds))
            {
                return true;
            }
        }
        return false;
    }

    private void TeardownCurrentRun()
    {
        CloseCsvWriter();

        if (_currentSimObject != null)
        {
            var colliders = _currentSimObject.GetComponentsInChildren<Collider>();
            foreach (var col in colliders)
            {
                col.enabled = false;
            }

            _currentSimObject.SetActive(false);
            Destroy(_currentSimObject);
            _currentSimObject = null;
            _currentRb = null;
        }
    }

    private void CloseCsvWriter()
    {
        if (_csvWriter != null)
        {
            try
            {
                _csvWriter.Flush();
                _csvWriter.Close();
                _csvWriter.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[KinematicGenerator] Error closing CSV writer: {ex.Message}");
            }
            finally
            {
                _csvWriter = null;
            }
        }
    }

    private void TerminateEngine()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void OnDisable()
    {
        TeardownCurrentRun();
    }

    void OnDestroy()
    {
        TeardownCurrentRun();
        if (_runtimeVolume != null && _runtimeVolume.gameObject.name == "_RuntimeMotionBlurVolume")
        {
            Destroy(_runtimeVolume.gameObject);
        }
    }

    void OnApplicationQuit()
    {
        TeardownCurrentRun();
    }
}
