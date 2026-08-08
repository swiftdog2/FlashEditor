using System;
using System.Collections.Generic;

namespace FlashEditor.Definitions.Audio.Synth {
    /// <summary>
    ///     The client's MIDI synthesiser: sixteen channels of state, a voice per sounding note, and
    ///     a stereo mix at 22050 Hz.
    /// </summary>
    /// <remarks>
    ///     Transcribed from <c>Node_Sub31_Sub2</c>, with the mix loop from <c>Class268</c> and the
    ///     voice kernel from <c>Node_Sub31_Sub5</c>. Every constant below cites the line it came
    ///     from, because the arithmetic is what makes this the game's synthesiser rather than a
    ///     general MIDI one: the same notes through a General MIDI device play the right pitches on
    ///     entirely the wrong instruments, which is exactly what this exists to replace.
    ///     <para>
    ///     <b>What is deliberately not reproduced</b>, each because it is a client concern rather
    ///     than a sound one, or because the data it needs is not decoded here:
    ///     <list type="bullet">
    ///     <item>
    ///     <b>Index-4 samples.</b> Bank 0 keys are silent. 14 of the patch bank's 21,491 keys.
    ///     </item>
    ///     <item>
    ///     <b>Voice stealing.</b> The client caps the mix at 32 streams by a priority pass
    ///     (<c>Class268.java:224-347</c>); this mixes every voice. The editor is not competing with
    ///     a game for a CPU, and the audible difference is that a dense passage keeps quiet notes
    ///     the client would have dropped.
    ///     </item>
    ///     <item>
    ///     <b>Portamento and the CC81 re-trigger.</b> Both are channel modes no music track in
    ///     either cache has been observed to use, and both change note-on behaviour rather than
    ///     timbre.
    ///     </item>
    ///     <item>
    ///     <b>Aftertouch.</b> The client decodes and discards it (<c>Node_Sub31_Sub2.java:674-683</c>,
    ///     <c>:1461-1469</c>), so ignoring it matches.
    ///     </item>
    ///     </list>
    ///     </para>
    /// </remarks>
    public sealed class MidiSynthesiser {
        /// <summary>The client's output rate, and therefore the rate every step is computed against.</summary>
        /// <remarks><c>Class233.java:25</c> configures the device at 22050 Hz.</remarks>
        public const int OutputRate = 22050;

        /// <summary>MIDI channels.</summary>
        public const int Channels = 16;

        /// <summary>
        ///     Frames between control updates: envelopes, pitch and gain all move at this rate.
        /// </summary>
        /// <remarks>
        ///     <c>Node_Sub31_Sub2.java:687</c>: <c>outputRate / 100</c>, so ten milliseconds. It is
        ///     also the ramp length the client asks for when it pushes a new gain into a stream,
        ///     which is why a voice interpolates its gain across exactly this many frames.
        /// </remarks>
        public const int ControlTick = OutputRate / 100;

        /// <summary>
        ///     Envelope clock units per control tick at unity rate.
        /// </summary>
        /// <remarks>
        ///     <c>Node_Sub31_Sub2.java:732</c>. Breakpoint times are compared against the clock
        ///     shifted left by 8, so one stored time unit is two control ticks, or 20 milliseconds.
        /// </remarks>
        private const int EnvelopeStep = 128;

        /// <summary>Semitone divisions the pitch offset is stated in.</summary>
        /// <remarks><c>Node_Sub31_Sub2.java:1096</c> uses <c>1/3072</c>, which is <c>1/(256 * 12)</c>.</remarks>
        private const double PitchDivisions = 3072.0;

        /// <summary>Scale from a note's distance above middle C to an envelope rate exponent.</summary>
        /// <remarks><c>Node_Sub31_Sub2.java:726</c>: <c>1/196608</c>, which is <c>1/(256 * 768)</c>.</remarks>
        private const double KeyScale = 1.0 / 196608.0;

        private readonly MidiSoundBank bank;
        private readonly List<SynthVoice> voices = new List<SynthVoice>();
        private readonly SynthVoice?[,] keyVoices = new SynthVoice?[Channels, 128];
        private readonly SynthVoice?[,] muteGroupVoices = new SynthVoice?[Channels, 128];

