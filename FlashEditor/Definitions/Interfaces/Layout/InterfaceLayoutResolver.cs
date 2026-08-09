using System;
using System.Collections.Generic;

namespace FlashEditor.Definitions.Interfaces.Layout {
    /// <summary>
    ///     What the resolver had to work around while laying a component out.
    /// </summary>
    /// <remarks>
    ///     Reported rather than thrown or silently absorbed. A component the client would refuse to
    ///     draw must still appear in the editor, and the user has to be able to tell a component
    ///     that is genuinely off screen from one the resolver could not compute.
    /// </remarks>
    [Flags]
    public enum InterfaceLayoutDiagnostics {
        /// <summary>Nothing unusual.</summary>
        None = 0,

        /// <summary>
        ///     An aspect-ratio cross-link was skipped because its denominator was zero.
        /// </summary>
        /// <remarks>
        ///     The client throws <c>ArithmeticException</c> here (<c>Class253.java:344</c>,
        ///     <c>:348</c>). Unreachable from stored bytes - the aspect pair is CS2-only and defaults
        ///     to 1:1 - so nothing on disk exercises it.
        /// </remarks>
        DegenerateAspect = 1 << 0,

        /// <summary>The component's parent field names a file its group does not hold.</summary>
        DanglingParent = 1 << 1,

        /// <summary>The component is in a parent cycle, so no root reaches it.</summary>
        CyclicParent = 1 << 2,

        /// <summary>
        ///     The component's parent is not a layer, so the client never lays it out or draws it.
        /// </summary>
        /// <remarks>
        ///     Recursion happens only through type 0 (<c>Class224_Sub2.java:173</c>, and the draw
        ///     pass agrees at <c>Node_Sub10_Sub24.java:407</c>). A component parented to a sprite or
        ///     a rectangle is dead weight in the file.
        /// </remarks>
        ParentIsNotALayer = 1 << 3
    }

    /// <summary>One component's resolved geometry.</summary>
    public sealed class InterfaceLayoutNode {
        internal InterfaceLayoutNode(InterfaceComponentDefinition component, int depth,
            InterfaceRect relative, InterfaceRect absolute, InterfaceRect clip, bool isDrawn,
            InterfaceParentage parentage, InterfaceLayoutDiagnostics diagnostics) {
            Component = component;
            Depth = depth;
            Relative = relative;
            Absolute = absolute;
            Clip = clip;
            IsDrawn = isDrawn;
            Parentage = parentage;
            Diagnostics = diagnostics;
        }

        /// <summary>The component this describes.</summary>
        public InterfaceComponentDefinition Component { get; }

        /// <summary>How far below a root it sits. A root is 0.</summary>
        public int Depth { get; }

        /// <summary>Its rectangle relative to its parent's content origin.</summary>
        public InterfaceRect Relative { get; }

        /// <summary>Its rectangle in canvas coordinates.</summary>
        public InterfaceRect Absolute { get; }

        /// <summary>
        ///     The rectangle it is clipped to.
        /// </summary>
        /// <remarks>
        ///     Not simply <see cref="Absolute"/> intersected with the inherited clip: type 2 passes
        ///     the inherited clip through untouched, and type 9 extends the right and bottom by one
        ///     pixel because a line's endpoint is inclusive.
        /// </remarks>
        public InterfaceRect Clip { get; }

        /// <summary>Whether the client would lay this component out and draw it at all.</summary>
        public bool IsDrawn { get; }

        /// <summary>How the component is attached to its interface.</summary>
        public InterfaceParentage Parentage { get; }

        /// <summary>What the resolver had to work around.</summary>
        public InterfaceLayoutDiagnostics Diagnostics { get; }
    }

