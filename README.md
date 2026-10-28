# AR Coin Drive

A small Unity AR prototype for Android. The first playable loop lets the player scan and tap an upward-facing horizontal surface, attach the play area to an AR anchor, drive around toy barriers inside a visible 1.5 m play area, and collect floating coins.

## Included

- Unity 6 project scaffold with AR Foundation and ARCore XR packages pinned to 6.6.2.
- Editor menu command that generates the AR session, camera rig, visible translucent plane mesh, horizontal and vertical plane detection, raycast and occlusion managers, and demo scene.
- Optional environment-depth sampling that can stop the car near visible obstacles on supported devices.
- Procedural lightweight car and coin models; no third-party model download is required for the first test.
- Touch steering with forward and reverse, a guided coin route, three virtual toy barriers, reset button, virtual area boundaries, translucent detected-plane visualization, and an AR tracking/depth diagnostic readout.

## Open and run

1. Open this folder with Unity 6.0 (6000.0) or a compatible later Unity 6 editor. Let Package Manager resolve the packages.
2. Switch the build target to Android.
3. In Edit > Project Settings > XR Plug-in Management, install XR Plug-in Management if Unity asks, then enable ARCore for Android. Set ARCore Depth to Optional so devices without Depth API support can still run the game.
4. After pulling updates, run Tools > AR Coin Drive > Create Demo Scene again. This regenerates Assets/Scenes/ARCoinDriveDemo.unity, creates the plane-visualizer prefab/material, and sets Android minimum API level to 29 (Android 10), matching ARCore's Vulkan requirement.
5. Connect an ARCore-compatible Android phone, enable USB debugging, and build/run. The Unity Editor does not provide live AR camera tracking.
6. Slowly move the phone to scan a textured, well-lit floor. Detected planes are tinted teal. Tap a horizontal floor or tabletop (not a wall) to place the arena, then steer and use GO/BACK to drive.

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
