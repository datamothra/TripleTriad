using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

// player stuff
public class PlayerVoice : MonoBehaviour
{
    public TMP_Text noteLabel, tagLabel;
    [HideInInspector] public GameManager game;   
    [HideInInspector] public Triad.Synth synth;
    public float moveSpeed = 6.5f, innerRadius = 2.15f, outerRadius = 4.55f;
    public float[] speedMultipliers = { 1f, 1f, 1f };   // moveSpeed is multiplied by this, one per player, in join order

    public int wedge;                 // pitch of the wedge 
    public int voice;                 // synth voice = player index
    public float lastStrikeTime = -99f;
    public int lastStrikeMidi;

    public float coolDownTime = 0.06f;
    public float strikeVelocity = 0.85f;  // how hard a strike hits the synth, 0 to 1
    public int octaveMidi = 60;           // MIDI note of the C wedge, 60 = middle C
    public int[] spawnWedges = { 0, 4, 7 };   // where each player starts, in join order (C, E, G)
    public float spawnRadius = 3.4f;

    public SpriteRenderer pad;

    public Color[] colours = { Color.cyan, Color.magenta, Color.yellow };   // one per player, in join order

    Vector2 move;

    public int Midi => octaveMidi + wedge;

    void Awake()
    {
        voice = Mathf.Max(0, GetComponent<PlayerInput>().playerIndex);         // 0, 1, 2 in join order
        if (pad != null && colours.Length > 0) pad.color = colours[voice % colours.Length];
        wedge = spawnWedges.Length > 0 ? spawnWedges[voice % spawnWedges.Length] : 0;
        transform.position = GameManager.Polar(spawnRadius, wedge);
        if (noteLabel != null) noteLabel.text = "P" + (voice + 1);   // the label on the pad is who you are, it never changes

    }

    // Player controller stuff
    void OnMove(InputValue v) { move = v.Get<Vector2>(); }
    void OnRestart(InputValue v)  { if (v.isPressed && game != null) game.Restart(); }
    void OnNextSong(InputValue v) { if (v.isPressed && game != null) game.ChangeSong(+1); }
    void OnPrevSong(InputValue v) { if (v.isPressed && game != null) game.ChangeSong(-1); }

    void OnStrike(InputValue v)
    {
        if (!v.isPressed || Time.time - lastStrikeTime < coolDownTime) return;          // 
        if (synth != null) synth.Strike(voice, Midi, strikeVelocity);                       // monophonic
        lastStrikeTime = Time.time;
        lastStrikeMidi = Midi;
    }

    void Update()
    {
        if (game != null && game.canonMusicMode) return; // 音乐模式统一先移动，再读取按键和判定。
        float mult = speedMultipliers.Length > 0 ? speedMultipliers[voice % speedMultipliers.Length] : 1f;
        transform.position += (Vector3)(move * moveSpeed * mult * Time.deltaTime);
        var p = (Vector2)transform.position;                     // keep them in the running band
        float r = p.magnitude;
        if (r > 0.01f) transform.position = p / r * Mathf.Clamp(r, innerRadius, outerRadius);
        wedge = game != null ? game.ring.WedgeAt(transform.position) : GameManager.WedgeAt(transform.position);   // angle on the ring = wedge = note
    }
}