    /// <summary>
    ///     Turns an interface's four layout mode bytes into pixel rectangles, ported from the 637
    ///     client.
    /// </summary>
    /// <remarks>
    ///     <b>What this is.</b> Every component stores a base position, a base size, and four mode
    ///     bytes that decide how those resolve against its parent. The bytes have decoded for as
    ///     long as the codec has existed and nothing has ever computed a rectangle from them, which
    ///     is why the interface tab has no canvas: there is no hit testing and no drag without one.
    ///     <para>
    ///     <b>The client's two resolvers take their parent extents in OPPOSITE argument orders.</b>
    ///     <c>Class253.method3180(parentHeight, parentWidth, ...)</c> sizes;
    ///     <c>KeyStroke.method986(parentWidth, parentHeight, ...)</c> positions. Settled from the
    ///     call sites - <c>Class224_Sub2.java:154-155</c> and <c>Node_Sub45.java:60-61</c>, the
    ///     second of which passes the canvas globals and so names each argument outright. Getting
    ///     this backwards produces a layout that is plausible everywhere and correct nowhere.
    ///     </para>
    ///     <para>
    ///     <b>Which resolved field is which axis is settled by a JDK signature, not by a name.</b>
    ///     <c>Node_Sub10_Sub24.java:102-104</c> passes the four values to
    ///     <c>java.awt.Rectangle.setBounds(int x, int y, int width, int height)</c>, which cannot be
    ///     obfuscated. The obfuscated wire field names are shifted by one position - the client's
    ///     <c>RSInterface.height</c> is the base <i>width</i> and its <c>width</c> is the base
    ///     <i>position Y</i> - and this project's decoder already uses the corrected names.
    ///     </para>
    ///     <para>
    ///     <b>Sizing runs before positioning</b>, because both position axes read the extent the
    ///     sizing pass just wrote (<c>KeyStroke.java:16, 21, 24, 30, 35, 39, 41, 47</c>). Both call
    ///     sites order them that way.
    ///     </para>
    ///     <para>
    ///     <b>Arithmetic is 32-bit signed and wrapping, and the two integer operations are not
    ///     interchangeable.</b> <c>&gt;&gt;</c> is an arithmetic shift and floors toward negative
    ///     infinity; <c>/</c> truncates toward zero. They differ for every negative numerator that
    ///     is not an exact multiple, and 117 components in this cache have a negative base position
    ///     on a shift-mode axis. Do not widen anything to <c>long</c> "for safety" - that diverges
    ///     from the client precisely where the client overflows.
    ///     </para>
    /// </remarks>
    public static class InterfaceLayoutResolver {
        /// <summary>
        ///     Resolves a component's extents against its parent's.
        /// </summary>
        /// <remarks>
        ///     <b>Modes other than 0, 1, 2 and 4 leave the extent unchanged, and that is the client's
        ///     behaviour rather than an omission here.</b> <c>Class253.java:321</c> and <c>:336</c>
        ///     are bare <c>if</c>s with no <c>else</c>, so a mode-3 component keeps whatever extent
        ///     it had - which for a component being laid out for the first time is 0
        ///     (<c>RSInterface.java:301</c>, <c>:329</c>).
        ///     <para>
        ///     <b>Modes 3 and 4 occur zero times in either supported cache</b>, on both axes. The
        ///     mode-3 fall-through and both aspect-ratio cross-links are therefore reachable only
        ///     through CS2 opcode 1001, and no byte on disk exercises them. They are implemented
        ///     anyway, and this note exists because a branch nothing exercises is defended by
        ///     nothing.
        ///     </para>
        /// </remarks>
        /// <param name="component">The component.</param>
        /// <param name="parentWidth">The parent's content width.</param>
        /// <param name="parentHeight">The parent's content height.</param>
        /// <param name="previous">The extent the component already had, for the fall-through modes.</param>
        /// <param name="aspect">The CS2 aspect pair, defaulting to 1:1 as the client does.</param>
        /// <param name="diagnostics">Set when a cross-link had to be skipped.</param>
        /// <returns>The resolved width and height.</returns>
        public static (int Width, int Height) ResolveSize(InterfaceComponentDefinition component,
            int parentWidth, int parentHeight, (int Width, int Height) previous,
            (int Width, int Height) aspect, out InterfaceLayoutDiagnostics diagnostics) {
            if (component == null)
                throw new ArgumentNullException(nameof(component));

            diagnostics = InterfaceLayoutDiagnostics.None;

            int width = previous.Width;
            int height = previous.Height;

            switch (component.WidthMode) {
                case 0:
                    width = component.BaseWidth;                                  //Class253.java:328
                    break;
                case 1:
                    width = parentWidth - component.BaseWidth;                    //Class253.java:325
                    break;
                case 2:
                    width = (parentWidth * component.BaseWidth) >> 14;            //Class253.java:322
                    break;
            }

            switch (component.HeightMode) {
                case 0:
                    height = component.BaseHeight;                                //Class253.java:334
                    break;
                case 1:
                    height = parentHeight - component.BaseHeight;                 //Class253.java:340
                    break;
                case 2:
                    height = (parentHeight * component.BaseHeight) >> 14;         //Class253.java:337
                    break;
            }

            /* The two cross-links run in this order and the second reads the first's output, so a
               component with mode 4 on both axes recomputes its width from the STALE height and
               then its height from the NEW width. On a first pass both start at 0 and it stays
               0 x 0. That is what the client does; it is not a bug to tidy up. */
            if (component.WidthMode == 4) {                                       //Class253.java:343
                if (aspect.Height != 0)
                    width = aspect.Width * height / aspect.Height;                //Class253.java:344
                else
                    diagnostics |= InterfaceLayoutDiagnostics.DegenerateAspect;
            }

            if (component.HeightMode == 4) {                                      //Class253.java:347
                if (aspect.Width != 0)
                    height = width * aspect.Height / aspect.Width;                //Class253.java:348
                else
                    diagnostics |= InterfaceLayoutDiagnostics.DegenerateAspect;
            }

            return (width, height);
        }

