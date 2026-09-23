using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// 卡农试玩：谱面使用音频中的实际秒数，不使用逐帧累加的节拍。
public class CanonRhythm : MonoBehaviour
{
    [Serializable] public class Note
    {
        public float time;
        public Chord.Note root;
        public Chord.Quality quality;
        public bool auto;
    }
    [Serializable] public class Chart { public float duration = 30; public Note[] notes; }
    class Target
    {
        public Note note;
        public GameObject visual;
        public readonly bool[] pressed = new bool[3];
        public readonly int[] pitches = new int[3];
        public readonly float[] errors = new float[3];
    }

    public float timingOffsetMs; // 正数让判定晚于原始音频，供设备延迟校准。
    public const float GoodWindow = 0.30f;
    public const float PerfectWindow = 0.15f;
    const float Approach = 3.0f;
    readonly List<Target> targets = new List<Target>();
    GameManager game;
    AudioSource music;
    Chart chart;
    Material arcMaterial;
    double startDsp;
    int spawnIndex, score, combo, misses, completed;
    bool playing, paused;
    double pauseDsp;
    float feedbackUntil;
    public float SongTime => (float)((paused ? pauseDsp : AudioSettings.dspTime) - startDsp) - timingOffsetMs / 1000f;
    public bool Playing => playing;
    public int Completed => completed;
    public int Misses => misses;
    public Chart LoadedChart => chart;

    void Start()
    {
        game = GetComponent<GameManager>();
        var source = Resources.Load<AudioClip>("Canon30");
        var data = Resources.Load<TextAsset>("CanonChart");
        if (source == null || data == null)
        {
            game.bannerLabel.text = "Missing Canon30 / CanonChart in Resources";
            enabled = false;
            return;
        }
        chart = JsonUtility.FromJson<Chart>(data.text);
        music = gameObject.AddComponent<AudioSource>();
        music.clip = source;
        music.playOnAwake = false;
        music.spatialBlend = 0;
        music.volume = 0.75f;
        // 独立音源，避免 Synth 的音频过滤器覆盖背景钢琴曲。
        arcMaterial = new Material(Shader.Find("Sprites/Default"));
        var manager = GetComponent<PlayerInputManager>();
        manager.DisableJoining();
        manager.enabled = false;
        var holder = new GameObject("Keyboard Players");
        holder.SetActive(false);
        for (int i = 0; i < 3; i++)
        {
            var obj = Instantiate(manager.playerPrefab, holder.transform);
            obj.GetComponent<PlayerInput>().enabled = false;
            var p = obj.GetComponent<PlayerVoice>();
            p.game = game;
            p.synth = game.synth;
            game.Players.Add(p);
        }
        holder.SetActive(true);
        for (int i = 0; i < 3; i++)
        {
            var p = game.Players[i];
            p.voice = i;
            p.tagLabel.text = "P" + (i + 1);
            var pad = p.GetComponentInChildren<SpriteRenderer>();
            if (pad != null) pad.color = new[] { new Color(0.4f,0.85f,1), new Color(1,0.65f,0.4f), new Color(0.75f,0.6f,1) }[i];
        }
        ResetPositions();
        game.chordLabel.fontSize = 6;
        game.chordLabel.transform.localPosition = new Vector3(0, 0.55f, 0);
        game.chordNotes.fontSize = 3;
        game.chordNotes.transform.localPosition = new Vector3(0, -0.60f, 0);
        game.triangle.GetComponent<LineRenderer>().sortingOrder = 4;
        game.titleLabel.rectTransform.sizeDelta = new Vector2(1180, 64);
        game.titleLabel.fontSize = 16;
        game.scoreLabel.rectTransform.anchoredPosition = new Vector2(-24, -90);
        game.speedLabel.rectTransform.sizeDelta = new Vector2(650, 22);
        game.speedLabel.fontSize = 13;
        game.bannerLabel.text = "F1 / controller Start: play   |   Gold: press   |   Grey: stand in the notes";
        UpdateHud();
    }

    void ResetPositions()
    {
        int[] tones = Chord.Tones(chart.notes[0].root, chart.notes[0].quality);
        for (int i = 0; i < 3; i++)
        {
            game.Players[i].transform.position = GameManager.Polar(3.4f, tones[i]);
            game.Players[i].wedge = tones[i];
        }
    }

    public void Restart()
    {
        if (music == null) return;
        music.Stop();
        foreach (var t in targets) Destroy(t.visual);
        targets.Clear();
        spawnIndex = score = combo = misses = completed = 0;
        paused = false;
        playing = true;
        ResetPositions();
        // 预留四秒准备，音频和所有目标使用同一个起始时刻。
        startDsp = AudioSettings.dspTime + 4;
        music.PlayScheduled(startDsp);
        feedbackUntil = 0;
    }

