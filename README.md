# Datasets (FallSim)

This repository contains simulated image datasets generated via FallSim. The datasets capture objects in motion, recording their physical properties and positional data directly into the filenames.

## Example Capture

<!-- 
  TODO: Upload your example capture image to the repository and link it here.
  Example: ![Example Capture](./images/example_capture.png) 
-->
<img width="867" height="466" alt="c 3 _v 12 60 _d -6 12 x -10 41 y 3 62 z_k 3 _p -0 56 x 4 09 y 0 33 z" src="https://github.com/user-attachments/assets/0f9eabfa-8844-455e-9cc0-06ff66665c59" />

## Simulation Parameters

The current dataset generation uses the following parameters:

*   **Target Runtime:** 1 second
*   **Target Runs:** 3 series
*   **Captures/Second:** 60 captures

## File Naming Convention

Image files are generated as `.png` files with a specific naming convention that encodes the current state of the object dictated by FallSim. All variable values are wrapped in brackets `[]`.

**Format:**
`c[{count}]_v[{velocity}]_d[{directionX}]x[{directionY}]y[{directionZ}]z_k[{kind}]_p[{posX}]x[{posY}]y[{posZ}]z.png`

**Variable Breakdown:**

| Variable | Description |
| :--- | :--- |
| `count` | Value of `_screenshotCount`. |
| `velocity` | Current state velocity of the object (derived from the object's lerp speed). |
| `directionXYZ` | Current direction of the object (derived from lerp target and lerp speed per X, Y, Z axes). |
| `kind` | Integer value representing the object type (e.g., `0` = sphere, `1` = cylinder, `2` = square, etc.). |
| `posXYZ` | Current X, Y, and Z position of the object. |

**Example Filename:**
```text
c[16]_v[5.34]_d[-5.04]x[-9.81]y[1.30]z_k[4]_p[2.01]x[4.32]y[-1]z.png
```

## Changelog

### 9-30-26
*   Features 3 series of images.
*   Motion blur works correctly.
*   Established standard simulation parameters and naming conventions.

### 9-21-26
*   Initial rough draft (deprecated/not highly usable).
