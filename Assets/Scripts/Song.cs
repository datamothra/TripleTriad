using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Triad/Song")]
public class Song : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public Chord.Note root;
        public Chord.Quality quality = Chord.Quality.Major;
        public float beats = 2;
        public bool white;                 // a white note instead: just root (quality is ignored), and one player on it presses Square / X
    }

    public string meter = "4/4";          // for the title line; the beats field on each chord is what actually schedules
    public float bpm = 66;                 // beats per minute, where a beat is the meter's pulse (dotted quarter in 6/8)
    public List<Entry> entries = new List<Entry>();

    [Header("Background recording")]
    public AudioClip recording;            // tempo-synced to bpm; empty = no music, and the chart loops until game over
    public float firstChordSeconds;        // seconds into the recording where the first chord lands
}
