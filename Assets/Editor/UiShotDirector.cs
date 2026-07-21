// UI screenshot director: self-driving play-mode session that stages every UI state
// and captures Game-view screenshots into Docs/UI/shots/. Start via the menu item;
// it enters play, paces the match itself, and exits play when done.
// Exists so UI shots are reproducible after the visual redesign (before/after diffs)
// and because it must not depend on MCP execute_code (CodeDom cmdline overflow, 2026-07-19).
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class UiShotDirector
{
    const string FlagKey = "UiShot.Active";
    const string RetryKey = "UiShot.Retries";
    const string MapName = "THE MANSION";

    static int _step;
    static float _t;
    static float _stepEnteredAt;      // realtime, last-resort cap
    static float _stepEnteredAtGame;  // game time - frozen frames must not kill the run
    static MatchManager _mm;
    static PlayerRig _rig;
    static Character _player;
    static bool _hooked;

    static string ShotDir
    {
        get
        {
            var root = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(root, "Docs", "UI", "shots");
        }
    }

    [MenuItem("Tools/UI Shots/Run Full Session")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[UiShot] Already in play mode - stop play first.");
            return;
        }
        Directory.CreateDirectory(ShotDir);
        // If a concurrent session edits scripts on disk, do not reload mid-play.
        EditorPrefs.SetInt("ScriptCompilationDuringPlay", 1); // RecompileAfterFinishedPlaying
        DisableErrorPause();
        SessionState.SetBool(FlagKey, true);
        SessionState.SetInt(RetryKey, 0);
        Status("starting - entering play mode");
        EditorApplication.EnterPlaymode();
    }

    [MenuItem("Tools/UI Shots/Abort")]
    public static void Abort()
    {
        SessionState.SetBool(FlagKey, false);
        Status("aborted by menu");
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }

    static string ArmFile
    {
        get { return Path.Combine(ShotDir, "_arm.txt"); }
    }

    [InitializeOnLoadMethod]
    static void Bootstrap()
    {
        // Two ways in: the menu item (SessionState) or an arm file on disk written
        // by tooling — the file survives the play-mode domain reload, so dropping
        // it and entering play is equivalent to clicking the menu item.
        if (!SessionState.GetBool(FlagKey, false) && File.Exists(ArmFile))
        {
            EditorPrefs.SetInt("ScriptCompilationDuringPlay", 1);
            DisableErrorPause();
            SessionState.SetBool(FlagKey, true);
            SessionState.SetInt(RetryKey, 0);
            Status("armed via file - waiting for play mode");
            // arm file may request the play-mode entry itself (retry automation)
            if (File.ReadAllText(ArmFile).Contains("autoplay")
                && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Status("autoplay - entering play mode");
                EditorApplication.delayCall += () =>
                {
                    if (!EditorApplication.isPlayingOrWillChangePlaymode)
                        EditorApplication.EnterPlaymode();
                };
            }
        }
        if (!SessionState.GetBool(FlagKey, false)) return;
        Hook();
    }

    static void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        _step = 0;
        EditorApplication.update += Tick;
    }

    static void Finish(string msg)
    {
        SessionState.SetBool(FlagKey, false);
        try { if (File.Exists(ArmFile)) File.Delete(ArmFile); } catch { }
        Status(msg);
        EditorApplication.update -= Tick;
        _hooked = false;
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }

    static void Tick()
    {
        try
        {
            if (!Application.isPlaying) return; // wait for play to begin
            TickInner();
        }
        catch (Exception e)
        {
            Finish("FAILED step " + _step + ": " + e.Message);
        }
    }

    static void Step(int next)
    {
        _step = next;
        _stepEnteredAt = Time.realtimeSinceStartup;
        _stepEnteredAtGame = Time.time;
    }

    static bool Waited(float seconds) { return Time.time >= _t + seconds; }

    static void TickInner()
    {
        // stall guard: count GAME time so an unfocused (frozen) editor just pauses
        // the run instead of killing it; realtime only as a distant last resort
        if (_step > 0 && (Time.time - _stepEnteredAtGame > 60f
                          || Time.realtimeSinceStartup - _stepEnteredAt > 600f))
        {
            Finish("TIMEOUT in step " + _step);
            return;
        }

        switch (_step)
        {
            case 0: // find managers, speed up the lobby
                _mm = UnityEngine.Object.FindFirstObjectByType<MatchManager>();
                _rig = UnityEngine.Object.FindFirstObjectByType<PlayerRig>();
                if (_mm == null || _rig == null) return;
                _mm.joinIntervalMin = 0.15f;
                _mm.joinIntervalMax = 0.3f;
                _mm.lobbyCountdownSeconds = 30f;
                _mm.botVolunteerChance = 1f; // guarantee pad volunteers so the player stays hider
                Status("step0: managers found, lobby accelerated");
                Step(1);
                break;

            case 1: // wait for full roster
                if (_mm.Characters.Count < _mm.totalCharacters) return;
                _t = Time.time;
                Status("step1: room full");
                Step(2);
                break;

            case 2: // give volunteers time to reach the pad, then lobby shot
                if (!Waited(4f)) return;
                Shot("01-lobby.png");
                _t = Time.time;
                Step(3);
                break;

            case 3: // staged hider loading screen
                if (!Waited(1f)) return;
                LoadingScreen.Show(MapName, (_mm.totalCharacters - 1) + " HIDERS DEPLOYING", 6f, false);
                _t = Time.time;
                Status("step3: hider loading screen up");
                Step(4);
                break;

            case 4:
                if (!Waited(2.6f)) return;
                Shot("03-travel-hider.png");
                _t = Time.time;
                Step(5);
                break;

            case 5: // wait for it to self-destroy, then hunter variant
                if (!Waited(7.2f)) return;
                LoadingScreen.Show(MapName, "THE HUNT BEGINS", 6f, true);
                _t = Time.time;
                Status("step5: hunter loading screen up");
                Step(6);
                break;

            case 6:
                if (!Waited(2.6f)) return;
                Shot("04-travel-hunter.png");
                _t = Time.time;
                Step(7);
                break;

            case 7: // overlay gone -> cut the lobby countdown short
                if (!Waited(7.2f)) return;
                SetPhaseEndsAt(Time.time + 1.5f);
                Status("step7: lobby countdown cut");
                Step(8);
                break;

            case 8: // catch the hunter reveal: plate up, pick frozen, flash+shake settled
                var revealRoot = GetPrivate<GameObject>(_mm, "_revealRoot");
                var revealFoot = GetPrivate<Text>(_mm, "_revealFoot");
                var revealName = GetPrivate<Text>(_mm, "_revealName");
                if (revealRoot != null && revealRoot.activeSelf
                    && revealFoot != null && !string.IsNullOrEmpty(revealFoot.text)
                    && revealName != null && revealName.transform.localScale.x < 1.005f)
                {
                    Shot("02-hunter-reveal.png");
                    Status("step8: reveal captured");
                    Step(9);
                }
                else if (_mm.Phase == MatchPhase.Hide || _mm.Phase == MatchPhase.Travel)
                {
                    Status("step8: reveal missed, moving on");
                    Step(9);
                }
                break;

            case 9: // wait for HIDE; bail out and retry if the player got picked
                if (_mm.Phase != MatchPhase.Hide) return;
                _player = _mm.Characters.Find(c => c.isPlayer);
                if (_player == null) { Finish("FAILED: no player character"); return; }
                if (_player.team == Team.Hunter)
                {
                    int retries = SessionState.GetInt(RetryKey, 0);
                    if (retries >= 2) { Finish("FAILED: player kept being hunter"); return; }
                    SessionState.SetInt(RetryKey, retries + 1);
                    Status("step9: player is hunter - reloading scene for retry " + (retries + 1));
                    _mm = null; _rig = null; _player = null;
                    SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                    Step(0);
                    return;
                }
                _t = Time.time;
                Status("step9: hide phase, player is hider");
                Step(10);
                break;

            case 10: // hider controls
                if (!Waited(1.2f)) return;
                Shot("05-hide-controls.png");
                Step(11);
                break;

            case 11: // pose panel open (via the toggle so the dim + highlight come along)
                if (!Waited(1.8f)) return;
                InvokePrivate(_rig, "TogglePosePanel");
                Step(12);
                break;

            case 12:
                if (!Waited(2.4f)) return;
                Shot("06-pose-panel.png");
                Step(13);
                break;

            case 13: // close panel, enter paint mode
                if (!Waited(3.0f)) return;
                var panel2 = GetPrivate<GameObject>(_rig, "_posePanel");
                if (panel2 != null && panel2.activeSelf) InvokePrivate(_rig, "TogglePosePanel");
                InvokePrivate(_rig, "OnAction");
                Status("step13: paint mode entered");
                Step(14);
                break;

            case 14:
                if (!Waited(4.6f)) return;
                Shot("07-paint-mode.png");
                Step(15);
                break;

            case 15: // leave paint mode
                if (!Waited(5.4f)) return;
                var paint = GetPrivate<SelfPaintMode>(_rig, "_paint");
                if (paint != null && paint.Active) paint.Exit();
                Step(16);
                break;

            case 16: // cut hide short
                if (!Waited(6.2f)) return;
                SetPhaseEndsAt(Time.time + 1f);
                Status("step16: hide cut, waiting for hunter travel");
                Step(17);
                break;

            case 17: // hunter-incoming warning (travel back in)
                if (_mm.Phase != MatchPhase.Travel) return;
                _t = Time.time;
                Step(18);
                break;

            case 18:
                if (!Waited(1.2f)) return;
                Shot("11-hunter-incoming.png");
                Status("step18: hunter incoming captured");
                Step(19);
                break;

            case 19: // seek begins
                if (_mm.Phase != MatchPhase.Seek) return;
                _t = Time.time;
                Status("step19: seek phase");
                Step(20);
                break;

            case 20: // taunt for the "!" marker + gold style pulse
                if (!Waited(0.6f)) return;
                if (_player.team == Team.Hider) _mm.DoTaunt(_player);
                Step(21);
                break;

            case 21:
                if (!Waited(1.0f)) return;
                Shot("08-seek-hider.png");
                Step(22);
                break;

            case 22: // convert the player -> hunter FPS view
                if (!Waited(2.0f)) return;
                if (_player.team == Team.Hider) _mm.Convert(_player);
                Status("step22: player converted to hunter");
                Step(23);
                break;

            case 23:
                if (!Waited(3.6f)) return;
                Shot("09-seek-hunter-fps.png");
                Step(24);
                break;

            case 24: // sweep remaining hiders to end the match
                if (!Waited(4.4f)) return;
                var hiders = new List<Character>();
                foreach (var c in _mm.Characters)
                    if (c != null && !c.isDecoy && c.team == Team.Hider) hiders.Add(c);
                foreach (var c in hiders) _mm.Convert(c);
                Status("step24: converted " + hiders.Count + " hiders to end the match");
                Step(25);
                break;

            case 25: // result screen
                if (_mm.Phase != MatchPhase.Result) return;
                _t = Time.time;
                Step(26);
                break;

            case 26: // let the resultB entrance sequence finish before shooting
                if (!Waited(1.5f)) return;
                Shot("10-result.png");
                Step(27);
                break;

            case 27: // one extra frame for the async capture, then wrap up
                if (!Waited(2.0f)) return;
                Finish("DONE");
                break;
        }
    }

    // ---------- helpers ----------

    static void Shot(string file)
    {
        ScreenCapture.CaptureScreenshot(Path.Combine(ShotDir, file));
        Status("captured " + file);
    }

    static void Status(string msg)
    {
        Debug.Log("[UiShot] " + msg);
        try { File.AppendAllText(Path.Combine(ShotDir, "_status.txt"), DateTime.Now.ToString("HH:mm:ss") + "  " + msg + "\n"); }
        catch { /* never let logging kill the run */ }
    }

    static void SetPhaseEndsAt(float value)
    {
        var f = typeof(MatchManager).GetField("_phaseEndsAt", BindingFlags.Instance | BindingFlags.NonPublic);
        if (f != null) f.SetValue(_mm, value);
    }

    static T GetPrivate<T>(object target, string field) where T : class
    {
        var f = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        return f == null ? null : f.GetValue(target) as T;
    }

    static void InvokePrivate(object target, string method)
    {
        var m = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (m != null) m.Invoke(target, null);
    }

    static void DisableErrorPause()
    {
        try
        {
            var t = typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntries");
            var m = t.GetMethod("SetConsoleFlag", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            m.Invoke(null, new object[] { 4, false }); // 4 = error pause
        }
        catch { }
    }
}