        /// <summary>
        ///     Resolves a component's position against its parent's extents and its own resolved size.
        /// </summary>
        /// <remarks>
        ///     <b>The final arm is a catch-all, not mode 5, and that is load-bearing.</b> The mode
        ///     byte is read as an unclamped signed byte (<c>RSInterface.java:1053-1056</c>); only the
        ///     CS2 setter clamps it, to 0..5 (<c>Class247.java:427-441</c>). So a stored 6, 127 or
        ///     -128 all take the last arm, and a <c>switch</c> with a <c>case 5</c> and no
        ///     <c>default</c> would silently leave those components at the origin.
        /// </remarks>
        /// <param name="component">The component.</param>
        /// <param name="parentWidth">The parent's content width.</param>
        /// <param name="parentHeight">The parent's content height.</param>
        /// <param name="width">The component's resolved width.</param>
        /// <param name="height">The component's resolved height.</param>
        /// <returns>The resolved position, relative to the parent's content origin.</returns>
        public static (int X, int Y) ResolvePosition(InterfaceComponentDefinition component,
            int parentWidth, int parentHeight, int width, int height) {
            if (component == null)
                throw new ArgumentNullException(nameof(component));

            int y;
            switch (component.YMode) {
                case 0:
                    y = component.BasePositionY;                                          //KeyStroke.java:14
                    break;
                case 1:
                    y = component.BasePositionY + (parentHeight - height) / 2;            //KeyStroke.java:16
                    break;
                case 2:
                    y = parentHeight - height - component.BasePositionY;                  //KeyStroke.java:30
                    break;
                case 3:
                    y = (parentHeight * component.BasePositionY) >> 14;                   //KeyStroke.java:27
                    break;
                case 4:
                    y = ((parentHeight * component.BasePositionY) >> 14)
                        + (parentHeight - height) / 2;                                    //KeyStroke.java:23-24
                    break;
                default:
                    y = (parentHeight - height)
                        - ((component.BasePositionY * parentHeight) >> 14);               //KeyStroke.java:20-21
                    break;
            }

            int x;
            switch (component.XMode) {
                case 0:
                    x = component.BasePositionX;                                          //KeyStroke.java:33
                    break;
                case 1:
                    x = component.BasePositionX + (parentWidth - width) / 2;              //KeyStroke.java:35
                    break;
                case 2:
                    x = parentWidth - width - component.BasePositionX;                    //KeyStroke.java:47
                    break;
                case 3:
                    x = (component.BasePositionX * parentWidth) >> 14;                    //KeyStroke.java:44
                    break;
                case 4:
                    x = (parentWidth - width) / 2
                        + ((parentWidth * component.BasePositionX) >> 14);                //KeyStroke.java:39
                    break;
                default:
                    x = parentWidth - width
                        - ((parentWidth * component.BasePositionX) >> 14);                //KeyStroke.java:41
                    break;
            }

            return (x, y);
        }

