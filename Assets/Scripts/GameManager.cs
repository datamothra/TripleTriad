using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.InputSystem;
public class GameManager : MonoBehaviour
{
    public Song[] songs;
    public Triad.Synth synth;
    public Ring ring;
    public Transform ringTemplate;
    public SpriteRenderer coreLine;
    public ChordTriangle triangle;
    public TMP_Text chordLabel, chordNotes, titleLabel, scoreLabel, bannerLabel, speedLabel;
    public Color accent = new Color(0.875f, 0.686f, 0.196f);
    public Color whiteNote = Color.white;    // a white note's arc and wedge (Square / X)

    [Header("Tempo")]
    [Range(0.4f, 2.5f)] public float speed = 1f;   // multiplies the song's bpm; a recording plays that much faster with its key unchanged (0.5 to 2). Works while playing

    [Header("Background recording")]
    public AudioMixer musicMixer;         // Music.mixer: its Pitch Shifter puts the key back when speed isn't 1
    [Range(0f, 1f)] public float musicVolume = 0.75f;
    public float pitchShiftLatencyMs = 30f;   // the Pitch Shifter delays the recording about this much (measured 26 to 43 ms), so it starts that early

    [Header("Timing window (in beats)")]
    public float earlyBeats = 0.5f;       // a strike this many beats before the downbeat still counts
    public float lateBeats = 0.25f;       // the chord is missed once this many beats have passed without it

    [Header("Ring turning")]
    public int shiftEvery = 2;            // the ring turns after this many chords, 0 = never
    public int shiftMinWedges = 1, shiftMaxWedges = 5;   // by a random number of wedges in this range
    public bool shiftEitherWay = true;    // off = always clockwise

    [Header("Scoring")]
    public int startLives = 5;
    public int chordPoints = 100;         // for every chord hit
    public int notePoints = 50;           // for every white note hit (one player)
    public int comboBonus = 20;           // extra per combo step
    public int comboCap = 10;             // combo steps that still add bonus

    [Header("Approach circle")]
    public float spawnScale = 24f, landScale = 3.5f;   // size when it appears, size on the red line
    public float approachChords = 1f;     // it appears this many of its own chord lengths before landing
    public float minApproachBeats = 3f;   // but never closes faster than this, so a short chord still gets fair warning

    [Header("Wedge highlights (alpha)")]
    public float rootTint = 0.70f;
    public float toneTint = 0.45f;        // third and fifth
    public float playerTint = 0.25f;      // the wedge a player stands in

    [Header("Beat pulse")]
    public float pulseDecay = 5f;         // how fast the pulse fades after each beat
    public float pulseGrow = 0.22f;       // how much the red line swells on the beat
    public float pulseDimAlpha = 0.7f;    // the red line's alpha between beats, 1 on the beat

    // a miss: the whole ring and the banner flash this colour, the camera shakes, the synth thuds
    [Header("Miss")]
    public Color missColour = new Color(0.9f, 0.1f, 0.15f);
    public float missSeconds = 0.6f;
    public float missTint = 0.85f;        // how strongly the ring turns missColour
    public float missShake = 0.35f;
    public Transform shakeCamera;         // drag Main Camera here, empty = no shake
    public int missThudMidi = 43;         // lowest of the three thud notes, a semitone apart
    public float missThudVelocity = 1f;

    [Header("Banner")]
    public float hitMessageSeconds = 3f, missMessageSeconds = 3f, songMessageSeconds = 2f;

    class Marker { public int index; public float land, approach; public GameObject go; }
    readonly List<Marker> markers = new List<Marker>();
    readonly List<PlayerVoice> players = new List<PlayerVoice>();
    public List<PlayerVoice> Players => players;
    Song song; int songIndex;
    int nextIndex, score, lives, combo, lastPlayerCount, lastBeatInt, chordsDone;
    float nextLand, beatPulse, bannerUntil, missFlash;
    Vector3 cameraHome;
    bool gameOver;
    Color bannerColour;                   // the banner's colour as set in the Inspector, a miss turns it red for a moment
    Color coreColour;                     // the red line's colour as set in the Inspector; only its alpha pulses
    Material arcMaterial;                 // shared by every white arc

