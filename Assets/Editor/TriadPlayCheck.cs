using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Triad > Validate (Play Mode): three virtual gamepads join and play the first song with a recording, through the
// players' own actions. Every gold chord gets Cross from all three and every white note Square from one player, except
// the first white note, if the song has any, which gets Cross on purpose and must miss. Checks the recording stays on
// the beat clock, and that the song finishes having lost only that life. Doesn't save the scene.
// The result goes to Logs/triad-check.txt.
[InitializeOnLoad]
public static class TriadPlayCheck
{
    const string Report = "Logs/triad-check.txt";
    static GameManager game;
    static Gamepad[] pads;
    static int phase, whiteHits, phaseFrame;
    static string misses = "", lastBanner = "";
    static double phaseAt;
    static float pressedLand = float.MinValue, worstDrift;
    static bool released = true, wrongButtonDone;
    static Vector3[] before;

    static TriadPlayCheck()
    {
        EditorApplication.update += Tick;
        if (SessionState.GetBool("TriadCheck", false) && !EditorApplication.isPlayingOrWillChangePlaymode) SessionState.SetBool("TriadCheck", false);
    }

    [MenuItem("Triad/Validate (Play Mode)")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        SessionState.SetBool("TriadCheck", true);
        SessionState.SetFloat("TriadCheckStarted", (float)EditorApplication.timeSinceStartup);
        SessionState.SetInt("TriadCheckUpdate", (int)InputSystem.settings.updateMode);
        SessionState.SetInt("TriadCheckBackground", (int)InputSystem.settings.backgroundBehavior);
        SessionState.SetInt("TriadCheckEditorInput", (int)InputSystem.settings.editorInputBehaviorInPlayMode);
        SessionState.SetBool("TriadCheckRunBackground", Application.runInBackground);
        game = null;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        EditorApplication.isPlaying = true;
    }

    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Press(GamepadButton button, int only = -1)
    {
        for (int i = 0; i < pads.Length; i++) if (only < 0 || only == i) InputSystem.QueueStateEvent(pads[i], new GamepadState(button));
        released = false;
    }
    static void ReleaseAll() { foreach (var p in pads) InputSystem.QueueStateEvent(p, new GamepadState()); released = true; }