        private readonly int[] program = new int[Channels];
        private readonly int[] bankSelect = new int[Channels];
        private readonly int[] volume = new int[Channels];
        private readonly int[] expression = new int[Channels];
        private readonly int[] pan = new int[Channels];
        private readonly int[] pitchBend = new int[Channels];
        private readonly int[] bendRange = new int[Channels];
        private readonly int[] modulation = new int[Channels];
        private readonly int[] registeredParameter = new int[Channels];
        private readonly int[] modeBits = new int[Channels];
        private readonly int[] channelMix = new int[Channels];
        private int tickAccumulator;
        private readonly HashSet<int> patchesSounded = new HashSet<int>();

        /// <summary>Overall gain, applied to every voice.</summary>
        /// <remarks><c>Node_Sub31_Sub2.anInt5836</c>, 256 being unity (<c>:218</c>).</remarks>
        public int MasterVolume { get; set; } = 256;

        /// <summary>How many voices are currently sounding.</summary>
        public int ActiveVoices => voices.Count;

        /// <summary>How many notes were dropped for want of a sample this player can render.</summary>
        public int DroppedNotes { get; private set; }

        /// <summary>
        ///     Every index-15 patch a note has actually sounded on.
        /// </summary>
        /// <remarks>
        ///     Which instruments a track reaches is what decides whether it is a useful case for
        ///     judging the player by ear - a track that only touches four patches isolates far less
        ///     than one that touches thirty. Recorded on note-on rather than on program change, so a
        ///     patch selected and never played does not appear.
        /// </remarks>
        public IReadOnlyCollection<int> PatchesSounded => patchesSounded;

        /// <summary>How many notes used a mute group, which is the drum-cutting behaviour.</summary>
        public int MuteGroupNotes { get; private set; }

        /// <summary>How many notes named a key whose sample loops for as long as the note is held.</summary>
        public int HeldNotes { get; private set; }

        /// <summary>The bank the voices draw their patches and samples from.</summary>
        public MidiSoundBank Bank => bank;

        /// <summary>
        ///     The index-15 patch a channel currently selects.
        /// </summary>
        /// <remarks>
        ///     Exposed because the derivation - the two bank-select controllers combined with the
        ///     program number - is the one piece of the synthesiser that is checkable without
        ///     hearing anything, and getting it wrong plays every note on the wrong instrument while
        ///     leaving the pitches and the timing right. See <see cref="ProgramChange"/>.
        /// </remarks>
        /// <param name="channel">The MIDI channel.</param>
        /// <returns>The patch id.</returns>
        public int PatchIdOf(int channel) {
            if (channel < 0 || channel >= Channels)
                throw new ArgumentOutOfRangeException(nameof(channel), channel, "MIDI has 16 channels.");

            return program[channel];
        }

        /// <summary>Builds a synthesiser over a cache's three audio indexes.</summary>
        /// <param name="bank">The patch and sample bank.</param>
        /// <exception cref="ArgumentNullException">The bank is null.</exception>
        public MidiSynthesiser(MidiSoundBank bank) {
            this.bank = bank ?? throw new ArgumentNullException(nameof(bank));
            ResetControllers();

            for (int channel = 0; channel < Channels; channel++)
                channelMix[channel] = 256;

            /* Channel 9 defaults to patch 128, the first drum kit, before any program change
               arrives. Class111_Sub1.java:31 does this at construction and Node_Sub7.java:318
               relies on it when it pre-scans a song for the patches it needs, so a track that never
               sends a program change on channel 9 still has drums. */
            program[9] = 128;
        }

        /// <summary>Restores every channel controller to the value the client resets it to.</summary>
        /// <remarks><c>Node_Sub31_Sub2.method1342</c> (:843-867).</remarks>
        public void ResetControllers() {
            for (int channel = 0; channel < Channels; channel++) {
                volume[channel] = 12800;
                pan[channel] = 8192;
                expression[channel] = 16383;
                pitchBend[channel] = 8192;
                modulation[channel] = 0;
                modeBits[channel] = 0;
                registeredParameter[channel] = 32767;
                bendRange[channel] = 256;
            }
        }

        /// <summary>Stops every voice at once.</summary>
        public void AllSoundOff() {
            voices.Clear();
            Array.Clear(keyVoices, 0, keyVoices.Length);
            Array.Clear(muteGroupVoices, 0, muteGroupVoices.Length);
        }

        // ===================================================================
        //  MIDI events
        // ===================================================================

