using System;
using System.Collections.Generic;
using FlashEditor.Cache;
using FlashEditor.Definitions.Audio.Synth;
using FlashEditor.Definitions.Tracks;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions.Tracks
{
    /// <summary>
    ///     Renders real tracks through the cache's own instruments and holds the result to what can
    ///     be checked without listening.
    /// </summary>
    /// <remarks>
    ///     <b>None of this says the player sounds right.</b> It says the notes reached voices, the
    ///     voices reached the mix and the mix is a signal. The four ways a synthesiser sounds wrong
    ///     while passing every one of these - wrong pitch, wrong instrument, wrong envelope and a
    ///     stuck note - are all distinguishable by ear and by nothing here, which is what
    ///     <c>reference/track-player-listening-checklist.md</c> is for.
    ///     <para>
    ///     A handful of tracks rather than all 963, because rendering is real-time work: this is an
    ///     inner-loop check that the path holds together, not a sweep.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheTrackPlaybackTests : IClassFixture<RealCacheFixture>
    {
        /// <summary>How many seconds of each track to render.</summary>
        private const int SecondsRendered = 5;

        /// <summary>
        ///     The tracks to render.
        /// </summary>
        /// <remarks>
        ///     Group 0 is "Scape Main", the one index-6 group whose identity is settled on its own -
        ///     its name hash is <c>hash("scape main")</c> - so it is the one track that can be named
        ///     without going through the enum join this project has already got wrong once. The rest
        ///     are simply spread across the index so a defect confined to one arrangement does not
        ///     pass unseen.
        /// </remarks>
        private static readonly int[] Tracks = { 0, 1, 62, 100, 150, 321, 500, 700, 900, 962 };

        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheTrackPlaybackTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>
        ///     Every sampled track renders audible stereo, and the notes it plays reach real
        ///     instruments.
        /// </summary>
        /// <remarks>
        ///     The assertion that carries weight is the dropped-note count. A note is dropped only
        ///     when its patch or its sample cannot be resolved, so a bank derivation or a sample-id
        ///     shift that was wrong would show up here as most of the track going silent rather than
        ///     as anything subtler - and the notes that are legitimately dropped, the index-4 keys,
        ///     are counted separately so they cannot absorb a real regression.
        /// </remarks>
        [RealCacheFact]
        public void EverySampledTrack_RendersAudibleStereoThroughRealInstruments()
        {
            RSCache cache = _fixture.OpenCache();
            var failures = new List<string>();
            int rendered = 0;

            foreach (int trackId in Tracks)
            {
                JagStream? group = cache.ReadFile(RSConstants.MUSIC_INDEX, trackId, 0);
                if (group == null)
                {
                    failures.Add($"track {trackId}: index 6 holds no group");
                    continue;
                }

                var track = new Track { Id = trackId }.Decode(group);
                if (track.Midi == null || track.Midi.Length == 0)
                {
                    failures.Add($"track {trackId}: decoded but built no MIDI");
                    continue;
                }

                var sequence = new MidiSequence(track.Midi);
                var synthesiser = new MidiSynthesiser(new MidiSoundBank(cache));
                var renderer = new TrackRenderer(sequence, synthesiser);

                int frames = MidiSynthesiser.OutputRate * SecondsRendered;
                var buffer = new short[MidiSynthesiser.ControlTick * 2];

                long produced = 0;
                long nonZero = 0;
                int peak = 0;
                int peakVoices = 0;
                bool differsAcrossChannels = false;

                while (produced < frames)
                {
                    int chunk = renderer.Render(buffer, MidiSynthesiser.ControlTick);
                    if (chunk <= 0)
                        break;

                    for (int i = 0; i < chunk * 2; i += 2)
                    {
                        int left = buffer[i];
                        int right = buffer[i + 1];
                        if (left != 0 || right != 0)
                            nonZero++;
                        if (left != right)
                            differsAcrossChannels = true;

                        peak = Math.Max(peak, Math.Max(Math.Abs(left), Math.Abs(right)));
                    }

                    peakVoices = Math.Max(peakVoices, synthesiser.ActiveVoices);
                    produced += chunk;
                }

                rendered++;
                _output.WriteLine(
                    $"track {trackId}: {sequence.Events.Count} events, division {sequence.Division}, " +
                    $"{produced} frames rendered, {nonZero} audible, peak {peak}, peak voices {peakVoices}, " +
                    $"{synthesiser.DroppedNotes} notes dropped, " +
                    $"{synthesiser.Bank.UnrenderedEffectKeys} index-4 lookups, " +
                    $"{synthesiser.Bank.FailedLookups} failed lookups, stereo {differsAcrossChannels}, " +
                    $"{synthesiser.PatchesSounded.Count} patches sounded, " +
                    $"{synthesiser.MuteGroupNotes} mute-group notes, {synthesiser.HeldNotes} held notes, " +
                    $"name '{track.Name}'");

                if (produced <= 0)
                    failures.Add($"track {trackId}: rendered nothing");
                if (nonZero * 4 < produced)
                    failures.Add($"track {trackId}: only {nonZero} of {produced} frames are audible");
                if (peak == 0)
                    failures.Add($"track {trackId}: the mix never left zero");
                if (peakVoices == 0)
                    failures.Add($"track {trackId}: no voice ever started");
                if (synthesiser.Bank.FailedLookups > 0)
                    failures.Add($"track {trackId}: {synthesiser.Bank.FailedLookups} patch or sample lookups failed");
            }

            Assert.True(rendered == Tracks.Length, $"{rendered} of {Tracks.Length} tracks rendered");
            Assert.True(failures.Count == 0, string.Join("\n  ", failures));
        }
    }
}
