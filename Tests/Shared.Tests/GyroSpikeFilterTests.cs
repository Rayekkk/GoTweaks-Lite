using Shared.Data;
using Xunit;

namespace Shared.Tests
{
    /// <summary>
    /// Median-of-5 gyro spike/torn-read filter (see Shared.Data.GyroSpikeFilter for the
    /// hardware rationale). These tests pin the two properties that matter: it must erase an
    /// isolated single-frame impulse (a torn read), both at rest and mid-motion, and it must
    /// NOT attenuate or clip genuine continuous motion.
    /// </summary>
    public class GyroSpikeFilterTests
    {
        [Fact]
        public void IsolatedSpikeAtRest_IsSuppressed()
        {
            var filter = new GyroSpikeFilter(5);
            short result = 0;
            // Quiet noise floor around zero, one isolated +768 torn-read spike in the middle.
            foreach (short sample in new short[] { 1, -1, 768, 0, 1 })
            {
                result = filter.Filter(sample);
            }
            // Median of {1,-1,768,0,1} sorted = {-1,0,1,1,768} -> median index 2 = 1.
            Assert.Equal(1, result);
        }

        [Fact]
        public void IsolatedSpikeDuringMotion_IsSuppressed()
        {
            // A smoothly increasing ramp (genuine rotation) with one torn-read spike injected.
            var filter = new GyroSpikeFilter(5);
            short result = 0;
            foreach (short sample in new short[] { 100, 110, 5000, 120, 130 })
            {
                result = filter.Filter(sample);
            }
            // Median of {100,110,5000,120,130} sorted = {100,110,120,130,5000} -> median = 120,
            // close to the true ramp value (not the 5000 spike).
            Assert.Equal(120, result);
        }

        [Fact]
        public void ConsecutiveConstantValue_PassesThroughUnchanged()
        {
            var filter = new GyroSpikeFilter(5);
            short result = 0;
            for (int i = 0; i < 10; i++)
            {
                result = filter.Filter(500);
            }
            Assert.Equal(500, result);
        }

        [Fact]
        public void SmoothRamp_IsNotClippedOrAttenuated()
        {
            // A continuously increasing signal (real fast rotation) should track the median of
            // its own recent window, not get clamped to some fixed ceiling.
            var filter = new GyroSpikeFilter(5);
            short last = 0;
            for (short v = 0; v <= 1000; v += 100)
            {
                last = filter.Filter(v);
            }
            // After the window fills with the last 5 values {600,700,800,900,1000}, the median
            // is 800 — well above the noise floor, proving the filter does not clip fast motion
            // down toward zero.
            Assert.Equal(800, last);
        }

        [Fact]
        public void FirstSamples_BeforeWindowFills_StillReturnAValue()
        {
            var filter = new GyroSpikeFilter(5);
            // Should not throw even when the ring buffer isn't full yet.
            Assert.Equal(0, filter.Filter(0));
            short result = filter.Filter(100);
            Assert.InRange(result, (short)0, (short)100);
        }
    }
}