        /// <summary>
        ///     Applies one MIDI channel message.
        /// </summary>
        /// <param name="status">The status byte.</param>
        /// <param name="data1">The first data byte.</param>
        /// <param name="data2">The second data byte, ignored where the message has only one.</param>
        public void Send(int status, int data1, int data2) {
            int channel = status & 0xf;
            switch (status & 0xf0) {
                case 0x80:
                    NoteOff(channel, data1 & 0x7f);
                    break;
                case 0x90:
                    if ((data2 & 0x7f) == 0)
                        NoteOff(channel, data1 & 0x7f);
                    else
                        NoteOn(channel, data1 & 0x7f, data2 & 0x7f);
                    break;
                case 0xb0:
                    Controller(channel, data1 & 0x7f, data2 & 0x7f);
                    break;
                case 0xc0:
                    ProgramChange(channel, data1 & 0x7f);
                    break;
                case 0xe0:
                    //Two seven-bit halves, least significant first (Node_Sub31_Sub2.java:657-661).
                    pitchBend[channel] = (data1 & 0x7f) | ((data2 & 0x7f) << 7);
                    break;
                default:
                    //Aftertouch and everything else the client decodes and discards.
                    break;
            }
        }

        /// <summary>
        ///     Selects a patch, combining the two bank-select controllers with the program number.
        /// </summary>
        /// <remarks>
        ///     <c>Node_Sub31_Sub2.java:647-651</c>:
        ///     <c>patch = (bankMSB &lt;&lt; 14) | (bankLSB &lt;&lt; 7) | program</c>. That is the whole
        ///     derivation - the patch id is the index-15 group id outright, with no table in between,
        ///     which is why the drum kits sit at 128 and above rather than being selected by channel.
        /// </remarks>
        /// <param name="channel">The channel.</param>
        /// <param name="value">The program number.</param>
        private void ProgramChange(int channel, int value) {
            int selected = bankSelect[channel] + value;
            if (selected == program[channel])
                return;

            program[channel] = selected;

            //A patch change invalidates the channel's mute groups, because the groups are the new
            //patch's rather than the old one's (Node_Sub31_Sub2.java:1035-1037).
            for (int group = 0; group < 128; group++)
                muteGroupVoices[channel, group] = null;
        }

        /// <summary>Applies a control change.</summary>
        /// <remarks>The map is <c>Node_Sub31_Sub2.method1337</c> (:508-646).</remarks>
        /// <param name="channel">The channel.</param>
        /// <param name="controller">The controller number.</param>
        /// <param name="value">Its seven-bit value.</param>
        private void Controller(int channel, int controller, int value) {
            switch (controller) {
                case 0:
                    /* Clears bits 14 to 20 and nothing else (Node_Sub31_Sub2.java:513-515 masks
                       with 0xffe03fff), so the bank LSB set by controller 32 survives an MSB
                       arriving after it. Clearing the low bits here instead loses the LSB, which
                       plays the right program from the wrong bank. */
                    bankSelect[channel] = (value << 14) + (bankSelect[channel] & unchecked((int) 0xffe03fff));
                    break;
                case 32:
                    bankSelect[channel] = (value << 7) + (bankSelect[channel] & ~0x3f80);
                    break;
                case 1:
                    modulation[channel] = (value << 7) + (modulation[channel] & 0x7f);
                    break;
                case 33:
                    modulation[channel] = value + (modulation[channel] & ~0x7f);
                    break;
                case 6:
                    if (registeredParameter[channel] == 16384)
                        bendRange[channel] = (value << 7) + (bendRange[channel] & 0x7f);
                    break;
                case 38:
                    if (registeredParameter[channel] == 16384)
                        bendRange[channel] = value + (bendRange[channel] & ~0x7f);
                    break;
                case 7:
                    volume[channel] = (value << 7) + (volume[channel] & 0x7f);
                    break;
                case 39:
                    volume[channel] = value + (volume[channel] & ~0x7f);
                    break;
                case 10:
                    pan[channel] = (value << 7) + (pan[channel] & 0x7f);
                    break;
                case 42:
                    pan[channel] = value + (pan[channel] & ~0x7f);
                    break;
                case 11:
                    expression[channel] = (value << 7) + (expression[channel] & 0x7f);
                    break;
                case 43:
                    expression[channel] = value + (expression[channel] & ~0x7f);
                    break;
                case 64:
                    //Sustain: while it is down the release envelope simply does not advance.
                    if (value < 64)
                        modeBits[channel] &= ~1;
                    else
                        modeBits[channel] |= 1;
                    break;
                case 100:
                    registeredParameter[channel] = value + 16384 + (registeredParameter[channel] & ~0x7f);
                    break;
                case 101:
                    registeredParameter[channel] = (value << 7) + 16384 + (registeredParameter[channel] & 0x7f);
                    break;
                case 120:
                    AllSoundOff();
                    break;
                case 121:
                    ResetControllers();
                    break;
                case 123:
                    AllNotesOff(channel);
                    break;
                default:
                    break;
            }
        }

