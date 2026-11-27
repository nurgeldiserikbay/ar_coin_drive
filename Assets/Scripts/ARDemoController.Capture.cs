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

    private IEnumerator CaptureAllScreens()
    {
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

    private void ClosePauseIfOpen()
    {
        if (_screen == UiScreen.Pause) ClosePause();
    }

    private IEnumerator Shot(string folder, string tag, string name)
    {
        // Let the screen's fade/slide-in finish and the garage turntable render.
        yield return new WaitForSecondsRealtime(0.9f);
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Path.Combine(folder, tag + "-" + name + ".png"));
        yield return null;
        yield return null;
    }
}
#endif
