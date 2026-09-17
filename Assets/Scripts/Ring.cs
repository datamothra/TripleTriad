using UnityEngine;

// recolours the 12 wedge sprites that sit under this object in the scene. a wedge's resting colour is
// whatever it has in the Inspector, and its note is where it sits, the same rule the players use
public class Ring : MonoBehaviour
{
    readonly SpriteRenderer[] wedges = new SpriteRenderer[12];
    readonly Color[] rest = new Color[12];

    public float shiftSpeed = 0.15f;      // seconds a shift takes, the same however far it turns

    float z, fromZ, targetZ, shiftStart;  // the ring's angle now, where this shift began, where it ends, when it began

    void Awake()
    {
        foreach (var sr in GetComponentsInChildren<SpriteRenderer>())
        {
            int w = GameManager.WedgeAt(sr.transform.localPosition);
            wedges[w] = sr;
            rest[w] = sr.color;
        }
    }

    // turn by whole wedges, + is clockwise like the notes
    public void Shift(int steps) { fromZ = z; targetZ -= 30f * steps; shiftStart = Time.time; }
    public void ResetShift() { z = fromZ = targetZ = 0f; transform.rotation = Quaternion.identity; }

    void Update()
    {
        float t = shiftSpeed > 0f ? Mathf.Clamp01((Time.time - shiftStart) / shiftSpeed) : 1f;
        z = Mathf.Lerp(fromZ, targetZ, Mathf.SmoothStep(0f, 1f, t));      // eases in and out
        transform.rotation = Quaternion.Euler(0f, 0f, z);
    }

    // which wedge a world position is over, wherever the ring has turned to
    public int WedgeAt(Vector2 world) => GameManager.WedgeAt(transform.InverseTransformPoint(world));

    public void ClearTints()
    {
        for (int w = 0; w < 12; w++) if (wedges[w]) wedges[w].color = rest[w];
    }

    public void Tint(int w, Color c)
    {
        w = (w % 12 + 12) % 12;
        if (wedges[w]) wedges[w].color = Color.Lerp(wedges[w].color, new Color(c.r, c.g, c.b, 1f), c.a);
    }
}