    // the beat clock runs on the audio hardware's clock, so it can't drift from the recording:
    // it was at anchorBeat at dsp time anchorDsp, and moves Bpm / 60 beats a second from there
    AudioSource music;
    AudioMixerGroup musicGroup;           // only used when speed isn't 1: straight to the speakers is cleaner and has no delay
    double anchorDsp;
    float anchorBeat;
    float rate = 1f;                      // speed as actually used; a recording only goes 0.5x to 2x
    const string PitchParam = "MusicPitchShift";   // exposed on Music.mixer

    public static Vector3 Polar(float r, int pitchClass)
    {
        float a = (90f - pitchClass * 30f) * Mathf.Deg2Rad;      // C at the top, clockwise
        return new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
    }

    // inverse of Polar: which of the twelve wedges a world position is in
    public static int WedgeAt(Vector2 p)
    {
        float a = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;
        return (Mathf.RoundToInt((90f - a) / 30f) % 12 + 12) % 12;
    }

    public float SongBeat => anchorBeat + (float)System.Math.Max(0.0, AudioSettings.dspTime - anchorDsp) * Bpm / 60f;   // holds still until anchorDsp
    public bool GameOver => gameOver;
    public int Lives => lives;
    public int Score => score;
    public Song CurrentSong => song;
    public AudioSource Music => music;
    bool Shifting => musicGroup != null && !Mathf.Approximately(rate, 1f);
    public float MusicLead => Shifting ? pitchShiftLatencyMs / 1000f * rate : 0f;   // recording seconds the source runs ahead of the clock
    float Bpm => song.bpm * rate;
    Song.Entry Entry(int i) => song.entries[i % song.entries.Count];
    Marker Current => markers.Count > 0 ? markers[0] : null;          // the chord about to land
    float CountInBeats => Entry(0).beats;
    // the beat where the recording runs out; no recording = the chart loops forever
    float EndBeat => song.backing != null ? (song.backing.length - song.backingOffset) * song.bpm / 60f : float.MaxValue;
    Color ChordColour(Song.Entry e) => e.white ? whiteNote : accent;
    string Name(Song.Entry e) => e.white ? Chord.Names[(int)e.root] : Chord.Label(e.root, e.quality);
    float Approach(Song.Entry e) => Mathf.Max(e.beats * approachChords, minApproachBeats);   // beats a ring takes to close

    // the chord about to land and its beat, for tests and tools
    public bool TryGetCurrentChord(out Song.Entry entry, out float land)
    {
        var m = Current;
        entry = m != null ? Entry(m.index) : null;
        land = m != null ? m.land : 0f;
        return m != null;
    }

    void Awake()
    {
        song = songs[0];
        // the recording gets its own object: an audio filter (like the synth's) would process every source on its object
        music = new GameObject("Music").AddComponent<AudioSource>();
        music.transform.SetParent(transform, false);
        music.playOnAwake = false;
        music.spatialBlend = 0f;
        var groups = musicMixer != null ? musicMixer.FindMatchingGroups("Master") : null;
        if (groups != null && groups.Length > 0) musicGroup = groups[0];
    }

    void Start()
    {
        coreColour = coreLine.color;
        bannerColour = bannerLabel.color;
        if (shakeCamera != null) cameraHome = shakeCamera.position;
        Restart();
    }

    // the PlayerInputManager on this same object sends these by name (Send Messages), like the players' actions
    void OnPlayerJoined(PlayerInput input)
    {
        var voice = input.GetComponent<PlayerVoice>();
        voice.game = this;
        voice.synth = synth;
        players.Add(voice);
    }

    void OnPlayerLeft(PlayerInput input) { players.Remove(input.GetComponent<PlayerVoice>()); }

