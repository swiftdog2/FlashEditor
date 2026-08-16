using Xunit;

namespace FlashEditor.Tests.Cache.RealCache
{
    /// <summary>
    ///     A <see cref="FactAttribute"/> that skips itself when no real revision-639 cache is
    ///     available, naming the reason so a skipped run is never mistaken for a passing one.
    /// </summary>
    /// <remarks>
    ///     xUnit resolves <see cref="FactAttribute.Skip"/> at discovery, so setting it from the
    ///     constructor is what allows the decision to depend on the machine rather than on a
    ///     compile-time constant.
    /// </remarks>
    public sealed class RealCacheFactAttribute : FactAttribute
    {
        /// <summary>Creates the attribute, skipping when no cache can be located.</summary>
        public RealCacheFactAttribute()
        {
            string reason = RealCacheLocator.SkipReason;
            if (reason != null)
                Skip = reason;
        }
    }

    /// <summary>
    ///     <see cref="RealCacheFactAttribute"/> for a test that takes cases.
    /// </summary>
    /// <remarks>
    ///     Separate because <see cref="TheoryAttribute"/> does not derive from
    ///     <see cref="FactAttribute"/> in a way that lets one attribute serve both, and a cached
    ///     sweep written as a theory over a plain <c>[Theory]</c> would fail rather than skip on a
    ///     machine with no cache - which is the mistake this pair exists to make impossible.
    /// </remarks>
    public sealed class RealCacheTheoryAttribute : TheoryAttribute
    {
        /// <summary>Creates the attribute, skipping when no cache can be located.</summary>
        public RealCacheTheoryAttribute()
        {
            string reason = RealCacheLocator.SkipReason;
            if (reason != null)
                Skip = reason;
        }
    }
}
