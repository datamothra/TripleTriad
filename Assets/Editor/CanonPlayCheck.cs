using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

// 从菜单运行真实 Play Mode 回归检查，不修改或保存场景。
[InitializeOnLoad]
public static class CanonPlayCheck
{
    static CanonRhythm rhythm;
    static GameManager game;
    static int phase, index;
    static double phaseAt;
    static Vector3[] before;
    static bool captured;
    static double started;
    static Keyboard testKeyboard;
    static bool checkedPause;
    static float pausedAt, pausedAudio;
    static CanonPlayCheck()
    {
        // 编辑过程中重编译会清空测试状态，结束旧测试，不在用户返回窗口后继续注入按键。
        if (!Application.isBatchMode && SessionState.GetBool("CanonCheck", false))
        {
            SessionState.SetBool("CanonCheck", false);
            EditorApplication.delayCall += () => { if (EditorApplication.isPlaying) EditorApplication.isPlaying = false; };
        }
        EditorApplication.update += Tick;
    }
    public static void RunBatch()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Triad.unity");
        // 等后台启动导入和脚本重载完成，再进入 Play Mode。
        SessionState.SetBool("CanonCheckQueued", true);
    }
    [MenuItem("Triad/Validate Canon (Play Mode)")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (SceneManager.GetActiveScene().isDirty || SceneManager.GetActiveScene().name != "Triad")
        { File.WriteAllText("Tools/canon-check.txt", "BLOCKED: open saved Triad scene first."); return; }
        SessionState.SetBool("CanonCheck", true);
        SessionState.SetInt("CanonCheckBackground", (int)InputSystem.settings.backgroundBehavior);
        SessionState.SetInt("CanonCheckUpdate", (int)InputSystem.settings.updateMode);
        SessionState.SetInt("CanonCheckEditorInput", (int)InputSystem.settings.editorInputBehaviorInPlayMode);
        SessionState.SetBool("CanonCheckRunBackground", Application.runInBackground);
        started = EditorApplication.timeSinceStartup;
        phase = 0;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        EditorApplication.isPlaying = true;
    }
    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Tick()
    {
        if (Application.isBatchMode && SessionState.GetBool("CanonCheckQueued", false)
            && EditorApplication.timeSinceStartup > 25 && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
        { SessionState.SetBool("CanonCheckQueued", false); Run(); }
        if (!SessionState.GetBool("CanonCheck", false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            Application.runInBackground = true;
            EditorApplication.QueuePlayerLoopUpdate();
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.Update();
            if (EditorApplication.timeSinceStartup - started > 70) throw new Exception("Play check timed out at phase " + phase);
            if (rhythm == null) { rhythm = UnityEngine.Object.FindAnyObjectByType<CanonRhythm>(); if (rhythm == null || rhythm.LoadedChart == null) return; game = rhythm.GetComponent<GameManager>(); }
            if (phase == 0)
            {
                if (Keyboard.current == null) testKeyboard = InputSystem.AddDevice<Keyboard>();
                Require(game.Players.Count == 3, "Expected three keyboard players, got " + game.Players.Count);
                Require(rhythm.LoadedChart.notes.Length == 13, "Incorrect easy chart count");
                before = game.Players.ConvertAll(p => p.transform.position).ToArray();
                InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.W, Key.UpArrow, Key.Numpad5));
                phaseAt = EditorApplication.timeSinceStartup; phase = 1;
            }
            else if (phase == 1 && EditorApplication.timeSinceStartup - phaseAt > .2)
            {
                for (int i = 0; i < 3; i++) Require(game.Players[i].transform.position.y > before[i].y,
                    "Keyboard movement failed P" + i + " frame " + Time.frameCount + " dt " + Time.deltaTime + " key " + Keyboard.current.wKey.isPressed);
                InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
                rhythm.Restart(); phase = 2; index = 0;
            }
            else if (phase == 2)
            {
                if (!checkedPause && rhythm.SongTime > 10)
                {
                    pausedAt = rhythm.SongTime;
                    pausedAudio = rhythm.GetComponent<AudioSource>().time;
                    rhythm.SendMessage("SetPaused", true);
                    phaseAt = EditorApplication.timeSinceStartup;
                    phase = 4;
                    return;
                }
                if (rhythm.SongTime > 1 && rhythm.SongTime < 29.5f)
                    Require(Mathf.Abs(rhythm.GetComponent<AudioSource>().time - rhythm.SongTime) < .15f, "Audio and chart clocks diverged");
                if (index < rhythm.LoadedChart.notes.Length)
                {
                    var note = rhythm.LoadedChart.notes[index];
                    int[] tones = Chord.Tones(note.root, note.quality);
                    for (int p = 0; p < 3; p++) game.Players[p].transform.position = GameManager.Polar(3.4f, tones[p]);
                    if (rhythm.SongTime >= note.time && rhythm.SongTime <= note.time + .15f)
                    {
                        // 首个故意不按，第二个三人重复同音，其他和弦模拟真实三键同时按下。
                        if (!note.auto && index == 1) for (int p = 0; p < 3; p++) rhythm.Strike(p, tones[0], rhythm.SongTime);
                        else if (!note.auto && index > 1) InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.Space, Key.Enter, Key.Numpad0));
                    }
                    if (rhythm.SongTime > note.time + .18f)
                    {
                        InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
                        index++;
                    }
                }
                if (!captured && rhythm.SongTime > 14) { CapturePreview(); captured = true; }
                if (rhythm.SongTime >= 30.15f)
                {
                    Require(!rhythm.Playing, "Music did not finish");
                    Require(rhythm.Completed == rhythm.LoadedChart.notes.Length, "Unjudged targets: " + rhythm.Completed);
                    Require(rhythm.Misses == 2, "Expected exactly 2 deliberate misses, got " + rhythm.Misses);
                    rhythm.Restart();
                    Require(rhythm.Completed == 0 && rhythm.Misses == 0 && rhythm.SongTime < -3.5f, "Restart did not reset");
                    rhythm.SendMessage("SetPaused", true);
                    phaseAt = EditorApplication.timeSinceStartup;
                    phase = 3;
                }
            }
            else if (phase == 3 && EditorApplication.timeSinceStartup - phaseAt > .2)
            {
                Require(rhythm.SongTime < -3.5f, "Paused clock advanced");
                rhythm.SendMessage("SetPaused", false);
                Require(rhythm.SongTime < -3.5f, "Resume shifted clock");
                File.WriteAllText("Tools/canon-check.txt", "PASSED: three simultaneous keyboard movement inputs; Space/Enter/Numpad0 hits; 13 targets; 4 automatic targets without presses; missing press and duplicate pitch rejected; audio/clock agreement within 150ms throughout playback; 30s finish; restart; countdown and mid-song pause/resume.");
                Stop();
            }
            else if (phase == 4 && EditorApplication.timeSinceStartup - phaseAt > .3)
            {
                Require(Mathf.Abs(rhythm.SongTime - pausedAt) < .02f, "Music pause advanced chart");
                Require(Mathf.Abs(rhythm.GetComponent<AudioSource>().time - pausedAudio) < .05f, "Paused audio advanced");
                rhythm.SendMessage("SetPaused", false);
                checkedPause = true;
                phase = 2;
            }
        }
        catch (Exception e) { File.WriteAllText("Tools/canon-check.txt", "FAILED: " + e); Debug.LogException(e); Stop(); }
    }
    static void CapturePreview()
    {
        var camera = Camera.main;
        var canvas = UnityEngine.Object.FindAnyObjectByType<Canvas>();
        var mode = canvas.renderMode;
        var oldCamera = canvas.worldCamera;
        var oldDistance = canvas.planeDistance;
        var previousTarget = camera.targetTexture;
        var previousActive = RenderTexture.active;
        var target = new RenderTexture(1280, 720, 24);
        var picture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 5;
            camera.targetTexture = target;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = target;
            picture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            picture.Apply();
            File.WriteAllBytes("Tools/canon-play.png", picture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            canvas.renderMode = mode;
            canvas.worldCamera = oldCamera;
            canvas.planeDistance = oldDistance;
            UnityEngine.Object.DestroyImmediate(picture);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
    static void Stop()
    {
        InputSystem.settings.backgroundBehavior = (InputSettings.BackgroundBehavior)SessionState.GetInt("CanonCheckBackground", 0);
        InputSystem.settings.updateMode = (InputSettings.UpdateMode)SessionState.GetInt("CanonCheckUpdate", 1);
        InputSystem.settings.editorInputBehaviorInPlayMode = (InputSettings.EditorInputBehaviorInPlayMode)SessionState.GetInt("CanonCheckEditorInput", 0);
        Application.runInBackground = SessionState.GetBool("CanonCheckRunBackground", false);
        if (testKeyboard != null) { InputSystem.RemoveDevice(testKeyboard); testKeyboard = null; }
        SessionState.SetBool("CanonCheck", false);
        EditorApplication.isPlaying = false;
        phase = 0; rhythm = null; captured = false; checkedPause = false;
        if (Application.isBatchMode) EditorApplication.Exit(File.ReadAllText("Tools/canon-check.txt").StartsWith("PASSED") ? 0 : 1);
    }
}