    void Update()
    {
        float dt = Time.deltaTime; //delta time shorthand
        UpdateRate();
        music.volume = musicVolume;

        bool running = !gameOver && players.Count == 3;
        if (!gameOver && players.Count < 3)
        {
            ClearMarkers();
            music.Stop();
            Say("waiting for " + (3 - players.Count) + " more controller" + (players.Count == 2 ? "" : "s") + ": press Cross to join", 1f);
        }
        if (running && lastPlayerCount < 3) StartClock();
        float songBeat = running ? SongBeat : 0f;
        if (running)
        {
            int beat = Mathf.FloorToInt(songBeat);
            if (beat != lastBeatInt) { lastBeatInt = beat; beatPulse = 1f; }

            while (nextLand <= EndBeat && songBeat >= nextLand - Approach(Entry(nextIndex))) Spawn();     // a ring appears Approach beats before its bar line
            foreach (var m in markers)
                m.go.transform.localScale = Vector3.one * Mathf.LerpUnclamped(landScale, spawnScale, (m.land - songBeat) / m.approach);
            if (Current != null && songBeat >= Current.land) Judge(songBeat);
            if (!gameOver && markers.Count == 0 && nextLand > EndBeat)    // every chord the recording has room for is done
            {
                gameOver = true;
                Say("FINISHED   team " + score + "   Options restarts", float.MaxValue);
            }
        }
        beatPulse = Mathf.Max(0f, beatPulse - dt * pulseDecay);
        lastPlayerCount = players.Count;

        // wedges: the chord's notes light up in its colour (root strongest), players lighten the one they stand in
        ring.ClearTints();
        var cur = Current;
        var e = cur != null ? Entry(cur.index) : null;
        int[] tones = e == null ? null : e.white ? new[] { (int)e.root } : Chord.Tones(e.root, e.quality);
        if (tones != null)
        {
            var c = ChordColour(e);
            for (int i = 0; i < tones.Length; i++) ring.Tint(tones[i], new Color(c.r, c.g, c.b, i == 0 ? rootTint : toneTint));
        }
        foreach (var p in players) ring.Tint(p.wedge, new Color(1, 1, 1, playerTint));

        // a miss washes over everything and fades out
        missFlash = Mathf.Max(0f, missFlash - dt / missSeconds);
        if (missFlash > 0f)
            for (int w = 0; w < 12; w++) ring.Tint(w, new Color(missColour.r, missColour.g, missColour.b, missTint * missFlash));
        bannerLabel.color = Color.Lerp(bannerColour, missColour, missFlash);
        if (shakeCamera != null) shakeCamera.position = cameraHome + (Vector3)(Random.insideUnitCircle * missShake * missFlash);
        triangle.inPosition = tones != null && !e.white && players.Count == 3 && new HashSet<int>(players.Select(p => p.wedge)).SetEquals(tones);

        // core: the count-in, then the chord that is coming
        if (running && songBeat < -CountInBeats)
        {
            chordLabel.text = (Mathf.FloorToInt(songBeat + 2f * CountInBeats) + 1).ToString();
            chordNotes.text = "";
            chordLabel.color = accent;
        }
        else if (tones != null)
        {
            chordLabel.text = Name(e);
            chordNotes.text = e.white ? "one player: Square / X" : Chord.Names[tones[0]] + " + " + Chord.Names[tones[1]] + " + " + Chord.Names[tones[2]];
            chordLabel.color = ChordColour(e);
        }
        else { chordLabel.text = ""; chordNotes.text = ""; }

        // the red line is the metronome
        coreLine.transform.localScale = Vector3.one * (landScale + pulseGrow * beatPulse);
        coreLine.color = new Color(coreColour.r, coreColour.g, coreColour.b, coreColour.a * Mathf.Lerp(pulseDimAlpha, 1f, beatPulse));

        int shown = cur != null ? cur.index : nextIndex;
        titleLabel.text = song.name + "   " + song.meter + "   " + Bpm.ToString("0") + " bpm   chord " + (shown % song.entries.Count + 1) + " of " + song.entries.Count + "     L1 / R1 change song   Options restart";
        scoreLabel.text = "TEAM " + score + "\nlives " + lives + (combo > 1 ? "\ncombo x" + combo : "") + (players.Count < 3 ? "\n" + players.Count + "/3 joined" : "");
        speedLabel.text = "SPEED " + rate.ToString("0.0#") + "x";
        if (Time.time > bannerUntil) bannerLabel.text = "";
    }

