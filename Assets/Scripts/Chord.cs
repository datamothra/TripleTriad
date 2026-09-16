// chord names and stuff, the chord values dont really matter that much in the curent form of the game
public static class Chord
{
    public enum Note { C, Cs, D, Ds, E, F, Fs, G, Gs, A, As, B }
    public enum Quality { Major = 1, Minor = 2, Diminished = 3, Augmented = 4 }   // numbered so saved songs keep their values
    public static readonly string[] Names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    public static int[] Tones(Note root, Quality q)
    {
        int third = q == Quality.Minor || q == Quality.Diminished ? 3 : 4;
        int fifth = q == Quality.Diminished ? 6 : q == Quality.Augmented ? 8 : 7;
        int r = (int)root;
        return new[] { r, (r + third) % 12, (r + fifth) % 12 };
    }

    public static string Label(Note root, Quality q) => Names[(int)root] + " " + q.ToString().ToLower();
}
