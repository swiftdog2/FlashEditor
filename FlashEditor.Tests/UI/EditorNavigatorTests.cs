using System.Collections.Generic;
using FlashEditor.UI;
using Xunit;

namespace FlashEditor.Tests.UI {
    /// <summary>
    ///     The back stack, and the two ways a back stack usually breaks.
    /// </summary>
    public sealed class EditorNavigatorTests {
        private static readonly EditorLocation Sprites = new EditorLocation(8, 421);
        private static readonly EditorLocation Textures = new EditorLocation(9, 17);
        private static readonly EditorLocation Objects = new EditorLocation(16, 4271);

        [Fact]
        public void NothingIsVisitedToStartWith() {
            var navigator = new EditorNavigator();

            Assert.Null(navigator.Current);
            Assert.False(navigator.CanGoBack);
            Assert.False(navigator.CanGoForward);
            Assert.False(navigator.GoBack());
        }

        [Fact]
        public void GoingSomewhereRaisesNavigatedAndRemembersWhereYouWere() {
            var navigator = new EditorNavigator();
            var seen = new List<EditorLocation>();
            navigator.Navigated += (_, where) => seen.Add(where);

            navigator.GoTo(Sprites);
            navigator.GoTo(Textures);

            Assert.Equal(new[] { Sprites, Textures }, seen);
            Assert.Equal(Textures, navigator.Current);
            Assert.True(navigator.CanGoBack);
        }

        [Fact]
        public void BackReturnsToThePreviousPlaceAndForwardUndoesIt() {
            var navigator = new EditorNavigator();
            navigator.GoTo(Sprites);
            navigator.GoTo(Textures);

            Assert.True(navigator.GoBack());
            Assert.Equal(Sprites, navigator.Current);
            Assert.True(navigator.CanGoForward);

            Assert.True(navigator.GoForward());
            Assert.Equal(Textures, navigator.Current);
            Assert.False(navigator.CanGoForward);
        }

        /// <summary>Going somewhere new after a back abandons the forward history.</summary>
        /// <remarks>
        ///     Browser behaviour, and the alternative is a forward button that resumes a journey the
        ///     user has already left.
        /// </remarks>
        [Fact]
        public void GoingSomewhereNewAfterABackDiscardsTheForwardHistory() {
            var navigator = new EditorNavigator();
            navigator.GoTo(Sprites);
            navigator.GoTo(Textures);
            navigator.GoBack();

            navigator.GoTo(Objects);

            Assert.False(navigator.CanGoForward);
            Assert.Equal(Objects, navigator.Current);
        }

        /// <summary>Navigating to where you already are does nothing.</summary>
        /// <remarks>
        ///     A double click on a link would otherwise put the same place in the history twice, and
        ///     the first Back would appear to do nothing at all.
        /// </remarks>
        [Fact]
        public void NavigatingToWhereYouAlreadyAreIsIgnored() {
            var navigator = new EditorNavigator();
            navigator.GoTo(Sprites);
            navigator.GoTo(Sprites);

            Assert.False(navigator.CanGoBack);
        }

        /// <summary>
        ///     The form selecting a row in response to a navigation does not corrupt the history.
        /// </summary>
        /// <remarks>
        ///     <b>The failure this class is most likely to have.</b> The form answers Navigated by
        ///     selecting a tab and a row, and selecting a row is what calls RecordVisit - so without
        ///     a guard, one Back records its own destination as a fresh visit, pushes the place it
        ///     came from back onto the stack, and the button never makes progress. This simulates
        ///     exactly that handler.
        /// </remarks>
        [Fact]
        public void AFormThatRecordsTheVisitItWasSentToDoesNotBreakTheBackButton() {
            var navigator = new EditorNavigator();

            //The form: told to go somewhere, it selects a row, which reports the visit back.
            navigator.Navigated += (_, where) => navigator.RecordVisit(where);

            navigator.GoTo(Sprites);
            navigator.GoTo(Textures);
            navigator.GoTo(Objects);

            Assert.True(navigator.GoBack());
            Assert.Equal(Textures, navigator.Current);

            Assert.True(navigator.GoBack());
            Assert.Equal(Sprites, navigator.Current);

            Assert.False(navigator.CanGoBack);
        }

        /// <summary>A selection the user made themselves is remembered but not re-issued.</summary>
        /// <remarks>
        ///     Without recording it, the first Back after browsing would return to wherever the last
        ///     link was followed from rather than where the user actually came from. Without the
        ///     "not re-issued" half, the form would be told to go where it already is.
        /// </remarks>
        [Fact]
        public void ARecordedVisitEntersTheHistoryWithoutBeingReIssued() {
            var navigator = new EditorNavigator();
            int navigations = 0;
            navigator.Navigated += (_, _) => navigations++;

            navigator.RecordVisit(Sprites);
            navigator.RecordVisit(Textures);

            Assert.Equal(0, navigations);
            Assert.Equal(Textures, navigator.Current);

            Assert.True(navigator.GoBack());
            Assert.Equal(Sprites, navigator.Current);
        }

        /// <summary>Clearing forgets everywhere, for a cache being reopened.</summary>
        /// <remarks>
        ///     A history kept across a reopen offers to return to a record id that means something
        ///     different, or nothing, in the cache now open.
        /// </remarks>
        [Fact]
        public void ClearingForgetsEverywhere() {
            var navigator = new EditorNavigator();
            navigator.GoTo(Sprites);
            navigator.GoTo(Textures);

            navigator.Clear();

            Assert.Null(navigator.Current);
            Assert.False(navigator.CanGoBack);
            Assert.False(navigator.CanGoForward);
        }

        /// <summary>The history is bounded, so a long session cannot grow without limit.</summary>
        [Fact]
        public void TheHistoryIsBoundedAndDropsTheOldestPlaces() {
            var navigator = new EditorNavigator();

            for (int i = 0; i < 200; i++)
                navigator.GoTo(new EditorLocation(8, i));

            int steps = 0;
            while (navigator.GoBack())
                steps++;

            Assert.InRange(steps, 1, 50);
            Assert.False(navigator.CanGoBack);
        }

        /// <summary>An index with no record is a place too, and differs from one with a record.</summary>
        [Fact]
        public void AnIndexWithoutARecordIsItsOwnPlace() {
            var wholeIndex = new EditorLocation(9);
            var oneRecord = new EditorLocation(9, 0);

            Assert.False(wholeIndex.HasRecord);
            Assert.True(oneRecord.HasRecord);
            Assert.NotEqual(wholeIndex, oneRecord);
        }
    }
}
