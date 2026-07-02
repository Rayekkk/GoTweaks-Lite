using Shared.Data;
using Xunit;

namespace Shared.Tests
{
    /// <summary>
    /// Smoke + invariant tests for the pure-math stick/trigger shaping that both
    /// the helper (real forwarding) and widget (live preview) run identically.
    /// Asserts the load-bearing invariants: center maps to zero, deadzones
    /// suppress sub-threshold input, and identity config preserves deflection.
    /// </summary>
    public class StickTriggerProcessorTests
    {
        [Fact]
        public void Stick_Center_IsZero()
        {
            StickTriggerProcessor.TransformStick(0, 0, StickConfig.Default, out var x, out var y);
            Assert.Equal(0, (int)x);
            Assert.Equal(0, (int)y);
        }

        [Fact]
        public void Stick_WithinRadialDeadzone_IsZero()
        {
            var cfg = StickConfig.Default;
            cfg.Shape = DeadzoneShape.Radial;
            cfg.DeadzoneX = 0.5f;
            cfg.DeadzoneY = 0.5f;

            // ~0.2 magnitude on X (0.2 * 32768 ≈ 6554), well inside the 0.5 deadzone.
            StickTriggerProcessor.TransformStick(6554, 0, cfg, out var x, out var y);
            Assert.Equal(0, (int)x);
            Assert.Equal(0, (int)y);
        }

        [Fact]
        public void Stick_IdentityConfig_PreservesDeflection()
        {
            // Default = ScaledRadial, 0 deadzone, Linear curve → near pass-through.
            StickTriggerProcessor.TransformStick(16000, 0, StickConfig.Default, out var x, out var y);
            Assert.InRange((int)x, 15800, 16001);  // ~15999 after 32767/32768 re-quantize
            Assert.Equal(0, (int)y);
        }

        [Fact]
        public void Trigger_Default_PassesThroughEnds()
        {
            Assert.Equal(0, (int)StickTriggerProcessor.TransformTrigger(0, TriggerConfig.Default));
            Assert.Equal(255, (int)StickTriggerProcessor.TransformTrigger(255, TriggerConfig.Default));
        }

        [Fact]
        public void Trigger_DeadzoneStart_SuppressesBelowThreshold()
        {
            var cfg = TriggerConfig.Default;
            cfg.DeadzoneStart = 0.5f;

            Assert.Equal(0, (int)StickTriggerProcessor.TransformTrigger(64, cfg));   // 0.25 < 0.5 → 0
            Assert.Equal(255, (int)StickTriggerProcessor.TransformTrigger(255, cfg)); // full press still maxes
        }
    }
}
