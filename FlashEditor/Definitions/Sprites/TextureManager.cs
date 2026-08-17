using FlashEditor;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FlashEditor.Cache;
using FlashEditor.Definitions.Sprites;
using static FlashEditor.Utils.DebugUtil;
using FlashEditor.IO;
using FlashEditor.Definitions.Models;

namespace FlashEditor.Definitions.Sprites {
    /// <summary>
    /// Loads all texture definitions from the materials index (index 26).
    /// The Hydra client stores texture metadata in a single columnar file
    /// (archive 0, file 0 of the MATERIALS index), read by Class260.
    /// </summary>
    public class TextureManager {
        private readonly RSCache cache;
        public static readonly SortedDictionary<int, TextureDefinition> Textures = new();
        private static readonly Bitmap _fallbackThumb = new Bitmap(100, 100);
        private static RSCache _cacheRef;

        /// <summary>Held across the check and the load in <see cref="EnsureLoaded"/>.</summary>
        private static readonly object LoadGate = new object();

        /// <summary>
        /// Raw bytes of the entire columnar file for lossless round-trip.
        /// </summary>
        public static byte[] RawIndexData;

        /// <summary>
        ///     The decoded index-26 table, or null when none has been read.
        /// </summary>
        /// <remarks>
        ///     The write path lives on it: it knows which columns were edited and which have to be
        ///     replayed byte for byte, neither of which the texture dictionary can say. It also
        ///     holds index 26's own shape rather than the dictionary's - <see cref="Textures"/>
        ///     additionally carries every texture index 9 declares, and encoding from that would
        ///     grow the table by the ones index 26 never had a record for.
        /// </remarks>
        public static MaterialTable? Materials { get; private set; }

        public TextureManager(RSCache cache) {
            this.cache = cache;
        }

        /// <summary>
        /// Drops every loaded texture definition and the render memos that were keyed against them.
        /// </summary>
        /// <remarks>
        /// The evaluator's composed-texture and sprite caches are keyed by id alone, so they are
        /// only meaningful for the cache that filled them. Clearing them here rather than at the
        /// call sites means opening a second cache cannot be served the first one's pixels -
        /// <see cref="Load"/> runs this before it reads anything.
        /// </remarks>
        public static void Clear() {
            foreach (var def in Textures.Values)
                def?.Dispose();
            Textures.Clear();
            RawIndexData = null;
            Materials = null;
            TextureGraphEvaluator.ClearCaches();
        }

        /// <summary>
        ///     Loads the texture store only if it is not already loaded for this cache.
        /// </summary>
        /// <remarks>
        ///     <b><see cref="Load"/> begins with <see cref="Clear"/>, which disposes every
        ///     <c>TextureDefinition</c> in a static store that the whole application shares.</b> So a
        ///     second component constructing its own <c>GLTextureCache</c> does not merely repeat
        ///     the decode - it destroys the rasters the model draw path and the Textures tab are
        ///     already holding, part way through a session, and the damage shows up somewhere else
        ///     entirely.
        ///     <para>
        ///     That is not hypothetical: the particle preview needs a texture cache of its own,
        ///     because GL handles do not cross contexts and it has its own. Only the handles need
        ///     duplicating; the decoded definitions are context-free and shared, which is what makes
        ///     one store correct here rather than one per consumer.
        ///     </para>
        /// </remarks>
        /// <param name="forCache">The cache the store must describe.</param>
        public static void EnsureLoaded(RSCache forCache) {
            if (forCache == null)
                throw new ArgumentNullException(nameof(forCache));

            /* Serialised, because the callers are no longer all on one thread: the Materials tab
               reaches this from the list panel's background worker, which is the right thread for a
               whole-index decode, while GL initialisation reaches it from the UI thread. Two loads
               running at once would each begin by disposing what the other was filling in. The
               fast path is inside the lock as well - a check outside it would let a second caller
               through while the first was still clearing. */
            lock (LoadGate) {
                if (ReferenceEquals(_cacheRef, forCache) && Textures.Count > 0)
                    return;

                new TextureManager(forCache).Load();
            }
        }

