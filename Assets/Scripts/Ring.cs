using UnityEngine;

// recolours the 12 wedge sprites that sit under this object in the scene. a wedge's resting colour is
// whatever it has in the Inspector, and its note is where it sits, the same rule the players use
public class Ring : MonoBehaviour
{
    readonly SpriteRenderer[] wedges = new SpriteRenderer[12];
    readonly Color[] rest = new Color[12];

    public float shiftSeconds = 0.25f;    // how long one wedge of turn takes

    public float shiftAmount = 30f;
    float targetZ;                        // where the ring is turning to; it sits still once it gets there

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
    public void Shift(int steps) { targetZ -= shiftAmount * steps; }
    public void ResetShift() { targetZ = 0f; transform.rotation = Quaternion.identity; }

    void Update()
    {
        float z = Mathf.MoveTowardsAngle(transform.eulerAngles.z, targetZ, 30f / shiftSeconds * Time.deltaTime);
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
