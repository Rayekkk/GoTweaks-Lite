using Shared.Data;
using Xunit;

namespace Shared.Tests
{
    /// <summary>
    /// The Legion Go 2 controller's HQ-IMU HID payload flips endianness depending on the
    /// controller's HID report state: little-endian when initialized (04:00:A1), BIG-endian
    /// when detached (04:3C:74). Reading the detached block little-endian was the root cause
    /// of the "gyro goes wild during VIIPER emulation" bug — misdiagnosed for a while as
    /// firmware torn reads. These tests pin the byte order both ways so that regression can't
    /// silently return.
    /// </summary>
    public class LegionImuByteOrderTests
    {
        [Fact]
        public void LittleEndian_LowByteFirst()
        {
            // 0x1234 stored LE: low byte 0x34 at offset, high byte 0x12 next.
            byte[] buffer = { 0x34, 0x12 };
            Assert.Equal(0x1234, LegionImuByteOrder.ReadInt16LittleEndian(buffer, 0));
        }

        [Fact]
        public void BigEndian_HighByteFirst()
        {
            // Same 0x1234 value, but BE storage: high byte 0x12 first, low byte 0x34 next.
            byte[] buffer = { 0x12, 0x34 };
            Assert.Equal(0x1234, LegionImuByteOrder.ReadInt16BigEndian(buffer, 0));
        }

        [Fact]
        public void SameBytes_DecodeDifferently_DependingOnEndianness()
        {
            // This is the exact failure mode of the original bug: identical raw bytes decode
            // to a wildly different value depending on which reader is used. A near-zero-at-rest
            // BE sample decodes as a large, quantized-looking LE "spike".
            byte[] asymmetric = { 0x00, 0x03 }; // BE: 0x0003 = 3 (quiet); LE: 0x0300 = 768 (looks like a torn-read spike)
            Assert.Equal(3, LegionImuByteOrder.ReadInt16BigEndian(asymmetric, 0));
            Assert.Equal(768, LegionImuByteOrder.ReadInt16LittleEndian(asymmetric, 0));
        }

        [Fact]
        public void NegativeValue_RoundTrips_BothOrders()
        {
            // -1 is 0xFFFF regardless of byte order — sanity check the sign-extension path.
            byte[] buffer = { 0xFF, 0xFF };
            Assert.Equal(-1, LegionImuByteOrder.ReadInt16LittleEndian(buffer, 0));
            Assert.Equal(-1, LegionImuByteOrder.ReadInt16BigEndian(buffer, 0));
        }

        [Fact]
        public void ReadImuInt16_DispatchesToLittleEndian_WhenNotBigEndian()
        {
            byte[] buffer = { 0x00, 0x03 };
            Assert.Equal(768, LegionImuByteOrder.ReadImuInt16(buffer, 0, bigEndian: false));
        }

        [Fact]
        public void ReadImuInt16_DispatchesToBigEndian_WhenBigEndian()
        {
            byte[] buffer = { 0x00, 0x03 };
            Assert.Equal(3, LegionImuByteOrder.ReadImuInt16(buffer, 0, bigEndian: true));
        }

        [Fact]
        public void OutOfBounds_ReturnsZero_InsteadOfThrowing()
        {
            byte[] buffer = { 0x12 }; // only 1 byte — needs 2
            Assert.Equal(0, LegionImuByteOrder.ReadInt16LittleEndian(buffer, 0));
            Assert.Equal(0, LegionImuByteOrder.ReadInt16BigEndian(buffer, 0));
            Assert.Equal(0, LegionImuByteOrder.ReadImuInt16(null!, 0, true));
        }
    }
}
