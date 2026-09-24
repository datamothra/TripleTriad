using UnityEngine;

namespace Triad
{
    // A struck-string piano model by additive synthesis: each note is six partials (the fundamental and five
    // overtones) with stiffness inharmonicity, each partial decaying exponentially at its own rate, plus a
    // hammer-noise transient. One voice per player, monophonic per voice.
    public class PianoModel
    {
        const int VoiceCount = 3;                     // one voice per player
        const int PartialCount = 6;                   // fundamental + 5 overtones
        const int NoteCount = VoiceCount * 2;         // two notes per voice: a retrigger releases the old note while the new one starts
        const float TwoPi = Mathf.PI * 2f;

        // ---- timbre --------------------------------------------------------------------------------------------
        static readonly float[] PartialLevels = { 1f, 0.55f, 0.32f, 0.2f, 0.12f, 0.07f };   // spectral rolloff, per partial
        const float BrightnessRolloffHz = 6000f;      // partials above this are attenuated: exp(-f / 6000)
        const float PartialDecaySpread = 0.45f;       // each higher partial decays (1 + n * this) times faster
        const float AttackSeconds = 0.004f;           // linear fade-in, so the hammer hit has no click
        const float PromptDecayRate = 5.5f;           // 1/s: the "prompt sound" fades into the "aftersound" over the first ~200 ms
        const float AftersoundLevel = 0.45f;          // level the note settles to once the prompt sound has faded
        const float HammerNoiseLevel = 0.12f;         // hammer-felt noise burst, scaled by velocity
        const float HammerNoiseDecayRate = 350f;      // 1/s: the burst is gone in a few milliseconds
        const float ReleaseRate = 150f;               // 1/s: a stolen note fades out in ~45 ms
        const float NoteGain = 0.7f;                  // headroom per note before the master soft clipper
        const float SilenceThreshold = 0.0008f;       // a note is freed once its partials' total amplitude drops below this
        const float NyquistGuard = 0.45f;             // partials above 0.45 * sampleRate are muted rather than aliased

        // one sounding note: partial oscillators and their envelopes
        class Note
        {
            public bool sounding;
            public float velocity;                    // 0 to 1
            public float ageSeconds;                  // since the strike, for the attack ramp
            public float promptDecay;                 // 1 at the strike, decays toward 0
            public float hammerNoise;                 // 1 at the strike, decays toward 0
            public readonly float[] phase = new float[PartialCount];              // 0 to 1 cycles
            public readonly float[] phaseIncrement = new float[PartialCount];     // cycles per sample
            public readonly float[] partialAmplitude = new float[PartialCount];   // current envelope level
            public readonly float[] partialDecayPerSample = new float[PartialCount];   // one-pole multiplier per sample
        }

        readonly Note[] notes = new Note[NoteCount];
        readonly int[] currentNote = new int[VoiceCount];   // which of a voice's two notes was struck last
        readonly float sampleRate;
        readonly float promptDecayPerSample, hammerNoiseDecayPerSample, releasePerSample;
        uint noiseState = 0x12345678;                 // xorshift32 PRNG for the hammer noise

        public float volume = 0.9f;                   // master level, before the soft clipper
        public float noteSeconds = 0.5f;              // time for the fundamental to decay by 60 dB

        public PianoModel(float sampleRate)
        {
            this.sampleRate = sampleRate;
            for (int i = 0; i < NoteCount; i++) notes[i] = new Note();
            for (int v = 0; v < VoiceCount; v++) currentNote[v] = v * 2;
            promptDecayPerSample = PerSample(PromptDecayRate);
            hammerNoiseDecayPerSample = PerSample(HammerNoiseDecayRate);
            releasePerSample = PerSample(ReleaseRate);
        }

        // the per-sample multiplier for an exponential decay of `rate` nepers per second
        float PerSample(float rate) => Mathf.Exp(-rate / sampleRate);

        // start a note on this voice; the voice's previous note, if still sounding, is released
        public void Strike(int voice, float frequencyHz, float velocity)
        {
            voice = Mathf.Clamp(voice, 0, VoiceCount - 1);
            int previous = currentNote[voice];
            int index = previous == voice * 2 ? voice * 2 + 1 : voice * 2;
            var old = notes[previous];
            if (old.sounding)
                for (int n = 0; n < PartialCount; n++) old.partialDecayPerSample[n] = releasePerSample;
            currentNote[voice] = index;

            var note = notes[index];
            note.velocity = Mathf.Clamp01(velocity);
            note.ageSeconds = 0f;
            note.promptDecay = 1f;
            note.hammerNoise = 1f;
            // string stiffness sharpens the overtones: partial k sits at k * f0 * sqrt(1 + B k^2). Bass strings are the least stiff
            float inharmonicity = frequencyHz < 130f ? 0.0002f : frequencyHz < 520f ? 0.0004f : 0.0008f;
            float decayRate = 6.9f / Mathf.Max(0.05f, noteSeconds);              // ln(1000) / seconds = -60 dB in noteSeconds
            float velocityBrightness = 0.5f + 0.5f * note.velocity;              // harder hits are brighter
            for (int n = 0; n < PartialCount; n++)
            {
                int k = n + 1;
                float partialHz = frequencyHz * k * Mathf.Sqrt(1f + inharmonicity * k * k);
                note.phaseIncrement[n] = partialHz < sampleRate * NyquistGuard ? partialHz / sampleRate : 0f;
                note.phase[n] = 0f;
                note.partialAmplitude[n] = PartialLevels[n] * Mathf.Exp(-partialHz / BrightnessRolloffHz) * velocityBrightness;
                note.partialDecayPerSample[n] = PerSample(decayRate * (1f + PartialDecaySpread * n));
            }
            note.sounding = true;
        }

