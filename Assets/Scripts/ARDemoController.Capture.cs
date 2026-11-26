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
