namespace Shared.Data
{
    /// <summary>
    /// Endian-aware int16 reader for the Legion Go 2 controller's HQ-IMU HID payload.
    ///
    /// The controller's HID report layout differs between its two states:
    ///   - initialized/attached (04:00:A1 header): IMU int16 fields are little-endian.
    ///   - detached/uninitialized (04:3C:74 header): IMU int16 fields are BIG-endian.
    ///
    /// Reading the detached block little-endian was the root cause of the long-standing
    /// "gyro goes wild during VIIPER emulation" bug: enabling emulation re-enumerates the
    /// controller (HidHide cycle-port) which drops it into the detached layout, and the
    /// mis-decoded bytes produced garbage quantized to +/-256-LSB multiples — misdiagnosed for
    /// a while as firmware torn reads. See the LeGo2-detached-IMU-endianness project memory.
    /// </summary>
    public static class LegionImuByteOrder
    {
        public static short ReadInt16LittleEndian(byte[] buffer, int offset)
        {
            if (buffer == null || offset < 0 || offset + 1 >= buffer.Length)
            {
                return 0;
            }

            int low = buffer[offset];
            int high = buffer[offset + 1];
            return unchecked((short)(low | (high << 8)));
        }

        public static short ReadInt16BigEndian(byte[] buffer, int offset)
        {
            if (buffer == null || offset < 0 || offset + 1 >= buffer.Length)
            {
                return 0;
            }

            int high = buffer[offset];
            int low = buffer[offset + 1];
            return unchecked((short)((high << 8) | low));
        }

        /// <summary>
        /// HQ-IMU field reader. Initialized (04:00:A1) reports encode IMU int16s
        /// little-endian; detached (04:3C:74) reports encode them BIG-endian.
        /// </summary>
        public static short ReadImuInt16(byte[] buffer, int offset, bool bigEndian)
        {
            return bigEndian
                ? ReadInt16BigEndian(buffer, offset)
                : ReadInt16LittleEndian(buffer, offset);
        }
    }
}
