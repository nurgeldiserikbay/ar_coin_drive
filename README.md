# AR Coin Drive

A small Unity AR prototype for Android. The first playable loop lets the player scan and tap a horizontal surface, place a tiny car, drive inside a visible 1.5 m play area, and collect floating coins.

## Included

- Unity 6 project scaffold with AR Foundation and ARCore XR packages pinned to 6.4.1.
- Editor menu command that generates the AR session, camera rig, horizontal plane detection, raycast manager, and demo scene.
- Procedural lightweight car and coin models; no third-party model download is required for the first test.
- Simple on-screen steering and drive controls, coin counter, reset button, and virtual area boundaries.

## Open and run

1. Open this folder with Unity 6.0 (6000.0) or a compatible later Unity 6 editor. Let Package Manager resolve the packages.
2. Switch the build target to Android.
3. In Edit > Project Settings > XR Plug-in Management, install XR Plug-in Management if Unity asks, then enable ARCore for Android.
4. Choose Tools > AR Coin Drive > Create Demo Scene. The generated scene is Assets/Scenes/ARCoinDriveDemo.unity.
5. Connect an ARCore-compatible Android phone, enable USB debugging, and build/run. The Unity Editor does not provide live AR camera tracking.
6. Slowly move the phone to scan a textured, well-lit floor, tap the detected floor, then use the arrow and GO controls.

## Current prototype limits

- This is a playable AR foundation check, not a finished game.
- The play boundary is virtual and fixed at 1.5 m square.
- Real furniture and arbitrary walls are not yet used for collision or pathfinding. Depth-based obstacle avoidance is a later feature and must be tested on supported devices.
- The car and coins are procedural placeholder models intended to prove scale and interaction. Replace them with authored stylized assets during visual production.
- The project has not been built or checked on a physical Android device in this environment.

## Packages

- com.unity.xr.arfoundation 6.4.1
- com.unity.xr.arcore 6.4.1
- com.unity.xr.management 4.5.0
