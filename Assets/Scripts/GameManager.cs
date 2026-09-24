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
    public Transform approachRingTemplate;   // copied once per gold chord: the circle that closes in
    public SpriteRenderer targetLine;        // the red line the rings land on
    public ChordTriangle triangle;
    public TMP_Text chordLabel, chordNotesLabel, titleLabel, scoreLabel, bannerLabel, speedLabel;
    public Color goldColor = new Color(0.875f, 0.686f, 0.196f);   // gold chords (Cross / A)
    public Color whiteColor = Color.white;                        // white notes (Square / X)

    [Header("Tempo")]
    [Range(0.4f, 2.5f)] public float speed = 1f;   // multiplies the song's bpm; a recording plays that much faster with its key unchanged (0.5 to 2). Works while playing

    [Header("Music")]
    public AudioMixer musicMixer;         // Music.mixer: its Pitch Shifter puts the key back when speed isn't 1
    [Range(0f, 1f)] public float musicVolume = 0.75f;

    [Header("Timing window (in beats)")]
    public float earlyBeats = 0.5f;       // a strike this many beats before the downbeat still counts
    public float lateBeats = 0.25f;       // the chord is missed once this many beats have passed without it

    [Header("Ring turning")]
    public int turnEveryChords = 2;       // the ring turns after this many chords, 0 = never
    public int turnMinWedges = 1, turnMaxWedges = 5;   // by a random number of wedges in this range
    public bool turnEitherWay = true;     // off = always clockwise

    [Header("Scoring")]
    public int startLives = 5;
    public int chordPoints = 100;         // for every gold chord hit
    public int whiteNotePoints = 50;      // for every white note hit (one player)
    public int comboBonus = 20;           // extra per combo step
    public int comboCap = 10;             // combo steps that still add bonus

    [Header("Approach rings")]
    public float ringStartSize = 24f;     // diameter when a ring appears
    public float minApproachBeats = 3f;   // a ring closes over its chord's length, but never faster than this

    [Header("Wedge highlights (alpha)")]
    public float rootHighlight = 0.70f;
    public float toneHighlight = 0.45f;   // third and fifth
    public float playerHighlight = 0.25f; // the wedge a player stands in

    [Header("Beat pulse")]
    public float beatPulseDecay = 5f;     // how fast the pulse fades after each beat
    public float beatPulseGrow = 0.22f;   // how much the red line swells on the beat
    public float beatPulseDimAlpha = 0.7f;   // the red line's alpha between beats, 1 on the beat

    // a miss: the whole ring and the banner flash this color, the camera shakes, the synth thuds
    [Header("Miss")]
    public Color missColor = new Color(0.9f, 0.1f, 0.15f);
    public float missFlashSeconds = 0.6f;
    public float missFlashStrength = 0.85f;   // how strongly the ring turns missColor
    public float missShakeAmount = 0.35f;
    public Transform shakeCamera;         // drag Main Camera here, empty = no shake

    [Header("Banner")]
    public float hitMessageSeconds = 3f, missMessageSeconds = 3f, songMessageSeconds = 2f;

    const string PitchShiftParameter = "MusicPitchShift";   // exposed on Music.mixer
    const float TargetLineSize = 3.5f;    // the red line's diameter: just inside the wedges' inner edge (3.44 across)
    const int MissThudMidi = 43;          // lowest of the three thud notes of a miss, a semitone apart
    // the Pitch Shifter holds back about one FFT window before it outputs anything: measured 26 to 43 ms with its
    // FFT size at 2048. The recording starts this much early when it goes through the mixer. Remeasure if the FFT size changes
    const float PitchShiftLatency = 0.030f;
    const float ArcDegrees = 28f;         // a white arc's span: a wedge is 30, the gap keeps neighboring arcs apart
    const float ArcWidth = 0.2f;

    // a chord (or white note) on screen: it has appeared and hasn't been judged yet
    class IncomingChord
    {
        public int entryIndex;            // which entry of the song
        public float landBeat;            // the beat it reaches the red line
        public float approachBeats;       // how long it takes to close
        public GameObject visual;         // its gold circle or white arc
    }
    readonly List<IncomingChord> incoming = new List<IncomingChord>();   // in landing order
    readonly List<PlayerVoice> players = new List<PlayerVoice>();
    public IReadOnlyList<PlayerVoice> Players => players;   // others can look, only joining and leaving change it

    Song song; int songIndex;
    int nextEntry, score, lives, combo, lastPlayerCount, lastWholeBeat, chordsJudged;
    float nextLandBeat, beatPulse, bannerHideTime, missFlash;
    bool gameOver;
    Vector3 cameraRestPosition;
    Color bannerRestColor;                // the banner's color as set in the Inspector, a miss turns it red for a moment
    Color targetLineColor;                // the red line's color as set in the Inspector; only its alpha pulses
    Material arcMaterial;                 // shared by every white arc

    // the beat clock runs on the audio hardware's clock, so it can't drift from the recording:
    // it was at anchorBeat at dsp time anchorDspTime, and moves PlayingBpm / 60 beats a second from there
    AudioSource recordingSource;
    AudioMixerGroup pitchShiftGroup;      // only used when speed isn't 1: straight to the speakers is cleaner and has no delay
    double anchorDspTime;
    float anchorBeat;
    float playbackRate = 1f;              // speed as actually used; a recording only goes 0.5x to 2x

    // where a note's wedge is: C at the top, clockwise by semitone
    public static Vector3 RingPosition(float radius, int note)
    {
        float a = (90f - note * 30f) * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f);
    }

    // inverse of RingPosition: which of the twelve wedges a position is in
    public static int WedgeAt(Vector2 position)
    {
        float a = Mathf.Atan2(position.y, position.x) * Mathf.Rad2Deg;
        return (Mathf.RoundToInt((90f - a) / 30f) % 12 + 12) % 12;
    }

    public float SongBeat => anchorBeat + (float)System.Math.Max(0.0, AudioSettings.dspTime - anchorDspTime) * PlayingBpm / 60f;   // holds still until anchorDspTime
    float PlayingBpm => song.bpm * playbackRate;
    bool UsingPitchShifter => pitchShiftGroup != null && !Mathf.Approximately(playbackRate, 1f);
    float RecordingLeadSeconds => UsingPitchShifter ? PitchShiftLatency * playbackRate : 0f;   // recording seconds the source runs ahead of the clock
    Song.Entry EntryAt(int i) => song.entries[i % song.entries.Count];
    IncomingChord NextToLand => incoming.Count > 0 ? incoming[0] : null;
    float CountInBeats => EntryAt(0).beats;
    // the beat where the recording runs out; no recording = the chart loops forever
    float RecordingEndBeat => song.recording != null ? (song.recording.length - song.firstChordSeconds) * song.bpm / 60f : float.MaxValue;
    Color ColorOf(Song.Entry entry) => entry.white ? whiteColor : goldColor;
    string DisplayName(Song.Entry entry) => entry.white ? Chord.NoteName((int)entry.root) : Chord.Label(entry.root, entry.quality);
    float ApproachBeatsFor(Song.Entry entry) => Mathf.Max(entry.beats, minApproachBeats);

    void Awake()
    {
        song = songs[0];
        // the recording gets its own object: an audio filter (like the synth's) would process every source on its object
        recordingSource = new GameObject("Recording").AddComponent<AudioSource>();
        recordingSource.transform.SetParent(transform, false);
        recordingSource.playOnAwake = false;
        recordingSource.spatialBlend = 0f;
        var groups = musicMixer != null ? musicMixer.FindMatchingGroups("Master") : null;
        if (groups != null && groups.Length > 0) pitchShiftGroup = groups[0];
    }

    void Start()
    {
        targetLineColor = targetLine.color;
        bannerRestColor = bannerLabel.color;
        if (shakeCamera != null) cameraRestPosition = shakeCamera.position;
        Restart();
    }

    // the PlayerInputManager on this same object sends these by name (Send Messages), like the players' actions
    void OnPlayerJoined(PlayerInput input)
    {
        var player = input.GetComponent<PlayerVoice>();
        player.Join(this, synth);
        players.Add(player);
    }

    void OnPlayerLeft(PlayerInput input) { players.Remove(input.GetComponent<PlayerVoice>()); }

    void Update()
    {
        float dt = Time.deltaTime;
        ApplySpeed();
        recordingSource.volume = musicVolume;

        bool running = !gameOver && players.Count == 3;
        if (!gameOver && players.Count < 3)
        {
            ClearIncoming();
            recordingSource.Stop();
            ShowBanner("waiting for " + (3 - players.Count) + " more controller" + (players.Count == 2 ? "" : "s") + ": press Cross to join", 1f);
        }
        if (running && lastPlayerCount < 3) StartClock();
        float songBeat = running ? SongBeat : 0f;
        if (running)
        {
            int wholeBeat = Mathf.FloorToInt(songBeat);
            if (wholeBeat != lastWholeBeat) { lastWholeBeat = wholeBeat; beatPulse = 1f; }

            // a ring appears its approach time before its downbeat, and shrinks to the red line's size exactly on it
            while (nextLandBeat <= RecordingEndBeat && songBeat >= nextLandBeat - ApproachBeatsFor(EntryAt(nextEntry))) SpawnNext();
            foreach (var chord in incoming)
                chord.visual.transform.localScale = Vector3.one * Mathf.LerpUnclamped(TargetLineSize, ringStartSize, (chord.landBeat - songBeat) / chord.approachBeats);
            if (NextToLand != null && songBeat >= NextToLand.landBeat) Judge(songBeat);
            if (!gameOver && incoming.Count == 0 && nextLandBeat > RecordingEndBeat)    // every chord the recording has room for is done
            {
                gameOver = true;
                ShowBanner("FINISHED   team " + score + "   Options restarts", float.MaxValue);
            }
        }
        beatPulse = Mathf.Max(0f, beatPulse - dt * beatPulseDecay);
        lastPlayerCount = players.Count;

        // wedges: the chord's notes light up in its color (root strongest), players lighten the one they stand in
        ring.ClearHighlights();
        var next = NextToLand;
        var entry = next != null ? EntryAt(next.entryIndex) : null;
        int[] notes = entry == null ? null : entry.white ? new[] { (int)entry.root } : Chord.Tones(entry.root, entry.quality);
        if (notes != null)
        {
            var c = ColorOf(entry);
            for (int i = 0; i < notes.Length; i++) ring.Highlight(notes[i], new Color(c.r, c.g, c.b, i == 0 ? rootHighlight : toneHighlight));
        }
        foreach (var p in players) ring.Highlight(p.Note, new Color(1, 1, 1, playerHighlight));

        // a miss washes over everything and fades out
        missFlash = Mathf.Max(0f, missFlash - dt / missFlashSeconds);
        if (missFlash > 0f)
            for (int w = 0; w < 12; w++) ring.Highlight(w, new Color(missColor.r, missColor.g, missColor.b, missFlashStrength * missFlash));
        bannerLabel.color = Color.Lerp(bannerRestColor, missColor, missFlash);
        if (shakeCamera != null) shakeCamera.position = cameraRestPosition + (Vector3)(Random.insideUnitCircle * missShakeAmount * missFlash);
        triangle.OnChord = notes != null && !entry.white && players.Count == 3 && new HashSet<int>(players.Select(p => p.Note)).SetEquals(notes);

        // center: the count-in, then the chord that is coming
        if (running && songBeat < -CountInBeats)
        {
            chordLabel.text = (Mathf.FloorToInt(songBeat + 2f * CountInBeats) + 1).ToString();
            chordNotesLabel.text = "";
            chordLabel.color = goldColor;
        }
        else if (notes != null)
        {
            chordLabel.text = DisplayName(entry);
            chordNotesLabel.text = entry.white ? "one player: Square / X" : Chord.NoteName(notes[0]) + " + " + Chord.NoteName(notes[1]) + " + " + Chord.NoteName(notes[2]);
            chordLabel.color = ColorOf(entry);
        }
        else { chordLabel.text = ""; chordNotesLabel.text = ""; }

        // the red line is the metronome
        targetLine.transform.localScale = Vector3.one * (TargetLineSize + beatPulseGrow * beatPulse);
        targetLine.color = new Color(targetLineColor.r, targetLineColor.g, targetLineColor.b, targetLineColor.a * Mathf.Lerp(beatPulseDimAlpha, 1f, beatPulse));

        int shown = next != null ? next.entryIndex : nextEntry;
        titleLabel.text = song.name + "   " + song.meter + "   " + PlayingBpm.ToString("0") + " bpm   chord " + (shown % song.entries.Count + 1) + " of " + song.entries.Count + "     L1 / R1 change song   Options restart";
        scoreLabel.text = "TEAM " + score + "\nlives " + lives + (combo > 1 ? "\ncombo x" + combo : "") + (players.Count < 3 ? "\n" + players.Count + "/3 joined" : "");
        speedLabel.text = "SPEED " + playbackRate.ToString("0.0#") + "x";
        if (Time.time > bannerHideTime) bannerLabel.text = "";
    }

    void SpawnNext()
    {
        var entry = EntryAt(nextEntry);
        GameObject visual;
        if (entry.white) visual = CreateWhiteArc((int)entry.root);
        else
        {
            visual = Instantiate(approachRingTemplate.gameObject, approachRingTemplate.parent);
            visual.name = "Ring " + DisplayName(entry);
            visual.SetActive(true);
            visual.GetComponent<SpriteRenderer>().color = goldColor;
        }
        incoming.Add(new IncomingChord { entryIndex = nextEntry, landBeat = nextLandBeat, approachBeats = ApproachBeatsFor(entry), visual = visual });
        nextLandBeat += entry.beats;                               // the next downbeat
        nextEntry++;
    }

    // an arc over one wedge, the same size as the approach circle; it lives on the ring so it turns with it
    GameObject CreateWhiteArc(int note)
    {
        if (arcMaterial == null) arcMaterial = new Material(Shader.Find("Sprites/Default"));
        var line = new GameObject("Arc " + Chord.NoteName(note) + " (white)").AddComponent<LineRenderer>();
        line.transform.SetParent(ring.transform, false);
        line.useWorldSpace = false;
        line.sharedMaterial = arcMaterial;
        line.startColor = line.endColor = whiteColor;
        line.startWidth = line.endWidth = ArcWidth;
        line.sortingOrder = 18;
        line.positionCount = 17;
        float middle = 90f - note * 30f;                         // C at the top, clockwise, like RingPosition
        for (int j = 0; j < 17; j++)
        {
            float a = (middle - ArcDegrees / 2f + j * ArcDegrees / 16f) * Mathf.Deg2Rad;
            line.SetPosition(j, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.5f);   // radius 0.5, so its scale is a diameter like the circle's
        }
        return line.gameObject;
    }

    // strikes are stamped in beats: a press from earlyBeats before the downbeat counts, and the chord is missed lateBeats after it
    void Judge(float songBeat)
    {
        var chord = NextToLand;
        var entry = EntryAt(chord.entryIndex);
        if (entry.white)                                         // a white note: anyone standing on it presses Square / X
        {
            if (players.Any(p => p.LastWhiteBeat >= chord.landBeat - earlyBeats && p.LastWhiteMidi % 12 == (int)entry.root)) Resolve(chord, true, null);
            else if (songBeat > chord.landBeat + lateBeats) Resolve(chord, false, "nobody pressed Square on it");
            return;
        }
        var struck = players.Where(p => p.LastGoldBeat >= chord.landBeat - earlyBeats).ToList();   // a gold chord: all three, on its three notes
        bool right = struck.Count == 3 && new HashSet<int>(struck.Select(p => p.LastGoldMidi % 12)).SetEquals(Chord.Tones(entry.root, entry.quality));
        if (right) Resolve(chord, true, null);
        else if (songBeat > chord.landBeat + lateBeats)
            Resolve(chord, false, struck.Count == 0 ? "nobody struck" : struck.Count < 3 ? (3 - struck.Count) + " didn't strike in time" : "wrong notes");
    }

    // a chord was hit or missed: score it, take it off screen, maybe turn the ring
    void Resolve(IncomingChord chord, bool hit, string reason)
    {
        var entry = EntryAt(chord.entryIndex);
        Destroy(chord.visual);                                   // first, so nothing below can leave it to be judged again
        incoming.Remove(chord);
        if (hit)
        {
            combo++;
            int points = (entry.white ? whiteNotePoints : chordPoints) + comboBonus * Mathf.Min(combo - 1, comboCap);
            score += points;
            ShowBanner(DisplayName(entry) + "  +" + points + (combo > 1 ? "   combo x" + combo : ""), hitMessageSeconds);
        }
        else
        {
            combo = 0; lives--;
            missFlash = 1f;
            for (int v = 0; v < 3; v++) synth.Strike(v, MissThudMidi + v, 1f);      // three low notes a semitone apart, an ugly thud
            ShowBanner("missed " + DisplayName(entry) + ": " + reason, missMessageSeconds);
        }
        if (turnEveryChords > 0 && ++chordsJudged % turnEveryChords == 0)
            ring.Turn(Random.Range(turnMinWedges, turnMaxWedges + 1) * (turnEitherWay && Random.value < 0.5f ? -1 : 1));
        if (lives <= 0)
        {
            gameOver = true;
            ClearIncoming();
            recordingSource.Stop();
            ShowBanner("GAME OVER   team " + score + "   Options restarts", float.MaxValue);
        }
    }

    void ShowBanner(string text, float seconds) { bannerLabel.text = text; bannerHideTime = Time.time + seconds; }

    void ClearIncoming()
    {
        foreach (var chord in incoming) Destroy(chord.visual);
        incoming.Clear();
    }

    void StartClock()
    {
        ClearIncoming();
        chordsJudged = 0;
        ring.ResetTurn();
        nextEntry = 0;
        nextLandBeat = 0f;                   // first chord lands on beat 0
        foreach (var p in players) p.ForgetStrikes();
        anchorBeat = -2f * CountInBeats;     // one counted bar, then the first ring closes over one bar
        anchorDspTime = AudioSettings.dspTime + 0.1;   // a moment ahead, so the recording can start on the exact sample
        lastWholeBeat = -99;
        ScheduleRecording();
    }

    // start the recording so that firstChordSeconds into it lands on beat 0, wherever the clock is
    void ScheduleRecording()
    {
        recordingSource.Stop();
        recordingSource.clip = song.recording;
        if (song.recording == null || players.Count < 3) return;
        recordingSource.outputAudioMixerGroup = UsingPitchShifter ? pitchShiftGroup : null;
        float at = song.firstChordSeconds + anchorBeat * 60f / song.bpm + RecordingLeadSeconds;   // seconds into the recording at anchorDspTime
        if (at >= song.recording.length) return;
        if (at >= 0f) { recordingSource.time = at; recordingSource.PlayScheduled(anchorDspTime); }
        else { recordingSource.time = 0f; recordingSource.PlayScheduled(anchorDspTime - at / playbackRate); }
    }

    // speed can change while playing: re-anchor the clock where it is so nothing jumps, and keep the recording's key
    void ApplySpeed()
    {
        float rate = song.recording != null ? Mathf.Clamp(speed, 0.5f, 2f) : speed;   // 0.5 to 2 is the Pitch Shifter's range
        if (Mathf.Approximately(rate, playbackRate)) return;
        bool wasUsingPitchShifter = UsingPitchShifter;
        anchorBeat = SongBeat; anchorDspTime = AudioSettings.dspTime;   // re-anchor where the clock is now, so it doesn't jump
        playbackRate = rate;
        recordingSource.pitch = rate;                            // plays faster or slower, and higher or lower
        if (musicMixer != null && !musicMixer.SetFloat(PitchShiftParameter, 1f / rate))   // so shift the key back by the inverse
            Debug.LogWarning("Triad: Music mixer has no exposed " + PitchShiftParameter + ", so the key follows the speed.");
        if (UsingPitchShifter != wasUsingPitchShifter && recordingSource.isPlaying)
        {
            anchorDspTime = AudioSettings.dspTime + 0.1;         // switching into or out of the mixer: hold a moment and pick the recording up in step
            ScheduleRecording();
        }
    }

    public void ChangeSong(int step)
    {
        songIndex = ((songIndex + step) % songs.Length + songs.Length) % songs.Length;
        song = songs[songIndex];
        ApplySpeed();
        Restart();
        ShowBanner(song.name + "   " + song.meter, songMessageSeconds);
    }

    public void Restart()
    {
        score = 0; lives = startLives; combo = 0; gameOver = false;
        bannerLabel.text = "";
        StartClock();
    }

    void OnDestroy() { if (arcMaterial != null) Destroy(arcMaterial); }
}
