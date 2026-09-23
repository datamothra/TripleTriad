using UnityEngine;

// fancy line between players, plights up when correct chord is played, used to light up compared to chord quality, maybe reinstate that as a mode
[RequireComponent(typeof(LineRenderer))]
public class ChordTriangle : MonoBehaviour
{
    public Color idle = new Color(1, 1, 1, 0.2f);
    public Color lit = new Color(0.875f, 0.686f, 0.196f);
    public float idleWidth = 0.07f, litWidth = 0.12f;
    public GameManager game;
    [HideInInspector] public bool inPosition;
    LineRenderer line;

    void Awake() { line = GetComponent<LineRenderer>(); }

    void Update()
    {
        var players = game.Players;
        line.positionCount = players.Count;
        for (int i = 0; i < players.Count; i++) line.SetPosition(i, players[i].transform.position);
        line.startColor = line.endColor = inPosition ? lit : idle;
        line.startWidth = line.endWidth = inPosition ? litWidth : idleWidth;
    }
}
