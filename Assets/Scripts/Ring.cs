using UnityEngine;

// the 12 wedge sprites that sit under this object in the scene: highlights them and turns them. a wedge's resting
// color is whatever it has in the Inspector, and its note is where it sits, the same rule the players use
public class Ring : MonoBehaviour
{
    readonly SpriteRenderer[] wedgeSprites = new SpriteRenderer[12];
    readonly Color[] restColors = new Color[12];

    public float turnSeconds = 0.15f;     // how long a turn takes, the same however far it goes

    float angle, turnFrom, turnTo, turnStartTime;   // degrees now, where this turn began and ends, and when it began

    void Awake()
    {
        foreach (var sprite in GetComponentsInChildren<SpriteRenderer>())
        {
            int note = GameManager.WedgeAt(sprite.transform.localPosition);
            wedgeSprites[note] = sprite;
            restColors[note] = sprite.color;
        }
    }

    // turn by whole wedges, + is clockwise like the notes
    public void Turn(int wedges) { turnFrom = angle; turnTo -= 30f * wedges; turnStartTime = Time.time; }
    public void ResetTurn() { angle = turnFrom = turnTo = 0f; transform.rotation = Quaternion.identity; }

    void Update()
    {
        float t = turnSeconds > 0f ? Mathf.Clamp01((Time.time - turnStartTime) / turnSeconds) : 1f;
        angle = Mathf.Lerp(turnFrom, turnTo, Mathf.SmoothStep(0f, 1f, t));      // eases in and out
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    // which wedge a world position is over, wherever the ring has turned to
    public int WedgeAt(Vector2 world) => GameManager.WedgeAt(transform.InverseTransformPoint(world));

    public void ClearHighlights()
    {
        for (int w = 0; w < 12; w++) if (wedgeSprites[w]) wedgeSprites[w].color = restColors[w];
    }

    // blend a wedge toward a color; the color's alpha is how strongly
    public void Highlight(int note, Color color)
    {
        note = (note % 12 + 12) % 12;
        if (wedgeSprites[note]) wedgeSprites[note].color = Color.Lerp(wedgeSprites[note].color, new Color(color.r, color.g, color.b, 1f), color.a);
    }
}
