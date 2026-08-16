using System;
using System.Collections.Generic;
using FlashEditor.Cache;
using FlashEditor.Definitions.Audio;
using FlashEditor.Definitions.Models;

namespace FlashEditor.Export {
    /// <summary>
    ///     What one model contributes to the export: where it lives, how big it is, and every id its
    ///     footer names.
    /// </summary>
    /// <remarks>
    ///     <b>Geometry is deliberately absent.</b> A model's vertices and faces are an asset rather
    ///     than data anyone queries, and index 7 holds more than sixty thousand of them; writing them
    ///     would multiply the export's size for content the 3D viewer and the OBJ exporter already
    ///     serve. What is kept is what nothing else states - the emitters, effectors and billboards
    ///     the footer attaches, which is the only route to "which models attach billboard 17".
    /// </remarks>
    public sealed class ModelReferenceRecord {
        /// <summary>Builds the record from a decoded model.</summary>
        /// <param name="groupId">The group, which is the model id on this index.</param>
        /// <param name="fileId">The file within the group.</param>
        /// <param name="model">The decoded model, whose geometry is not retained.</param>
        public ModelReferenceRecord(int groupId, int fileId, ModelFile model) {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            GroupId = groupId;
            FileId = fileId;
            Encoding = model.Encoding.ToString();
            VertexCount = model.VertexCount;
            FaceCount = model.FaceCount;
            TexturedFaceCount = model.TexturedFaceCount;

            var emitters = new List<int>();
            var emitterFaces = new List<int>();
            if (model.Emitters != null)
                foreach (ModelParticleEmitter emitter in model.Emitters) {
                    emitters.Add(emitter.EmitterId);
                    emitterFaces.Add(emitter.FaceIndex);
                }

            var effectors = new List<int>();
            var effectorVertices = new List<int>();
            if (model.Effectors != null)
                foreach (ModelParticleEffector effector in model.Effectors) {
                    effectors.Add(effector.EffectorId);
                    effectorVertices.Add(effector.VertexIndex);
                }

            var billboards = new List<int>();
            var billboardFaces = new List<int>();
            if (model.Bonds != null)
                foreach (ModelBond bond in model.Bonds) {
                    billboards.Add(bond.BillboardId);
                    billboardFaces.Add(bond.FaceIndex);
                }

            EmitterIds = emitters;
            EmitterFaces = emitterFaces;
            EffectorIds = effectors;
            EffectorVertices = effectorVertices;
            BillboardIds = billboards;
            BillboardFaces = billboardFaces;
        }

        /// <summary>The group, which is the model id.</summary>
        public int GroupId { get; }

        /// <summary>The file within the group.</summary>
        public int FileId { get; }

        /// <summary>Which of the three encoders wrote this model.</summary>
        public string Encoding { get; }

        /// <summary>How many vertices it holds.</summary>
        public int VertexCount { get; }

        /// <summary>How many faces it holds.</summary>
        public int FaceCount { get; }

        /// <summary>How many textured faces it holds.</summary>
        public int TexturedFaceCount { get; }

        /// <summary>The index-27 emitter configurations the footer attaches.</summary>
        public IReadOnlyList<int> EmitterIds { get; }

        /// <summary>The face each emitter rides on, index for index with <see cref="EmitterIds"/>.</summary>
        public IReadOnlyList<int> EmitterFaces { get; }

        /// <summary>The index-27 effector configurations the footer attaches.</summary>
        public IReadOnlyList<int> EffectorIds { get; }

        /// <summary>The vertex each effector rides on, index for index with <see cref="EffectorIds"/>.</summary>
        public IReadOnlyList<int> EffectorVertices { get; }

        /// <summary>The index-29 billboard configurations the footer bonds.</summary>
        public IReadOnlyList<int> BillboardIds { get; }

        /// <summary>The face each billboard is bonded to, index for index with <see cref="BillboardIds"/>.</summary>
        public IReadOnlyList<int> BillboardFaces { get; }
    }

    /// <summary>One sounding key of a MIDI patch, with the sample it plays.</summary>
    /// <remarks>
    ///     Only the keys that sound. A patch declares 128 and most of them are silent; a silent key
    ///     names no sample, so exporting it would be a row of nulls per key per patch.
    /// </remarks>
    public sealed class MidiPatchKeyRecord {
        /// <summary>Records one key.</summary>
        /// <param name="key">The MIDI key, 0 to 127.</param>
        /// <param name="bankIndex">The cache index its sample lives in, 4 or 14.</param>
        /// <param name="sampleId">The sample id within that bank.</param>
        /// <param name="held">Whether the voice sustains until released.</param>
        /// <param name="tuning">The key's tuning word.</param>
        /// <param name="volume">The key's volume.</param>
        public MidiPatchKeyRecord(int key, int bankIndex, int sampleId, bool held, int tuning, int volume) {
            Key = key;
            BankIndex = bankIndex;
            SampleId = sampleId;
            Held = held;
            Tuning = tuning;
            Volume = volume;
        }

        /// <summary>The MIDI key, 0 to 127.</summary>
        public int Key { get; }

        /// <summary>
        ///     The cache index the sample lives in: 4 for the synthesised sound-effect bank, 14 for
        ///     the Vorbis sample bank.
        /// </summary>
        /// <remarks>
        ///     Selected by bit 0 of the stored sample reference, which is the whole of the
        ///     distinction - the same id means a different file in each bank.
        /// </remarks>
        public int BankIndex { get; }

        /// <summary>The sample id within that bank.</summary>
        public int SampleId { get; }

        /// <summary>Whether the voice sustains until released rather than for a counted length.</summary>
        public bool Held { get; }

        /// <summary>The key's tuning word.</summary>
        public int Tuning { get; }

        /// <summary>The key's volume.</summary>
        public int Volume { get; }
    }

    /// <summary>One MIDI patch, reduced to the keys that sound and what they reference.</summary>
    public sealed class MidiPatchRecord {
        /// <summary>Builds the record from a decoded patch.</summary>
        /// <param name="groupId">The group, which is the MIDI program number.</param>
        /// <param name="fileId">The file within the group.</param>
        /// <param name="patch">The decoded patch.</param>
        public MidiPatchRecord(int groupId, int fileId, MidiPatchDefinition patch) {
            if (patch == null)
                throw new ArgumentNullException(nameof(patch));

            GroupId = groupId;
            FileId = fileId;
            PatchVolume = patch.PatchVolume;

            var keys = new List<MidiPatchKeyRecord>();
            foreach (int key in patch.UsedKeys) {
                MidiSampleBank? bank = patch.BankOf(key);
                if (bank == null)
                    continue;

                keys.Add(new MidiPatchKeyRecord(key,
                    bank == MidiSampleBank.SoundEffects ? RSConstants.SOUND_EFFECTS : RSConstants.SFX2_INDEX,
                    patch.SampleIdOf(key), patch.HeldOf(key), patch.TuningOf(key), patch.VolumeOf(key)));
            }

            Keys = keys;
        }

        /// <summary>The group, which is the MIDI program number.</summary>
        public int GroupId { get; }

        /// <summary>The file within the group.</summary>
        public int FileId { get; }

        /// <summary>The patch's overall volume.</summary>
        public int PatchVolume { get; }

        /// <summary>Every key that sounds.</summary>
        public IReadOnlyList<MidiPatchKeyRecord> Keys { get; }
    }
}
