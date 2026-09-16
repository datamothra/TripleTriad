using System.Collections.Generic;
using UnityEngine;

//making the wedges of notes programattically during Awake, drawing, coloring them and adding meshes


[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class RingMesh : MonoBehaviour
{
    public int wedges = 12;
    public float innerRadius = 1.8f, outerRadius = 4.9f;
    public float gapDegrees = 1.2f;        
    public int segments = 10;              // wedge smoothness, not related to the 12 notes
    public Color even = new Color(0.10f, 0.10f, 0.13f), odd = new Color(0.13f, 0.13f, 0.17f);

    Mesh mesh;
    Color[] wedgeColour;
    bool dirty;

    void Awake()
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        float step = 360f / wedges, half = step * 0.5f - gapDegrees * 0.5f;

        for (int w = 0; w < wedges; w++)
        {
            float centre = 90f - w * step;
            int first = verts.Count;
            for (int k = 0; k <= segments; k++)
            {
                float a = (centre - half + 2f * half * k / segments) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                verts.Add(dir * innerRadius);
                verts.Add(dir * outerRadius);
                if (k < segments)
                {
                    int i = first + k * 2;
                    tris.AddRange(new[] { i, i + 1, i + 2,  i + 1, i + 3, i + 2 });
                }
            }
        }

        mesh = new Mesh();
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        GetComponent<MeshFilter>().mesh = mesh;

        var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ?? Shader.Find("Sprites/Default");
        var renderer = GetComponent<MeshRenderer>();
        renderer.material = new Material(shader);    // sprite shaders use vertex colour and draw both faces
        renderer.sortingOrder = 0;

        wedgeColour = new Color[wedges];
        ClearTints();
        Apply();
    }

    public Color BaseColour(int w) => w % 2 == 0 ? even : odd;

    public void ClearTints()
    {
        for (int w = 0; w < wedges; w++) wedgeColour[w] = BaseColour(w);
        dirty = true;
    }

    // color blend.
    public void Tint(int w, Color c)
    {
        w = ((w % wedges) + wedges) % wedges;
        wedgeColour[w] = Color.Lerp(wedgeColour[w], new Color(c.r, c.g, c.b, 1f), c.a);
        dirty = true;
    }

    void LateUpdate() { if (dirty) Apply(); }

    void Apply()
    {
        dirty = false;
        int perWedge = (segments + 1) * 2;
        var colours = new Color[wedges * perWedge];
        for (int w = 0; w < wedges; w++)
        {
                //Mesh colors linear space conversion
            var c = QualitySettings.activeColorSpace == ColorSpace.Linear ? wedgeColour[w].linear : wedgeColour[w];
            for (int v = 0; v < perWedge; v++) colours[w * perWedge + v] = c;
        }
        mesh.SetColors(colours);
    }

    /// Which wedge a world position is in. called by the players to see which note they are on
    public static int WedgeAt(Vector2 p, int wedges = 12)
    {
        float angle = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;
        int i = Mathf.RoundToInt((90f - angle) / (360f / wedges));
        return ((i % wedges) + wedges) % wedges;
    }
}