        /// <summary>
        ///     The stored base that would put a component's edge at a wanted pixel.
        /// </summary>
        /// <remarks>
        ///     <b>The inverse of <see cref="ResolvePosition"/>, and the reason dragging a component
        ///     is not simply adding a delta to its base.</b> Only mode 0 stores a pixel offset.
        ///     Mode 2 measures from the far edge, so dragging right <i>decreases</i> the stored
        ///     value; modes 3, 4 and 5 store a Q0.14 fraction of the parent, so a one-pixel drag is
        ///     a change of <c>16384 / parent</c> in the stored number. An editor that added the
        ///     pixel delta to the base would move a mode-2 component the wrong way and move a
        ///     mode-3 component by about a two-hundredth of what was asked.
        ///     <para>
        ///     <b>The shift modes are lossy and that is inherent, not a shortcut.</b> A base is an
        ///     integer, so on a 765-wide parent one unit of base is 0.047 of a pixel and the wanted
        ///     pixel is reachable exactly; on a narrow parent it is not, and the nearest
        ///     representable position is stored. Callers should re-resolve and show the result
        ///     rather than assuming the drag landed where the pointer did - which the canvas does,
        ///     because it redraws from the resolver after every edit.
        ///     </para>
        /// </remarks>
        /// <param name="mode">The positioning mode for the axis.</param>
        /// <param name="wanted">The pixel the edge should land on, relative to the parent's content origin.</param>
        /// <param name="parentExtent">The parent's content extent on that axis.</param>
        /// <param name="ownExtent">The component's own resolved extent on that axis.</param>
        /// <returns>The base value to store.</returns>
        public static int BaseForPosition(int mode, int wanted, int parentExtent, int ownExtent) {
            switch (mode) {
                case 0:
                    return wanted;

                case 1:
                    //resolved = base + (parent - own) / 2
                    return wanted - (parentExtent - ownExtent) / 2;

                case 2:
                    //resolved = parent - own - base
                    return parentExtent - ownExtent - wanted;

                case 3:
                    //resolved = (base * parent) >> 14
                    return ToFraction(wanted, parentExtent);

                case 4:
                    //resolved = ((parent * base) >> 14) + (parent - own) / 2
                    return ToFraction(wanted - (parentExtent - ownExtent) / 2, parentExtent);

                default:
                    //resolved = (parent - own) - ((base * parent) >> 14)
                    return ToFraction(parentExtent - ownExtent - wanted, parentExtent);
            }
        }

        /// <summary>
        ///     The stored base that would give a component a wanted extent.
        /// </summary>
        /// <remarks>
        ///     The inverse of <see cref="ResolveSize"/> for the three modes that have one. Modes 3
        ///     and 4 do not: mode 3 leaves the extent at whatever it already was, and mode 4 derives
        ///     it from the aspect pair, so neither reads the base at all and no stored value would
        ///     produce the wanted size. Both return the base unchanged, and a caller resizing such a
        ///     component has to say that nothing happened rather than write a number the client will
        ///     ignore. Neither occurs in either supported cache.
        /// </remarks>
        /// <param name="mode">The sizing mode for the axis.</param>
        /// <param name="wanted">The extent the component should resolve to.</param>
        /// <param name="parentExtent">The parent's content extent on that axis.</param>
        /// <param name="current">The base currently stored, returned for the modes with no inverse.</param>
        /// <returns>The base value to store.</returns>
        public static int BaseForSize(int mode, int wanted, int parentExtent, int current) {
            switch (mode) {
                case 0:
                    return wanted;

                case 1:
                    //resolved = parent - base
                    return parentExtent - wanted;

                case 2:
                    return ToFraction(wanted, parentExtent);

                default:
                    return current;
            }
        }

