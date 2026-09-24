using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

// player stuff
public class PlayerVoice : MonoBehaviour
{
    public TMP_Text noteLabel, tagLabel;
    public float moveSpeed = 6.5f;
    public float strikeVelocity = 0.85f;  // how hard a strike hits the synth, 0 to 1
    public int octaveMidi = 60;           // MIDI note of the C wedge, 60 = middle C
    public int[] spawnWedges = { 0, 4, 7 };   // where each player starts, in join order (C, E, G)

    public SpriteRenderer pad;

    public Color[] colors = { Color.cyan, Color.magenta, Color.yellow };   // one per player, in join order

    // the running band sits on the wedge sprites, so these follow the ring art rather than taste
    const float InnerRadius = 2.15f, OuterRadius = 4.55f, SpawnRadius = 3.4f;
    const float CoolDownTime = 0.06f;     // a press can't register twice within this

    // set at runtime, so not saved with the prefab or shown in the Inspector
    [System.NonSerialized] public GameManager game;
    [System.NonSerialized] public Triad.Synth synth;
    [System.NonSerialized] public int wedge;                 // the note under the player
    [System.NonSerialized] public int voice;                 // synth voice = player index
    [System.NonSerialized] public float lastStrikeTime = -99f;   // for the cooldown, in seconds
    [System.NonSerialized] public float lastStrikeBeat = float.MinValue, lastWestBeat = float.MinValue;   // on the song's beat clock: Cross / A, Square / X
    [System.NonSerialized] public int lastStrikeMidi, lastWestMidi;

    Vector2 move;

    public int Midi => octaveMidi + wedge;

    void Awake()
    {
        voice = Mathf.Max(0, GetComponent<PlayerInput>().playerIndex);         // 0, 1, 2 in join order
        if (pad != null && colors.Length > 0) pad.color = colors[voice % colors.Length];
        wedge = spawnWedges.Length > 0 ? spawnWedges[voice % spawnWedges.Length] : 0;
        transform.position = GameManager.Polar(SpawnRadius, wedge);
        if (tagLabel != null) tagLabel.text = "P" + (voice + 1);     // the tag on the pad is who you are, it never changes
    }

    // Player controller stuff
    void OnMove(InputValue v) { move = v.Get<Vector2>(); }
    void OnRestart(InputValue v)  { if (v.isPressed && game != null) game.Restart(); }
    void OnNextSong(InputValue v) { if (v.isPressed && game != null) game.ChangeSong(+1); }
    void OnPrevSong(InputValue v) { if (v.isPressed && game != null) game.ChangeSong(-1); }

    // gold chords take Cross / A, white chords Square / X; either way the press sounds your note
    void OnStrike(InputValue v)     { if (v.isPressed && Strike()) { lastStrikeBeat = game.SongBeat; lastStrikeMidi = Midi; } }
    void OnStrikeWest(InputValue v) { if (v.isPressed && Strike()) { lastWestBeat = game.SongBeat; lastWestMidi = Midi; } }

    bool Strike()
    {
        if (game == null || Time.time - lastStrikeTime < CoolDownTime) return false;
        if (synth != null) synth.Strike(voice, Midi, strikeVelocity);                       // monophonic
        lastStrikeTime = Time.time;
        return true;
    }

    public void ForgetStrikes() { lastStrikeBeat = lastWestBeat = float.MinValue; }   // a new run starts its beat clock again

    void Update()
    {
        transform.position += (Vector3)(move * moveSpeed * Time.deltaTime);
        var p = (Vector2)transform.position;                     // keep them in the running band
        float r = p.magnitude;
        if (r > 0.01f) transform.position = p / r * Mathf.Clamp(r, InnerRadius, OuterRadius);
        wedge = game != null ? game.ring.WedgeAt(transform.position) : GameManager.WedgeAt(transform.position);   // angle on the ring = wedge = note
        if (noteLabel != null) noteLabel.text = Chord.Names[wedge];
    }
}
