using UnityEngine;
using UnityEngine.Events;

public struct FallTelemetry
{
    public bool isValid;
    public int kind;
    public float velocity;
    public Vector3 direction;
    public Vector3 position;
}

public class FallSim : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private GameObject fallObjectContainer;
    [SerializeField] private GameObject[] objectPool;
    
    [Header("Physical and Material Properties")]
    [SerializeField] private Material objectMaterial;
    [SerializeField] private PhysicsMaterial objectPhysicsMaterial;
    
    [Header("Spawn Randomization Bounds")]
    [SerializeField] private Vector2 spawnBoundsX = new Vector2(-5f, 10f);
    [SerializeField] private Vector2 spawnBoundsY = new Vector2(5f, 15f);
    [SerializeField] private Vector2 spawnBoundsZ = new Vector2(-5f, 10f);
    
    [Header("Scale Range (Uniform)")]
    [SerializeField] private float minScale = 1f;
    [SerializeField] private float maxScale = 1f;

    [Header("FallSim Parameters")]
    [SerializeField] private Vector3 lerpTarget;
    [SerializeField] private float minLerpSpeed = 0.1f;
    [SerializeField] private float maxLerpSpeed = 0.8f;
    
    [Header("Events")]
    public UnityEvent fallSimReady;

    // Internal state
    private Vector3 _positionOffset;
    private float _lerpSpeed;
    private GameObject _fallObject;
    private Rigidbody _fallObjectRb;
    private int _currentKindIndex;

    public FallTelemetry GetCurrentTelemetry()
    {
        if (_fallObject == null) return new FallTelemetry { isValid = false };

        Vector3 vel = _fallObjectRb != null ? _fallObjectRb.linearVelocity : Vector3.zero;

        return new FallTelemetry
        {
            isValid = true,
            kind = _currentKindIndex,
            velocity = vel.magnitude,
            direction = vel,
            position = _fallObject.transform.position
        };
    }

    public void OnFallSimReady()
    {
        Debug.Log("FallSim ready!");
        fallSimReady?.Invoke();
    }

    public void OnFallSimStart()
    {
        Debug.Log("FallSim started!");
        if (_fallObjectRb != null)
        {
            _fallObjectRb.useGravity = true;
            _fallObjectRb.AddForce(lerpTarget - _fallObject.transform.position, ForceMode.VelocityChange);
        }
    }

    public void OnFallSimEnd()
    {
        StopFallSim();
        Debug.Log("FallSim ended!");
    }

    [Header("High-Fidelity Physics Settings")]
    [Tooltip("Runs multiple physics steps per rendered frame for extreme accuracy.")]
    [SerializeField, Range(1, 10)] private int physicsOversampleMultiplier = 4;
    [SerializeField] private int solverIterations = 50;
    [SerializeField] private int solverVelocityIterations = 50;

    private void SyncPhysicsTimestep()
    {
        // Force high solver iterations for realistic collisions and resting states
        Physics.defaultSolverIterations = solverIterations;
        Physics.defaultSolverVelocityIterations = solverVelocityIterations;

        if (Time.captureFramerate > 0)
        {
            // Divide the timestep by the oversample multiplier. 
            // At 60 FPS and a 4x multiplier, physics runs at 240Hz.
            Time.fixedDeltaTime = 1.0f / (Time.captureFramerate * physicsOversampleMultiplier);
            Time.maximumDeltaTime = Time.fixedDeltaTime; 
        }
    }

    private void PrepareFallSim()
    {
        SyncPhysicsTimestep();

        Vector3 scaleHalf = fallObjectContainer.transform.localScale / 2f;
        _positionOffset = new Vector3(
            Random.Range(spawnBoundsX.x + scaleHalf.x, spawnBoundsX.y),
            Random.Range(spawnBoundsY.x + scaleHalf.y, spawnBoundsY.y),
            Random.Range(spawnBoundsZ.x + scaleHalf.z, spawnBoundsZ.y)
        );
        
        _lerpSpeed = Random.Range(minLerpSpeed, maxLerpSpeed);
        float randomScale = Random.Range(minScale, maxScale);

        _currentKindIndex = Random.Range(0, objectPool.Length);
        _fallObject = Instantiate(objectPool[_currentKindIndex], fallObjectContainer.transform);
        
        _fallObject.transform.SetPositionAndRotation(_positionOffset, Quaternion.Euler(0, Random.Range(0, 360), 0));
        _fallObject.transform.localScale = Vector3.one * randomScale; 
        _fallObject.GetComponent<Renderer>().material = objectMaterial;
        
        _fallObjectRb = _fallObject.GetComponent<Rigidbody>();
        if (_fallObjectRb != null)
        {
            _fallObjectRb.useGravity = false;
            _fallObjectRb.interpolation = RigidbodyInterpolation.Interpolate;
            
            // Upgrade from Continuous to ContinuousDynamic for interactions with other objects
            _fallObjectRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; 
        }
        
        OnFallSimReady();
    }

    private void StopFallSim()
    {
        if (_fallObject != null)
        {
            Destroy(_fallObject);
        }
    }
    
    public void ResetAndPrepare()
    {
        StopFallSim();
        PrepareFallSim();
    }
    
    void Start() => PrepareFallSim();
}