        /// <summary>Releases every held key on a channel.</summary>
        /// <param name="channel">The channel.</param>
        private void AllNotesOff(int channel) {
            for (int note = 0; note < 128; note++)
                NoteOff(channel, note);
        }

        /// <summary>
        ///     Starts a note, if the channel's patch has a sample for that key.
        /// </summary>
        /// <remarks><c>Node_Sub31_Sub2.method1346</c> (:936-1020).</remarks>
        /// <param name="channel">The channel.</param>
        /// <param name="note">The key.</param>
        /// <param name="velocity">The velocity, 1..127.</param>
        private void NoteOn(int channel, int note, int velocity) {
            MidiPatchDefinition? patch = bank.Patch(program[channel]);
            if (patch == null) {
                DroppedNotes++;
                return;
            }

            MidiSampleBank? keyBank = patch.BankOf(note);
            if (keyBank == null)
                return;                                     //A key with no sample is silence, not a fault.

            PcmSample? sample = bank.Sample(keyBank.Value, patch.SampleIdOf(note));
            if (sample == null) {
                DroppedNotes++;
                return;
            }

            int keyVolume = unchecked((sbyte) patch.VolumeOf(note));
            int baseGain = (1024 + patch.PatchVolume * velocity * (velocity * keyVolume)) >> 11;

            int envelopeIndex = patch.EnvelopeOf(note);
            MidiPatchEnvelope? envelope = envelopeIndex >= 0 && envelopeIndex < patch.Envelopes.Count
                ? patch.Envelopes[envelopeIndex]
                : null;

            var voice = new SynthVoice(sample, patch, envelope, channel, note, patch.MuteGroupOf(note), baseGain,
                patch.PanOf(note) & 0xff, (note << 8) - (patch.TuningOf(note) & 0x7fff)) {
                Looping = patch.HeldOf(note)
            };

            //A key's mute group holds at most one sounding voice per channel: starting a second cuts
            //the first, which is how a hi-hat closes an open hi-hat.
            if (voice.MuteGroup >= 0 && voice.MuteGroup < 128) {
                SynthVoice? occupant = muteGroupVoices[channel, voice.MuteGroup];
                if (occupant != null && occupant.Held) {
                    keyVoices[channel, occupant.Note] = null;
                    occupant.ReleaseClock = 0;
                }

                muteGroupVoices[channel, voice.MuteGroup] = voice;
            }

            SynthVoice? previous = keyVoices[channel, note];
            if (previous != null && previous.Held)
                previous.ReleaseClock = 0;

            patchesSounded.Add(program[channel]);
            if (voice.MuteGroup >= 0)
                MuteGroupNotes++;
            if (voice.Looping)
                HeldNotes++;

            keyVoices[channel, note] = voice;
            UpdateVoice(voice);
            voice.LeftGain = voice.TargetLeftGain;
            voice.RightGain = voice.TargetRightGain;
            voices.Add(voice);
        }

        /// <summary>Releases a note, which starts its release envelope.</summary>
        /// <remarks><c>Node_Sub31_Sub2.method1353</c> (:1176-1206).</remarks>
        /// <param name="channel">The channel.</param>
        /// <param name="note">The key.</param>
        private void NoteOff(int channel, int note) {
            SynthVoice? voice = keyVoices[channel, note];
            if (voice == null)
                return;

            keyVoices[channel, note] = null;
            if (voice.ReleaseClock < 0)
                voice.ReleaseClock = 0;
        }

        // ===================================================================
        //  Rendering
        // ===================================================================

