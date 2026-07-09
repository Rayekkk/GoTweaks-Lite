using System;

namespace Shared.Data
{
    /// <summary>
    /// Per-axis running-median gyro spike/torn-read filter. Not thread-safe; each instance
    /// is owned by a single reader (e.g. one axis of one Legion controller's gyro stream).
    ///
    /// The Legion controller firmware publishes non-atomic gyro register reads (a byte updated
    /// mid-report inside the MCU before the report is assembled), producing an int16 that lands
    /// at the torn-read boundaries (~+/-256 raw LSB — the high byte off by one) on top of the
    /// true reading. Under VIIPER emulation the controller streams the detached HID layout and
    /// the torn-read rate is very high (observed ~40-50% on an axis) — this is the "random
    /// self-moving aim" symptom.
    ///
    /// Filter: a straight per-axis running MEDIAN over a short window. A median removes isolated
    /// single-frame impulses (torn reads) completely, at rest AND during motion, while
    /// reproducing genuine continuous rotation faithfully (the median of a smooth ramp is just
    /// the ramp delayed by ~window/2 frames — it does NOT clip or attenuate real motion).
    /// Window=5 tolerates up to 2 torn frames inside the window and adds ~2 frames (~16 ms) of
    /// group delay at 125 Hz, imperceptible for aim.
    ///
    /// NOTE: an earlier median+MAD "substitute only when the window is quiet" design was wrong —
    /// it disarmed during motion (MAD high), which is exactly when the jitter is felt. A plain
    /// always-on median is both simpler and correct here.
    /// </summary>
    public sealed class GyroSpikeFilter
    {
        private readonly short[] _ring;
        private readonly short[] _scratch;
        private int _count;
        private int _pos;

        public GyroSpikeFilter(int window)
        {
            _ring = new short[window];
            _scratch = new short[window];
        }

        public short Filter(short raw)
        {
            _ring[_pos] = raw;
            _pos = (_pos + 1) % _ring.Length;
            if (_count < _ring.Length) _count++;
            Array.Copy(_ring, _scratch, _count);
            Array.Sort(_scratch, 0, _count);
            return _scratch[_count / 2];
        }
    }
}
