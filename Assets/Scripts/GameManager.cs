using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
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

   


// leniency on timing
    public float earlyBeats = 0.5f;       
    public float lateBeats = 0.25f;      

    public int shiftEvery = 2;            // the ring turns after this many chords, 0 = never
    public int shiftMinWedges = 1, shiftMaxWedges = 5;   // by a random number of wedges in this range
    public bool shiftEitherWay = true;    // off = always clockwise

    // a miss: the whole ring and the banner flash this colour, the camera shakes, the synth thuds
    public Color missColour = new Color(0.9f, 0.1f, 0.15f);
    public float missSeconds = 0.6f;
    public float missShake = 0.35f;
    public Transform shakeCamera;         // drag Main Camera here, empty = no shake



    public float spawnScale = 24f, landScale = 3.5f;
    [Range(0.4f, 2.5f)] public float speed = 1f;   // set in the Inspector before pressing Play

    class Marker { public int index; public float land, beats; public GameObject go; }
    readonly List<Marker> markers = new List<Marker>();
    readonly List<PlayerVoice> players = new List<PlayerVoice>();
    public List<PlayerVoice> Players => players;
    Song song; int songIndex;
    int nextIndex, score, lives, combo, lastPlayerCount, lastBeatInt, chordsDone;
    float songBeat, nextLand, beatPulse, bannerUntil, missFlash;
    Vector3 cameraHome;
    bool gameOver;
    Color bannerColour;                   // the banner's colour as set in the Inspector, a miss turns it red for a moment
    Color coreColour;                     // the red line's colour as set in the Inspector; only its alpha pulses

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

    float Bpm => song.bpm * speed;
    float BeatSeconds => 60f / Bpm;
    Song.Entry Entry(int i) => song.entries[i % song.entries.Count];
    Marker Current => markers.Count > 0 ? markers[0] : null;          // the chord about to land
    float CountInBeats => Entry(0).beats;

    void Awake()
    {
        song = songs[0];
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

        bool running = !gameOver && players.Count == 3;
        if (!gameOver && players.Count < 3)
        {
            ClearMarkers();
            Say("waiting for " + (3 - players.Count) + " more controller" + (players.Count == 2 ? "" : "s") + ": press Cross to join", 1f);
        }
        if (running)
        {
            if (lastPlayerCount < 3) StartClock();
            songBeat += dt * Bpm / 60f;
            int beat = Mathf.FloorToInt(songBeat);
            if (beat != lastBeatInt) { lastBeatInt = beat; beatPulse = 1f; }

            while (songBeat >= nextLand - Entry(nextIndex).beats) Spawn();     // a ring appears one chord-length before its bar line
            foreach (var m in markers)
                m.go.transform.localScale = Vector3.one * Mathf.LerpUnclamped(landScale, spawnScale, (m.land - songBeat) / m.beats);
            if (Current != null && songBeat >= Current.land) Judge(players);
        }
        beatPulse = Mathf.Max(0f, beatPulse - dt * 5f);
        lastPlayerCount = players.Count;

        // wedges: the chord's three notes light up (root strongest), players lighten the one they stand in
        ring.ClearTints();
        var cur = Current;
        int[] tones = cur != null ? Chord.Tones(Entry(cur.index).root, Entry(cur.index).quality) : null;
        if (tones != null)
        {
            ring.Tint(tones[0], new Color(accent.r, accent.g, accent.b, 0.70f));
            ring.Tint(tones[1], new Color(accent.r, accent.g, accent.b, 0.45f));
            ring.Tint(tones[2], new Color(accent.r, accent.g, accent.b, 0.45f));
        }
        foreach (var p in players) ring.Tint(p.wedge, new Color(1, 1, 1, 0.25f));

        // a miss washes over everything and fades out
        missFlash = Mathf.Max(0f, missFlash - dt / missSeconds);
        if (missFlash > 0f)
            for (int w = 0; w < 12; w++) ring.Tint(w, new Color(missColour.r, missColour.g, missColour.b, 0.85f * missFlash));
        bannerLabel.color = Color.Lerp(bannerColour, missColour, missFlash);
        if (shakeCamera != null) shakeCamera.position = cameraHome + (Vector3)(Random.insideUnitCircle * missShake * missFlash);
        triangle.inPosition = tones != null && players.Count == 3 && new HashSet<int>(players.Select(p => p.wedge)).SetEquals(tones);

        // core: the count-in, then the chord that is coming
        if (running && songBeat < -CountInBeats)
        {
            chordLabel.text = (Mathf.FloorToInt(songBeat + 2f * CountInBeats) + 1).ToString();
            chordNotes.text = "";
        }
        else if (tones != null)
        {
            var e = Entry(cur.index);
            chordLabel.text = Chord.Label(e.root, e.quality);
            chordNotes.text = Chord.Names[tones[0]] + " + " + Chord.Names[tones[1]] + " + " + Chord.Names[tones[2]];
        }
        else { chordLabel.text = ""; chordNotes.text = ""; }
        chordLabel.color = accent;

        // the red line is the metronome
        coreLine.transform.localScale = Vector3.one * (landScale + 0.22f * beatPulse);
        coreLine.color = new Color(coreColour.r, coreColour.g, coreColour.b, coreColour.a * (0.7f + 0.3f * beatPulse));

        int shown = cur != null ? cur.index : nextIndex;
        titleLabel.text = song.name + "   " + song.meter + "   " + Bpm.ToString("0") + " bpm   chord " + (shown % song.entries.Count + 1) + " of " + song.entries.Count + "     L1 / R1 change song   Options restart";
        scoreLabel.text = "TEAM " + score + "\nlives " + lives + (combo > 1 ? "\ncombo x" + combo : "") + (players.Count < 3 ? "\n" + players.Count + "/3 joined" : "");
        speedLabel.text = "SPEED " + speed.ToString("0.0") + "x";
        if (Time.time > bannerUntil) bannerLabel.text = "";
    }

    void Spawn()
    {
        var e = Entry(nextIndex);
        var go = Instantiate(ringTemplate.gameObject, ringTemplate.parent);
        go.name = "Ring " + Chord.Label(e.root, e.quality);
        go.SetActive(true);
        go.GetComponent<SpriteRenderer>().color = accent;
        markers.Add(new Marker { index = nextIndex, land = nextLand, beats = e.beats, go = go });
        nextLand += e.beats;                                       // the next bar line
        nextIndex++;
    }

    void Judge(List<PlayerVoice> players)
    {
        var m = Current;
        var e = Entry(m.index);
        float landTime = Time.time - (songBeat - m.land) * BeatSeconds;   // strikes are stamped in seconds
        var struck = players.Where(p => p.lastStrikeTime >= landTime - earlyBeats * BeatSeconds).ToList();
        bool right = struck.Count == 3 && new HashSet<int>(struck.Select(p => p.lastStrikeMidi % 12)).SetEquals(Chord.Tones(e.root, e.quality));
        if (right) Finish(m, true, null);
        else if (songBeat > m.land + lateBeats)
            Finish(m, false, struck.Count == 0 ? "nobody struck" : struck.Count < 3 ? (3 - struck.Count) + " didn't strike in time" : "wrong notes");
    }

    void Finish(Marker m, bool hit, string why)
    {
        var e = Entry(m.index);
        if (hit)
        {
            combo++;
            int pts = 100 + 20 * Mathf.Min(combo - 1, 10);
            score += pts;
            Say(Chord.Label(e.root, e.quality) + "  +" + pts + (combo > 1 ? "   combo x" + combo : ""), 3f);
        }
        else
        {
            combo = 0; lives--;
            missFlash = 1f;
            for (int v = 0; v < 3; v++) synth.Strike(v, 43 + v, 1f);      // three low notes a semitone apart, an ugly thud
            Say("missed " + Chord.Label(e.root, e.quality) + ": " + why, 3f);
        }
        Destroy(m.go);
        markers.Remove(m);
        if (shiftEvery > 0 && ++chordsDone % shiftEvery == 0) ring.Shift(Random.Range(shiftMinWedges, shiftMaxWedges + 1) * (shiftEitherWay && Random.value < 0.5f ? -1 : 1));
        if (lives <= 0)
        {
            gameOver = true;
            ClearMarkers();
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
        songBeat = -2f * CountInBeats;       // one counted bar, then the first ring closes over one bar
        lastBeatInt = -99;
    }

    public void ChangeSong(int step)
    {
        songIndex = ((songIndex + step) % songs.Length + songs.Length) % songs.Length;
        song = songs[songIndex];
        Restart();
        Say(song.name + "   " + song.meter, 2f);
    }

    public void Restart()
    {
        score = 0; lives = 5; combo = 0; gameOver = false;
        bannerLabel.text = "";
        StartClock();
    }
}
