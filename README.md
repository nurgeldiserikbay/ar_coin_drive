# AR Coin Drive

A small Unity AR prototype for Android. The player scans and taps an upward-facing horizontal surface, the play area is attached to an AR anchor, and each round is a 45-second race to collect 8 floating coins inside a visible 1.5 m play area while steering around toy barriers. Every round gets a new random layout, and the fastest full clear is saved as the best time.

## Included

- Unity 6 project scaffold with AR Foundation and ARCore XR packages pinned to 6.6.2.
- Editor menu command that generates the AR session, camera rig, visible translucent plane mesh, horizontal and vertical plane detection, raycast and occlusion managers, and demo scene.
- Optional environment-depth sampling that can stop the car near visible obstacles on supported devices.
- Procedural lightweight car and coin models; no third-party model download is required for the first test.
- Multi-touch steering with forward and reverse (hold a steering arrow and GO/BACK with two thumbs at once; WASD/arrow keys in the Editor), a guided coin route, three virtual toy barriers, reset button, virtual area boundaries, translucent detected-plane visualization, and an AR tracking, camera-frame, plane, anchor, and depth diagnostic readout.
- Round flow: the timer starts on the first GO or steering input; when all coins are collected or time runs out, AGAIN starts a new random layout on the same anchor and RESET returns to floor scanning.
- Toy RC-car handling: acceleration, braking and rolling friction; the car turns only while moving and steering flips in reverse. Collisions use the car's rotated body box against each barrier, and coins are collected when any part of the car touches them.
- Procedural model materials come from one shared `Resources/ARCoinDriveLit` material (Standard shader) and are cached per colour, so the shader is included in builds and resets do not leak materials.

## Open and run

1. Open this folder with Unity 6.0 (6000.0) or a compatible later Unity 6 editor. Let Package Manager resolve the packages.
2. Switch the build target to Android.
3. In Edit > Project Settings > XR Plug-in Management, ARCore is already the only Android loader, and both ARCore and ARCore Depth are set to Optional, so the game installs on any Android 10+ phone (see "Without AR" below). Verify this if Unity asks to install XR Plug-in Management.
4. After pulling updates, run Tools > AR Coin Drive > Create Demo Scene again. This regenerates Assets/Scenes/ARCoinDriveDemo.unity, creates the plane-visualizer prefab/material, and sets Android minimum API level to 29 (Android 10), matching ARCore's Vulkan requirement.
5. Connect an ARCore-compatible Android phone, enable USB debugging, and build/run. The Unity Editor does not provide live AR camera tracking.
6. Slowly move the phone to scan a textured, well-lit floor. Detected planes are tinted teal (the tint hides once the arena is placed). Tap a horizontal floor or tabletop (not a wall) to place the arena, then press GO to start the timer and steer with the arrows while holding GO or BACK.

## Without AR

- On start the game runs `ARSession.CheckAvailability()`. If the device cannot run AR, it switches automatically to a virtual table: a 3D tabletop with the same arena, rounds and controls, viewed through a chase camera.
- If ARCore is supported but not installed, ARSession asks the player to install Google Play Services for AR. The scanning screen also has a PLAY WITHOUT AR button for declined installs, missing camera permission, or a room that is too dark to track.
- In the Editor AR is unavailable, so the virtual table starts automatically, which makes quick gameplay testing possible without a phone.
- `ARRigSetup` repairs older generated scenes at runtime: it adds the camera TrackedPoseDriver (without it the camera never follows the phone), an ARAnchorManager, and plane visuals when no plane prefab is assigned.

## Current prototype limits

- This is a playable AR foundation check, not a finished game.
- The play boundary is virtual and fixed at 1.5 m square. The play area is attached to a plane anchor when the provider supports trackable attachments.
- The prototype requests vertical plane detection and stops the car near some tracked flat vertical surfaces. This does not recognize every object or guarantee safe real-world collision avoidance.
- On supported devices, environment-depth sampling provides an approximate proximity guard and environment occlusion for visible objects. It is geometric depth sampling, not AI object recognition or complete room reconstruction.
- Depth readings depend on device support, camera angle, tracking, and visibility. The app falls back to vertical-plane checks when depth is unavailable; both paths need real-phone testing.
- The car and coins are procedural placeholder models intended to prove scale and interaction. Replace them with authored stylized assets during visual production.
- The project has not been built or checked on a physical Android device in this environment.

## Packages

- com.unity.xr.arfoundation 6.6.2
- com.unity.xr.arcore 6.6.2
- com.unity.xr.management 4.7.0