    void Update()
    {
        if (chart == null) return;
        var k = Keyboard.current;
        bool restart = k != null && (k.f1Key.wasPressedThisFrame || k.rKey.wasPressedThisFrame);
        foreach (var pad in Gamepad.all) restart |= pad.startButton.wasPressedThisFrame;
        if (restart) Restart();
        if (k != null && k.escapeKey.wasPressedThisFrame && playing) SetPaused(!paused);
        if (paused) return;
        // 三名玩家共享键盘；手柄按连接顺序控制 P1、P2、P3。
        for (int i = 0; i < 3; i++) ReadPlayer(i);
        if (playing)
        {
            float now = SongTime;
            while (spawnIndex < chart.notes.Length && chart.notes[spawnIndex].time - Approach <= now)
                Spawn(chart.notes[spawnIndex++]);
            for (int i = targets.Count - 1; i >= 0; i--)
            {
                var t = targets[i];
                float remaining = Mathf.Clamp01((t.note.time - now) / Approach);
                float radius = Mathf.Lerp(1.75f, 8, remaining);
                t.visual.transform.localScale = Vector3.one * radius;
                if (t.note.auto && now >= t.note.time)
                    Finish(t, InPosition(t.note), "AUTO");
                else if (!t.note.auto && now >= t.note.time - GoodWindow)
                {
                    if (AllPressed(t))
                    {
                        bool perfect = true;
                        for (int p = 0; p < 3; p++) perfect &= Mathf.Abs(t.errors[p]) <= PerfectWindow;
                        Finish(t, true, perfect ? "PERFECT" : "GOOD");
                    }
                    else if (now > t.note.time + GoodWindow) Finish(t, false, "MISS");
                }
            }
            if (now < 0) game.bannerLabel.text = "Ready  " + Mathf.CeilToInt(-now);
            else if (Time.unscaledTime > feedbackUntil) game.bannerLabel.text = "";
            if (now >= chart.duration)
            {
                playing = false;
                music.Stop();
                game.bannerLabel.text = "FINISHED   " + score + " points   /   " + misses + " misses   |   R: retry";
            }
        }
        UpdateHud();
    }

    void ReadPlayer(int i)
    {
        var k = Keyboard.current;
        Vector2 move = Vector2.zero;
        bool strike = false;
        if (k != null)
        {
            Key up = i == 0 ? Key.W : i == 1 ? Key.UpArrow : Key.Numpad5;
            Key left = i == 0 ? Key.A : i == 1 ? Key.LeftArrow : Key.Numpad1;
            Key down = i == 0 ? Key.S : i == 1 ? Key.DownArrow : Key.Numpad2;
            Key right = i == 0 ? Key.D : i == 1 ? Key.RightArrow : Key.Numpad3;
            Key hit = i == 0 ? Key.Space : i == 1 ? Key.Enter : Key.Numpad0;
            move = new Vector2((k[right].isPressed ? 1 : 0) - (k[left].isPressed ? 1 : 0),
                (k[up].isPressed ? 1 : 0) - (k[down].isPressed ? 1 : 0));
            strike = k[hit].wasPressedThisFrame;
        }
        if (i < Gamepad.all.Count)
        {
            move += Gamepad.all[i].leftStick.ReadValue() + Gamepad.all[i].dpad.ReadValue();
            strike |= Gamepad.all[i].buttonSouth.wasPressedThisFrame;
        }
        var p = game.Players[i];
        Vector2 pos = p.transform.position;
        pos += Vector2.ClampMagnitude(move, 1) * p.moveSpeed * Time.deltaTime;
        if (pos.sqrMagnitude > 0.001f) pos = pos.normalized * Mathf.Clamp(pos.magnitude, p.innerRadius, p.outerRadius);
        p.transform.position = pos;
        p.wedge = GameManager.WedgeAt(pos);
        p.noteLabel.text = Chord.Names[p.wedge];
        if (strike && playing) Strike(i, p.wedge, SongTime);
    }

    // 一次按键只记录到最近的一个金色目标；错误音不能覆盖已经命中的按键。
    public void Strike(int player, int pitch, float at)
    {
        Target closest = null;
        float distance = GoodWindow;
        foreach (var t in targets)
        {
            float d = Mathf.Abs(t.note.time - at);
            if (!t.note.auto && !t.pressed[player] && d <= distance) { closest = t; distance = d; }
        }
        if (closest == null) return;
        int[] tones = Chord.Tones(closest.note.root, closest.note.quality);
        if (Array.IndexOf(tones, pitch) < 0) return;
        closest.pressed[player] = true;
        closest.pitches[player] = pitch;
        closest.errors[player] = at - closest.note.time;
        game.synth.Strike(player, 60 + pitch, 0.32f);
    }