        /// <summary>Whether a sizing mode reads its stored base at all.</summary>
        /// <param name="mode">The sizing mode.</param>
        /// <returns>Whether resizing by writing the base would do anything.</returns>
        public static bool SizeModeUsesItsBase(int mode) {
            return mode is 0 or 1 or 2;
        }

        /// <summary>
        ///     A pixel value as the Q0.14 fraction of a parent extent that resolves closest to it.
        /// </summary>
        /// <remarks>
        ///     Rounded rather than truncated, so a drag lands on the nearer of the two representable
        ///     positions instead of always the lower one - which over a series of small drags would
        ///     otherwise creep steadily towards the origin.
        ///     <para>
        ///     A zero parent extent has no fraction that means anything, so the base is left at
        ///     zero rather than dividing.
        ///     </para>
        /// </remarks>
        private static int ToFraction(int pixels, int parentExtent) {
            if (parentExtent == 0)
                return 0;

            long scaled = (long) pixels << 14;
            long half = parentExtent / 2;

            return (int) (scaled >= 0
                ? (scaled + half) / parentExtent
                : (scaled - half) / parentExtent);
        }

        /// <summary>
        ///     The content extents a layer offers its children.
        /// </summary>
        /// <remarks>
        ///     <b>A scrolling layer offers its scroll extent, not its visible box.</b>
        ///     <c>Class63.java:104-106</c>: the layout pass substitutes <c>scrollMaxH</c> for the
        ///     resolved width and <c>scrollMaxV</c> for the resolved height whenever they are
        ///     non-zero. A layer 200 pixels wide with a 600-pixel scroll extent gives its children a
        ///     parent width of 600, and laying them out against 200 would pile every proportional
        ///     child into the visible third.
        ///     <para>
        ///     <b>Divergence from the client, deliberate.</b> The client's single-component relayout
        ///     path takes the parent's extents raw, with no scroll substitution
        ///     (<c>Node_Sub45.java:57-58</c>), so a CS2 reposition of one child of a scrolling layer
        ///     lays it out against the visible box while the full pass uses the scroll box, and the
        ///     two disagree until the next full pass. This resolver applies the scroll rule
        ///     everywhere. That is the third case <c>CLAUDE.md</c> describes: the client contradicts
        ///     itself, the data has no opinion, and copying it faithfully would reproduce a defect.
        ///     </para>
        /// </remarks>
        /// <param name="layer">The parent component.</param>
        /// <param name="resolved">Its own resolved rectangle.</param>
        /// <returns>The extents its children resolve against.</returns>
        public static (int Width, int Height) ContentExtentsOf(InterfaceComponentDefinition layer,
            InterfaceRect resolved) {
            if (layer == null)
                throw new ArgumentNullException(nameof(layer));

            int width = layer.ScrollMaxHorizontal != 0 ? layer.ScrollMaxHorizontal : resolved.Width;
            int height = layer.ScrollMaxVertical != 0 ? layer.ScrollMaxVertical : resolved.Height;

            return (width, height);
        }