    void Spawn()
    {
        var e = Entry(nextIndex);
        GameObject go;
        if (e.white) go = WhiteArc((int)e.root);
        else
        {
            go = Instantiate(ringTemplate.gameObject, ringTemplate.parent);
            go.name = "Ring " + Name(e);
            go.SetActive(true);
            go.GetComponent<SpriteRenderer>().color = accent;
        }
        markers.Add(new Marker { index = nextIndex, land = nextLand, approach = Approach(e), go = go });
        nextLand += e.beats;                                       // the next bar line
        nextIndex++;
    }

    const float ArcDegrees = 28f;   // a wedge is 30; the gap keeps neighbouring arcs apart
    const float ArcWidth = 0.2f;

    // an arc over one wedge, the same size as the approach circle; it lives on the ring so it turns with it
    GameObject WhiteArc(int note)
    {
        if (arcMaterial == null) arcMaterial = new Material(Shader.Find("Sprites/Default"));
        var line = new GameObject("Arc " + Chord.Names[note] + " (white)").AddComponent<LineRenderer>();
        line.transform.SetParent(ring.transform, false);
        line.useWorldSpace = false;
        line.sharedMaterial = arcMaterial;
        line.startColor = line.endColor = whiteNote;
        line.startWidth = line.endWidth = ArcWidth;
        line.sortingOrder = 18;
        line.positionCount = 17;
        float mid = 90f - note * 30f;                            // C at the top, clockwise, like Polar
        for (int j = 0; j < 17; j++)
        {
            float a = (mid - ArcDegrees / 2f + j * ArcDegrees / 16f) * Mathf.Deg2Rad;
            line.SetPosition(j, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.5f);   // radius 0.5, so its scale is a diameter like the circle's
        }
        return line.gameObject;
    }

    // strikes are stamped in beats: a press from earlyBeats before the downbeat counts, and the chord is missed lateBeats after it
    void Judge(float songBeat)
    {
        var m = Current;
        var e = Entry(m.index);
        if (e.white)                                             // a white note: anyone standing on it presses Square / X
        {
            if (players.Any(p => p.lastWestBeat >= m.land - earlyBeats && p.lastWestMidi % 12 == (int)e.root)) Finish(m, true, null);
            else if (songBeat > m.land + lateBeats) Finish(m, false, "nobody pressed Square on it");
            return;
        }
        var struck = players.Where(p => p.lastStrikeBeat >= m.land - earlyBeats).ToList();   // a gold chord: all three, on its three notes
        bool right = struck.Count == 3 && new HashSet<int>(struck.Select(p => p.lastStrikeMidi % 12)).SetEquals(Chord.Tones(e.root, e.quality));
        if (right) Finish(m, true, null);
        else if (songBeat > m.land + lateBeats)
            Finish(m, false, struck.Count == 0 ? "nobody struck" : struck.Count < 3 ? (3 - struck.Count) + " didn't strike in time" : "wrong notes");
    }