    bool AllPressed(Target t)
    {
        for (int i = 0; i < 3; i++) if (!t.pressed[i]) return false;
        return new HashSet<int>(t.pitches).SetEquals(Chord.Tones(t.note.root, t.note.quality));
    }

    public bool InPosition(Note n)
    {
        var occupied = new HashSet<int>();
        foreach (var p in game.Players)
        {
            float radius = p.transform.position.magnitude;
            if (radius < p.innerRadius - 0.01f || radius > p.outerRadius + 0.01f) return false;
            occupied.Add(GameManager.WedgeAt(p.transform.position));
        }
        return occupied.SetEquals(Chord.Tones(n.root, n.quality));
    }

    void Spawn(Note n)
    {
        var root = new GameObject(n.auto ? "Grey position arcs" : "Gold strike ring");
        int[] tones = n.auto ? Chord.Tones(n.root, n.quality) : new[] { 0 };
        foreach (int pitch in tones)
        {
            var line = new GameObject("Arc").AddComponent<LineRenderer>();
            line.transform.SetParent(root.transform, false);
            line.sharedMaterial = arcMaterial;
            line.useWorldSpace = false;
            line.loop = !n.auto;
            line.positionCount = n.auto ? 17 : 97;
            line.startWidth = line.endWidth = n.auto ? 0.20f : 0.055f;
            line.startColor = line.endColor = n.auto ? new Color(0.72f,0.75f,0.80f) : game.accent;
            line.sortingOrder = 18;
            for (int j = 0; j < line.positionCount; j++)
            {
                float angle = n.auto ? 90 - pitch * 30 - 14 + j * 28f / 16 : j * 360f / 96;
                line.SetPosition(j, new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad), 0));
            }
        }
        targets.Add(new Target { note = n, visual = root });
    }

    void Finish(Target t, bool success, string kind)
    {
        completed++;
        if (success) { combo++; score += t.note.auto ? 50 : kind == "PERFECT" ? 150 : 100; }
        else { combo = 0; misses++; }
        game.bannerLabel.text = success ? kind + "   combo " + combo : "MISS  " + Chord.Label(t.note.root, t.note.quality);
        feedbackUntil = Time.unscaledTime + 0.65f;
        Destroy(t.visual);
        targets.Remove(t);
    }

    void UpdateHud()
    {
        float now = playing ? Mathf.Clamp(SongTime, 0, chart.duration) : completed > 0 ? chart.duration : 0;
        game.titleLabel.text = "CANON IN D  |  30 SECOND PIANO\nP1 WASD + Space     P2 Arrows + Enter     P3 Num 5/1/2/3 + Num 0";
        game.scoreLabel.text = "TEAM " + score + "\nCombo " + combo + "\nMiss " + misses;
        game.speedLabel.text = now.ToString("0.0") + " / 30 s    F1 / R: restart   Esc: pause   Gamepads: stick + Cross/A";
        game.ring.ClearTints();
        Note next = targets.Count > 0 ? targets[0].note : spawnIndex < chart.notes.Length ? chart.notes[spawnIndex] : null;
        if (next == null) { game.chordLabel.text = "DONE"; game.chordNotes.text = ""; return; }
        var tones = Chord.Tones(next.root, next.quality);
        Color c = next.auto ? new Color(0.72f,0.75f,0.8f,0.7f) : new Color(game.accent.r,game.accent.g,game.accent.b,0.65f);
        foreach (int tone in tones) game.ring.Tint(tone, c);
        foreach (var p in game.Players) game.ring.Tint(p.wedge, new Color(1,1,1,0.22f));
        game.triangle.inPosition = InPosition(next);
        game.chordLabel.text = Chord.Label(next.root, next.quality);
        game.chordLabel.color = c;
        game.chordNotes.text = string.Join(" + ", Array.ConvertAll(tones, t => Chord.Names[t])) + (next.auto ? "\nSTAND" : "\nPRESS");
        foreach (var n in chart.notes)
            if (!n.auto && n.time > next.time)
            { game.chordNotes.text += "\nNext: " + Chord.Label(n.root, n.quality); break; }
    }

    void SetPaused(bool value)
    {
        if (value == paused) return;
        if (value) { pauseDsp = AudioSettings.dspTime; paused = true; music.Pause(); game.bannerLabel.text = "PAUSED   Esc: resume"; }
        else
        {
            double elapsed = pauseDsp - startDsp;
            startDsp += AudioSettings.dspTime - pauseDsp;
            paused = false;
            if (elapsed < 0) { music.Stop(); music.PlayScheduled(startDsp); }
            else music.UnPause();
            game.bannerLabel.text = "";
        }
    }
    void OnApplicationFocus(bool focus) { if (!Application.isBatchMode && !focus && playing && !paused) SetPaused(true); }
    void OnDestroy() { if (music != null) music.Stop(); if (arcMaterial != null) Destroy(arcMaterial); }
}