        /// <summary>
        ///     The extents a component was resolved against.
        /// </summary>
        /// <remarks>
        ///     <b>Needed by anything that inverts the layout, and easy to get wrong in a way that
        ///     looks right.</b> A root resolves against the canvas, but a child resolves against its
        ///     parent's <i>content</i> box - which is the scroll extent where the parent scrolls, not
        ///     the parent's own rectangle. Feeding the canvas size in for a child produces a base
        ///     that resolves somewhere else entirely, and for a mode-2 component it produces one at
        ///     roughly the opposite end of the parent, because that mode measures from the far edge.
        ///     <para>
        ///     Shared rather than reimplemented per caller for exactly that reason: the first test
        ///     written against <see cref="BaseForPosition"/> passed the canvas extents for every
        ///     component and failed on the first mode-2 child it reached.
        ///     </para>
        /// </remarks>
        /// <param name="tree">The interface's tree.</param>
        /// <param name="resolved">The resolved nodes, as <see cref="ResolveGroup"/> returned them.</param>
        /// <param name="fileId">The component.</param>
        /// <param name="canvas">The box a root resolves against.</param>
        /// <returns>The parent extents that component was laid out against.</returns>
        public static (int Width, int Height) ParentExtentsFor(InterfaceComponentTree tree,
            IReadOnlyDictionary<int, InterfaceLayoutNode> resolved, int fileId, InterfaceRect canvas) {
            if (tree == null)
                throw new ArgumentNullException(nameof(tree));
            if (resolved == null)
                throw new ArgumentNullException(nameof(resolved));

            if (!tree.Components.TryGetValue(fileId, out InterfaceComponentDefinition? component))
                return (canvas.Width, canvas.Height);

            int parentId = component.RawParentId;

            /* A dangling or cyclic parent falls back to the canvas, which is exactly what
               ResolveGroup did for the same component - so the inverse is consistent with the
               forward pass rather than merely plausible. */
            if (parentId == InterfaceComponentDefinition.NoParent
                || !tree.Components.TryGetValue(parentId, out InterfaceComponentDefinition? parent)
                || !resolved.TryGetValue(parentId, out InterfaceLayoutNode? parentNode)
                || !resolved.TryGetValue(fileId, out InterfaceLayoutNode? own)
                || !own.IsDrawn) {
                return (canvas.Width, canvas.Height);
            }

            return ContentExtentsOf(parent, parentNode.Absolute);
        }

        /// <summary>
        ///     Resolves every component of an interface, including the ones the client would not draw.
        /// </summary>
        /// <remarks>
        ///     <b>Every component gets a node, which is a deliberate divergence.</b> The client
        ///     never visits a component a root pass cannot reach
        ///     (<c>Class224_Sub2.java:153</c>), but an editor that dropped those would show an
        ///     interface with rows missing and no explanation. A component that is dangling, cyclic,
        ///     or parented to something that is not a layer is resolved against the root box, marked
        ///     <see cref="InterfaceLayoutNode.IsDrawn"/> false, and carries the reason in its
        ///     diagnostics so the surface can say why.
        ///     <para>
        ///     Never throws for malformed input. A cycle, a dangling parent, a non-layer parent and a
        ///     zero aspect denominator are all data this cache either contains or permits, and an
        ///     exception here would take out a whole group's display for one bad record.
        ///     </para>
        /// </remarks>
        /// <param name="tree">The interface's component tree.</param>
        /// <param name="canvas">The box a root resolves against.</param>
        /// <returns>One node per component, keyed by file id.</returns>
        public static IReadOnlyDictionary<int, InterfaceLayoutNode> ResolveGroup(
            InterfaceComponentTree tree, InterfaceRect canvas) {
            if (tree == null)
                throw new ArgumentNullException(nameof(tree));

            var resolved = new Dictionary<int, InterfaceLayoutNode>(tree.Components.Count);

            /* An explicit stack, never recursion: the format permits a 770-level chain inside the
               771-file group this cache holds, and the visited set inside InDrawOrder is what makes
               a cycle terminate rather than a depth cap, which would be a tolerance. */
            var pending = new Stack<(int FileId, int Depth, int ParentWidth, int ParentHeight,
                int OriginX, int OriginY, InterfaceRect Clip)>();

            foreach (int root in tree.Roots)
                pending.Push((root, 0, canvas.Width, canvas.Height, canvas.X, canvas.Y, canvas));

            while (pending.Count > 0) {
                var frame = pending.Pop();

                if (resolved.ContainsKey(frame.FileId))
                    continue;

                InterfaceComponentDefinition component = tree.Components[frame.FileId];

                InterfaceLayoutNode node = Resolve(component, tree.ParentageOf(frame.FileId),
                    frame.Depth, frame.ParentWidth, frame.ParentHeight, frame.OriginX, frame.OriginY,
                    frame.Clip, true);

                resolved[frame.FileId] = node;

                //Only a layer has children the client would visit. A component parented to anything
                //else is reported below rather than laid out here.
                if (component.ComponentType != 0)
                    continue;

                (int contentWidth, int contentHeight) = ContentExtentsOf(component, node.Absolute);

                /* The child origin is the parent's absolute position less its scroll offset, which
                   is how Node_Sub10_Sub24.java:414-416 recurses. The scroll POSITION is runtime
                   state and is zero for a stored record, so it is not applied here; the canvas
                   applies it when it is showing a scrolled layer. */
                int childOriginX = node.Absolute.X;
                int childOriginY = node.Absolute.Y;

                IReadOnlyList<int> children = tree.ChildrenOf(frame.FileId);
                for (int i = children.Count - 1; i >= 0; i--) {
                    pending.Push((children[i], frame.Depth + 1, contentWidth, contentHeight,
                        childOriginX, childOriginY, node.Clip));
                }
            }

            //Everything a root could not reach, resolved against the canvas so it can still be shown.
            foreach (KeyValuePair<int, InterfaceComponentDefinition> entry in tree.Components) {
                if (resolved.ContainsKey(entry.Key))
                    continue;

                resolved[entry.Key] = Resolve(entry.Value, tree.ParentageOf(entry.Key), 0,
                    canvas.Width, canvas.Height, canvas.X, canvas.Y, canvas, false);
            }

            return resolved;
        }

