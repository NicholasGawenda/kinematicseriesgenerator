using System;
using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using UnityEngine.Rendering; // Required for Volumes
using UnityEngine.Rendering.Universal;
using Random = UnityEngine.Random; // Required for URP Post-Processing

public class CameraPositioner : MonoBehaviour
{
    [SerializeField] private Camera screenshotCamera;
    [SerializeField] private Transform cameraLookAtTarget;
    [FormerlySerializedAs("simulatedFramesPerSecond")] [SerializeField] private int capturesPerSecond = 30;
    [SerializeField] private string screenshotDirectory;
    [SerializeField] private FallSim fallSim;

    [Header("Motion Blur (Point Spread)")] [SerializeField]
    private Volume cameraVolume; // Drag the GameObject with the Volume component here

    [SerializeField] private float minBlurIntensity = 0f;
    [SerializeField] private float maxBlurIntensity = 1f;
    private MotionBlur _motionBlur;

    private string _currentOutputDirectory;
    private string _sessionTimestamp;
    private int _runCounter = 1;
    private int _screenshotCount;

    public UnityEvent cameraReady;
    private bool _isCapturing = false;

    void Start()
    {
        Time.captureFramerate = capturesPerSecond;
        if (capturesPerSecond > 0)
        {
            Time.fixedDeltaTime = 1.0f / capturesPerSecond;
        }

        _sessionTimestamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        if (fallSim == null)
        {
            fallSim = GetComponent<FallSim>();
            if (fallSim == null)
            {
                fallSim = FindFirstObjectByType<FallSim>();
            }
        }

        // Try to grab the Motion Blur component from the volume
        if (cameraVolume != null && cameraVolume.profile.TryGet(out _motionBlur))
        {
            Debug.Log("Motion Blur successfully linked.");
        }
        else
        {
            Debug.LogWarning("Camera Volume is missing or does not have a Motion Blur override!");
        }

        SetScreenshotDirectory();
        PrepareCamera();
        Debug.Log("Camera positioner ready!");
        cameraReady.Invoke();
    }

    public void TakeScreenshotAction()
    {
        _isCapturing = true;
    }

    public void ResetAndPrepare()
    {
        _isCapturing = false;
        SetScreenshotDirectory();
        PrepareCamera();
        cameraReady.Invoke();
    }

    void LateUpdate()
    {
        if (_isCapturing)
        {
            TakeScreenshot();
        }
    }

    private void SetScreenshotDirectory()
    {
        if (!screenshotDirectory.EndsWith("/") && !screenshotDirectory.EndsWith("\\"))
        {
            screenshotDirectory += "/";
        }

        if (!System.IO.Directory.Exists(screenshotDirectory))
        {
            System.IO.Directory.CreateDirectory(screenshotDirectory);
        }

        string seriesFolderName = _sessionTimestamp + "_Run_" + _runCounter;
        _currentOutputDirectory = screenshotDirectory + seriesFolderName + "/";

        if (!System.IO.Directory.Exists(_currentOutputDirectory))
        {
            System.IO.Directory.CreateDirectory(_currentOutputDirectory);
        }

        _runCounter++;
        _screenshotCount = 0;
    }

    private void TakeScreenshot()
    {
        /* {count}c_{velocity}v_d{directionX}x{directionY}y{directionZ}z_{kind}k_p{posX}x{posY}y{posZ}z.png
            where
            count is val of _screenshotCount
            velocity( of current state of object, derived from objects lerp speed), 
            direction( of current state of object, derived from objects lerp target and lerp speed per XYZ),
            kind( of current object, as an integer value. E.g. sphere=0, cylinder=1, square=2, etc.),
            posXYZ( of current object),
            are all derived from the object dictated by FallSim
            
            all values will be wrapped in brackets such that and example file name would look like:
            c[16]_v[5.34]_d[-5.04]x[-9.81]y[1.30]z_k[4]_p[2.01]x[4.32]y[-1]z.png
        */

        if (fallSim == null)
        {
            fallSim = GetComponent<FallSim>();
            if (fallSim == null)
            {
                fallSim = FindFirstObjectByType<FallSim>();
            }
        }

        FallTelemetry telemetry = fallSim != null ? fallSim.GetCurrentTelemetry() : default;
        if (!telemetry.isValid)
        {
            return;
        }

        string fileName = string.Format(
            CultureInfo.InvariantCulture,
            "c[{0}]_v[{1:F2}]_d[{2:F2}]x[{3:F2}]y[{4:F2}]z_k[{5}]_p[{6:F2}]x[{7:F2}]y[{8:F2}]z.png",
            _screenshotCount,
            telemetry.velocity,
            telemetry.direction.x,
            telemetry.direction.y,
            telemetry.direction.z,
            telemetry.kind,
            telemetry.position.x,
            telemetry.position.y,
            telemetry.position.z
        );

        ScreenCapture.CaptureScreenshot(Path.Combine(_currentOutputDirectory, fileName));
        
        _screenshotCount++;
    }

    private void PrepareCamera()
    {
        // Randomize the motion blur intensity for this run
        if (_motionBlur != null)
        {
            float randomBlur = Random.Range(minBlurIntensity, maxBlurIntensity);
            _motionBlur.intensity.value = randomBlur;
            Debug.Log($"Run {_runCounter}: Motion Blur intensity set to {randomBlur}");
        }

        screenshotCamera.transform.LookAt(cameraLookAtTarget);
    }
}
