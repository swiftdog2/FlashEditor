using System;
using System.Collections.Generic;
using FlashEditor.Cache;
using FlashEditor.Definitions.Billboards;
using FlashEditor.Definitions.Config;
using FlashEditor.Definitions.Entities;
using FlashEditor.Definitions.Interfaces;
using FlashEditor.Definitions.LoadingScreens;
using FlashEditor.Definitions.Particles;
using FlashEditor.Definitions.SpotAnims;

namespace FlashEditor.Export {
    /// <summary>
    ///     The id-to-id relations this export resolves, and nothing else.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>This list is a ceiling, not a starting point.</b> Every entry is a join the work list
    ///     records as measured and resolving in this cache, with both sides already decoded. Nothing
    ///     here was inferred from a correlation, and nothing may be added on the strength of one.
    ///     </para>
    ///     <para>
    ///     The standing lesson is the world map icon join: its first evidence rested on two
    ///     self-proving rows and a shift sweep too narrow to falsify itself, and it was wrong. A
    ///     near-total aggregate match is not evidence either - the track-name join landed 958 of 970
    ///     keys on a real group and was still keyed on the wrong thing. A join earns its place by
    ///     what the relation rejects, and a new one belongs in the work list before it belongs here.
    ///     </para>
    /// </remarks>
    public static class CacheExportJoins {
        /// <summary>The joins resolved, one line each, for the export's own header.</summary>
        /// <remarks>
        ///     Written into the export rather than only into this file, so a reader can see the whole
        ///     ceiling without reading the source.
        /// </remarks>
        public static readonly IReadOnlyList<string> Resolved = new[] {
            "floor definition texture -> index 9",
            "item opcode 132 quest requirement -> config group 35",
            "item opcode 249 parameter key -> config group 11",
            "NPC opcode 249 parameter key -> config group 11",
            "object opcode 102 map scene icon -> config group 34",
            "object opcode 107 world map element -> config group 36",
            "object opcode 249 parameter key -> config group 11",
            "object ambient sound -> index 4",
            "object morph varbit -> index 22",
            "interface hook element 0 -> index 12",
            "interface sprite, font and model -> indexes 8, 13 and 7",
            "spot animation model and animation -> indexes 7 and 20",
            "billboard material -> index 26",
            "model footer emitters and effectors -> index 27",
            "model footer bonds -> index 29 (which models attach a billboard)",
            "midi patch key -> index 14 or index 4, selected by bit 0 of the sample reference",
            "loading screen element -> indexes 32 and 13"
        };

        /// <summary>
        ///     Joins the work list records as measured that this export does not resolve, with why.
        /// </summary>
        /// <remarks>
        ///     Stated rather than left as an absence. A reader who knows the measured list would
        ///     otherwise have to guess whether a missing join was an oversight or a decision.
        /// </remarks>
        public static readonly IReadOnlyList<string> NotResolved = new[] {
            "map tile underlay and overlay -> config groups 1 and 4. Index 5 is written as a" +
            " manifest, so no terrain tile is exported to carry the reference.",
            "interface hook element 0 -> index 12 is resolved for the first operand of each hook" +
            " array only, which is what names the script; the remaining operands are its arguments" +
            " and are exported as values rather than as references."
        };

        /// <summary>
        ///     Every reference one decoded record carries, resolved.
        /// </summary>
        /// <remarks>
        ///     Dispatches on the row's own type. A row type with no arm yields nothing, which is the
        ///     correct answer for every index whose records name no other index.
        /// </remarks>
        /// <param name="row">The decoded record.</param>
        /// <param name="resolver">Resolves an id against the reference tables.</param>
        /// <returns>The resolutions, possibly none.</returns>
        public static IEnumerable<ExportedReference> Extract(object row, CacheReferenceResolver resolver) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            if (resolver == null)
                throw new ArgumentNullException(nameof(resolver));