        public void Load() {
            Clear();
            _cacheRef = cache;

            // Step 1: Load Materials metadata (index 26) — the full set of texture IDs
            try {
                LoadFromMaterialsIndex();
            } catch (Exception ex) {
                Debug($"TextureManager: MATERIALS index unavailable ({ex.Message})", LOG_DETAIL.BASIC);
            }

            // Step 2: Load sprite references from TEXTURES (index 9) — merges into existing entries
            LoadFromTextureIndex();

            Debug($"Loaded {Textures.Count} texture definitions total", LOG_DETAIL.BASIC);
        }

        private void LoadFromMaterialsIndex() {
            RSContainer container = cache.GetContainer(RSConstants.MATERIALS, 0);
            if (container == null || container.GetStream() == null) {
                Debug("TextureManager: no materials container at archive 0", LOG_DETAIL.BASIC);
                return;
            }

            JagStream data = container.GetStream();
            data.Seek0();

            // Store raw data for round-trip
            RawIndexData = new byte[data.Length];
            data.Read(RawIndexData, 0, RawIndexData.Length);
            data.Seek0();
            container.ReleaseData();

            DecodeColumnar(new JagStream(RawIndexData));

            Debug($"Loaded {Textures.Count} texture definitions from MATERIALS index", LOG_DETAIL.BASIC);
        }

        private void LoadFromTextureIndex() {
            RSReferenceTable table;
            try {
                table = cache.GetReferenceTable(RSConstants.TEXTURES);
            } catch (Exception ex) {
                Debug($"TextureManager: TEXTURES reference table unavailable: {ex.Message}", LOG_DETAIL.BASIC);
                return;
            }

            // Textures in index 9 are stored as multiple files within archive 0.
            // Each file = one texture definition. The file ID = texture ID.
            // Texture.Decode extracts sprite file IDs referenced from SPRITES (index 8).
            int loaded = 0, errors = 0, withSprites = 0, withGraphs = 0, graphBails = 0;
            foreach (var (archiveId, archiveEntry) in table.GetArchiveEntries()) {
                try {
                    int[] fileIds = archiveEntry.GetValidFileIds();
                    if (fileIds.Length == 0) continue;

                    RSContainer container = cache.GetContainer(RSConstants.TEXTURES, archiveId);
                    if (container == null || container.GetStream() == null)
                        continue;

                    RSArchive archive = RSCache.GetArchive(container, fileIds);

                    foreach (int fileId in fileIds) {
                        try {
                            JagStream stream = archive.GetFile(fileId);
                            if (stream == null) continue;
                            stream.Seek0();

                            Texture tex = Texture.Decode(stream);

                            // Texture index stores one graph per archive: archiveId = texture ID.
                            // fileId is always 0 within each archive.
                            int textureId = (fileIds.Length == 1 && fileId == 0) ? archiveId : fileId;

                            // Merge into existing entry from Materials, or create new
                            var def = Textures.ContainsKey(textureId) ? Textures[textureId] : new TextureDefinition { id = textureId };
                            def.spriteFileIds = tex.FileIds;

                            // Store graph for lazy rendering (don't render at load time)
                            def.graph = tex.Graph;
                            //Carried alongside it because the graph is lossy by design - see
                            //TextureDefinition.graphRecord. Without this a texture can be shown
                            //and never written back.
                            def.graphRecord = tex.Record;

                            Textures[textureId] = def;
                            loaded++;
                            if (tex.Count > 0) withSprites++;
                            if (tex.Graph != null) withGraphs++;
                            else graphBails++;
                        } catch (Exception ex) {
                            Debug($"TextureManager: error decoding texture file {fileId} in archive {archiveId}: {ex.Message}", LOG_DETAIL.BASIC);
                            errors++;
                        }
                    }

                    container.ReleaseData();
                } catch (Exception ex) {
                    Debug($"TextureManager: error loading texture archive {archiveId}: {ex.Message}", LOG_DETAIL.BASIC);
                    errors++;
                }
            }

            Debug($"LoadFromTextureIndex: loaded {loaded} textures ({withSprites} with sprite IDs, {withGraphs} with graphs, {graphBails} graph bails), {errors} errors", LOG_DETAIL.BASIC);
        }