        private static InterfaceLayoutNode Resolve(InterfaceComponentDefinition component,
            InterfaceParentage parentage, int depth, int parentWidth, int parentHeight,
            int originX, int originY, InterfaceRect inheritedClip, bool isDrawn) {
            (int width, int height) = ResolveSize(component, parentWidth, parentHeight,
                (0, 0), (1, 1), out InterfaceLayoutDiagnostics diagnostics);

            (int x, int y) = ResolvePosition(component, parentWidth, parentHeight, width, height);

            var relative = new InterfaceRect(x, y, width, height);
            var absolute = new InterfaceRect(originX + x, originY + y, width, height);

            diagnostics |= parentage switch {
                InterfaceParentage.Dangling => InterfaceLayoutDiagnostics.DanglingParent,
                InterfaceParentage.Cyclic => InterfaceLayoutDiagnostics.CyclicParent,
                _ => InterfaceLayoutDiagnostics.None
            };

            if (!isDrawn && parentage == InterfaceParentage.Child)
                diagnostics |= InterfaceLayoutDiagnostics.ParentIsNotALayer;

            return new InterfaceLayoutNode(component, depth, relative, absolute,
                ClipFor(component, absolute, inheritedClip), isDrawn, parentage, diagnostics);
        }

        /// <summary>
        ///     The rectangle a component's contents are clipped to.
        /// </summary>
        /// <remarks>
        ///     Two exceptions, both read out of <c>Node_Sub10_Sub24.java:190-208</c> and both
        ///     corroborated by the independently written hit-test pass at
        ///     <c>client.java:724-742</c>:
        ///     <list type="bullet">
        ///     <item>
        ///     <b>Type 2 inherits the clip untouched</b> and is not clipped to its own box at all
        ///     (<c>:204-208</c>). No type-2 component exists in either supported cache.
        ///     </item>
        ///     <item>
        ///     <b>Type 9, a line, extends its right and bottom by one pixel</b> before the
        ///     intersection (<c>:197-200</c>), because a line's endpoint is inclusive where a
        ///     rectangle's edge is not. Omitting this clips the last pixel off every one of the 367
        ///     line components both caches hold, and it is invisible in any test that does not draw.
        ///     </item>
        ///     </list>
        /// </remarks>
        private static InterfaceRect ClipFor(InterfaceComponentDefinition component,
            InterfaceRect absolute, InterfaceRect inherited) {
            if (component.ComponentType == 2)
                return inherited;

            InterfaceRect own = component.ComponentType == 9
                ? new InterfaceRect(absolute.X, absolute.Y, absolute.Width + 1, absolute.Height + 1)
                : absolute;

            return own.Intersect(inherited);
        }
    }
}
