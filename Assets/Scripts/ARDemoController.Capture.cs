#if UI_CAPTURE
using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>
/// UI review build only (scripting define UI_CAPTURE, Windows player): walks through every screen
/// at several phone and tablet sizes, in portrait and landscape, and saves a screenshot of each.
/// The UI scale is forced to what a real phone of that size in dp would use, so layouts match.
/// Never part of a phone build.
/// </summary>
public sealed partial class ARDemoController
{
    private static readonly (string Name, int Width, int Height)[] CaptureDevices =
    {
        ("phone", 411, 914),     // modern 20:9 phone, in dp
        ("small", 360, 640),     // small 16:9 phone
        ("tablet", 800, 1280)    // 10" tablet
    };

    public static bool CaptureForceAdOffer;

    // Store-art hooks: the player's car drives itself, the UI can be hidden, the camera held still.
    public static bool CaptureAutopilot;
    public static bool CaptureHideUi;
    public static bool CaptureCameraLocked;
    private bool _captureKeepHint;

    private IEnumerator CaptureAllScreens()
    {
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-layouts") >= 0)
        {
            yield return LayoutShots();
            Application.Quit();
            yield break;
        }

        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-storeart") >= 0)
        {
            yield return StoreArtShots();
            Application.Quit();
            yield break;
        }

        var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "shots"));
        Directory.CreateDirectory(folder);
        yield return new WaitForSecondsRealtime(2f);

        // A track with a loop and two ramps, so the stunt shots have something to show.
        GameSettings.Track = TrackLayout.Stadium;
        GameSettings.TrackSize = TrackSize.L;
        var enteredGame = false;
        foreach (var device in CaptureDevices)
        {
            foreach (var landscape in new[] { false, true })
            {
                var dpW = landscape ? device.Height : device.Width;
                var dpH = landscape ? device.Width : device.Height;
                // As large as the desktop allows, keeping the phone's shape.
                var k = Mathf.Min(1.6f, 1500f / dpW, 980f / dpH);
                var pxW = Mathf.RoundToInt(dpW * k);
                var pxH = Mathf.RoundToInt(dpH * k);
                Screen.SetResolution(pxW, pxH, FullScreenMode.Windowed);
                for (var i = 0; i < 60 && (Screen.width != pxW || Screen.height != pxH); i++) yield return null;
                UiKit.ForcedScale = Screen.width / (float)dpW;
                var tag = device.Name + (landscape ? "-land" : "-port");
                Debug.Log("CAPTURE size " + tag + " " + Screen.width + "x" + Screen.height + " scale " + UiKit.ForcedScale);

                ClosePauseIfOpen();
                _vipOpen = false;
                _screen = UiScreen.Menu;
                yield return Shot(folder, tag, "01-menu");
                _vipOpen = true;
                yield return Shot(folder, tag, "02-vip");
                _vipOpen = false;
                _screen = UiScreen.Lobby;
                yield return Shot(folder, tag, "03-lobby");
                OpenSettings(UiScreen.Menu);
                yield return Shot(folder, tag, "04-settings");
                OpenGarage(UiScreen.Menu);
                yield return Shot(folder, tag, "05-garage");
                _screen = UiScreen.Tracks;
                yield return Shot(folder, tag, "06-tracks");
                _pickForAR = false;
                _screen = UiScreen.Modes;
                yield return Shot(folder, tag, "06b-modes");
                _screen = UiScreen.Locations;
                yield return Shot(folder, tag, "07-locations");

                if (!enteredGame)
                {
                    EnterVirtualMode();
                    enteredGame = true;
                    yield return new WaitForSecondsRealtime(3f);
                }

                _screen = UiScreen.Game;
                ChangeMode(GameMode.Free);
                yield return Shot(folder, tag, "08-hud-free");
                ChangeMode(GameMode.Race);
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot(folder, tag, "09-hud-race");
                yield return StuntShots(folder, tag);
                ChangeMode(GameMode.Battle);
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot(folder, tag, "10-hud-battle");
                OpenPause();
                yield return Shot(folder, tag, "11-pause");
                ClosePause();

                ChangeMode(GameMode.Race);
                yield return new WaitForSecondsRealtime(0.5f);
                _state = GameState.Finished;
                if (_player != null) _player.Place = 1;
                _roundEarned = 360;
                _newBest = true;
                CaptureForceAdOffer = true;
                yield return Shot(folder, tag, "12-results");
                CaptureForceAdOffer = false;
                _adBonus = 360;
                yield return Shot(folder, tag, "13-results-bonus");
                _adBonus = 0;
                StartRound();
            }
        }

        Debug.Log("CAPTURE_DONE " + folder);
        Application.Quit();
    }

    /// <summary>The player frozen half way round the loop, then in the air over a ramp mid-spin.</summary>
    private IEnumerator StuntShots(string folder, string tag)
    {
        if (_player == null) yield break;
        TrackStunt loop = null;
        TrackStunt ramp = null;
        foreach (var stunt in RaceTrack.Shared.Stunts)
        {
            if (stunt.Kind == StuntKind.Loop && loop == null) loop = stunt;
            if (stunt.Kind == StuntKind.Ramp && ramp == null) ramp = stunt;
        }

        Time.timeScale = 0f;
        if (loop != null)
        {
            _player.Loop = loop;
            _player.LoopAngle = 2.3f;
            _player.LoopEntrySpeed = 0.6f;
            _player.LoopLane = RaceTrack.Shared.LoopEntryLane;
            _player.Heading = loop.Heading;
            PlaceOnLoop(_player);
            ApplyTransform(_player);
            _snapChaseCamera = true;
            yield return Shot(folder, tag, "09b-loop");
            _player.Loop = null;
        }

        if (ramp != null)
        {
            _player.Position = ramp.Point + ramp.Tangent * 0.22f;
            _player.Heading = ramp.Heading;
            _player.Height = 0.11f;
            _player.Pitch = 8f;
            _player.Spin = 130f;
            ApplyTransform(_player);
            _snapChaseCamera = true;
            yield return Shot(folder, tag, "09c-jump");
        }

        _player.Height = 0f;
        _player.Pitch = 0f;
        _player.Spin = 0f;
        Time.timeScale = 1f;
    }

    /// <summary>
    /// Google Play listing shots (run the capture build with -storeart): full-screen 1920x1080
    /// landscape, UI sized like a 1080p phone, real races driven by the computer.
    /// Saved to storeart/ next to the exe; tools/StoreArtCompose.ps1 adds the captions.
    /// </summary>
    private IEnumerator StoreArtShots()
    {
        var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "storeart"));
        Directory.CreateDirectory(folder);
        Screen.SetResolution(1920, 1080, FullScreenMode.FullScreenWindow);
        for (var i = 0; i < 120 && (Screen.width != 1920 || Screen.height != 1080); i++) yield return null;
        UiKit.ForcedScale = Screen.width / 732f;
        Debug.Log("STOREART size " + Screen.width + "x" + Screen.height);
        yield return new WaitForSecondsRealtime(2f);

        GameSettings.Track = TrackLayout.Stadium;
        GameSettings.TrackSize = TrackSize.L;
        GameSettings.Bots = 3;
        GameSettings.Location = GameLocation.House;

        // A desktop has no AR; the menu should look as it does on an ARCore phone.
        _arChecked = true;
        _arSupported = true;
        _screen = UiScreen.Menu;
        yield return Shot(folder, "art", "menu");
        OpenGarage(UiScreen.Menu);
        yield return Shot(folder, "art", "garage");

        EnterVirtualMode();
        yield return new WaitForSecondsRealtime(3f);
        _screen = UiScreen.Game;
        if (_mode != GameMode.Race) ChangeMode(GameMode.Race);
        else StartRound();

        // House, from where the player stands.
        _view = ViewMode.Phone;
        yield return DriveFor(6f);
        yield return Shot(folder, "art", "race-phone");
        _view = ViewMode.Chase;
        _snapChaseCamera = true;
        yield return DriveFor(2.5f);
        yield return Shot(folder, "art", "race-chase");

        yield return StuntArt(folder);

        // Backyard: behind the car, the track picker and a battle; then the workshop.
        GameSettings.Location = GameLocation.Backyard;
        GameSettings.Track = TrackLayout.Twister;
        EnterVirtualMode();
        yield return new WaitForSecondsRealtime(2f);
        ApplyTrackChoice();
        StartRound();
        _view = ViewMode.Chase;
        _snapChaseCamera = true;
        yield return DriveFor(5f);
        yield return Shot(folder, "art", "backyard-chase");
        CaptureHideUi = true;
        yield return Shot(folder, "art", "backyard-clean");
        CaptureHideUi = false;
        _screen = UiScreen.Tracks;
        yield return Shot(folder, "art", "tracks");
        _screen = UiScreen.Game;

        ChangeMode(GameMode.Battle);
        _view = ViewMode.Chase;
        _snapChaseCamera = true;
        yield return DriveFor(1.2f);
        yield return Shot(folder, "art", "battle-chase");
        _view = ViewMode.Phone;
        yield return Shot(folder, "art", "battle-phone");

        ChangeMode(GameMode.Race);
        GameSettings.Location = GameLocation.Workshop;
        GameSettings.Track = TrackLayout.Serpent;
        EnterVirtualMode();
        yield return new WaitForSecondsRealtime(2f);
        ApplyTrackChoice();
        StartRound();
        _view = ViewMode.Phone;
        yield return DriveFor(5f);
        yield return Shot(folder, "art", "workshop-phone");

        CaptureAutopilot = false;
        Debug.Log("STOREART_DONE " + folder);
    }

    /// <summary>Every location from above and at an angle, with a race track and the battle ring (layout review).</summary>
    private IEnumerator LayoutShots()
    {
        var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "layouts"));
        Directory.CreateDirectory(folder);
        Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
        yield return new WaitForSecondsRealtime(2f);
        CaptureHideUi = true;
        GameSettings.Bots = 3;
        foreach (var location in new[] { GameLocation.House, GameLocation.Backyard, GameLocation.Workshop })
        {
            GameSettings.Location = location;
            foreach (var size in new[] { TrackSize.S, TrackSize.L })
            {
                GameSettings.Track = TrackLayout.Twister;
                GameSettings.TrackSize = size;
                EnterVirtualMode();
                yield return new WaitForSecondsRealtime(2f);
                _screen = UiScreen.Game;
                if (_mode != GameMode.Race) ChangeMode(GameMode.Race);
                ApplyTrackChoice();
                StartRound();
                yield return LayoutViews(folder, location + "-race-" + size);
            }

            GameSettings.ArenaSize = 1;
            ChangeMode(GameMode.Battle);
            yield return LayoutViews(folder, location + "-battle-M");
            ChangeMode(GameMode.Race);
        }

        CaptureHideUi = false;
        CaptureCameraLocked = false;
    }

    private IEnumerator LayoutViews(string folder, string name)
    {
        yield return new WaitForSecondsRealtime(1f);
        LockCamera(new Vector3(0f, 2.4f, -0.01f), Vector3.zero);
        _arCamera.fieldOfView = 92f;
        yield return Shot(folder, "top", name);
        LockCamera(new Vector3(0f, 1.9f, -2.3f), new Vector3(0f, 0f, 0.3f));
        _arCamera.fieldOfView = 60f;
        yield return Shot(folder, "angle", name);
        CaptureCameraLocked = false;
    }

    /// <summary>Starts the round at once (no countdown) and lets every car race for a while.</summary>
    private IEnumerator DriveFor(float seconds)
    {
        CaptureAutopilot = true;
        if (_state == GameState.Ready || _state == GameState.Countdown)
        {
            _state = GameState.Countdown;
            _countdown = 0.01f;
        }

        yield return new WaitForSecondsRealtime(seconds);
        SetMessage(null);
        _hint = null;
    }

    /// <summary>The loop and a jump seen from the roadside, frozen mid-stunt, with and without UI.</summary>
    private IEnumerator StuntArt(string folder)
    {
        TrackStunt loop = null;
        TrackStunt ramp = null;
        foreach (var stunt in RaceTrack.Shared.Stunts)
        {
            if (stunt.Kind == StuntKind.Loop && loop == null) loop = stunt;
            if (stunt.Kind == StuntKind.Ramp && ramp == null) ramp = stunt;
        }

        Time.timeScale = 0f;
        if (loop != null)
        {
            _player.Loop = loop;
            _player.LoopAngle = 2.7f;
            _player.LoopEntrySpeed = 0.6f;
            _player.LoopLane = RaceTrack.Shared.LoopEntryLane;
            _player.Heading = loop.Heading;
            PlaceOnLoop(_player);
            ApplyTransform(_player);
            var center = new Vector3(loop.Point.x, RaceTrack.LoopRadius, loop.Point.y);
            var side = new Vector3(loop.Outward.x, 0f, loop.Outward.y);
            var along = new Vector3(loop.Tangent.x, 0f, loop.Tangent.y);
            LockCamera(center + side * 0.62f - along * 0.30f + Vector3.up * 0.04f, center + along * 0.03f);
            yield return Shot(folder, "art", "loop");
            CaptureHideUi = true;
            yield return Shot(folder, "art", "loop-clean");
            CaptureHideUi = false;
            _player.Loop = null;
        }

        if (ramp != null)
        {
            _player.Position = ramp.Point + ramp.Tangent * 0.2f;
            _player.Heading = ramp.Heading;
            _player.Airborne = true;
            _player.VSpeed = 0.25f;
            _player.Height = 0.12f;
            _player.Spin = 140f;
            ApplyTransform(_player);
            var car = new Vector3(_player.Position.x, _player.Height, _player.Position.y);
            var side = new Vector3(ramp.Outward.x, 0f, ramp.Outward.y);
            var along = new Vector3(ramp.Tangent.x, 0f, ramp.Tangent.y);
            LockCamera(car + side * 0.48f + along * 0.10f - Vector3.up * 0.06f, car - along * 0.10f);
            ShowHint("AIR!  Steer to spin");
            _captureKeepHint = true;
            yield return Shot(folder, "art", "jump");
            _captureKeepHint = false;
            CaptureHideUi = true;
            yield return Shot(folder, "art", "jump-clean");
            CaptureHideUi = false;
        }

        _player.Airborne = false;
        _player.VSpeed = 0f;
        _player.Height = 0f;
        _player.Pitch = 0f;
        _player.Spin = 0f;
        CaptureCameraLocked = false;
        _snapChaseCamera = true;
        Time.timeScale = 1f;
    }

    /// <summary>Holds the camera at a point in play-area coordinates, looking at another.</summary>
    private void LockCamera(Vector3 localPosition, Vector3 localTarget)
    {
        CaptureCameraLocked = true;
        var position = _arena.TransformPoint(localPosition);
        var target = _arena.TransformPoint(localTarget);
        _arCamera.fieldOfView = 55f;
        _arCamera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position, _arena.up));
    }

    private void ClosePauseIfOpen()
    {
        if (_screen == UiScreen.Pause) ClosePause();
    }

    private IEnumerator Shot(string folder, string tag, string name)
    {
        // Let the screen's fade/slide-in finish and the garage turntable render.
        yield return new WaitForSecondsRealtime(0.9f);
        if (!_captureKeepHint) _hint = null;
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Path.Combine(folder, tag + "-" + name + ".png"));
        yield return null;
        yield return null;
    }
}
#else
public sealed partial class ARDemoController
{
    // Phone builds: the store-art hooks are always off.
    private static readonly bool CaptureAutopilot = false;
    private static readonly bool CaptureHideUi = false;
    private static readonly bool CaptureCameraLocked = false;
}
#endif