        // fill an interleaved buffer; runs on the audio thread, so no allocation here
        public void Render(float[] buffer, int channels)
        {
            int frames = buffer.Length / channels;
            float secondsPerSample = 1f / sampleRate;
            for (int frame = 0; frame < frames; frame++)
            {
                float mix = 0f;
                for (int i = 0; i < NoteCount; i++)
                {
                    var note = notes[i];
                    if (!note.sounding) continue;
                    note.ageSeconds += secondsPerSample;
                    float attack = note.ageSeconds < AttackSeconds ? note.ageSeconds / AttackSeconds : 1f;
                    note.promptDecay *= promptDecayPerSample;

                    // the partials: sine oscillators, each stepping its own exponential envelope
                    float partials = 0f, totalAmplitude = 0f;
                    for (int n = 0; n < PartialCount; n++)
                    {
                        float phase = note.phase[n] + note.phaseIncrement[n];
                        if (phase >= 1f) phase -= 1f;
                        note.phase[n] = phase;
                        float amplitude = note.partialAmplitude[n] * note.partialDecayPerSample[n];
                        note.partialAmplitude[n] = amplitude;
                        totalAmplitude += amplitude;
                        partials += Mathf.Sin(phase * TwoPi) * amplitude;
                    }

                    // the hammer: a short burst of white noise at the strike
                    if (note.hammerNoise > 0.001f)
                    {
                        noiseState ^= noiseState << 13; noiseState ^= noiseState >> 17; noiseState ^= noiseState << 5;
                        float white = (noiseState & 0xFFFF) / 32768f - 1f;                 // -1 to 1
                        partials += white * note.hammerNoise * HammerNoiseLevel * note.velocity;
                        note.hammerNoise *= hammerNoiseDecayPerSample;
                    }

                    // two-stage decay: the prompt sound at full level settling into the quieter aftersound
                    float promptLevel = AftersoundLevel + (1f - AftersoundLevel) * note.promptDecay;
                    mix += partials * promptLevel * attack * note.velocity * NoteGain;
                    if (totalAmplitude < SilenceThreshold) note.sounding = false;
                }

                // master: level, then a soft clipper so three hard strikes can't overload
                mix *= volume;
                mix = mix / (1f + Mathf.Abs(mix) * 0.5f);
                for (int c = 0; c < channels; c++) buffer[frame * channels + c] = mix;
            }
        }
    }

    // the scene component: owns the model and renders it through an audio filter on its own AudioSource
    public class Synth : MonoBehaviour
    {
        [Tooltip("Seconds for a note to decay by 60 dB.")] public float noteSeconds = 0.5f;
        [Tooltip("Master level before the soft clipper.")] public float volume = 0.9f;
        PianoModel model;

        void Awake()
        {
            model = new PianoModel(AudioSettings.outputSampleRate);
            // a looping silent clip keeps the AudioSource playing, so OnAudioFilterRead is called for every buffer
            var source = gameObject.AddComponent<AudioSource>();
            source.clip = AudioClip.Create("silence", 4410, 1, 44100, false);
            source.loop = true;
            source.spatialBlend = 0f;
            source.Play();
        }

        // strike a MIDI note on a player's voice; equal temperament, A4 = 440 Hz
        public void Strike(int voice, int midi, float velocity)
        {
            if (model == null) model = new PianoModel(AudioSettings.outputSampleRate);   // a script reload in Play mode drops it
            model.noteSeconds = noteSeconds;
            model.volume = volume;
            model.Strike(voice, 440f * Mathf.Pow(2f, (midi - 69) / 12f), velocity);
        }

        // audio-thread callback: the buffer arrives holding the silent clip, and leaves holding the piano
        void OnAudioFilterRead(float[] buffer, int channels)
        {
            var m = model;
            if (m == null) { System.Array.Clear(buffer, 0, buffer.Length); return; }
            m.Render(buffer, channels);
        }
    }
}
