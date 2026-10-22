# Kinematic Series Dataset Generator

[![Unity](https://img.shields.io/badge/Unity-6000.0.36f1%20(Unity%206)-black.svg?logo=unity)](https://unity.com/)
[![Render Pipeline](https://img.shields.io/badge/Pipeline-Universal%20Render%20Pipeline%20(URP)-blue.svg)](https://unity.com/srp/universal-render-pipeline)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

An automated, headless-capable synthetic dataset generator built in **Unity (C# / URP)** designed to produce high-fidelity image sequences paired with synchronous physical ground-truth telemetry. 

The generated datasets serve as training-ready benchmark corpora for computer vision models predicting **3D kinematic displacement** (velocity vector direction, magnitude, and spatial trajectories) and **distance proximity** from single monocular images, utilizing **point spread motion blur** as the primary visual artifact of velocity.

---

## Key Features

- **Monolithic Physics & Capture Pipeline**: Consolidated into a single monolithic script ([`KinematicGenerator.cs`](Assets/Scripts/KinematicGenerator.cs)) eliminating fragile multi-script dependencies.
- **Physical Ground Truth**: Telemetry is extracted directly from the PhysX engine (`Rigidbody.linearVelocity`, `transform.position`, `kind`) at the exact render moment (`WaitForEndOfFrame`), eliminating OpenCV heuristics and color thresholding errors.
- **True URP Motion Blur**: Automatically configures post-processing volumes with `MotionBlurMode.CameraAndObjects` and enforces per-object motion vector generation to guarantee velocity point spread blur on moving objects.
- **Proximity / Distance Sensor Simulation**: Resolves monocular projective scale ambiguity by scaling apparent object dimensions and reporting a simulated LiDAR/time-of-flight distance reading ($D_{\text{sensor}} = \frac{D_{\text{actual}}}{S / S_{\text{ref}}}$).
- **Dynamic Time of Day & Lighting**: Randomizes solar elevation (pitch), azimuth (yaw), and solar color temperature (dawn golden-orange to midday daylight), seamlessly coupled with Unity's procedural skybox and ambient lighting.
- **Environment & Texture Customization**:
  - Independent color randomization for the three walls and the ground floor.
  - Selective tinting that randomizes flat-shaded objects while preserving custom texture maps (e.g. detailed office supply models).
- **Subtle Camera Pose Perturbation**: Introduces realistic camera translation and rotation jitter per run.
- **Deterministic Sub-Stepping**: Locks framerates (`Time.captureFramerate = 30`) and sub-steps physics at $120\text{ Hz}$ (`Time.fixedDeltaTime = 1 / (fps * 4)`) with 50 solver iterations for high collision and trajectory fidelity.
- **Smart Pause Reduction**: Automatically detects out-of-bounds or settled/resting objects, immediately transitioning to the next run without recording stagnant frames.

---

## Data Schema & Output Format

Each simulation run generates a dedicated folder inside the configured output directory (`Dataset/<Timestamp>_Run_<Index>/`) containing the captured frames and a `metadata.csv` file.

### 1. Filename Nomenclature
Captured images follow a structured, descriptive nomenclature:
```text
c[{count}]_v[{velocity}]_d[{directionX}]x[{directionY}]y[{directionZ}]z_k[{kind}]_p[{posX}]x[{posY}]y[{posZ}]z.png
```
*Example:* `c[0]_v[8.45]_d[0.82]x[0.21]y[-0.53]z_k[2]_p[-1.15]x[3.42]y[0.85]z.png`

| Token | Type | Description |
| :--- | :--- | :--- |
| `c[{count}]` | `int` | Frame index within the current run (0, 1, 2, ...). |
| `v[{velocity}]` | `float` | Instantaneous linear velocity magnitude $\|\vec{v}\|_2$ (m/s). |
| `d[...]` | `float` | Normalized 3D velocity direction unit vector ($x, y, z$). |
| `k[{kind}]` | `int` | Numerical model kind ID. |
| `p[...]` | `float` | World-space centroid position coordinates ($x, y, z$). |

*(Optional: Set `includeDistanceInFilename = true` in the Inspector to append `_dist[{distance}]` to the filename).*

### 2. `metadata.csv` Telemetry
Every frame logs a synchronized ground-truth entry formatted with `CultureInfo.InvariantCulture`:

```csv
frame_id,timestamp,kind,pos_x,pos_y,pos_z,vel_mag,vel_dir_x,vel_dir_y,vel_dir_z,cam_dist,distance_sensor,blur_intensity
```

| Field | Type | Description |
| :--- | :--- | :--- |
| `frame_id` | `string` | Relative image filename. |
| `timestamp` | `float` | Elapsed simulation time for the run (seconds). |
| `kind` | `int` | Numerical ID of the spawned 3D object model. |
| `pos_x, pos_y, pos_z` | `float` | World coordinates of the object centroid (meters). |
| `vel_mag` | `float` | Instantaneous linear velocity magnitude $\|\vec{v}\|_2$ (m/s). |
| `vel_dir_x, y, z` | `float` | Unit velocity direction vector components ($\vec{v} / \|\vec{v}\|_2$). |
| `cam_dist` | `float` | True Euclidean distance from camera optical center to object centroid. |
| `distance_sensor` | `float` | Simulated distance sensor reading scaled by apparent object size ($D / S$). |
| `blur_intensity` | `float` | Active Universal Render Pipeline (URP) `MotionBlur` volume intensity. |

---

## Simulated Distance Sensor & Scale Mechanics

Monocular vision suffers from inherent projective scale ambiguity: a small object close to the camera subtends the exact same visual angle $\theta$ as a large object further away:

$$\theta \approx \frac{\text{Size}}{\text{Distance}}$$

To train models capable of disambiguating distance, `KinematicGenerator` randomly samples object scales and computes an effective distance sensor reading:

$$D_{\text{sensor}} = \frac{D_{\text{actual}}}{S / S_{\text{ref}}}$$

- **Large Object ($S = 2.0$, $S_{\text{ref}} = 1.0$):** $D_{\text{sensor}} = D_{\text{actual}} / 2.0$ $\implies$ reports a closer reading, simulating a camera closer to a unit object.
- **Small Object ($S = 0.5$, $S_{\text{ref}} = 1.0$):** $D_{\text{sensor}} = D_{\text{actual}} / 0.5 = 2 \cdot D_{\text{actual}}$ $\implies$ reports a further reading.
- **Custom Model Compensation:** Because real-world imported models (e.g., calculators, tape dispensers) are modeled in centimeters ($\sim 0.1\text{m}$) compared to Unity primitive meters ($1.0\text{m}$), custom models use an adaptive scale range (`customModelMinSize` to `customModelMaxSize`, default $3.5\text{--}6.0$) to equalize visual footprint.

---

## Inspector Configuration & Parameters

| Group | Parameter | Default | Description |
| :--- | :--- | :--- | :--- |
| **Environment** | `Ground`, `MiddleWall`, `LeftWall`, `RightWall` | Auto | Scene bounding geometry references. |
| **Camera Alignment** | `Auto Position Camera` | `true` | Positions camera opposite middle wall facing the flight area. |
| | `Camera Distance` | `10.0` | Distance from spawn center along opposing wall normal. |
| | `Camera Height Offset` | `1.0` | Vertical offset of camera relative to target focal point. |
| **Camera Pose Jitter** | `Randomize Camera Pose` | `true` | Enables subtle translation and rotation jitter per run. |
| | `Camera Position Jitter` | `(0.35, 0.20, 0.35)` | Maximum translation offset per axis (meters). |
| | `Camera Rotation Jitter` | `(1.5, 2.0, 1.0)` | Maximum rotation offset (degrees pitch, yaw, roll). |
| **Spawn Ranges** | `Spawn Elevation Offset` | `5.0` | Elevation height above ground keeping trajectory in mid-air. |
| | `Spawn Radius` | `2.0` | Radius of spherical spawn volume. |
| | `Minimum Ground Clearance` | `3.5` | Strict lower bound preventing floor collisions on spawn. |
| | `Show Spawn Gizmos` | `true` | Visualizes the spherical spawn volume in Scene view. |
| **Object Scale** | `Min Size` / `Max Size` | `0.8` / `1.6` | Scale range for primitive shapes (Cube, Sphere, Cylinder, Capsule). |
| | `Custom Model Min/Max Size` | `3.5` / `6.0` | Scale range for real-world imported models (Calculators, etc.). |
| | `Reference Size` | `1.0` | Baseline unit scale representing 1:1 physical distance. |
| **Spawnable Assets** | `Spawnable Prefabs` | `[]` | Optional manual prefab array (merges with directory prefabs). |
| | `Spawnables Directory` | `Assets/Spawnables` | Folder containing spawnable 3D prefabs. |
| **Material Tinting** | `Spawnable Material` | Auto | Material targeted for random coloring (e.g. `spawnableMaterial`). |
| | `Randomize Spawnable Color` | `true` | Randomizes flat material colors while preserving textured objects. |
| **Time of Day** | `Randomize Time Of Day` | `true` | Randomizes solar pitch, azimuth, color, and skybox per run. |
| | `Sun Pitch Range` | `(10.0, 75.0)` | Solar elevation angle range in degrees (dawn/dusk to noon). |
| | `Sun Yaw Range` | `(-160.0, 160.0)` | Solar azimuth/compass range in degrees. |
| | `Sun Intensity Range` | `(0.8, 2.2)` | Solar light intensity from dawn/dusk to midday. |
| **Environment Colors**| `Randomize Environment Colors`| `true` | Randomizes colors of all three walls and the ground floor per run. |
| **Motion Blur (URP)**| `Enable Motion Blur` | `true` | Programmatically creates/configures active URP Volume. |
| | `Motion Blur Intensity` | `1.0` | Strength of point spread velocity streaks. |
| | `Motion Blur Quality` | `High` | URP Motion Blur quality pass level. |
| **Kinematic Impulse** | `Min / Max Initial Speed` | `6.0` / `12.0` | Transverse velocity across camera view (m/s). |
| | `Vertical Loft Range` | `(1.5, 4.0)` | Upward loft producing a high parabolic mid-air arc. |
| | `Angular Impulse Max` | `6.0` | Multi-axis rotational torque (rad/s). |
| **Run Management** | `Target Runs` | `10` | Total number of simulation runs before termination. |
| | `Frames Per Run` | `35` | Maximum frame captures per run. |
| | `Terminate On Rest` | `true` | Automatically ends run when object settles on floor ($< 0.25\text{ m/s}$). |
| | `Terminate When Out Of Bounds`| `true` | Automatically ends run when object exits camera frustum. |

---

## Project Structure

```text
KinematicDatasetFinal/
├── Assets/
│   ├── Materials/
│   │   ├── spawnableMaterial.mat   # Flat base material with MOTIONVECTORS pass enabled
│   │   ├── Wall.mat                # Wall material
│   │   └── Ground.mat              # Floor material
│   ├── Resources/
│   │   └── Spawnables/             # Runtime fallback spawnable prefabs
│   ├── Scripts/
│   │   └── KinematicGenerator.cs   # Monolithic generation engine
│   ├── Settings/
│   │   ├── PC_RPAsset.asset        # Universal Render Pipeline Asset
│   │   ├── PC_Renderer.asset       # URP Forward Renderer
│   │   └── SampleSceneProfile.asset# Post-processing volume profile (Motion Blur enabled)
│   ├── Spawnables/                 # Active spawnable prefabs (Cube, Calculator, Stapler, etc.)
│   └── Scenes/
│       └── SampleScene.unity       # Simulation stage with walls, floor, camera, and sun
├── Dataset/                        # Generated output runs (timestamped directories)
├── .gitignore                      # Git configuration (tracks Assets/ only)
├── DATAFLOW.md                     # Deep technical architecture specification
└── README.md                       # Project documentation
```

---

## Headless & Automated Execution (CLI)

The generator can be executed in headless batchmode on local machines or remote GPU clusters. All parameters can be overridden via command-line flags:

```bash
# Example: macOS standalone / headless execution
/Applications/Unity/Hub/Editor/6000.0.36f1/Unity.app/Contents/MacOS/Unity \
  -batchmode \
  -nographics \
  -projectPath "/path/to/KinematicDatasetFinal" \
  -executeMethod UnityEditor.EditorApplication.isPlaying \
  -targetRuns 100 \
  -framesPerRun 40 \
  -capturesPerSecond 30 \
  -outputDirectory "CustomDataset" \
  -minSize 0.8 \
  -maxSize 1.6 \
  -customModelMinSize 3.5 \
  -customModelMaxSize 6.0 \
  -randomizeTimeOfDay true \
  -randomizeEnvironmentColors true \
  -randomizeCameraPose true
```