        /// <summary>
        ///     Mixes audio, running a control update every <see cref="ControlTick"/> frames.
        /// </summary>
        /// <remarks>
        ///     The order is the client's: <c>Node_Sub31_Sub1.method1325</c> (:77-103) mixes a tick
        ///     and then calls the control update, so the gains a tick is mixed with are the ones the
        ///     previous update computed.
        ///     <para>
        ///     A caller may ask for fewer frames than a control tick, which is how the sequencer
        ///     splits a block at an event boundary (<c>Node_Sub31_Sub2.java:325-328</c>) instead of
        ///     quantising every event to a ten millisecond grid. The control clock accumulates
        ///     across those calls so envelopes still advance at exactly 100 Hz.
        ///     </para>
        /// </remarks>
        /// <param name="mix">The interleaved stereo accumulator, at least <paramref name="frames"/> frames.</param>
        /// <param name="frames">How many frames to render, at most <see cref="ControlTick"/>.</param>
        public void Render(int[] mix, int frames) {
            if (mix == null)
                throw new ArgumentNullException(nameof(mix));

            for (int i = 0; i < frames * 2; i++)
                mix[i] = 0;

            tickAccumulator += frames;
            bool control = tickAccumulator >= ControlTick;
            if (control)
                tickAccumulator -= ControlTick;

            for (int i = voices.Count - 1; i >= 0; i--) {
                SynthVoice voice = voices[i];
                voice.Mix(mix, 0, frames);
                if (control)
                    Tick(voice);

                if (!voice.Finished)
                    continue;

                if (keyVoices[voice.Channel, voice.Note] == voice)
                    keyVoices[voice.Channel, voice.Note] = null;
                if (voice.MuteGroup >= 0 && voice.MuteGroup < 128 &&
                    muteGroupVoices[voice.Channel, voice.MuteGroup] == voice)
                    muteGroupVoices[voice.Channel, voice.MuteGroup] = null;

                voices.RemoveAt(i);
            }
        }

        /// <summary>
        ///     Advances one voice's envelope clocks by a control tick and recomputes its gain, pan
        ///     and pitch.
        /// </summary>
        /// <remarks><c>Node_Sub31_Sub2.method1340</c> (:685-821).</remarks>
        /// <param name="voice">The voice.</param>
        private void Tick(SynthVoice voice) {
            voice.Ticks++;

            MidiPatchEnvelope? envelope = voice.Envelope;
            if (envelope != null) {
                voice.LfoPhase += envelope.VibratoRate;

                double scale = ((voice.Note - 60) << 8) * KeyScale;

                if (envelope.Decay > 0) {
                    voice.DecayClock += envelope.DecayRate <= 0
                        ? EnvelopeStep
                        : (int) (0.5 + EnvelopeStep * Math.Pow(2.0, envelope.DecayRate * scale));

                    //51200 * 16: the point at which the exponential has run to nothing.
                    if (envelope.Decay * voice.DecayClock >= 819200)
                        voice.Finished = true;
                }

                byte[] attack = AttackChain(envelope);
                if (attack.Length > 0) {
                    voice.AttackClock += envelope.AttackRate <= 0
                        ? EnvelopeStep
                        : (int) (0.5 + Math.Pow(2.0, scale * envelope.AttackRate) * EnvelopeStep);

                    while (voice.AttackIndex < attack.Length - 2 &&
                           voice.AttackClock > (attack[voice.AttackIndex + 2] << 8))
                        voice.AttackIndex += 2;

                    if (voice.AttackIndex == attack.Length - 2 &&
                        unchecked((sbyte) attack[voice.AttackIndex + 1]) == 0)
                        voice.Finished = true;
                }

                byte[] release = ReleaseChain(envelope);
                bool sustained = (modeBits[voice.Channel] & 1) != 0;
                bool ownsGroup = voice.MuteGroup >= 0 && voice.MuteGroup < 128 &&
                                 muteGroupVoices[voice.Channel, voice.MuteGroup] == voice;

                if (voice.ReleaseClock >= 0 && release.Length > 0 && !sustained && !ownsGroup) {
                    voice.ReleaseClock += envelope.ReleaseRate <= 0
                        ? EnvelopeStep
                        : (int) (EnvelopeStep * Math.Pow(2.0, scale * envelope.ReleaseRate) + 0.5);

                    while (voice.ReleaseIndex < release.Length - 2 &&
                           (release[voice.ReleaseIndex + 2] << 8) < voice.ReleaseClock)
                        voice.ReleaseIndex += 2;

                    if (voice.ReleaseIndex == release.Length - 2)
                        voice.Finished = true;
                }
            } else if (voice.ReleaseClock >= 0) {
                /* A key with no envelope has nothing to end it, so a released one would ring for
                   the whole sample. The client cannot reach this - every key that names a sample
                   names an envelope too - and stopping it here is a guard rather than a rule. */
                voice.Finished = true;
            }

            UpdateVoice(voice);
        }

