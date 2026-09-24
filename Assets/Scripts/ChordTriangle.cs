using UnityEngine;

// the line between the three players; it lights up in gold when they stand on the coming chord's notes
[RequireComponent(typeof(LineRenderer))]
public class ChordTriangle : MonoBehaviour
{
    public Color idleColor = new Color(1, 1, 1, 0.2f);   // lit uses the game's gold
    public float idleWidth = 0.07f, litWidth = 0.12f;
    public GameManager game;
    [System.NonSerialized] public bool onChord;
    LineRenderer line;

    void Awake() { line = GetComponent<LineRenderer>(); }

    void Update()
    {
        var players = game.Players;
        line.positionCount = players.Count;
        for (int i = 0; i < players.Count; i++) line.SetPosition(i, players[i].transform.position);
        line.startColor = line.endColor = onChord ? game.goldColor : idleColor;
        line.startWidth = line.endWidth = onChord ? litWidth : idleWidth;
    }
}
