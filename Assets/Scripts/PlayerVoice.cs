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

    public int wedge;                 // pitch of the wedge 
    public int voice;                 // synth voice = player index
    public float lastStrikeTime = -99f;
    public int lastStrikeMidi;

    public float coolDownTime = 0.06f;

    public SpriteRenderer pad;

    public Color[] colours = { Color.cyan, Color.magenta, Color.yellow };   // one per player, in join order

    const int OctaveMidi = 60;       
    Vector2 move;

    public int Midi => OctaveMidi + wedge;

    void Awake()
    {
        voice = GetComponent<PlayerInput>().playerIndex;         // 0, 1, 2 in join order
        if (pad != null && colours.Length > 0) pad.color = colours[voice % colours.Length];
        wedge = new[] { 0, 4, 7 }[voice % 3];                    // spawn on C, E, G
        transform.position = GameManager.Polar(3.4f, wedge);
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
        if (synth != null) synth.Strike(voice, Midi, 0.85f);                       // monophonic
        lastStrikeTime = Time.time;
        lastStrikeMidi = Midi;
    }

    void Update()
    {
        transform.position += (Vector3)(move * moveSpeed * Time.deltaTime);
        var p = (Vector2)transform.position;                     // keep them in the running band
        float r = p.magnitude;
        if (r > 0.01f) transform.position = p / r * Mathf.Clamp(r, innerRadius, outerRadius);
        wedge = game != null ? game.ring.WedgeAt(transform.position) : GameManager.WedgeAt(transform.position);   // angle on the ring = wedge = note
    }
}