        /// <summary>Recomputes a voice's target gains and its playback step.</summary>
        /// <param name="voice">The voice.</param>
        private void UpdateVoice(SynthVoice voice) {
            int gain = Gain(voice);
            int position = PanOf(voice);

            //Constant power: unity at centre, sqrt(2) at either extreme
            //(Node_Sub31_Sub5.java:159-164 and :466-471).
            voice.TargetLeftGain = (int) (gain * Math.Sqrt((16384 - position) / 8192.0) + 0.5);
            voice.TargetRightGain = (int) (gain * Math.Sqrt(position / 8192.0) + 0.5);
            voice.Step = Step(voice);
        }

        /// <summary>
        ///     A voice's gain, from the channel controllers, the velocity term and both envelopes.
        /// </summary>
        /// <remarks>
        ///     <c>Node_Sub31_Sub2.method1334</c> (:401-460), in the same order and with the same
        ///     rounding, because each stage is an integer shift and reordering them changes the
        ///     result. The volume and expression product is squared, which is what makes a fade
        ///     sound like a fade rather than a linear ramp.
        /// </remarks>
        /// <param name="voice">The voice.</param>
        /// <returns>The gain in 6-bit fixed point, 64 being unity.</returns>
        private int Gain(SynthVoice voice) {
            int channel = voice.Channel;
            if (channelMix[channel] == 0)
                return 0;

            int gain = (volume[channel] * expression[channel] + 4096) >> 13;
            gain = (16384 + gain * gain) >> 15;
            gain = (16384 + voice.BaseGain * gain) >> 15;
            gain = (MasterVolume * gain + 128) >> 8;
            gain = (128 + gain * channelMix[channel]) >> 8;

            MidiPatchEnvelope? envelope = voice.Envelope;
            if (envelope == null)
                return gain;

            if (envelope.Decay > 0)
                gain = (int) (0.5 + Math.Pow(0.5, voice.DecayClock * 1.953125E-5 * envelope.Decay) * gain);

            byte[] attack = AttackChain(envelope);
            if (attack.Length > 0)
                gain = (Interpolate(attack, voice.AttackIndex, voice.AttackClock) * gain + 32) >> 6;

            byte[] release = ReleaseChain(envelope);
            if (voice.ReleaseClock > 0 && release.Length > 0)
                gain = (32 + Interpolate(release, voice.ReleaseIndex, voice.ReleaseClock) * gain) >> 6;

            return gain;
        }

        /// <summary>
        ///     The level of a breakpoint chain at a clock position, linearly interpolated.
        /// </summary>
        /// <remarks>
        ///     <c>Node_Sub31_Sub2.java:421-434</c>. Times are read unsigned and levels signed, which
        ///     is why they cannot share a reader: a level above 63 is a negative gain and a time
        ///     above 127 is not.
        /// </remarks>
        /// <param name="chain">The chain, alternating time and level.</param>
        /// <param name="index">The pair the clock currently sits in.</param>
        /// <param name="clock">The clock.</param>
        /// <returns>The interpolated level, 64 being unity.</returns>
        private static int Interpolate(byte[] chain, int index, int clock) {
            int level = unchecked((sbyte) chain[index + 1]);
            if (index >= chain.Length - 2)
                return level;

            int from = chain[index] << 8;
            int to = chain[index + 2] << 8;
            if (to == from)
                return level;

            int next = unchecked((sbyte) chain[index + 3]);
            return level + (next - level) * (clock - from) / (to - from);
        }

