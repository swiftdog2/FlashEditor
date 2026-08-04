using FlashEditor.cache;
using FlashEditor.Definitions.Editing;
using System;
using System.Drawing;
using System.Windows.Forms;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.Definitions.QuickChat {
    /// <summary>
    ///     The Quick Chat tab: indexes 24 and 25, menus and messages.
    /// </summary>
    /// <remarks>
    ///     <b>One tab for two indexes, and two selectors rather than one.</b> The constant names
    ///     <c>QUICK_CHAT_MESSAGES</c> and <c>QUICK_CHAT_MENU</c> describe a split that does not exist:
    ///     each index is a complete bank holding both families, separated by group - group 0 is the
    ///     menu tree and group 1 the message templates, in both. The client proves it by construction,
    ///     building one menu loader and one message loader over the same pair of indexes
    ///     (InterfaceSettings.java:297-300). A tab per index would have had to invent a name for a
    ///     distinction that is not there, so the bank is one selector and the record family is the
    ///     other.
    ///     <para>
    ///     The panel is registered against index 24 because a tab names one index; index 25 is listed
    ///     alongside it exactly as the Tracks tab lists index 11 beside index 6.
    ///     </para>
    /// </remarks>
    public sealed class QuickChatEditorPanel : UserControl {
        /* Consolas 9 on every child. The form puts Consolas 12 on the tab control and everything
           under it inherits, which is half again what these grids are laid out for. */
        private static readonly Font GridFont = new Font("Consolas", 9F);

        private readonly ComboBox banks = new ComboBox {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = GridFont
        };

        private readonly ComboBox families = new ComboBox {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = GridFont
        };

        private readonly Label bankLabel = new Label {
            AutoSize = true,
            Font = GridFont,
            Text = "Bank",
            TextAlign = ContentAlignment.MiddleLeft
        };

        private readonly Label familyLabel = new Label {
            AutoSize = true,
            Font = GridFont,
            Text = "   Records",
            TextAlign = ContentAlignment.MiddleLeft
        };

        //FlowLayoutPanel rather than absolute positions, so the captions and the boxes stay together
        //at whatever font ratio the form scales to.
        private readonly FlowLayoutPanel selector = new FlowLayoutPanel {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        //AutoSize rather than a stated height, so the line the summary needs is the line it gets.
        private readonly Label header = new Label {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = GridFont,
            Text = NoCacheText
        };

        private readonly DefinitionListPanel records = new DefinitionListPanel {
            //Bound with a null cache while nothing is selected, so the panel's own default would
            //claim no cache is loaded while a cache is open behind it.
            EmptyMessage = NoSelectionText
        };

        private readonly DetailFieldGrid fields = new DetailFieldGrid();

        /* No splitter states a minimum size. Setting one re-checks the current distance against it,
           and a container is still at its 150x100 default when a field initialiser runs. */
        private readonly SplitContainer listAndFields = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };

        private const string NoCacheText = "No cache loaded";
        private const string NoSelectionText = "Select a record to see what it holds";

        private RSCache? cache;
        private bool splitterPlaced;

        /// <summary>Creates the panel.</summary>
        public QuickChatEditorPanel() {
            Dock = DockStyle.Fill;

            //Derived from the font rather than written as a pixel count: the form is
            //AutoScaleMode.Font, so a literal width is multiplied at runtime and clips the caption.
            banks.Width = banks.Font.Height * 22;
            families.Width = families.Font.Height * 14;

            BuildLayout();

            banks.SelectedIndexChanged += (_, _) => ShowSelection();
            families.SelectedIndexChanged += (_, _) => ShowSelection();
            records.SelectedRowChanged += (_, _) => ShowRecord(records.SelectedRow as IQuickChatListing);
        }

        /// <summary>
        ///     Points the panel at a cache, or clears it when given none.
        /// </summary>
        /// <remarks>
        ///     <c>Editor.LoadEditorTab</c> calls this on every visit to the tab, so a rebind of the
        ///     cache already on display has to be a no-op or the selected bank is thrown away each
        ///     time. Identity is the right test because opening a cache builds a new
        ///     <see cref="RSCache"/>.
        /// </remarks>
        /// <param name="newCache">The open cache, or null to unbind.</param>
        public void Bind(RSCache? newCache) {
            if (ReferenceEquals(newCache, cache))
                return;

            cache = newCache;
            fields.ClearObjects();
            header.Text = newCache == null ? NoCacheText : NoSelectionText;

            if (newCache == null) {
                records.Bind(null, new QuickChatMenuListDescriptor(RSConstants.QUICK_CHAT_MESSAGES));
                return;
            }

            ShowSelection();
        }

        /// <summary>Places the splitter once the layout pass has given the container a real size.</summary>
        /// <param name="levent">The event data.</param>
        protected override void OnLayout(LayoutEventArgs levent) {
            base.OnLayout(levent);
            PlaceSplitter();
        }

        /// <summary>Divides the panel proportionally, once, when it first has a size worth dividing.</summary>
        /// <remarks>
        ///     A <see cref="SplitContainer"/> defaults to a splitter distance of 50 <i>pixels</i>, not
        ///     half, so the distance has to be stated - and stating it in the designer would make it
        ///     one more literal the form multiplies by its font ratio.
        /// </remarks>
        private void PlaceSplitter() {
            if (splitterPlaced || listAndFields.Height < 200)
                return;

            //Set before the assignment, not after: changing a splitter distance lays the panel out
            //again, and this is called from that layout.
            splitterPlaced = true;

            try {
                listAndFields.SplitterDistance = Math.Max(listAndFields.Panel1MinSize, listAndFields.Height * 3 / 5);
            } catch (InvalidOperationException ex) {
                splitterPlaced = false;
                Debug("Quick chat tab splitter not placed yet: " + ex.Message, LOG_DETAIL.ADVANCED);
            }
        }

        private void BuildLayout() {
            banks.Items.Add(new BankOption(RSConstants.QUICK_CHAT_MESSAGES, "first bank"));
            banks.Items.Add(new BankOption(RSConstants.QUICK_CHAT_MENU, "second bank, ids | 0x8000"));
            banks.SelectedIndex = 0;

            families.Items.Add(FamilyOption.Menus);
            families.Items.Add(FamilyOption.Messages);
            families.SelectedIndex = 0;

            selector.Controls.Add(bankLabel);
            selector.Controls.Add(banks);
            selector.Controls.Add(familyLabel);
            selector.Controls.Add(families);

            listAndFields.Panel1.Controls.Add(records);
            listAndFields.Panel2.Controls.Add(fields);

            //Docking resolves from the end of the Controls collection backwards, so the strips have
            //to be added after the filled splitter, and in bottom-to-top order among themselves.
            Controls.Add(listAndFields);
            Controls.Add(header);
            Controls.Add(selector);

            //Bound before any cache arrives so the record grid has headings from the start.
            records.Bind(null, new QuickChatMenuListDescriptor(RSConstants.QUICK_CHAT_MESSAGES));
        }

        /// <summary>
        ///     Loads whichever group the two selectors name.
        /// </summary>
        /// <remarks>
        ///     A fresh descriptor per selection, because <c>DefinitionListPanel.Bind</c> treats the
        ///     same descriptor instance as the same thing to show and would keep the previous group's
        ///     rows on screen.
        /// </remarks>
        private void ShowSelection() {
            fields.ClearObjects();

            if (cache == null || banks.SelectedItem is not BankOption bank || families.SelectedItem is not FamilyOption family) {
                header.Text = cache == null ? NoCacheText : NoSelectionText;
                return;
            }

            int group = family == FamilyOption.Menus ? QuickChatBank.MenuGroup : QuickChatBank.MessageGroup;
            int declared = cache.GetFileIds(bank.IndexId, group).Length;

            header.Text = "Index " + bank.IndexId + " group " + group + " - " + family + " - " +
                          declared + " record(s) declared";

            records.Bind(cache, family == FamilyOption.Menus
                ? new QuickChatMenuListDescriptor(bank.IndexId)
                : (IDefinitionListDescriptor) new QuickChatMessageListDescriptor(bank.IndexId));
        }

        /// <summary>Fills the field grid from the selected record.</summary>
        /// <remarks>No cache read: the row already carries the whole decoded record.</remarks>
        /// <param name="listing">The selected record, or null.</param>
        private void ShowRecord(IQuickChatListing? listing) {
            fields.ShowFields(listing);

            if (listing != null)
                header.Text = listing.Summary;
        }

        /// <summary>Which of the two banks the list is showing.</summary>
        private sealed class BankOption {
            internal BankOption(int indexId, string role) {
                IndexId = indexId;
                Role = role;
            }

            /// <summary>The cache index, 24 or 25.</summary>
            internal int IndexId { get; }

            /// <summary>What the client uses this bank for, since the constant name does not say.</summary>
            internal string Role { get; }

            /// <summary>The entry as the combo box shows it.</summary>
            /// <returns>The index id and the bank's role.</returns>
            public override string ToString() {
                return "Index " + IndexId + "  " + Role;
            }
        }

        /// <summary>Which record family within a bank the list is showing.</summary>
        /// <remarks>
        ///     An enum rather than two group ids because the group number is an implementation detail
        ///     of the format and the family is what the user is choosing.
        /// </remarks>
        private enum FamilyOption {
            /// <summary>Group 0: the menu tree.</summary>
            Menus,

            /// <summary>Group 1: the message templates.</summary>
            Messages
        }
    }
}