        /// <summary>
        /// Decodes the columnar texture definition format from the materials index.
        /// Matches the Class260 constructor in the Hydra client.
        /// </summary>
        /// <remarks>
        ///     The format itself lives in <see cref="MaterialTable"/>, which keeps each record's
        ///     stored bytes alongside its fields. Two readers of one column-major layout would be
        ///     free to disagree about it, and only one of them would be the one that writes.
        /// </remarks>
        public static void DecodeColumnar(JagStream s) {
            MaterialTable table = MaterialTable.Decode(s);
            Materials = table;

            foreach (TextureDefinition def in table.Slots)
                if (def != null)
                    Textures[def.id] = def;
        }

        /// <summary>
        /// Encodes all texture definitions back into the columnar format.
        /// Returns a JagStream ready for writing to MATERIALS index archive 0.
        /// </summary>
        /// <remarks>
        ///     Per column, per record: a column nobody edited comes back as the bytes it was read
        ///     from, and an edited one is written from its field. Returning the whole stored blob
        ///     whenever it was present is what this used to do, and it discarded every field edit in
        ///     silence - the editor could change a material and save, and the cache would not move.
        /// </remarks>
        public static JagStream EncodeColumnar() {
            MaterialTable? table = Materials;
            if (table != null)
                return table.Encode();

            // Nothing was decoded through the table, so the captured blob is all there is.
            if (RawIndexData != null) {
                var raw = new JagStream(RawIndexData.Length);
                raw.Write(RawIndexData, 0, RawIndexData.Length);
                raw.Flip();
                return raw;
            }

            return EncodeFromFields();
        }

        /// <summary>
        /// Encodes from field values (used when textures have been edited).
        /// </summary>
        /// <remarks>
        ///     Every column of every record, ignoring what was stored - which is lossy wherever the
        ///     format is not canonical, and is why <see cref="EncodeColumnar"/> is the write path
        ///     rather than this. The table is built from <see cref="Textures"/>, so it spans every
        ///     texture the editor is holding rather than only the ones index 26 declared.
        /// </remarks>
        public static JagStream EncodeFromFields() {
            return MaterialTable.FromDefinitions(Textures).EncodeFromFields();
        }

        /// <summary>
        /// Lazily loads a texture thumbnail if it hasn't been loaded yet.
        /// Prefers sprite thumbnails (proven path) over graph rendering
        /// (experimental) to keep model rendering correct.
        /// </summary>
        // Diagnostic counters for BASIC-level summary logging
        private static int _diagSpriteOk, _diagSpriteFail, _diagGraphOk, _diagGraphFail, _diagNoData;

        public static void EnsureRendered(TextureDefinition def) {
            if (def == null) {
                Debug("TextureManager.EnsureRendered: def is NULL", LOG_DETAIL.ADVANCED);
                return;
            }
            if (def.thumb != null) {
                Debug($"Tex {def.id}: already rendered ({def.thumb.Width}x{def.thumb.Height})", LOG_DETAIL.INSANE);
                return;
            }
            if (_cacheRef == null) {
                Debug("TextureManager.EnsureRendered: _cacheRef is NULL — cannot render anything!", LOG_DETAIL.BASIC);
                return;
            }

            bool hasGraph = def.graph != null;
            bool hasSprites = def.spriteFileIds != null && def.spriteFileIds.Length > 0;
            if (LOG_LEVEL >= LOG_DETAIL.ADVANCED) {
                // hasGraph is the null check; the compiler cannot see through the bool.
                int nodeCount = hasGraph ? (def.graph!.Nodes?.Length ?? 0) : 0;
                Debug($"Tex {def.id}: BEGIN - graph={hasGraph} (nodes={nodeCount}), sprites={hasSprites} ({def.spriteFileIds?.Length ?? 0} ids), " +
                      $"water=0x{def.waterParams:X8}, transpose={def.transposePixels}", LOG_DETAIL.ADVANCED);
            }

            // Try graph rendering first — this evaluates the full procedural
            // texture pipeline and produces the most accurate result.
            if (hasGraph) {
                var graphSw = System.Diagnostics.Stopwatch.StartNew();
                try {
                    Debug($"Tex {def.id}: graph render starting — colourOut={def.graph!.ColourOutputIndex}, " +
                          $"alphaOut={def.graph.AlphaOutputIndex}, brightnessOut={def.graph.BrightnessOutputIndex}", LOG_DETAIL.ADVANCED);

                    // Log node types in the graph for diagnosis.
                    // Gated on the level rather than left to Debug to discard: Debug takes an
                    // already-formatted string, so the dictionary, the LINQ and the string.Join
                    // all run per texture whatever the level is, and the default level is BASIC.
                    if (LOG_LEVEL >= LOG_DETAIL.ADVANCED && def.graph.Nodes != null) {
                        var nodeTypes = new System.Collections.Generic.Dictionary<int, int>();
                        int spriteNodes = 0;
                        foreach (var n in def.graph.Nodes) {
                            if (n == null) continue;
                            nodeTypes[n.Type] = nodeTypes.GetValueOrDefault(n.Type) + 1;
                            if ((n.Type == 18 || n.Type == 39) && n.SpriteId >= 0)
                                spriteNodes++;
                        }
                        Debug($"Tex {def.id}: graph node types: [{string.Join(", ", nodeTypes.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}x{kv.Value}"))}], " +
                              $"spriteNodes={spriteNodes}", LOG_DETAIL.ADVANCED);
                    }