        /// <summary>
        ///     A voice's playback step in 8.8 fixed point, from its pitch offset, the channel's bend
        ///     and its vibrato.
        /// </summary>
        /// <remarks>
        ///     <c>Node_Sub31_Sub2.method1350</c> (:1070-1107). The exponent divides by 3072, which
        ///     settles the pitch offset's unit as a 256th of a semitone; a step is clamped to at
        ///     least 1 so a voice can never stall.
        /// </remarks>
        /// <param name="voice">The voice.</param>
        /// <returns>The step, negative if the voice is running backwards through a ping-pong loop.</returns>
        private int Step(SynthVoice voice) {
            int channel = voice.Channel;
            int offset = voice.PitchOffset;
            offset += ((pitchBend[channel] - 8192) * bendRange[channel]) >> 12;

            MidiPatchEnvelope? envelope = voice.Envelope;
            if (envelope != null && envelope.VibratoRate > 0 &&
                (envelope.VibratoDepth > 0 || modulation[channel] > 0)) {
                int depth = Math.Max(0, envelope.VibratoDepth) << 2;
                int ramp = Math.Max(0, envelope.VibratoDelay) << 1;
                if (ramp > 0 && voice.Ticks < ramp)
                    depth = voice.Ticks * depth / ramp;

                depth += modulation[channel] >> 7;

                //A 512-step oscillator: 2 * pi / 512.
                offset += (int) (depth * Math.Sin(0.01227184630308513 * (voice.LfoPhase & 0x1ff)));
            }

            int step = (int) (voice.Sample.SampleRate * 256.0 * Math.Pow(2.0, offset / PitchDivisions) / OutputRate
                              + 0.5);
            if (step < 1)
                step = 1;

            return voice.Step < 0 ? -step : step;
        }

        /// <summary>
        ///     Where a voice sits in the stereo field, 0 hard left to 16384 hard right.
        /// </summary>
        /// <remarks>
        ///     <c>Node_Sub31_Sub2.method1351</c> (:1109-1123). The channel's pan does not replace the
        ///     key's, it bends it: a channel panned hard left collapses every key's own position
        ///     towards the left rather than overriding it.
        /// </remarks>
        /// <param name="voice">The voice.</param>
        /// <returns>The pan position.</returns>
        private int PanOf(SynthVoice voice) {
            int channelPan = pan[voice.Channel];
            if (channelPan < 8192)
                return (voice.KeyPan * channelPan + 32) >> 6;

            return 16384 - ((32 + (128 - voice.KeyPan) * (16384 - channelPan)) >> 6);
        }

        // ===================================================================
        //  Envelope chains
        // ===================================================================

        /// <summary>
        ///     Rebuilds the attack envelope as the interleaved time and level array the client walks.
        /// </summary>
        /// <remarks>
        ///     The codec keeps levels and time deltas apart, which is the right shape for an editor
        ///     and the wrong one for the interpolator: the client indexes one array by pairs
        ///     (<c>Node_Sub44.java:293-295</c> for the levels, <c>:327-332</c> for the times) and
        ///     the times are a running total of <c>1 + delta</c> stored back through a byte, so they
        ///     wrap rather than saturate. Rebuilding it here keeps the wrap.
        /// </remarks>
        /// <param name="envelope">The envelope.</param>
        /// <returns>The chain, alternating time and level.</returns>
        internal static byte[] AttackChain(MidiPatchEnvelope envelope) {
            int points = envelope.AttackLevels.Length;
            if (points == 0)
                return Array.Empty<byte>();

            var chain = new byte[points * 2];
            int time = 0;
            for (int point = 0; point < points; point++) {
                if (point > 0) {
                    time += 1 + envelope.AttackTimeDeltas[point - 1];
                    chain[point * 2] = (byte) time;
                }

                chain[point * 2 + 1] = unchecked((byte) envelope.AttackLevels[point]);
            }

            return chain;
        }

        /// <summary>
        ///     Rebuilds the release envelope the same way.
        /// </summary>
        /// <remarks>
        ///     Two entries are not stored and have to be put back: the first level is a fixed 64
        ///     (<c>Node_Sub44.java:178</c>) and the last is left at zero, so a release always starts
        ///     at unity and ends in silence. A chain that omitted either would end a note at the
        ///     wrong level, which is audible as a click.
        /// </remarks>
        /// <param name="envelope">The envelope.</param>
        /// <returns>The chain, alternating time and level.</returns>
        internal static byte[] ReleaseChain(MidiPatchEnvelope envelope) {
            int points = envelope.ReleaseTimeDeltas.Length;
            if (points == 0)
                return Array.Empty<byte>();

            var chain = new byte[points * 2 + 2];
            chain[1] = 64;

            int time = 0;
            for (int point = 0; point < points; point++) {
                time += 1 + envelope.ReleaseTimeDeltas[point];
                chain[point * 2 + 2] = (byte) time;

                if (point < envelope.ReleaseLevels.Length)
                    chain[point * 2 + 3] = unchecked((byte) envelope.ReleaseLevels[point]);
            }

            return chain;
        }
    }
}
