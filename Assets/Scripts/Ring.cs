using UnityEngine;

// recolours the 12 wedge sprites that sit under this object in the scene. a wedge's resting colour is
// whatever it has in the Inspector, and its note is where it sits, the same rule the players use
public class Ring : MonoBehaviour
{
    readonly SpriteRenderer[] wedges = new SpriteRenderer[12];
    readonly Color[] rest = new Color[12];

    void Awake()
    {
        foreach (var sr in GetComponentsInChildren<SpriteRenderer>())
        {
            int w = GameManager.WedgeAt(sr.transform.localPosition);
            wedges[w] = sr;
            rest[w] = sr.color;
        }
    }

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
