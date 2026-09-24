// note names, chord spellings and labels; Tones is what judging compares the players' notes against
public static class Chord
{
    public enum Note { C, Cs, D, Ds, E, F, Fs, G, Gs, A, As, B }
    public enum Quality { Major = 1, Minor = 2, Diminished = 3, Augmented = 4 }   // numbered so saved songs keep their values
    static readonly string[] noteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
    public static string NoteName(int note) => noteNames[(note % 12 + 12) % 12];

    public static int[] Tones(Note root, Quality q)
    {
        int third = q == Quality.Minor || q == Quality.Diminished ? 3 : 4;
        int fifth = q == Quality.Diminished ? 6 : q == Quality.Augmented ? 8 : 7;
        int r = (int)root;
        return new[] { r, (r + third) % 12, (r + fifth) % 12 };
    }

    public static string Label(Note root, Quality q) => NoteName((int)root) + " " + q.ToString().ToLower();
}