            switch (row) {
                case ObjectDefinition definition:
                    return FromObject(definition, resolver);
                case ItemDefinition definition:
                    return FromItem(definition, resolver);
                case NPCDefinition definition:
                    return FromNpc(definition, resolver);
                case GraphicListing listing:
                    return FromGraphic(listing, resolver);
                case InterfaceComponentRow component:
                    return FromInterfaceComponent(component, resolver);
                case BillboardListing listing:
                    return FromBillboard(listing, resolver);
                case ConfigListing listing:
                    return FromConfig(listing, resolver);
                case LoadingScreenListing listing:
                    return FromLoadingScreen(listing, resolver);
                case ModelReferenceRecord model:
                    return FromModel(model, resolver);
                case MidiPatchRecord patch:
                    return FromMidiPatch(patch, resolver);
                default:
                    return Array.Empty<ExportedReference>();
            }
        }

        /// <summary>The five object joins.</summary>
        /// <param name="definition">The object.</param>
        /// <param name="resolver">The resolver.</param>
        /// <returns>The resolutions.</returns>
        private static IEnumerable<ExportedReference> FromObject(ObjectDefinition definition,
            CacheReferenceResolver resolver) {
            var references = new List<ExportedReference>();

            Add(references, resolver.Config("mapSceneIcon", "object opcode 102 -> config group 34",
                ConfigGroup.MapSceneIcon, definition.mapSceneIcon));
            Add(references, resolver.Config("mapElementId", "object opcode 107 -> config group 36",
                ConfigGroup.MapElement, definition.mapElementId));
            Add(references, resolver.Group("ambientSoundId", "object ambient sound -> index 4",
                RSConstants.SOUND_EFFECTS, definition.ambientSoundId));
            Add(references, resolver.Definition("morphVarbit", "object morph varbit -> index 22",
                RSConstants.SCRIPT_CONFIGS, definition.morphVarbit));

            if (definition.parameters != null)
                foreach (KeyValuePair<int, object> parameter in definition.parameters)
                    Add(references, ParameterKey(resolver, "parameters[" + parameter.Key + "]", parameter.Key));

            return references;
        }

        /// <summary>The two item joins.</summary>
        /// <param name="definition">The item.</param>
        /// <param name="resolver">The resolver.</param>
        /// <returns>The resolutions.</returns>
        private static IEnumerable<ExportedReference> FromItem(ItemDefinition definition,
            CacheReferenceResolver resolver) {
            var references = new List<ExportedReference>();

            if (definition.quests != null)
                for (int i = 0; i < definition.quests.Length; i++)
                    Add(references, resolver.Config("quests[" + i + "]",
                        "item opcode 132 -> config group 35", ConfigGroup.Quest, definition.quests[i]));

            foreach (KeyValuePair<int, object> parameter in definition.itemParamEntries)
                Add(references, ParameterKey(resolver, "itemParams[" + parameter.Key + "]", parameter.Key));

            return references;
        }

        /// <summary>The NPC parameter join.</summary>
        /// <param name="definition">The NPC.</param>
        /// <param name="resolver">The resolver.</param>
        /// <returns>The resolutions.</returns>
        private static IEnumerable<ExportedReference> FromNpc(NPCDefinition definition,
            CacheReferenceResolver resolver) {
            var references = new List<ExportedReference>();

            foreach (KeyValuePair<int, object> parameter in definition.Parameters)
                Add(references, ParameterKey(resolver, "parameters[" + parameter.Key + "]", parameter.Key));

            return references;
        }

        /// <summary>The two spot-animation joins.</summary>
        /// <param name="listing">The spot animation.</param>
        /// <param name="resolver">The resolver.</param>
        /// <returns>The resolutions.</returns>
        private static IEnumerable<ExportedReference> FromGraphic(GraphicListing listing,
            CacheReferenceResolver resolver) {
            var references = new List<ExportedReference>();

            Add(references, resolver.Group("modelId", "spot animation model -> index 7",
                RSConstants.MODELS_INDEX, listing.Record.ModelId));
            Add(references, resolver.Definition("animationId", "spot animation animation -> index 20",
                RSConstants.ANIMATIONS_INDEX, listing.Record.AnimationId));

            return references;
        }