                    Bitmap rendered = TextureGraphEvaluator.Render(def.graph, 128, 128, _cacheRef, def.transposePixels, def.id);
                    graphSw.Stop();

                    if (rendered != null) {
                        Debug($"Tex {def.id}: graph render OK in {graphSw.ElapsedMilliseconds}ms — {rendered.Width}x{rendered.Height}", LOG_DETAIL.ADVANCED);
                        //waterParams is not a tint over the generated pixels. It is packed
                        //water-shader parameters (Class151_Sub2.java:152-166) that the client hands
                        //to the renderer at RenderType_Sub1.java:4441 and never multiplies the graph
                        //output by - doing so here scaled every texture towards black, and it is
                        //zero in every record of both caches, so it scaled them all the way. The
                        //stand-in colour when there is nothing to render is representativeHsl.
                        def.thumb = rendered;
                        System.Threading.Interlocked.Increment(ref _diagGraphOk);
                        return;
                    }
                    Debug($"Tex {def.id}: graph render returned NULL in {graphSw.ElapsedMilliseconds}ms", LOG_DETAIL.BASIC);
                } catch (Exception ex) {
                    graphSw.Stop();
                    Debug($"Tex {def.id}: graph render FAILED in {graphSw.ElapsedMilliseconds}ms — {ex.GetType().Name}: {ex.Message}", LOG_DETAIL.BASIC);
                    Debug($"Tex {def.id}: graph stack: {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}", LOG_DETAIL.ADVANCED);
                }
                System.Threading.Interlocked.Increment(ref _diagGraphFail);
            }

            // Fall back to loading a sprite thumbnail directly from the cache.
            // Try ALL referenced sprite IDs, not just the first one.
            if (hasSprites) {
                //string.Join runs regardless of level once it is an argument, so it is gated
                if (LOG_LEVEL >= LOG_DETAIL.ADVANCED)
                    Debug($"Tex {def.id}: sprite fallback - trying IDs: [{string.Join(", ", def.spriteFileIds!)}]", LOG_DETAIL.ADVANCED);
                for (int si = 0; si < def.spriteFileIds!.Length; si++) {
                    int spriteId = def.spriteFileIds[si];
                    try {
                        Debug($"Tex {def.id}: loading sprite {spriteId} (index {si}/{def.spriteFileIds.Length})", LOG_DETAIL.INSANE);
                        SpriteDefinition sprite = _cacheRef.GetSprite(spriteId);
                        if (sprite == null) {
                            Debug($"Tex {def.id}: sprite {spriteId} — GetSprite returned null", LOG_DETAIL.ADVANCED);
                            continue;
                        }
                        int frameCount = sprite.GetFrameCount();
                        Debug($"Tex {def.id}: sprite {spriteId} — {frameCount} frames", LOG_DETAIL.ADVANCED);
                        if (frameCount > 0) {
                            var frame = sprite.GetFrame(0);
                            if (frame == null) {
                                Debug($"Tex {def.id}: sprite {spriteId} — frame 0 is null", LOG_DETAIL.ADVANCED);
                                continue;
                            }
                            if (frame.thumb == null) {
                                Debug($"Tex {def.id}: sprite {spriteId} — frame 0 thumb is null (w={frame.GetWidth()}, h={frame.GetHeight()})", LOG_DETAIL.ADVANCED);
                                continue;
                            }
                            Debug($"Tex {def.id}: sprite {spriteId} — frame 0 OK ({frame.thumb.Width}x{frame.thumb.Height})", LOG_DETAIL.ADVANCED);
                            def.thumb = new Bitmap(frame.thumb);
                            System.Threading.Interlocked.Increment(ref _diagSpriteOk);
                            return;
                        }
                    } catch (Exception ex) {
                        Debug($"Tex {def.id}: sprite {spriteId} FAILED — {ex.GetType().Name}: {ex.Message}", LOG_DETAIL.ADVANCED);
                    }
                }
                Debug($"Tex {def.id}: ALL {def.spriteFileIds.Length} sprite IDs failed", LOG_DETAIL.BASIC);
                System.Threading.Interlocked.Increment(ref _diagSpriteFail);
            }

            if (!hasGraph && !hasSprites) {
                Debug($"Tex {def.id}: no graph and no sprite IDs — using its representative colour", LOG_DETAIL.ADVANCED);
                System.Threading.Interlocked.Increment(ref _diagNoData);
            } else {
                Debug($"Tex {def.id}: EXHAUSTED all paths — falling back to its representative colour", LOG_DETAIL.BASIC);
            }

            def.thumb = SolidThumb(RepresentativeRgb(def));
        }

        /// <summary>
        /// The colour the client draws for a texture it cannot generate.
        /// </summary>
        /// <remarks>
        /// This is not a placeholder. The materials index declares 1,408 textures while the
        /// texture index only holds 946 graphs, so every id from 946 up has no procedural
        /// content at all - <c>Class260.method8</c> returns false for them and the client uses
        /// <see cref="TextureDefinition.representativeHsl"/> instead. Rendering that colour is therefore
        /// the correct result for those textures rather than a stand-in for a failure.
        /// </remarks>
        internal static int RepresentativeRgb(TextureDefinition def) {
            int hsl = def.representativeHsl & 0xFFFF;

            //Class345.method3825 at neutral brightness: the lightness is clamped into [2, 126]
            //and the hue and saturation bits pass through untouched.
            int lightness = hsl & 0x7F;
            if (lightness < 2) lightness = 2;
            else if (lightness > 126) lightness = 126;

            return ModelDefinition.RawHslToRgb((hsl & 0xFF80) | lightness);
        }

        private static Bitmap SolidThumb(int rgb) {
            var bmp = new Bitmap(128, 128);
            using (var g = Graphics.FromImage(bmp))
                g.Clear(Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF));
            return bmp;
        }

        /// <summary>Prints a diagnostic summary of texture rendering results.</summary>
        public static void PrintDiagnostics() {
            int total = _diagSpriteOk + _diagSpriteFail + _diagGraphOk + _diagGraphFail + _diagNoData;
            Debug($"=== TEXTURE DIAGNOSTICS ===", LOG_DETAIL.BASIC);
            Debug($"  Graph rendered OK: {_diagGraphOk}", LOG_DETAIL.BASIC);
            Debug($"  Graph render FAIL: {_diagGraphFail}", LOG_DETAIL.BASIC);
            Debug($"  Sprite fallback OK: {_diagSpriteOk}", LOG_DETAIL.BASIC);
            Debug($"  Sprite fallback FAIL: {_diagSpriteFail}", LOG_DETAIL.BASIC);
            Debug($"  No graph or sprites: {_diagNoData}", LOG_DETAIL.BASIC);
            Debug($"  Total attempted: {total}", LOG_DETAIL.BASIC);
            Debug($"===========================", LOG_DETAIL.BASIC);
            _diagSpriteOk = _diagSpriteFail = _diagGraphOk = _diagGraphFail = _diagNoData = 0;
        }

        internal static Image GetThumbnailForTexture(string key) {
            if (int.TryParse(key, out int id) && Textures.TryGetValue(id, out var def)) {
                EnsureRendered(def);
                if (def.thumb != null)
                    return def.thumb;
            }

            return _fallbackThumb;
        }
    }
}
