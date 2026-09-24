using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

// one player: moves around the ring, and strikes the note under them
public class PlayerVoice : MonoBehaviour
{
    public TMP_Text noteLabel, playerLabel;
    public SpriteRenderer padSprite;
    public float moveSpeed = 6.5f;
    public float strikeVelocity = 0.85f;  // how hard a strike hits the synth, 0 to 1
    public int midiOfC = 60;              // MIDI note of the C wedge, 60 = middle C
    public int[] startNotes = { 0, 4, 7 };   // where each player starts, in join order (C, E, G)
    public Color[] playerColors = { Color.cyan, Color.magenta, Color.yellow };   // one per player, in join order

    // the running band sits on the wedge sprites, so these follow the ring art rather than taste
    const float InnerRadius = 2.15f, OuterRadius = 4.55f, StartRadius = 3.4f;
    const float PressCooldown = 0.06f;    // a press can't register twice within this

    // anyone can read these; only the player changes them. Unity doesn't save properties, so they stay out of the Inspector
    public int Note { get; private set; }                    // the note under the player, 0 = C
    public int PlayerIndex { get; private set; }             // 0, 1, 2 in join order; also the synth voice
    public float LastGoldBeat { get; private set; } = float.MinValue;    // on the song's beat clock
    public float LastWhiteBeat { get; private set; } = float.MinValue;
    public int LastGoldMidi { get; private set; }
    public int LastWhiteMidi { get; private set; }

    GameManager game;                     // both handed over by Join
    Triad.Synth synth;
    float lastPressTime = -99f;           // for the cooldown, in seconds
    Vector2 moveInput;

    public int MidiNote => midiOfC + Note;

    // the game calls this when the player joins
    public void Join(GameManager game, Triad.Synth synth) { this.game = game; this.synth = synth; }

    void Awake()
    {
        PlayerIndex = Mathf.Max(0, GetComponent<PlayerInput>().playerIndex);
        if (padSprite != null && playerColors.Length > 0) padSprite.color = playerColors[PlayerIndex % playerColors.Length];
        Note = startNotes.Length > 0 ? startNotes[PlayerIndex % startNotes.Length] : 0;
        transform.position = GameManager.RingPosition(StartRadius, Note);
        if (playerLabel != null) playerLabel.text = "P" + (PlayerIndex + 1);   // who you are, it never changes
    }

    // the PlayerInput on this object calls these by action name (Send Messages)
    void OnMove(InputValue v) { moveInput = v.Get<Vector2>(); }
    void OnRestart(InputValue v)  { if (v.isPressed && game != null) game.Restart(); }
    void OnNextSong(InputValue v) { if (v.isPressed && game != null) game.ChangeSong(+1); }
    void OnPrevSong(InputValue v) { if (v.isPressed && game != null) game.ChangeSong(-1); }

    // gold chords take Cross / A, white notes Square / X; either way the press sounds your note
    void OnStrikeGold(InputValue v)  { if (v.isPressed && PlayNote()) { LastGoldBeat = game.SongBeat; LastGoldMidi = MidiNote; } }
    void OnStrikeWhite(InputValue v) { if (v.isPressed && PlayNote()) { LastWhiteBeat = game.SongBeat; LastWhiteMidi = MidiNote; } }

    bool PlayNote()
    {
        if (game == null || Time.time - lastPressTime < PressCooldown) return false;
        if (synth != null) synth.Strike(PlayerIndex, MidiNote, strikeVelocity);   // one voice per player
        lastPressTime = Time.time;
        return true;
    }

    public void ForgetStrikes() { LastGoldBeat = LastWhiteBeat = float.MinValue; }   // a new run starts its beat clock again

    void Update()
    {
        transform.position += (Vector3)(moveInput * moveSpeed * Time.deltaTime);
        var p = (Vector2)transform.position;                     // keep them in the running band
        float r = p.magnitude;
        if (r > 0.01f) transform.position = p / r * Mathf.Clamp(r, InnerRadius, OuterRadius);
        Note = game != null ? game.ring.WedgeAt(transform.position) : GameManager.WedgeAt(transform.position);   // angle on the ring = wedge = note
        if (noteLabel != null) noteLabel.text = Chord.NoteName(Note);
    }
}