    static void Tick()
    {
        if (!SessionState.GetBool("TriadCheck", false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            Application.runInBackground = true;
            EditorApplication.QueuePlayerLoopUpdate();
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.Update();
            if (EditorApplication.timeSinceStartup - SessionState.GetFloat("TriadCheckStarted", 0f) > 120) throw new Exception("timed out in phase " + phase);
            if (game == null)
            {
                game = UnityEngine.Object.FindAnyObjectByType<GameManager>();
                if (game == null || game.CurrentSong == null) { game = null; return; }
                phase = 0; whiteHits = 0; worstDrift = 0; pressedLand = float.MinValue; misses = lastBanner = "";
                released = true; wrongButtonDone = false;
            }
            var now = EditorApplication.timeSinceStartup;

            if (phase == 0)          // pick the first song with a recording, then three pads join like controllers pressing Cross
            {
                for (int i = 0; i < game.songs.Length && game.CurrentSong.backing == null; i++) game.ChangeSong(+1);
                Require(game.CurrentSong.backing != null, "No song has a background recording");
                game.speed = 1f;
                pads = new Gamepad[3];
                for (int i = 0; i < 3; i++)
                {
                    pads[i] = InputSystem.AddDevice<Gamepad>("TriadCheckPad" + i);
                    PlayerInputManager.instance.JoinPlayer(-1, -1, null, pads[i]);
                }
                Require(game.Players.Count == 3, "Expected three players to join, got " + game.Players.Count);
                phaseAt = now; phase = 1;
            }
            else if (phase == 1 && now - phaseAt > 0.2)   // Move action: push every stick up
            {
                before = game.Players.ConvertAll(p => p.transform.position).ToArray();
                foreach (var p in pads) InputSystem.QueueStateEvent(p, new GamepadState { leftStick = Vector2.up });
                phaseAt = now; phaseFrame = Time.frameCount; phase = 2;
            }
            else if (phase == 2 && now - phaseAt > 0.25 && Time.frameCount - phaseFrame >= 10)   // real frames, not just time: a background editor runs few
            {
                for (int i = 0; i < 3; i++) Require(game.Players[i].transform.position.y > before[i].y, "Move action didn't move P" + (i + 1));
                ReleaseAll();
                phase = 3;
            }
            else if (phase == 3) Play(now);
        }
        catch (Exception e) { Finish("FAILED: " + e.Message + (misses != "" ? "  Misses:" + misses : "")); Debug.LogException(e); }
    }

    static void Play(double now)
    {
        var song = game.CurrentSong;
        float beat = game.SongBeat;
        if (game.bannerLabel.text != lastBanner)                 // keep every miss for the report
        {
            lastBanner = game.bannerLabel.text;
            if (lastBanner.StartsWith("missed")) misses += " [beat " + beat.ToString("0.0") + ": " + lastBanner + "]";
        }

        // the recording must sit where the beat clock says (skip the moment it starts, it's scheduled a little ahead)
        float expected = song.backingOffset + beat * 60f / song.bpm + game.MusicLead;
        if (game.Music.isPlaying && expected > 0.3f)
        {
            float drift = game.Music.time - expected;
            worstDrift = Mathf.Max(worstDrift, Mathf.Abs(drift));
            Require(Mathf.Abs(drift) < 0.08f, "Recording is " + (drift * 1000).ToString("0") + " ms off the beat clock at beat " + beat.ToString("0.00"));
        }

        if (!released) ReleaseAll();
        else if (game.TryGetCurrentChord(out var e, out float land) && land != pressedLand && beat >= land - 0.45f)
        {
            // stand on the chord's three notes (a white note: one player, taking turns), wherever the ring has turned to,
            // then strike on the downbeat
            int who = whiteHits % 3;
            if (e.white) game.Players[who].transform.position = game.ring.transform.TransformPoint(GameManager.Polar(3.4f, (int)e.root));
            else
            {
                int[] tones = Chord.Tones(e.root, e.quality);
                for (int i = 0; i < 3; i++) game.Players[i].transform.position = game.ring.transform.TransformPoint(GameManager.Polar(3.4f, tones[i]));
            }
            if (beat >= land - 0.03f)                              // lands a frame or so later, right on the beat
            {
                bool wrong = e.white && !wrongButtonDone;          // the first white note gets Cross, and must miss
                if (e.white) Press(wrong ? GamepadButton.South : GamepadButton.West, who);
                else Press(GamepadButton.South);
                if (wrong) wrongButtonDone = true; else if (e.white) whiteHits++;
                pressedLand = land;
            }
        }

        if (game.GameOver)
        {
            Require(game.bannerLabel.text.StartsWith("FINISHED"), "Ended without finishing: " + game.bannerLabel.text);
            int expectLives = game.startLives - (wrongButtonDone ? 1 : 0);   // a song without white chords should lose nothing
            Require(game.Lives == expectLives, "Expected " + expectLives + " lives at the end, got " + game.Lives);
            Finish("PASSED: " + song.name + ": three virtual gamepads joined through PlayerInputManager; Move, Strike (Cross) and StrikeWest (Square) actions; "
                + (wrongButtonDone ? whiteHits + " white notes hit with Square by one player each, Cross on a white note missed" : "no white notes in this song") + "; score " + game.Score + "; recording within "
                + (worstDrift * 1000).ToString("0") + " ms of the beat clock; finished at the end of the recording.");
        }
    }

    static void Finish(string result)
    {
        File.WriteAllText(Report, result);
        Debug.Log("Triad check: " + result);
        if (pads != null) { foreach (var p in pads) if (p != null) InputSystem.RemoveDevice(p); pads = null; }
        InputSystem.settings.backgroundBehavior = (InputSettings.BackgroundBehavior)SessionState.GetInt("TriadCheckBackground", 0);
        InputSystem.settings.updateMode = (InputSettings.UpdateMode)SessionState.GetInt("TriadCheckUpdate", 1);
        InputSystem.settings.editorInputBehaviorInPlayMode = (InputSettings.EditorInputBehaviorInPlayMode)SessionState.GetInt("TriadCheckEditorInput", 0);
        Application.runInBackground = SessionState.GetBool("TriadCheckRunBackground", false);
        SessionState.SetBool("TriadCheck", false);
        game = null;
        EditorApplication.isPlaying = false;
    }
}
