using UnityEngine;

// fancy line between players, plights up when correct chord is played, used to light up compared to chord quality, maybe reinstate that as a mode
[RequireComponent(typeof(LineRenderer))]
public class ChordTriangle : MonoBehaviour
{
    public Color idle = new Color(1, 1, 1, 0.2f);   // lit uses the game's accent gold
    public float idleWidth = 0.07f, litWidth = 0.12f;
    public GameManager game;
    [System.NonSerialized] public bool inPosition;
    LineRenderer line;

    void Awake() { line = GetComponent<LineRenderer>(); }

    void Update()
    {
        var players = game.Players;
        line.positionCount = players.Count;
        for (int i = 0; i < players.Count; i++) line.SetPosition(i, players[i].transform.position);
        line.startColor = line.endColor = inPosition ? game.accent : idle;
        line.startWidth = line.endWidth = inPosition ? litWidth : idleWidth;
    }
}
