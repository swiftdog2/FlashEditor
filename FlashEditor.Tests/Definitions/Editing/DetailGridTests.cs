using System;
using System.Linq;
using System.Reflection;
using BrightIdeasSoftware;
using FlashEditor.Definitions.Editing;
using Xunit;

namespace FlashEditor.Tests.Definitions.Editing {
    /// <summary>
    ///     The null-row rule, and a sweep that fails when a grid is written without it.
    /// </summary>
    /// <remarks>
    ///     <b>Written because the rule had been broken eleven times.</b> It is stated in the
    ///     repository's UI conventions and implemented correctly in <c>DefinitionColumn</c>, and
    ///     then ten private copies of <c>AddColumn</c> plus a handful of hand-written aspect getters
    ///     re-implemented it without the guard. The symptom is a
    ///     <c>NullReferenceException</c> out of a paint, which took the form down when a cache was
    ///     closed: unbinding calls <c>ClearObjects</c>, the grid evaluates aspects for the rows it
    ///     is recycling, and <c>(SomeRow) null</c> is a legal cast whose members are not.
    /// </remarks>
    public sealed class DetailGridTests {
        private sealed class Row {
            internal string Value { get; init; } = "";
        }

        private static OLVColumn ColumnOf(ObjectListView grid) {
            return grid.AllColumns[0];
        }

        /// <summary>A null row yields an empty cell rather than reaching the reader.</summary>
        [Fact]
        public void ANullRowNeverReachesTheReader() {
            using var grid = new ObjectListView();
            bool readerRan = false;

            DetailGrid.AddColumn(grid, "Value", 80, row => {
                readerRan = true;
                return ((Row) row).Value;
            });

            Assert.Null(ColumnOf(grid).AspectGetter(null!));
            Assert.False(readerRan, "The reader was called with a null row.");
        }

        /// <summary>A real row reaches the reader unchanged.</summary>
        [Fact]
        public void ARealRowReachesTheReader() {
            using var grid = new ObjectListView();
            DetailGrid.AddColumn(grid, "Value", 80, row => ((Row) row).Value);

            Assert.Equal("hello", ColumnOf(grid).AspectGetter(new Row { Value = "hello" }));
        }

        /// <summary>
        ///     A row of the wrong type still throws.
        /// </summary>
        /// <remarks>
        ///     Deliberate. It can only mean a grid was wired to a row type it does not produce, and
        ///     blanking those cells would hide that permanently - the guard is for absence, not for
        ///     type confusion.
        /// </remarks>
        [Fact]
        public void AWrongTypeRowStillThrows() {
            using var grid = new ObjectListView();
            DetailGrid.AddColumn(grid, "Value", 80, row => ((Row) row).Value);

            Assert.ThrowsAny<Exception>(() => ColumnOf(grid).AspectGetter("not a row"));
        }

        /// <summary>
        ///     Every private <c>AddColumn</c> in the production assembly routes through
        ///     <see cref="DetailGrid"/>.
        /// </summary>
        /// <remarks>
        ///     <b>The sweep that stops the eleventh copy.</b> Ten panels each grew their own
        ///     <c>AddColumn</c> and none of them guarded a null row; fixing all ten is worth little
        ///     if the next panel writes an eleventh. This asserts the shape rather than the
        ///     behaviour - it cannot see inside a lambda - but "there is exactly one implementation
        ///     of this rule" is the property that actually held the bug at bay.
        ///     <para>
        ///     If a new panel legitimately needs its own column builder, the fix is to route it
        ///     through <c>DetailGrid</c>, not to relax this.
        ///     </para>
        /// </remarks>
        [Fact]
        public void NoPanelBuildsColumnsWithoutTheSharedGuard() {
            Assembly production = typeof(DetailGrid).Assembly;

            var offenders = production.GetTypes()
                .Where(type => type != typeof(DetailGrid))
                .SelectMany(type => type
                    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                        | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Where(method => method.Name == "AddColumn"))
                .Select(method => method.DeclaringType!.FullName + "." + method.Name)
                .Distinct()
                .OrderBy(name => name)
                .ToList();

            /* A panel MAY keep a wrapper of its own - several do, so their call sites stay short -
               and those are listed here by name. What must not happen is a wrapper that builds an
               OLVColumn itself, because that is where the guard goes missing. The check is that the
               list of wrappers is known, so adding one is a deliberate act with a test change
               attached rather than a silent eleventh copy. */
            var known = new[] {
                "FlashEditor.Definitions.Animation.AnimationDefinitionEditorPanel.AddColumn",
                "FlashEditor.Definitions.Audio.SoundEffectEditorPanel.AddColumn",
                "FlashEditor.Definitions.ClientScripts.ClientScriptEditorPanel.AddColumn",
                "FlashEditor.Definitions.Compression.HuffmanEditorPanel.AddColumn",
                "FlashEditor.Definitions.Config.ConfigEditorPanel.AddColumn",
                "FlashEditor.Definitions.Defaults.DefaultsEditorPanel.AddColumn",
                "FlashEditor.Definitions.Editing.AnimationEditorPanel.AddColumn",
                "FlashEditor.Definitions.Editing.DetailFieldGrid.AddColumn",
                "FlashEditor.Definitions.Enums.EnumEditorPanel.AddColumn",
                "FlashEditor.Definitions.Fonts.FontEditorPanel.AddColumn",
                "FlashEditor.Definitions.Interfaces.InterfaceEditorPanel.AddColumn",
                "FlashEditor.Definitions.Interfaces.InterfaceHookPanel.AddColumn",
                "FlashEditor.Definitions.VarBits.VarBitEditorPanel.AddColumn"
            };

            string[] unexpected = offenders.Except(known).ToArray();

            Assert.True(unexpected.Length == 0,
                "A new AddColumn appeared and has to route through DetailGrid so the null-row " +
                "guard is not re-lost: " + string.Join(", ", unexpected));
        }
    }
}