        /// <summary>
        ///     The interface joins: the sprite, font and model a component names, and the script
        ///     each of its hook arrays runs.
        /// </summary>
        /// <remarks>
        ///     Element 0 of a hook array is the script id and the rest are its arguments, so only
        ///     element 0 is resolved as a reference. Resolving the arguments too would report an
        ///     argument that happens to equal a script id as a call into that script, which is
        ///     exactly the kind of accidental join this export refuses to make.
        /// </remarks>
        /// <param name="row">The component.</param>
        /// <param name="resolver">The resolver.</param>
        /// <returns>The resolutions.</returns>
        private static IEnumerable<ExportedReference> FromInterfaceComponent(InterfaceComponentRow row,
            CacheReferenceResolver resolver) {
            var references = new List<ExportedReference>();
            InterfaceComponentDefinition component = row.Component;

            Add(references, resolver.Group("spriteId", "interface sprite -> index 8",
                RSConstants.SPRITES_INDEX, component.SpriteId));
            Add(references, resolver.Group("fontId", "interface font -> index 13",
                RSConstants.FONTS_INDEX, component.FontId));
            Add(references, resolver.Group("modelId", "interface model -> index 7",
                RSConstants.MODELS_INDEX, component.ModelId));

            for (int i = 0; i < component.Hooks.Length; i++)
                Add(references, HookScript(resolver, "hooks[" + i + "]", component.Hooks[i]));

            Add(references, HookScript(resolver, "versionedHook", component.VersionedHook));

            return references;
        }

        /// <summary>The billboard material join.</summary>
        /// <param name="listing">The billboard.</param>
        /// <param name="resolver">The resolver.</param>
        /// <returns>The resolutions.</returns>
        private static IEnumerable<ExportedReference> FromBillboard(BillboardListing listing,
            CacheReferenceResolver resolver) {
            var references = new List<ExportedReference>();

            Add(references, resolver.Definition("materialId", "billboard material -> index 26",
                RSConstants.MATERIALS, listing.Record.MaterialId));

            return references;
        }

        /// <summary>
        ///     The floor texture join, taken from the family rather than from the record type.
        /// </summary>
        /// <remarks>
        ///     <see cref="ConfigFamily.Texture"/> is the family's own statement of which field names
        ///     an index-9 texture, and it is null for every family that names none. Reading it here
        ///     rather than casting to the two floor types is what keeps this from claiming a texture
        ///     reference on a family that does not have one.
        /// </remarks>
        /// <param name="listing">The config record.</param>
        /// <param name="resolver">The resolver.</param>
        /// <returns>The resolutions.</returns>
        private static IEnumerable<ExportedReference> FromConfig(ConfigListing listing,
            CacheReferenceResolver resolver) {
            var references = new List<ExportedReference>();

            ConfigFamily family = ConfigFamily.For(listing.Address.GroupId);
            object? definition = listing.Record.Definition;

            if (family.Texture != null && definition != null && family.Texture(definition) is int texture)
                Add(references, resolver.Group("texture", "floor definition texture -> index 9",
                    RSConstants.TEXTURES, texture));

            return references;
        }

        /// <summary>The two loading screen joins.</summary>
        /// <param name="listing">The screen.</param>
        /// <param name="resolver">The resolver.</param>
        /// <returns>The resolutions.</returns>
        private static IEnumerable<ExportedReference> FromLoadingScreen(LoadingScreenListing listing,
            CacheReferenceResolver resolver) {
            var references = new List<ExportedReference>();

            IReadOnlyList<LoadingScreenElement> elements = listing.Record.Elements;
            for (int i = 0; i < elements.Count; i++) {
                switch (elements[i]) {
                    case LoadingScreenSpriteElement sprite:
                        Add(references, resolver.Group("elements[" + i + "].spriteId",
                            "loading screen element -> index 32", RSConstants.LOADING_SPRITES,
                            sprite.SpriteId));
                        break;
                    case LoadingScreenType1Element element:
                        Add(references, Font(resolver, i, element.Placement));
                        break;
                    case LoadingScreenType2Element element:
                        Add(references, Font(resolver, i, element.Placement));
                        break;
                    case LoadingScreenType3Element element:
                        //Type 9 derives from type 3 and is caught here with it, which is right - it
                        //carries the same placement block and so the same font reference.
                        Add(references, Font(resolver, i, element.Placement));
                        break;
                }
            }

            return references;
        }

