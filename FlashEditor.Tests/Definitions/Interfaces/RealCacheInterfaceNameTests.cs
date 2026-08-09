using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Cache.Util;
using FlashEditor.Definitions.Interfaces;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Definitions.Interfaces
{
    /// <summary>
    ///     Pins the property that makes <see cref="InterfaceNames"/> safe to extend: no name is ever
    ///     shown whose re-hash disagrees with the identifier the loaded cache holds for that row.
    /// </summary>
    /// <remarks>
    ///     Recovering an index-3 name means proposing a string and checking its hash, and djb2 is 32
    ///     bits, so the failure mode is not a typo - it is a plausible string that collides. The
    ///     track-name join in <c>CLAUDE.md</c> is the same shape: 958 of 970 keys landed on a real
    ///     group and the mapping was still wrong. A count of recovered names is therefore worth very
    ///     little as a test, and this class asserts the mechanism instead.
    ///     <para>
    ///     Four claims, none of which a wrong name can satisfy: every displayed name re-hashes to the
    ///     stored identifier; flipping one bit of that identifier suppresses the name, so the check is
    ///     load-bearing rather than decorative; a recovered name fits <b>only</b> the group it is
    ///     given to, so a name attached to the wrong id cannot hide behind a correct-looking hash; and
    ///     a component name is scoped to its group, so one interface's name cannot be handed to
    ///     another interface's component of the same file id.
    ///     </para>
    ///     <para>
    ///     Coverage is printed rather than asserted against a literal. Index 3 is one of the eleven
    ///     indexes the two supported caches disagree on - 1067 groups and 40,883 components in the
    ///     vanilla b639 capture against 1078 and 42,256 in the repack - so a figure here would belong
    ///     to whichever cache happened to run. What is asserted is the relationship: the number of
    ///     names shown equals the number of table entries that verify, and that number is not zero.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheInterfaceNameTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheInterfaceNameTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        private SortedDictionary<int, RSArchiveEntry> Interfaces()
        {
            return _fixture.Table(RSConstants.INTERFACE_DEFINITIONS_INDEX).GetArchiveEntries();
        }

        /// <summary>
        ///     Every name index 3's editor would display re-hashes to the identifier stored beside it.
        /// </summary>
        /// <remarks>
        ///     The whole of index 3, not a sample: "no shipped name is unverified" is not a claim a run
        ///     over part of the table can make. An unnamed row must also produce no name at all, which
        ///     is the other half of the same property - a lookup that answered for a -1 identifier
        ///     would be inventing a name for a row the format says has none.
        /// </remarks>
        [RealCacheFact]
        public void EveryDisplayedNameRehashesToTheStoredIdentifier()
        {
            int groups = 0;
            int namedGroups = 0;
            int components = 0;
            int identifiedComponents = 0;
            int generatedNames = 0;
            int bespokeNames = 0;

            foreach (KeyValuePair<int, RSArchiveEntry> pair in Interfaces())
            {
                groups++;
                int groupId = pair.Key;
                int groupIdentifier = pair.Value.GetIdentifier();

                string groupName = InterfaceNames.GroupName(groupId, groupIdentifier);
                if (groupIdentifier == InterfaceNames.Unnamed)
                    Assert.Null(groupName);
                if (groupName != null)
                {
                    Assert.Equal(groupIdentifier, NameHasher.GetNameHash(groupName));
                    namedGroups++;
                }

                foreach (int fileId in pair.Value.GetValidFileIds())
                {
                    components++;
                    RSFileEntry child = pair.Value.GetFileEntry(fileId);
                    int identifier = child == null ? InterfaceNames.Unnamed : child.GetIdentifier();
                    if (identifier != InterfaceNames.Unnamed)
                        identifiedComponents++;

                    string name = InterfaceNames.ComponentName(groupId, fileId, identifier);
                    if (identifier == InterfaceNames.Unnamed)
                        Assert.Null(name);
                    if (name == null)
                        continue;

                    Assert.Equal(identifier, NameHasher.GetNameHash(name));
                    if (name == "com_" + fileId)
                        generatedNames++;
                    else
                        bespokeNames++;
                }
            }

            _output.WriteLine("cache: " + _fixture.Profile.Name);
            _output.WriteLine("groups: " + namedGroups + " named of " + groups + " declared");
            _output.WriteLine("components: " + (generatedNames + bespokeNames) + " named of " +
                              components + " declared, " + identifiedComponents +
                              " of which carry an identifier");
            _output.WriteLine("  com_<fileId>: " + generatedNames + ", bespoke: " + bespokeNames);

            //A table that silently emptied would satisfy every assertion above by naming nothing.
            Assert.True(namedGroups > 0, "no interface name verified against this cache");
            Assert.True(generatedNames > 0, "the com_<fileId> rule resolved nothing");
            Assert.True(bespokeNames > 0, "no bespoke component name verified against this cache");
        }

        /// <summary>
        ///     Corrupting a stored identifier by one bit suppresses the name it would have produced.
        /// </summary>
        /// <remarks>
        ///     A verification that cannot fail is not a verification. This drives the same rows through
        ///     the same lookups with the identifier flipped, which is what a wrong table entry or a
        ///     cache from another build looks like from inside the method, and requires every one of
        ///     them to fall back to "unnamed". It mutates only its local copy of the identifier, so the
        ///     cache is untouched and the production code is not edited to prove the point.
        /// </remarks>
        [RealCacheFact]
        public void AnIdentifierOffByOneBitProducesNoName()
        {
            int groupsChecked = 0;
            int componentsChecked = 0;

            foreach (KeyValuePair<int, RSArchiveEntry> pair in Interfaces())
            {
                int groupId = pair.Key;
                int groupIdentifier = pair.Value.GetIdentifier();

                if (InterfaceNames.GroupName(groupId, groupIdentifier) != null)
                {
                    Assert.Null(InterfaceNames.GroupName(groupId, groupIdentifier ^ 1));
                    groupsChecked++;
                }

                foreach (int fileId in pair.Value.GetValidFileIds())
                {
                    RSFileEntry child = pair.Value.GetFileEntry(fileId);
                    int identifier = child == null ? InterfaceNames.Unnamed : child.GetIdentifier();
                    if (identifier == InterfaceNames.Unnamed)
                        continue;
                    if (InterfaceNames.ComponentName(groupId, fileId, identifier) == null)
                        continue;

                    Assert.Null(InterfaceNames.ComponentName(groupId, fileId, identifier ^ 1));
                    componentsChecked++;
                }
            }

            _output.WriteLine("suppressed " + groupsChecked + " group names and " + componentsChecked +
                              " component names by flipping one bit of the stored identifier");
            Assert.True(groupsChecked > 0);
            Assert.True(componentsChecked > 0);
        }

        /// <summary>
        ///     A recovered interface name fits its own group's identifier and no other group's.
        /// </summary>
        /// <remarks>
        ///     This is the assertion a collision cannot satisfy by luck twice. A name attached to the
        ///     wrong group id would still re-hash correctly if it collided with that group's
        ///     identifier, and the first test would pass; it would then also have to be the <i>only</i>
        ///     group in the table holding that hash, which is what is required here. Distinct names
        ///     follow from the same requirement and are asserted with it, because two groups sharing a
        ///     recovered name would mean two groups sharing an identifier.
        /// </remarks>
        [RealCacheFact]
        public void ARecoveredNameFitsExactlyOneInterface()
        {
            SortedDictionary<int, RSArchiveEntry> interfaces = Interfaces();
            var byIdentifier = new Dictionary<int, List<int>>();
            foreach (KeyValuePair<int, RSArchiveEntry> pair in interfaces)
            {
                int identifier = pair.Value.GetIdentifier();
                if (identifier == InterfaceNames.Unnamed)
                    continue;
                if (!byIdentifier.TryGetValue(identifier, out List<int> ids))
                    byIdentifier[identifier] = ids = new List<int>();
                ids.Add(pair.Key);
            }

            var seen = new Dictionary<string, int>();
            foreach (KeyValuePair<int, RSArchiveEntry> pair in interfaces)
            {
                string name = InterfaceNames.GroupName(pair.Key, pair.Value.GetIdentifier());
                if (name == null)
                    continue;

                List<int> fits = byIdentifier[NameHasher.GetNameHash(name)];
                Assert.Equal(new[] { pair.Key }, fits.ToArray());

                Assert.False(seen.ContainsKey(name),
                    "interface " + pair.Key + " and " + (seen.TryGetValue(name, out int first) ? first : -1) +
                    " both recovered the name " + name);
                seen[name] = pair.Key;
            }

            _output.WriteLine(seen.Count + " recovered interface names, each fitting exactly one group");
            Assert.True(seen.Count > 0);
        }

        /// <summary>
        ///     The shipped table is stored in the case it was verified in.
        /// </summary>
        /// <remarks>
        ///     <see cref="NameHasher"/> lower-cases before hashing, so an entry typed in mixed case
        ///     would still pass every hash check while displaying a name the cache does not hold. The
        ///     hash cannot catch that one, so it is caught here instead.
        /// </remarks>
        [Fact]
        public void EveryTableEntryIsStoredLowerCase()
        {
            List<string> names = InterfaceNameTable.Groups.Values
                .Concat(InterfaceNameTable.Components.Values.SelectMany(inner => inner.Values))
                .ToList();

            foreach (string name in names)
            {
                Assert.NotEqual("", name);
                Assert.Equal(name.ToLowerInvariant(), name);
            }

            Assert.NotEmpty(names);
        }

        /// <summary>
        ///     A component name belongs to its own interface and is never offered to another.
        /// </summary>
        /// <remarks>
        ///     Needs no cache: the point is that the lookup is keyed on the group as well as the file,
        ///     which the previous signature was not. A bespoke name plus its identifier is asked for
        ///     under a group id that does not own it, and must come back unnamed.
        /// </remarks>
        [Fact]
        public void ABespokeComponentNameIsScopedToItsInterface()
        {
            Assert.NotEmpty(InterfaceNameTable.Components);

            foreach (KeyValuePair<int, Dictionary<int, string>> group in InterfaceNameTable.Components)
            {
                foreach (KeyValuePair<int, string> component in group.Value)
                {
                    int identifier = NameHasher.GetNameHash(component.Value);

                    Assert.Equal(component.Value,
                        InterfaceNames.ComponentName(group.Key, component.Key, identifier));
                    Assert.Null(InterfaceNames.ComponentName(group.Key, component.Key, identifier ^ 1));

                    //An id no interface uses, so the only thing that can answer is a lookup that
                    //ignored the group - which is the defect this scoping exists to prevent.
                    Assert.Null(InterfaceNames.ComponentName(int.MaxValue, component.Key, identifier));
                }
            }
        }
    }
}