    void Finish(Marker m, bool hit, string why)
    {
        var e = Entry(m.index);
        Destroy(m.go);                                           // first, so nothing below can leave it to be judged again
        markers.Remove(m);
        if (hit)
        {
            combo++;
            int pts = (e.white ? notePoints : chordPoints) + comboBonus * Mathf.Min(combo - 1, comboCap);
            score += pts;
            Say(Name(e) + "  +" + pts + (combo > 1 ? "   combo x" + combo : ""), hitMessageSeconds);
        }
        else
        {
            combo = 0; lives--;
            missFlash = 1f;
            for (int v = 0; v < 3; v++) synth.Strike(v, missThudMidi + v, missThudVelocity);      // three low notes a semitone apart, an ugly thud
            Say("missed " + Name(e) + ": " + why, missMessageSeconds);
        }
        if (shiftEvery > 0 && ++chordsDone % shiftEvery == 0) ring.Shift(Random.Range(shiftMinWedges, shiftMaxWedges + 1) * (shiftEitherWay && Random.value < 0.5f ? -1 : 1));
        if (lives <= 0)
        {
            gameOver = true;
            ClearMarkers();
            music.Stop();
            Say("GAME OVER   team " + score + "   Options restarts", float.MaxValue);
        }
    }

    void Say(string text, float seconds) { bannerLabel.text = text; bannerUntil = Time.time + seconds; }

    void ClearMarkers()
    {
        foreach (var m in markers) Destroy(m.go);
        markers.Clear();
    }

    void StartClock()
    {
        ClearMarkers();
        chordsDone = 0;
        ring.ResetShift();
        nextIndex = 0;
        nextLand = 0f;                       // first chord lands on beat 0
        foreach (var p in players) p.ForgetStrikes();
        anchorBeat = -2f * CountInBeats;     // one counted bar, then the first ring closes over one bar
        anchorDsp = AudioSettings.dspTime + 0.1;   // a moment ahead, so the recording can start on the exact sample
        lastBeatInt = -99;
        ScheduleMusic();
    }

    // start the recording so that backingOffset seconds into it lands on beat 0, wherever the clock is
    void ScheduleMusic()
    {
        music.Stop();
        music.clip = song.backing;
        if (song.backing == null || players.Count < 3) return;
        music.outputAudioMixerGroup = Shifting ? musicGroup : null;
        float at = song.backingOffset + anchorBeat * 60f / song.bpm + MusicLead;   // seconds into the recording at anchorDsp
        if (at >= song.backing.length) return;
        if (at >= 0f) { music.time = at; music.PlayScheduled(anchorDsp); }
        else { music.time = 0f; music.PlayScheduled(anchorDsp - at / rate); }
    }

    // speed can change while playing: re-anchor the clock where it is so nothing jumps, and keep the recording's key
    void UpdateRate()
    {
        float r = song.backing != null ? Mathf.Clamp(speed, 0.5f, 2f) : speed;   // 0.5 to 2 is the Pitch Shifter's range
        if (Mathf.Approximately(r, rate)) return;
        bool wasShifting = Shifting;
        anchorBeat = SongBeat; anchorDsp = AudioSettings.dspTime;   // re-anchor where the clock is now, so it doesn't jump
        rate = r;
        music.pitch = r;                                         // plays faster or slower, and higher or lower
        if (musicMixer != null && !musicMixer.SetFloat(PitchParam, 1f / r))   // so shift the key back by the inverse
            Debug.LogWarning("Triad: Music mixer has no exposed " + PitchParam + ", so the key follows the speed.");
        if (Shifting != wasShifting && music.isPlaying)
        {
            anchorDsp = AudioSettings.dspTime + 0.1;             // switching into or out of the mixer: hold a moment and pick the recording up in step
            ScheduleMusic();
        }
    }

    public void ChangeSong(int step)
    {
        songIndex = ((songIndex + step) % songs.Length + songs.Length) % songs.Length;
        song = songs[songIndex];
        UpdateRate();
        Restart();
        Say(song.name + "   " + song.meter, songMessageSeconds);
    }

    public void Restart()
    {
        score = 0; lives = startLives; combo = 0; gameOver = false;
        bannerLabel.text = "";
        StartClock();
    }

    void OnDestroy() { if (arcMaterial != null) Destroy(arcMaterial); }
}