        /// <summary>The three model footer joins.</summary>
        /// <remarks>
        ///     <b>An index 27 id is a file id within a fixed group, not a group id.</b> Emitters are
        ///     files of group 0 and effectors files of group 1 - two formats with no opcode in
        ///     common - and the client fetches each by file within its own group
        ///     (<c>ParticleType.list</c> and <c>Class21.method263</c>). Resolving them through
        ///     <c>Definition</c> treated the id as a group id, and the index declares two groups, so
        ///     every emitter above id 1 was reported as undeclared.
        /// </remarks>
        /// <param name="model">The model's footer references.</param>
        /// <param name="resolver">The resolver.</param>
        /// <returns>The resolutions.</returns>
        private static IEnumerable<ExportedReference> FromModel(ModelReferenceRecord model,
            CacheReferenceResolver resolver) {
            var references = new List<ExportedReference>();

            for (int i = 0; i < model.EmitterIds.Count; i++)
                Add(references, resolver.File("emitters[" + i + "]",
                    "model footer emitter -> index 27", RSConstants.CONFIG_PARTICLES,
                    ParticleEmitterDefinition.GroupId, model.EmitterIds[i]));

            for (int i = 0; i < model.EffectorIds.Count; i++)
                Add(references, resolver.File("effectors[" + i + "]",
                    "model footer effector -> index 27", RSConstants.CONFIG_PARTICLES,
                    ParticleEffectorDefinition.GroupId, model.EffectorIds[i]));

            for (int i = 0; i < model.BillboardIds.Count; i++)
                Add(references, resolver.Definition("bonds[" + i + "]",
                    "model footer bond -> index 29", RSConstants.CONFIG_BILLBOARD, model.BillboardIds[i]));

            return references;
        }

        /// <summary>The midi patch key join, one reference per sounding key.</summary>
        /// <param name="patch">The patch.</param>
        /// <param name="resolver">The resolver.</param>
        /// <returns>The resolutions.</returns>
        private static IEnumerable<ExportedReference> FromMidiPatch(MidiPatchRecord patch,
            CacheReferenceResolver resolver) {
            var references = new List<ExportedReference>();

            foreach (MidiPatchKeyRecord key in patch.Keys)
                Add(references, resolver.Group("keys[" + key.Key + "].sampleId",
                    "midi patch key -> index " + key.BankIndex, key.BankIndex, key.SampleId));

            return references;
        }

        /// <summary>The script a hook array runs, taken from its first operand.</summary>
        /// <param name="resolver">The resolver.</param>
        /// <param name="field">The field naming the hook array.</param>
        /// <param name="hook">The hook array, empty when the component has no hook there.</param>
        /// <returns>The resolution, or null when there is no hook.</returns>
        private static ExportedReference? HookScript(CacheReferenceResolver resolver, string field,
            InterfaceScriptOperand[] hook) {
            if (hook == null || hook.Length == 0)
                return null;

            //A string first operand is not a script id. The client reads the type byte before the
            //value, so treating one as an id would resolve a text length as a script.
            if (hook[0].TypeByte != InterfaceScriptOperand.IntegerType)
                return null;

            return resolver.Group(field + "[0]", "interface hook element 0 -> index 12",
                RSConstants.CLIENT_SCRIPTS_INDEX, hook[0].Integer);
        }

        /// <summary>The font a placement block names.</summary>
        /// <param name="resolver">The resolver.</param>
        /// <param name="element">The element's position in the screen.</param>
        /// <param name="placement">The placement block.</param>
        /// <returns>The resolution, or null when it names no font.</returns>
        private static ExportedReference? Font(CacheReferenceResolver resolver, int element,
            LoadingScreenPlacement placement) {
            return resolver.Group("elements[" + element + "].fontId",
                "loading screen element -> index 13", RSConstants.FONTS_INDEX, placement.FontId);
        }

        /// <summary>An opcode 249 parameter key, which addresses a parameter type.</summary>
        /// <param name="resolver">The resolver.</param>
        /// <param name="field">The field naming the entry.</param>
        /// <param name="key">The parameter key.</param>
        /// <returns>The resolution.</returns>
        private static ExportedReference? ParameterKey(CacheReferenceResolver resolver, string field, int key) {
            return resolver.Config(field, "opcode 249 parameter key -> config group 11",
                ConfigGroup.ParameterType, key);
        }

        /// <summary>Appends a resolution when there was one.</summary>
        /// <param name="into">The list being built.</param>
        /// <param name="reference">The resolution, or null.</param>
        private static void Add(List<ExportedReference> into, ExportedReference? reference) {
            if (reference != null)
                into.Add(reference);
        }
    }
}
