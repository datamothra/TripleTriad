using UnityEngine;

namespace Triad
{
// karplus strong "piano", seein what unity can do with software synthesis

//"monophonic", but technically three voices since this is the one synth that the players just trigger
    public class PianoEngine
    {
        const int Voices = 3, Partials = 6; // one voice for each player
        const float TwoPi = Mathf.PI * 2f;
        static readonly float[] PartialAmp = { 1f, 0.55f, 0.32f, 0.2f, 0.12f, 0.07f };

        
        const int Slots = Voices * 2; //still monophonic, but need an extra slot to work smoothly

        //each voice and extra slot needs its own thing

        //velocity, length, attack part of the decay, hammer noise
        readonly float[] vel = new float[Slots], age = new float[Slots], fast = new float[Slots], hammer = new float[Slots];
        readonly float[,] phase = new float[Slots, Partials], inc = new float[Slots, Partials], env = new float[Slots, Partials], envK = new float[Slots, Partials];
        readonly bool[] active = new bool[Slots];
        readonly int[] lastSlot = new int[Voices];
        readonly float sampleRate, fastK, hammerK;
        uint noise = 0x12345678; //exciter

        public float volume = 0.9f;
        public float noteSeconds = 0.5f;    // full decay time, weird and linear but whatever ill fix later, fast gets multiplied into this

        public PianoEngine(float sampleRate)
        {
            this.sampleRate = sampleRate;
            fastK = Mathf.Exp(-5.5f / sampleRate);
            hammerK = Mathf.Exp(-350f / sampleRate);
        }

        public void Strike(int voice, float f, float velocity)
        {
            voice = Mathf.Clamp(voice, 0, Voices - 1);
            int old = lastSlot[voice], slot = old == voice * 2 ? voice * 2 + 1 : voice * 2;
            if (active[old])
            {
                float choke = Mathf.Exp(-150f / sampleRate);    // the previous note is gone in ~45 ms, without a click
                for (int n = 0; n < Partials; n++) envK[old, n] = choke;
            }
            lastSlot[voice] = slot;

            vel[slot] = Mathf.Clamp01(velocity);
            age[slot] = 0f; fast[slot] = 1f; hammer[slot] = 1f;
            float B = f < 130f ? 0.0002f : f < 520f ? 0.0004f : 0.0008f;          // string stiffness
            float decay = 6.9f / Mathf.Max(0.05f, noteSeconds);                     // -60 dB after noteSeconds
            for (int n = 0; n < Partials; n++)
            {
                int k = n + 1;
                float fn = f * k * Mathf.Sqrt(1f + B * k * k);
                inc[slot, n] = fn < sampleRate * 0.45f ? fn / sampleRate : 0f;
                phase[slot, n] = 0f;
                env[slot, n] = PartialAmp[n] * Mathf.Exp(-fn / 6000f) * (0.5f + 0.5f * vel[slot]);
                envK[slot, n] = Mathf.Exp(-decay * (1f + 0.45f * n) / sampleRate);
            }
            active[slot] = true;
        }

        public void Render(float[] data, int channels)
        {
            int frames = data.Length / channels;
            float inv = 1f / sampleRate;
            for (int fi = 0; fi < frames; fi++)
            {
                float s = 0f;
                for (int i = 0; i < Slots; i++)
                {
                    if (!active[i]) continue;
                    age[i] += inv;
                    float attack = age[i] < 0.004f ? age[i] / 0.004f : 1f;
                    fast[i] *= fastK;
                    float sum = 0f, total = 0f;
                    for (int n = 0; n < Partials; n++)
                    {
                        float p = phase[i, n] + inc[i, n]; if (p >= 1f) p -= 1f;
                        phase[i, n] = p;
                        float e = env[i, n] * envK[i, n]; env[i, n] = e; total += e;
                        sum += Mathf.Sin(p * TwoPi) * e;
                    }
                    if (hammer[i] > 0.001f)
                    {
                        noise ^= noise << 13; noise ^= noise >> 17; noise ^= noise << 5;
                        sum += ((noise & 0xFFFF) / 32768f - 1f) * hammer[i] * 0.12f * vel[i];
                        hammer[i] *= hammerK;
                    }
                    s += sum * (0.45f + 0.55f * fast[i]) * attack * vel[i] * 0.7f;
                    if (total < 0.0008f) active[i] = false;
                }
                s *= volume;
                s = s / (1f + Mathf.Abs(s) * 0.5f);   // gentle soft clip
                for (int c = 0; c < channels; c++) data[fi * channels + c] = s;
            }
        }
    }

 
    public class Synth : MonoBehaviour
    {
        [Tooltip("Seconds from strike to silence.")] public float noteSeconds = 0.5f;
        [Tooltip("Master output level.")] public float volume = 0.9f;
        PianoEngine engine;

        void Awake()
        {
            engine = new PianoEngine(AudioSettings.outputSampleRate);
            var src = gameObject.AddComponent<AudioSource>();
            src.clip = AudioClip.Create("silence", 4410, 1, 44100, false);   // the filter below writes the real audio
            src.loop = true;
            src.spatialBlend = 0f;
            src.Play();
        }

        public void Strike(int voice, int midi, float velocity)
        {
            engine.noteSeconds = noteSeconds;
            engine.volume = volume;
            engine.Strike(voice, 440f * Mathf.Pow(2f, (midi - 69) / 12f), velocity);
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            var e = engine;
            if (e == null) { System.Array.Clear(data, 0, data.Length); return; }
            e.Render(data, channels);
        }
    }
}
