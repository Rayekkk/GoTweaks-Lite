using Shared.Data;
using Xunit;

namespace Shared.Tests
{
    /// <summary>
    /// The timestamp-based conflict-resolution rules at the core of widget↔helper sync,
    /// extracted from GenericProperty.SetValue into a pure arbiter so they can be tested
    /// here (a concrete property can't be loaded in a non-packaged host — it pulls in WinRT
    /// ValueSet). These cover the trickiest rule: the UpdatedTime==0 "no info" handling
    /// (issue #79) and staleness rejection.
    /// </summary>
    public class PropertyUpdateArbiterTests
    {
        [Fact]
        public void StaleUpdate_IsRejected()
        {
            Assert.False(PropertyUpdateArbiter.TryResolveTimestamp(lastUpdatedTime: 100, incomingUpdatedTime: 50, out _));
        }

        [Fact]
        public void NewerUpdate_IsAccepted_WithIncomingTimestamp()
        {
            Assert.True(PropertyUpdateArbiter.TryResolveTimestamp(100, 200, out var effective));
            Assert.Equal(200, effective);
        }

        [Fact]
        public void EqualTimestamp_IsAccepted_NotTreatedAsStale()
        {
            // Boundary: incoming == last is NOT strictly older, so it is accepted.
            Assert.True(PropertyUpdateArbiter.TryResolveTimestamp(100, 100, out var effective));
            Assert.Equal(100, effective);
        }

        [Fact]
        public void ZeroIncoming_WithPriorRealTimestamp_IsRejected()
        {
            // issue #79: a zero-stamped ("no info") update must not clobber an authoritative value.
            Assert.False(PropertyUpdateArbiter.TryResolveTimestamp(lastUpdatedTime: 5_000, incomingUpdatedTime: 0, out _));
        }

        [Fact]
        public void ZeroIncoming_OnFreshProperty_IsAccepted_AndCoercedToNow()
        {
            long before = System.DateTime.Now.Ticks;
            Assert.True(PropertyUpdateArbiter.TryResolveTimestamp(lastUpdatedTime: 0, incomingUpdatedTime: 0, out var effective));
            long after = System.DateTime.Now.Ticks;

            // Coerced to "now" rather than left at 0.
            Assert.NotEqual(0, effective);
            Assert.InRange(effective, before, after);
        }

        [Theory]
        [InlineData(0, 1, true)]      // fresh, real incoming → accept
        [InlineData(0, 0, true)]      // fresh, zero incoming → accept (coerced)
        [InlineData(10, 0, false)]    // prior, zero incoming → reject (issue #79)
        [InlineData(10, 9, false)]    // prior, older incoming → reject (stale)
        [InlineData(10, 10, true)]    // prior, equal incoming → accept
        [InlineData(10, 11, true)]    // prior, newer incoming → accept
        public void DecisionMatrix(long last, long incoming, bool expectedAccept)
        {
            Assert.Equal(expectedAccept, PropertyUpdateArbiter.TryResolveTimestamp(last, incoming, out _));
        }
    }
}
