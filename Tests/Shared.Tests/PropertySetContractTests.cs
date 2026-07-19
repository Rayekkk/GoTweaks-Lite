using Shared.IPC;
using Xunit;

namespace Shared.Tests
{
    public class PropertySetContractTests
    {
        [Theory]
        [InlineData("Applied")]
        [InlineData("applied")]
        [InlineData("Rejected")]
        [InlineData("Failed")]
        public void RecognizesKnownOutcomes(string value)
        {
            Assert.True(PropertySetContract.IsKnownOutcome(value));
        }

        [Theory]
        [InlineData("")]
        [InlineData("Success")]
        [InlineData("Unknown")]
        public void RejectsLegacyOrUnknownOutcomes(string value)
        {
            Assert.False(PropertySetContract.IsKnownOutcome(value));
        }

        [Fact]
        public void RejectsMissingOutcome()
        {
            Assert.False(PropertySetContract.IsKnownOutcome(null));
        }

        [Fact]
        public void AppliedCheckIsCaseInsensitiveAndSpecific()
        {
            Assert.True(PropertySetContract.IsApplied("APPLIED"));
            Assert.False(PropertySetContract.IsApplied("Rejected"));
        }
    }
}
