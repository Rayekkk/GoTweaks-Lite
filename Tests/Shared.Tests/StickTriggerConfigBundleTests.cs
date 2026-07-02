using Shared.Data;
using Xunit;

namespace Shared.Tests
{
    /// <summary>
    /// Round-trips the hand-rolled Serialize/Deserialize for the stick/trigger
    /// config bundle (persisted as one flat string over the Viiper_StickTriggerConfig
    /// property). Defaults and corrupt input must fall back to identity/passthrough.
    /// </summary>
    public class StickTriggerConfigBundleTests
    {
        [Fact]
        public void Default_RoundTrips()
        {
            var d = StickTriggerConfigBundle.Default;
            var r = StickTriggerConfigBundle.Deserialize(d.Serialize());
            AssertEqual(d, r);
        }

        [Fact]
        public void NonDefault_RoundTrips()
        {
            var b = StickTriggerConfigBundle.Default;
            b.LeftStick.Shape = DeadzoneShape.Radial;
            b.LeftStick.DeadzoneX = 0.25f;
            b.LeftStick.AntiDeadzoneY = 0.1f;
            b.LeftStick.CurveY = SensitivityCurve.SCurve;
            b.RightStick.CurveX = SensitivityCurve.Aggressive;
            b.LeftTrigger.DeadzoneStart = 0.05f;
            b.RightTrigger.RangeMax = 0.8f;
            b.RightTrigger.AntiDeadzone = 0.1f;
            b.RightTrigger.Curve = SensitivityCurve.Instant;

            var r = StickTriggerConfigBundle.Deserialize(b.Serialize());

            Assert.Equal(DeadzoneShape.Radial, r.LeftStick.Shape);
            Assert.Equal(0.25, r.LeftStick.DeadzoneX, 3);
            Assert.Equal(0.1, r.LeftStick.AntiDeadzoneY, 3);
            Assert.Equal(SensitivityCurve.SCurve, r.LeftStick.CurveY);
            Assert.Equal(SensitivityCurve.Aggressive, r.RightStick.CurveX);
            Assert.Equal(0.05, r.LeftTrigger.DeadzoneStart, 3);
            Assert.Equal(0.8, r.RightTrigger.RangeMax, 3);
            Assert.Equal(0.1, r.RightTrigger.AntiDeadzone, 3);
            Assert.Equal(SensitivityCurve.Instant, r.RightTrigger.Curve);
        }

        [Fact]
        public void NullInput_ReturnsDefault()
        {
            AssertEqual(StickTriggerConfigBundle.Default, StickTriggerConfigBundle.Deserialize(null!));
        }

        [Fact]
        public void CorruptInput_ReturnsDefaultWithoutThrowing()
        {
            AssertEqual(StickTriggerConfigBundle.Default, StickTriggerConfigBundle.Deserialize("totally invalid }{ data"));
        }

        private static void AssertEqual(StickTriggerConfigBundle a, StickTriggerConfigBundle b)
        {
            AssertStick(a.LeftStick, b.LeftStick);
            AssertStick(a.RightStick, b.RightStick);
            AssertTrigger(a.LeftTrigger, b.LeftTrigger);
            AssertTrigger(a.RightTrigger, b.RightTrigger);
        }

        private static void AssertStick(StickConfig a, StickConfig b)
        {
            Assert.Equal(a.Shape, b.Shape);
            Assert.Equal(a.CurveX, b.CurveX);
            Assert.Equal(a.CurveY, b.CurveY);
            Assert.Equal(a.DeadzoneX, b.DeadzoneX, 3);
            Assert.Equal(a.DeadzoneY, b.DeadzoneY, 3);
            Assert.Equal(a.AntiDeadzoneX, b.AntiDeadzoneX, 3);
            Assert.Equal(a.AntiDeadzoneY, b.AntiDeadzoneY, 3);
        }

        private static void AssertTrigger(TriggerConfig a, TriggerConfig b)
        {
            Assert.Equal(a.Curve, b.Curve);
            Assert.Equal(a.DeadzoneStart, b.DeadzoneStart, 3);
            Assert.Equal(a.RangeMax, b.RangeMax, 3);
            Assert.Equal(a.AntiDeadzone, b.AntiDeadzone, 3);
        }
    }
}
