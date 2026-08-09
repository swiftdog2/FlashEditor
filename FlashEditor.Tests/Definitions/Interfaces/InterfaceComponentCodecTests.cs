using System;
using System.Collections.Generic;
using FlashEditor.Definitions.Interfaces;
using Xunit;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions.Interfaces
{
    /// <summary>
    ///     Pins the index-3 component codec against bytes it did not produce.
    /// </summary>
    /// <remarks>
    ///     Round-tripping this encoder against this decoder proves nothing, so the two sources here
    ///     are the cache and the client. The five captured records are files of the shipped 639 cache
    ///     verbatim, and <c>RealCacheInterfaceTests</c> asserts they still are. The five synthetic
    ///     records are laid out by hand to the read order in <c>RSInterface.unpackConfig</c> and
    ///     exercise the branches the cache cannot reach.
    ///     <para>
    ///     <b>Why the synthetic half exists at all.</b> Every file in this cache stores version byte
    ///     255, so six branches never fire; and the 0x80 name flag, the extended model transform, an
    ///     action high nibble above 1, an operand type byte other than 0 or 1, a slot value of 4095
    ///     and a cp1252 byte that does not round trip all occur zero times. A byte-identity sweep over
    ///     index 3 is evidence about <b>none</b> of them - it would pass just as happily on a decoder
    ///     that had them all wrong. These tests are the only defence, and they must not be replaced by
    ///     a sweep that cannot reach the code they cover.
    ///     </para>
    /// </remarks>
    public class InterfaceComponentCodecTests
    {
        // ===================================================================
        //  Captured from the shipped cache
        // ===================================================================

        /// <summary>Interface 0 component 0: a model with the six-field transform block.</summary>
        private static readonly byte[] ModelComponent =
        {
            0xFF,                                //version 255, if3
            0x06,                                //type 6, model
            0x00, 0x00,                          //content type
            0x00, 0x5C, 0x00, 0x08,              //x 92, y 8
            0x00, 0x25, 0x00, 0xE6,              //width 37, height 230
            0x00, 0x00, 0x00, 0x00,              //width, height, x and y modes
            0x00, 0x01,                          //parent, component 1 of this interface
            0x00,                                //settings
            0x34, 0x39,                          //model 13369
            0x01,                                //model settings: bit 0, six-field block
            0xFF, 0xFE, 0x00, 0x7B,              //model offsets -2, 123
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00,  //rotations
            0x02, 0xF4,                          //zoom 756, unsigned in this block
            0x0D, 0x7D,                          //animation 3453
            0x00, 0x00, 0x00,                    //access mask
            0x00,                                //slot table absent
            0x00,                                //option base
            0x00,                                //action byte
            0x00,                                //selected action
            0x00, 0x00, 0x00,                    //drag deadzone, drag delay, hint slot
            0x00,                                //tooltip
            0x00, 0x00, 0x00, 0x00, 0x00,        //twenty hook arrays, all absent
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00         //five trigger arrays, all absent
        };

        /// <summary>Interface 42 component 24: a line.</summary>
        private static readonly byte[] LineComponent =
        {
            0xFF, 0x09,
            0x00, 0x00,
            0x00, 0x1D, 0x00, 0x78,              //x 29, y 120
            0x01, 0x88, 0x00, 0x00,              //width 392, height 0
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x11,                          //parent, component 17
            0x00,
            0x01,                                //line width 1
            0x00, 0x33, 0x33, 0x33,              //colour
            0x00,                                //not flipped
            0x00, 0x00, 0x00,                    //access mask
            0x00, 0x00, 0x00, 0x00,              //slot table, option base, action, selected action
            0x00, 0x00, 0x00,                    //deadzone, delay, hint
            0x00,                                //tooltip
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00
        };

        /// <summary>
        ///     Interface 535 component 39: a rectangle with an action byte whose high nibble is 1.
        /// </summary>
        /// <remarks>
        ///     One of only 139 files in the cache that takes the first action block. The nibble is
        ///     never stored anywhere, so this is the record that proves the block is read at all.
        /// </remarks>
        private static readonly byte[] RectangleComponent =
        {
            0xFF, 0x03,
            0x00, 0x00,
            0x00, 0x0E, 0x00, 0x08,              //x 14, y 8
            0x00, 0x5B, 0x00, 0x29,              //width 91, height 41
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x16,                          //parent, component 22
            0x00,
            0x00, 0xFF, 0xFF, 0x00,              //colour 0x00FFFF00
            0x01,                                //filled
            0xFA,                                //transparency 250
            0x00, 0x00, 0x02,                    //access mask, below the gate bits
            0x00,                                //slot table absent
            0x00,                                //option base
            0x11,                                //action byte: high nibble 1, one option
            0x4F, 0x70, 0x65, 0x6E, 0x00,        //"Open"
            0x00, 0x00, 0x31,                    //first action block: index 0, value 49
            0x00,                                //selected action
            0x00, 0x00, 0x00,
            0x00,                                //tooltip
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00
        };

        /// <summary>
        ///     Interface 746 component 2: a layer carrying a four-entry slot table and a hook.
        /// </summary>
        /// <remarks>
        ///     One of the four files in the cache whose slot table has more than one entry. The client
        ///     scatters the entries into three parallel arrays keyed by slot, which loses their order;
        ///     this record is what shows the codec keeps it.
        /// </remarks>
        private static readonly byte[] LayerWithSlotsComponent =
        {
            0xFF, 0x00,
            0x00, 0x00,
            0x00, 0x2B, 0x00, 0x0A,              //x 43, y 10
            0x00, 0x66, 0x00, 0x24,              //width 102, height 36
            0x00, 0x00, 0x00, 0x00,
            0xFF, 0xFF,                          //no parent
            0x00,
            0x00, 0x00, 0x00, 0x00,              //scroll extents
            0x00,                                //layer flag byte
            0x00, 0x00, 0x00,                    //access mask
            0x10, 0x01, 0x62, 0x00,              //slot 0, value 1, bytes 98 and 0
            0x20, 0x01, 0x63, 0x00,              //slot 1, value 1, bytes 99 and 0
            0x30, 0x01, 0x61, 0x00,              //slot 2, value 1, bytes 97 and 0
            0x40, 0x01, 0x60, 0x00,              //slot 3, value 1, bytes 96 and 0
            0x00,                                //slot table terminator
            0x00,                                //option base
            0x00,                                //action byte
            0x00,                                //selected action
            0x00, 0x00, 0x00,
            0x00,                                //tooltip
            0x00, 0x00, 0x00, 0x00, 0x00,        //hooks 0..8 absent
            0x00, 0x00, 0x00, 0x00,
            0x02,                                //hook 9: two operands
            0x00, 0x00, 0x00, 0x04, 0xD2,        //integer 1234
            0x00, 0x80, 0x00, 0x00, 0x04,        //integer -2147483644
            0x00, 0x00, 0x00, 0x00, 0x00,        //hooks 10..19 absent
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00         //five trigger arrays, all absent
        };

        /// <summary>
        ///     Interface 747 component 14: a layer whose access mask opens the three target shorts.
        /// </summary>
        /// <remarks>
        ///     One of only 140 files that takes the gate, and it settles the reader as well as the
        ///     mask. Its mask is <c>0x005000</c>, which is non-zero only in bits 12 and 14 - so a
        ///     decoder using the little-endian 24-bit reader (<c>RSBuffer.method1192</c>) would read
        ///     <c>0x000050</c> instead, miss the gate and desynchronise by six bytes.
        /// </remarks>
        private static readonly byte[] TargetGatedComponent =
        {
            0xFF, 0x00,
            0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,              //x 0, y 0
            0x00, 0x39, 0x00, 0x22,              //width 57, height 34
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x08,                          //parent, component 8
            0x00,
            0x00, 0x00, 0x00, 0x00,              //scroll extents
            0x00,                                //layer flag byte
            0x00, 0x50, 0x00,                    //access mask 0x005000, big-endian
            0x00,                                //slot table absent
            0x00,                                //option base
            0x00,                                //action byte
            0x00,                                //selected action
            0x00, 0x00, 0x00,
            0x41, 0x74, 0x74, 0x61, 0x63, 0x6B, 0x00,        //tooltip "Attack"
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,  //three target shorts, all the sentinel
            0x00, 0x00, 0x00,                    //hooks 0..2 absent
            0x02,                                //hook 3: two operands
            0x00, 0x00, 0x00, 0x00, 0x12,        //integer 18
            0x00, 0x02, 0xEB, 0x00, 0x06,        //integer 48955398
            0x02,                                //hook 4: two operands
            0x00, 0x00, 0x00, 0x00, 0x11,        //integer 17
            0x00, 0x02, 0xEB, 0x00, 0x06,        //integer 48955398
            0x00, 0x00, 0x00, 0x00, 0x00,        //hooks 5..19 absent
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00         //five trigger arrays, all absent
        };

        /// <summary>
        ///     The captured records with the addresses they were read from.
        /// </summary>
        /// <remarks>
        ///     Exposed so <c>RealCacheInterfaceTests</c> can check each is still what the cache
        ///     stores, without a second copy of the bytes.
        /// </remarks>
        /// <returns>Group, file and a fresh copy of the bytes, for each captured record.</returns>
        public static IEnumerable<(int GroupId, int FileId, byte[] Bytes)> CapturedComponents()
        {
            yield return (0, 0, (byte[])ModelComponent.Clone());
            yield return (42, 24, (byte[])LineComponent.Clone());
            yield return (535, 39, (byte[])RectangleComponent.Clone());
            yield return (746, 2, (byte[])LayerWithSlotsComponent.Clone());
            yield return (747, 14, (byte[])TargetGatedComponent.Clone());
        }

        /// <summary>Every captured record consumes its buffer exactly and re-encodes to it.</summary>
        [Fact]
        public void EveryCapturedComponent_ConsumesExactlyAndReEncodesToItsBytes()
        {
            foreach ((int groupId, int fileId, byte[] bytes) in CapturedComponents())
            {
                var stream = new JagStream(bytes);
                InterfaceComponentDefinition component =
                    new InterfaceComponentDefinition(groupId, fileId).Decode(stream);

                Assert.Equal(bytes.Length, stream.Position);
                Assert.Equal(bytes, component.Encode().ToArray());
            }
        }

        /// <summary>
        ///     A model component decodes into the fields the CS2 setters name, not the ones the
        ///     decompiled identifiers do.
        /// </summary>
        /// <remarks>
        ///     This is what settles the one-position shift in the client's field names. Read by the
        ///     identifiers, this record has an unsigned X of 92 and a signed Y of 8, a signed width of
        ///     37 and an unsigned height of 230 - and a "content type" of 92, which nothing compares
        ///     against anything. Read the way <c>if_setposition</c> and <c>if_setsize</c> write them,
        ///     every value lands where the renderer uses it.
        /// </remarks>
        [Fact]
        public void ACapturedModel_DecodesIntoTheFieldsTheCs2SettersName()
        {
            InterfaceComponentDefinition component = Decode(0, 0, ModelComponent);

            Assert.Equal(-1, component.Version);
            Assert.Equal(6, component.ComponentType);
            Assert.Null(component.AuthoringName);
            Assert.Equal(0, component.ContentType);
            Assert.Equal(92, component.BasePositionX);
            Assert.Equal(8, component.BasePositionY);
            Assert.Equal(37, component.BaseWidth);
            Assert.Equal(230, component.BaseHeight);

            //The parent is stored as a sixteen-bit sibling index and folded into a component id.
            Assert.Equal(1, component.RawParentId);
            Assert.Equal(1, component.ParentComponentId);
            Assert.False(component.IsHidden);

            Assert.Equal(13369, component.ModelId);
            Assert.True(component.HasModelTransform);
            Assert.False(component.HasExtendedModelTransform);
            Assert.Equal(-2, component.ModelOffsetX);
            Assert.Equal(123, component.ModelOffsetY);
            Assert.Equal(756, component.ModelZoom);
            Assert.Equal(3453, component.AnimationId);

            Assert.Equal(0, component.HookArrayCount);
            Assert.Equal(0, component.TriggerArrayCount);
        }

        /// <summary>The parent fold folds this component's own group into the high half.</summary>
        /// <remarks>
        ///     Decoding the same bytes at a different address has to give a different parent id and
        ///     the same stored short. That is the whole content of the fold, and it is why the encoder
        ///     writes <see cref="InterfaceComponentDefinition.RawParentId"/> rather than the decoded
        ///     value - which for interface 535 is 35,127,318 and does not fit a short.
        /// </remarks>
        [Fact]
        public void TheParentFold_CarriesTheGroupIdInTheHighHalf()
        {
            InterfaceComponentDefinition component = Decode(535, 39, RectangleComponent);

            Assert.Equal(22, component.RawParentId);
            Assert.Equal((535 << 16) | 22, component.ParentComponentId);
            Assert.Equal(RectangleComponent, component.Encode().ToArray());

            component.SetParent((535 << 16) | 9);
            Assert.Equal(9, component.RawParentId);

            component.SetParent(-1);
            Assert.Equal(InterfaceComponentDefinition.NoParent, component.RawParentId);
            Assert.Equal(-1, component.ParentComponentId);

            //A parent in another interface cannot be expressed, so it is refused rather than silently
            //truncated to its low sixteen bits.
            Assert.Throws<ArgumentOutOfRangeException>(() => component.SetParent((536 << 16) | 9));
        }

        /// <summary>The slot table keeps its entries in stream order, duplicates and all.</summary>
        [Fact]
        public void ASlotTable_KeepsItsEntriesInStreamOrder()
        {
            InterfaceComponentDefinition component = Decode(746, 2, LayerWithSlotsComponent);

            Assert.Equal(4, component.Slots.Count);
            Assert.Equal(new[] { 0, 1, 2, 3 }, Map(component.Slots, slot => slot.Slot));
            Assert.Equal(new[] { 1, 1, 1, 1 }, Map(component.Slots, slot => slot.RawValue));
            Assert.Equal(new[] { 98, 99, 97, 96 }, Map(component.Slots, slot => (int)slot.First));

            Assert.Equal(2, component.Hooks[9].Length);
            Assert.Equal(1234, component.Hooks[9][0].Integer);
            Assert.Equal(-2147483644, component.Hooks[9][1].Integer);
            Assert.Equal(1, component.HookArrayCount);
        }

        /// <summary>
        ///     The access mask is read big-endian, and the gate is bits 11 to 17 of it.
        /// </summary>
        [Fact]
        public void TheAccessMask_IsBigEndianAndGatesOnBits11To17()
        {
            InterfaceComponentDefinition component = Decode(747, 14, TargetGatedComponent);

            Assert.Equal(0x005000, component.AccessMask);
            Assert.True(component.HasTargetShorts);
            Assert.Equal(InterfaceComponentDefinition.NoReference, component.RawTargetVerb);
            Assert.Equal(InterfaceComponentDefinition.NoReference, component.RawTargetCursor);
            Assert.Equal(InterfaceComponentDefinition.NoReference, component.RawTargetOperand);
            Assert.Equal("Attack", component.Tooltip.Text);

            //The little-endian reader in the same client class would produce this instead, and it
            //does not take the gate - which is where a decoder that picked the wrong one desyncs.
            Assert.Equal(0, 0x000050 & InterfaceComponentDefinition.TargetGateMask);
        }

        /// <summary>The first action block is read when the high nibble is above 0.</summary>
        [Fact]
        public void AnActionHighNibble_TakesTheFirstOptionBlock()
        {
            InterfaceComponentDefinition component = Decode(535, 39, RectangleComponent);

            Assert.Equal(1, component.ActionHighNibble);
            Assert.Single(component.ContextOptions);
            Assert.Equal("Open", component.ContextOptions[0].Text);
            Assert.Equal(0, component.OptionArrayIndex1);
            Assert.Equal(49, component.OptionArrayValue1);
            Assert.Equal(16776960, component.Colour);
            Assert.True(component.RectangleFilled);
            Assert.Equal(250, component.Transparency);
        }

        // ===================================================================
        //  Branches this cache cannot reach
        // ===================================================================

        /// <summary>
        ///     An if1 record, which opens all six version-gated branches at once.
        /// </summary>
        /// <remarks>
        ///     Version byte 0 rather than 255. Laid out by hand to
        ///     <c>RSInterface.java:1065-1069, 1163-1165, 1279-1307, 1320-1322</c>: the settings bit,
        ///     the trailing text byte, <c>anInt2317</c>, the parameter table and the twenty-first hook
        ///     array. <b>Nothing in this cache exercises any of them</b>, so this record is the only
        ///     test they have, and its type-0 counterpart is the branch the cache always takes -
        ///     see <see cref="AnIf1Layer_HasNoTrailingFlagByte"/>.
        /// </remarks>
        private static readonly byte[] If1Component =
        {
            0x00,                                //version 0, so every gate below opens
            0x04,                                //type 4, text
            0x00, 0x00,
            0x00, 0x0A, 0x00, 0x14,              //x 10, y 20
            0x00, 0x1E, 0x00, 0x28,              //width 30, height 40
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x07,                          //parent, component 7
            0x03,                                //settings: hidden, plus the version-gated bit 1
            0x00, 0x0C,                          //font 12
            0x68, 0x69, 0x00,                    //"hi"
            0x0F, 0x01, 0x02,                    //line height, horizontal and vertical alignment
            0x01,                                //shadow
            0x00, 0xFF, 0x00, 0x00,              //colour
            0x20,                                //transparency
            0x2A,                                //the version-gated trailing text byte
            0x00, 0x08, 0x00,                    //access mask, bit 11 set so the gate opens
            0x00,                                //slot table absent
            0x00,                                //option base
            0x00,                                //action byte
            0x00,                                //selected action
            0x00, 0x00, 0x00,
            0x00,                                //tooltip
            0xFF, 0xFF, 0x00, 0x05, 0xFF, 0xFF,  //three target shorts
            0x01, 0x02,                          //the version-gated short, 258
            0x01,                                //one integer parameter
            0x00, 0x00, 0x2A,                    //key 42, read by the big-endian 24-bit reader
            0x00, 0x00, 0x01, 0x00,              //value 256
            0x01,                                //one string parameter
            0x00, 0x00, 0x2B,                    //key 43
            0x00, 0x6F, 0x6B, 0x00,              //version byte then "ok"
            0x01, 0x00, 0x00, 0x00, 0x00, 0x63,  //hook 0: one integer operand, 99
            0x00, 0x00, 0x00, 0x00,              //hooks 1..9 absent
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x01, 0x01, 0x61, 0x00,              //the version-gated hook: one string operand, "a"
            0x00, 0x00, 0x00, 0x00, 0x00,        //hooks 10..19 absent
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x02, 0x00, 0x00, 0x00, 0x01,        //trigger 0: two entries, 1 and 2
            0x00, 0x00, 0x00, 0x02,
            0x00, 0x00, 0x00, 0x00               //triggers 1..4 absent
        };

        /// <summary>
        ///     A component with the type byte's 0x80 name flag set.
        /// </summary>
        /// <remarks>
        ///     Set on 0 of the 42,256 files here. The name is colon-delimited: CS2 6702 takes the
        ///     substring before the first ':' (<c>Class247.java:7051-7056</c>).
        /// </remarks>
        private static readonly byte[] NamedComponent =
        {
            0xFF,
            0x8A,                                //0x80 name flag over type 10
            0x6E, 0x61, 0x6D, 0x65, 0x3A, 0x78, 0x00,   //"name:x"
            0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0xFF, 0xFF,
            0x00,
            0x00, 0x00, 0x00,                    //access mask
            0x00, 0x00, 0x00, 0x00,              //slot table, option base, action, selected action
            0x00, 0x00, 0x00,
            0x00,                                //tooltip
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00
        };

        /// <summary>
        ///     A model taking the seven-field transform block, with a slot value of 4095 and an action
        ///     high nibble of 2.
        /// </summary>
        /// <remarks>
        ///     Three branches the cache reaches zero times, in one record. The zoom in this block is
        ///     <b>signed</b> where the six-field block's is unsigned, so -100 here is 65,436 to a
        ///     decoder that copied the other block's reader.
        /// </remarks>
        private static readonly byte[] ExtendedModelComponent =
        {
            0xFF, 0x06,
            0x00, 0x00,
            0x00, 0x01, 0x00, 0x02,              //x 1, y 2
            0x00, 0x03, 0x00, 0x04,              //width 3, height 4
            0x01, 0x02, 0x03, 0x04,              //width mode 1 and height mode 2 open two extra shorts
            0x00, 0x09,
            0x00,
            0x12, 0x34,                          //model 0x1234
            0x02,                                //model settings: bit 1 with bit 0 clear
            0xFF, 0xFF, 0x00, 0x02,              //offsets -1, 2
            0xFF, 0xFE,                          //the extended block's extra signed short, -2
            0x00, 0x10, 0x00, 0x20, 0x00, 0x30,  //rotations
            0xFF, 0x9C,                          //zoom -100, signed only in this block
            0x00, 0x64,                          //animation 100
            0x00, 0x0A,                          //width mode extra
            0x00, 0x0B,                          //height mode extra
            0x00, 0x00, 0x00,                    //access mask
            0xBF, 0xFF, 0x01, 0xFF,              //slot 10, value 4095, bytes 1 and -1
            0x11, 0x23, 0x00, 0x00,              //slot 0, value 0x123
            0x00,                                //slot table terminator
            0x00,                                //option base
            0x21,                                //action byte: high nibble 2, one option
            0x41, 0x00,                          //"A"
            0x00, 0x00, 0x2A,                    //first action block
            0x01, 0x00, 0x2B,                    //second action block, which nothing in the cache takes
            0x00,
            0x00, 0x00, 0x00,
            0x00,
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00
        };

        /// <summary>
        ///     A hook array holding an operand whose type byte is neither 0 nor 1.
        /// </summary>
        /// <remarks>
        ///     Type 7 reads no payload at all and leaves the element null in the client, so 2..255
        ///     are aliases of each other. The 47,538 operands in this cache are 46,033 ints and 1,505
        ///     strings and nothing else.
        /// </remarks>
        private static readonly byte[] UnknownOperandComponent =
        {
            0xFF, 0x0A,
            0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0xFF, 0xFF,
            0x00,
            0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00,
            0x00,
            0x02,                                //hook 0: two operands
            0x07,                                //type 7, which reads nothing
            0x00, 0x00, 0x00, 0x00, 0x05,        //type 0, integer 5
            0x00, 0x00, 0x00, 0x00, 0x00,        //hooks 1..19 absent
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00
        };

        /// <summary>
        ///     A component whose tooltip holds a byte the cache's cp1252 cannot round trip.
        /// </summary>
        /// <remarks>
        ///     0x81 is one of the five unassigned slots in the 0x80-0x9F band. It decodes to '?' and
        ///     '?' re-encodes to 0x3F, so a codec that kept only the decoded text would rewrite this
        ///     file. No string in index 3 carries one, which is exactly why the sweep cannot catch it.
        /// </remarks>
        private static readonly byte[] LossyStringComponent =
        {
            0xFF, 0x0A,
            0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0xFF, 0xFF,
            0x00,
            0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00,
            0x81, 0x41, 0x00,                    //tooltip: an unassigned cp1252 slot, then 'A'
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00
        };

        /// <summary>Every synthetic record consumes its buffer exactly and re-encodes to it.</summary>
        /// <remarks>
        ///     Exact consumption is the sharp half. A gated branch read when it should not be, or
        ///     skipped when it should not be, shifts every field after it - and on the if1 record that
        ///     means five separate branches each of which would otherwise be a silent mis-parse.
        /// </remarks>
        [Fact]
        public void EverySyntheticComponent_ConsumesExactlyAndReEncodesToItsBytes()
        {
            foreach (byte[] bytes in new[]
            {
                If1Component, NamedComponent, ExtendedModelComponent,
                UnknownOperandComponent, LossyStringComponent
            })
            {
                var stream = new JagStream(bytes);
                InterfaceComponentDefinition component =
                    new InterfaceComponentDefinition(3, 4).Decode(stream);

                Assert.Equal(bytes.Length, stream.Position);
                Assert.Equal(bytes, component.Encode().ToArray());
            }
        }

        /// <summary>An if1 record opens all six of the version-gated branches.</summary>
        [Fact]
        public void AnIf1Component_OpensEveryVersionGatedBranch()
        {
            InterfaceComponentDefinition component = Decode(3, 4, If1Component);

            Assert.Equal(0, component.Version);
            Assert.Equal(0x03, component.SettingsFlags);
            Assert.True(component.IsHidden);

            //Type 4's trailing byte, read only when the version is non-negative.
            Assert.Equal(0x2A, component.TextVersionedByte);
            Assert.Equal("hi", component.Message.Text);
            Assert.Equal(12, component.FontId);

            //The three target shorts, then anInt2317, then the parameter table.
            Assert.True(component.HasTargetShorts);
            Assert.Equal(5, component.RawTargetCursor);
            Assert.Equal(258, component.RawVersionedShort);

            Assert.Single(component.IntegerParameters);
            Assert.Equal(42, component.IntegerParameters[0].Key);
            Assert.Equal(256, component.IntegerParameters[0].Integer);
            Assert.Single(component.StringParameters);
            Assert.Equal(43, component.StringParameters[0].Key);
            Assert.Equal("ok", component.StringParameters[0].Text!.Text);

            //The twenty-first hook array sits between slots 9 and 10, so reading it in the wrong
            //place shifts the last ten hooks and all five trigger arrays.
            Assert.Single(component.VersionedHook);
            Assert.Equal(InterfaceScriptOperand.StringType, component.VersionedHook[0].TypeByte);
            Assert.Equal("a", component.VersionedHook[0].Text!.Text);
            Assert.Equal(99, component.Hooks[0][0].Integer);
            Assert.Equal(1, component.HookArrayCount);
            Assert.Equal(new[] { 1, 2 }, component.Triggers[0]);
        }

        /// <summary>
        ///     An if1 layer takes its flag from the settings byte instead of a trailing byte.
        /// </summary>
        /// <remarks>
        ///     The mirror image of every other version gate: this is the one branch the cache
        ///     <i>always</i> takes and an if1 file never would. Built by hand rather than captured,
        ///     because it is the absence of a byte that has to be proven.
        /// </remarks>
        [Fact]
        public void AnIf1Layer_HasNoTrailingFlagByte()
        {
            var built = new List<byte>
            {
                0x00,                                                   //version 0
                0x00,                                                   //type 0, layer
                0x00, 0x00,                                             //content type
                0x00, 0x00, 0x00, 0x00,                                 //x, y
                0x00, 0x00, 0x00, 0x00,                                 //width, height
                0x00, 0x00, 0x00, 0x00,                                 //modes
                0xFF, 0xFF,                                             //no parent
                0x02,                                                   //settings bit 1 carries the flag
                0x00, 0x11, 0x00, 0x22                                  //scroll extents
                //and no trailing flag byte, because the version is non-negative
            };
            built.AddRange(new byte[] { 0x00, 0x00, 0x00 });             //access mask, below the gate
            built.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 });       //slots, base, action, selected
            built.AddRange(new byte[] { 0x00, 0x00, 0x00 });             //deadzone, delay, hint
            built.Add(0x00);                                             //tooltip
            built.AddRange(new byte[] { 0x00, 0x00 });                   //the version-gated short
            built.AddRange(new byte[] { 0x00, 0x00 });                   //two empty parameter tables
            built.AddRange(new byte[21]);                                //ten hooks, the extra one, ten hooks
            built.AddRange(new byte[5]);                                 //five trigger arrays

            byte[] bytes = built.ToArray();
            var stream = new JagStream(bytes);
            InterfaceComponentDefinition component =
                new InterfaceComponentDefinition(3, 4).Decode(stream);

            Assert.Equal(bytes.Length, stream.Position);
            Assert.Equal(0x11, component.ScrollMaxHorizontal);
            Assert.Equal(0x22, component.ScrollMaxVertical);
            Assert.Equal(bytes, component.Encode().ToArray());

            //And the captured if3 layer does carry the byte, which is the same statement from the
            //other side: the same field, two different encodings, chosen by the version.
            InterfaceComponentDefinition if3Layer = Decode(746, 2, LayerWithSlotsComponent);
            Assert.Equal(-1, if3Layer.Version);
            Assert.Equal(0, if3Layer.LayerFlagByte);
        }

        /// <summary>The 0x80 type-byte flag reads a name and is rebuilt from its presence.</summary>
        [Fact]
        public void TheNameFlag_ReadsAnAuthoringNameAndIsRebuiltFromIt()
        {
            InterfaceComponentDefinition component = Decode(3, 4, NamedComponent);

            Assert.Equal(10, component.ComponentType);
            Assert.NotNull(component.AuthoringName);
            Assert.Equal("name:x", component.AuthoringName!.Text);

            //Dropping the name has to clear the flag, or the re-encode reads its own type byte as a
            //name marker and desynchronises on the very next field.
            component.AuthoringName = null;
            byte[] withoutName = component.Encode().ToArray();
            Assert.Equal(0x0A, withoutName[1]);
            Assert.Equal(NamedComponent.Length - 7, withoutName.Length);
        }

        /// <summary>The seven-field model block reads a signed zoom and two extra shorts.</summary>
        [Fact]
        public void TheExtendedModelBlock_ReadsASignedZoom()
        {
            InterfaceComponentDefinition component = Decode(3, 4, ExtendedModelComponent);

            Assert.False(component.HasModelTransform);
            Assert.True(component.HasExtendedModelTransform);
            Assert.Equal(-1, component.ModelOffsetX);
            Assert.Equal(2, component.ModelOffsetY);
            Assert.Equal(-2, component.ModelExtendedOffset);
            Assert.Equal(48, component.ModelRotateZ);

            //Signed here, unsigned in the six-field block. A decoder that shared one reader would
            //answer 65,436.
            Assert.Equal(-100, component.ModelZoom);

            Assert.Equal(100, component.AnimationId);
            Assert.Equal(10, component.ModelWidthExtra);
            Assert.Equal(11, component.ModelHeightExtra);
        }

        /// <summary>A slot value of 4095 decodes to -1 and encodes back to 4095.</summary>
        [Fact]
        public void ASlotValueOf4095_DecodesToMinusOneAndSurvivesTheHeaderPacking()
        {
            InterfaceComponentDefinition component = Decode(3, 4, ExtendedModelComponent);

            Assert.Equal(2, component.Slots.Count);
            Assert.Equal(InterfaceSlotEntry.MaxSlot, component.Slots[0].Slot);
            Assert.Equal(InterfaceSlotEntry.NoValue, component.Slots[0].RawValue);
            Assert.Equal(-1, component.Slots[0].Value);
            Assert.Equal(1, component.Slots[0].First);
            Assert.Equal(-1, component.Slots[0].Second);

            Assert.Equal(0, component.Slots[1].Slot);
            Assert.Equal(0x123, component.Slots[1].RawValue);

            //The header byte packs the slot index above the value's top four bits, which is what makes
            //the raw twelve-bit value load bearing rather than cosmetic.
            byte[] encoded = component.Encode().ToArray();
            Assert.Equal(ExtendedModelComponent, encoded);
        }

        /// <summary>Both action blocks are read when the high nibble is above 1.</summary>
        [Fact]
        public void AnActionHighNibbleAbove1_TakesBothOptionBlocks()
        {
            InterfaceComponentDefinition component = Decode(3, 4, ExtendedModelComponent);

            Assert.Equal(2, component.ActionHighNibble);
            Assert.Single(component.ContextOptions);
            Assert.Equal(0, component.OptionArrayIndex1);
            Assert.Equal(42, component.OptionArrayValue1);
            Assert.Equal(1, component.OptionArrayIndex2);
            Assert.Equal(43, component.OptionArrayValue2);

            //2 through 15 all read exactly two blocks, and the nibble is stored nowhere the client can
            //read back - so it has to be kept rather than recomputed from "how many blocks there are".
            component.ActionHighNibble = 9;
            byte[] encoded = component.Encode().ToArray();
            Assert.Equal(0x91, encoded[55]);
            Assert.Equal(ExtendedModelComponent.Length, encoded.Length);
        }

        /// <summary>An unknown operand type byte reads no payload and is written back unchanged.</summary>
        [Fact]
        public void AnUnknownOperandType_ReadsNoPayloadAndKeepsItsByte()
        {
            InterfaceComponentDefinition component = Decode(3, 4, UnknownOperandComponent);

            Assert.Equal(2, component.Hooks[0].Length);
            Assert.Equal(7, component.Hooks[0][0].TypeByte);
            Assert.Null(component.Hooks[0][0].Text);
            Assert.Equal(InterfaceScriptOperand.IntegerType, component.Hooks[0][1].TypeByte);
            Assert.Equal(5, component.Hooks[0][1].Integer);
        }

        /// <summary>
        ///     A string byte that cp1252 cannot round trip survives, because the raw bytes are kept.
        /// </summary>
        /// <remarks>
        ///     The second half is the point: writing the decoded text back is what a codec that stored
        ///     only the string would do on every save, and it changes the file. Nothing in index 3
        ///     triggers it today, so this is the whole defence.
        /// </remarks>
        [Fact]
        public void ALossyCp1252Byte_SurvivesBecauseTheRawBytesAreKept()
        {
            InterfaceComponentDefinition component = Decode(3, 4, LossyStringComponent);

            Assert.Equal("?A", component.Tooltip.Text);
            Assert.Equal(new byte[] { 0x81, 0x41 }, component.Tooltip.RawBytes());
            Assert.Equal(LossyStringComponent, component.Encode().ToArray());

            //Assigning back the text the getter just returned is lossy, which is what the raw capture
            //exists to avoid doing on a save nobody asked for.
            string decoded = component.Tooltip.Text;
            component.Tooltip.Text = decoded;
            Assert.Equal(new byte[] { 0x3F, 0x41 }, component.Tooltip.RawBytes());
            Assert.NotEqual(LossyStringComponent, component.Encode().ToArray());
        }

        /// <summary>An absent hook array and an empty one are the same single zero byte.</summary>
        /// <remarks>
        ///     Stated as a test rather than as a comment, because it is the reason there is no
        ///     "was this null or empty" flag on the hook arrays. The format cannot express a
        ///     present-but-empty array, so a count of 0 <i>is</i> absence and there is no encoding to
        ///     record.
        /// </remarks>
        [Fact]
        public void AnEmptyHookArray_IsTheSameByteAsAnAbsentOne()
        {
            InterfaceComponentDefinition component = Decode(42, 24, LineComponent);

            Assert.All(component.Hooks, hook => Assert.Empty(hook));
            Assert.All(component.Triggers, trigger => Assert.Empty(trigger));

            component.Hooks[4] = Array.Empty<InterfaceScriptOperand>();
            component.Triggers[2] = Array.Empty<int>();

            Assert.Equal(LineComponent, component.Encode().ToArray());
        }

        private static InterfaceComponentDefinition Decode(int groupId, int fileId, byte[] bytes)
        {
            return new InterfaceComponentDefinition(groupId, fileId)
                .Decode(new JagStream((byte[])bytes.Clone()));
        }

        private static int[] Map(IReadOnlyList<InterfaceSlotEntry> slots, Func<InterfaceSlotEntry, int> read)
        {
            var values = new int[slots.Count];
            for (int i = 0; i < slots.Count; i++)
                values[i] = read(slots[i]);
            return values;
        }
    }
}
