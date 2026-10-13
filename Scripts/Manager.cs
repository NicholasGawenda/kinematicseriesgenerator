using UnityEngine;
using UnityEngine.Events;

public class Manager : MonoBehaviour
{
    [Header("Background Materials")]
    [SerializeField] private Material groundMaterial;
    [SerializeField] private Material leftWallMaterial;
    [SerializeField] private Material rightWallMaterial;
    [SerializeField] private Material centerWallMaterial;

    [Header("Simulation Settings")]
    [SerializeField] private float targetRuntime;
    [SerializeField] private int targetRuns;
    
    [Header("Events")]
    public UnityEvent seriesStart;
    public UnityEvent seriesEnd;
    
    private bool _fallSimReady;
    private bool _cameraReady;
    
    private bool _running;
    private int _currentRuns;
    private float _finalTic;
    
    public void SetCameraReady()
    {
        _cameraReady = true;
        Debug.Log("Manager heard: Camera is ready!");
        TryStartSeries();
    }

    public void SetFallReady()
    {
        _fallSimReady = true;
        Debug.Log("Manager heard: FallSim is ready!");
        TryStartSeries();
    }
    
    public void TryStartSeries()
    {
        if (!_fallSimReady || !_cameraReady)
        {
            Debug.Log("Waiting for systems to be ready...");
            return;
        }

        _finalTic = Time.time + targetRuntime;
        Debug.Log($"Both systems ready. Starting Simulation Run {_currentRuns + 1} of {targetRuns}!\nSimulation will end at {_finalTic}, will run for {targetRuntime} seconds.");
        
        _running = true;
        _fallSimReady = false;
        _cameraReady = false;
        
        seriesStart?.Invoke();
    }

    void Update()
    {
        if (!_running || Time.time < _finalTic) return;

        _running = false;
        _currentRuns++;
        
        if (_currentRuns < targetRuns)
        {
            Debug.Log($"Run {_currentRuns} complete. Resetting for next run...");
            seriesEnd?.Invoke();
        }
        else
        {
            Debug.Log("Target runs reached. Simulation complete.");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}