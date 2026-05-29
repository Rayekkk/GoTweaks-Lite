using System;
using System.Threading;
using NLog;
using Windows.Devices.Sensors;
using XboxGamingBarHelper.Labs;

namespace XboxGamingBarHelper.ControllerEmulation
{
    internal readonly struct GyroSample
    {
        public readonly float GyroXDegPerSecond;
        public readonly float GyroYDegPerSecond;
        public readonly float GyroZDegPerSecond;
        public readonly float AccelXG;
        public readonly float AccelYG;
        public readonly float AccelZG;
        public readonly long TimestampTicksUtc;

        public GyroSample(
            float gyroXDegPerSecond,
            float gyroYDegPerSecond,
            float gyroZDegPerSecond,
            float accelXG,
            float accelYG,
            float accelZG,
            long timestampTicksUtc)
        {
            GyroXDegPerSecond = gyroXDegPerSecond;
            GyroYDegPerSecond = gyroYDegPerSecond;
            GyroZDegPerSecond = gyroZDegPerSecond;
            AccelXG = accelXG;
            AccelYG = accelYG;
            AccelZG = accelZG;
            TimestampTicksUtc = timestampTicksUtc;
        }
    }

    internal interface IGyroSourceAdapter : IDisposable
    {
        string Name { get; }

        bool Start();

        void Stop();

        bool TryGetLatestSample(out GyroSample sample);
    }

    internal sealed class WindowsSensorGyroSourceAdapter : IGyroSourceAdapter
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private const uint FallbackReportIntervalMs = 4;
        private const int WarmupTimeoutMs = 800;
        private readonly string name;
        private readonly object sampleLock = new object();
        private Gyrometer gyrometer;
        private Accelerometer accelerometer;
        private bool started;
        private uint originalGyroReportInterval;
        private uint originalAccelReportInterval;
        private long lastGyroTimestampTicksUtc;
        private bool hasAccelSample;
        private float latestAccelX;
        private float latestAccelY;
        private float latestAccelZ;
        private GyroSample latestSample;
        private bool hasUnreadSample;
        private ManualResetEventSlim firstGyroSampleEvent;

        public string Name => name;

        public WindowsSensorGyroSourceAdapter(string name)
        {
            this.name = name;
        }

        public bool Start()
        {
            try
            {
                gyrometer = Gyrometer.GetDefault();
                accelerometer = Accelerometer.GetDefault();
                started = gyrometer != null;

                if (!started)
                {
                    Logger.Warn($"Gyro source '{name}' unavailable: Windows gyrometer not found");
                    return false;
                }

                lock (sampleLock)
                {
                    lastGyroTimestampTicksUtc = 0;
                    hasAccelSample = false;
                    latestAccelX = 0.0f;
                    latestAccelY = 0.0f;
                    latestAccelZ = 0.0f;
                    latestSample = default;
                    hasUnreadSample = false;
                }

                originalGyroReportInterval = gyrometer.ReportInterval;
                uint gyroInterval = gyrometer.MinimumReportInterval > 0
                    ? gyrometer.MinimumReportInterval
                    : FallbackReportIntervalMs;
                gyrometer.ReportInterval = gyroInterval;

                if (accelerometer != null)
                {
                    originalAccelReportInterval = accelerometer.ReportInterval;
                    uint accelInterval = accelerometer.MinimumReportInterval > 0
                        ? accelerometer.MinimumReportInterval
                        : FallbackReportIntervalMs;
                    accelerometer.ReportInterval = accelInterval;
                }
                else
                {
                    originalAccelReportInterval = 0;
                }

                firstGyroSampleEvent = new ManualResetEventSlim(false);
                gyrometer.ReadingChanged += OnGyrometerReadingChanged;
                if (accelerometer != null)
                {
                    accelerometer.ReadingChanged += OnAccelerometerReadingChanged;
                }

                if (!TryWarmupGyroReading(out long firstTimestampTicksUtc))
                {
                    Logger.Warn($"Gyro source '{name}' warmup failed: no readings received within {WarmupTimeoutMs}ms");
                    Stop();
                    return false;
                }

                lastGyroTimestampTicksUtc = firstTimestampTicksUtc;
                Logger.Info($"Gyro source '{name}' started (gyro interval: {gyrometer.ReportInterval}ms, accelerometer available: {accelerometer != null})");
                return true;
            }
            catch (Exception ex)
            {
                started = false;
                Logger.Warn($"Gyro source '{name}' failed to start: {ex.Message}");
                return false;
            }
        }

        public void Stop()
        {
            if (gyrometer != null)
            {
                try
                {
                    gyrometer.ReadingChanged -= OnGyrometerReadingChanged;
                }
                catch
                {
                    // Ignore event detach failures.
                }

                try
                {
                    gyrometer.ReportInterval = originalGyroReportInterval;
                }
                catch
                {
                    // Ignore report interval restore failures.
                }
            }

            if (accelerometer != null)
            {
                try
                {
                    accelerometer.ReadingChanged -= OnAccelerometerReadingChanged;
                }
                catch
                {
                    // Ignore event detach failures.
                }

                try
                {
                    accelerometer.ReportInterval = originalAccelReportInterval;
                }
                catch
                {
                    // Ignore report interval restore failures.
                }
            }

            started = false;
            gyrometer = null;
            accelerometer = null;
            firstGyroSampleEvent?.Dispose();
            firstGyroSampleEvent = null;

            lock (sampleLock)
            {
                lastGyroTimestampTicksUtc = 0;
                hasAccelSample = false;
                latestAccelX = 0.0f;
                latestAccelY = 0.0f;
                latestAccelZ = 0.0f;
                latestSample = default;
                hasUnreadSample = false;
            }
        }

        private bool TryWarmupGyroReading(out long timestampTicksUtc)
        {
            timestampTicksUtc = 0;
            if (gyrometer == null)
            {
                return false;
            }

            if (firstGyroSampleEvent != null && firstGyroSampleEvent.Wait(WarmupTimeoutMs))
            {
                lock (sampleLock)
                {
                    if (lastGyroTimestampTicksUtc > 0)
                    {
                        timestampTicksUtc = lastGyroTimestampTicksUtc;
                        return true;
                    }
                }
            }

            // Fallback for devices where event delivery is delayed.
            var reading = gyrometer.GetCurrentReading();
            if (reading == null)
            {
                return false;
            }

            long ticks = reading.Timestamp.UtcDateTime.Ticks;
            if (ticks <= 0)
            {
                ticks = DateTime.UtcNow.Ticks;
            }

            float accelX = 0.0f;
            float accelY = 0.0f;
            float accelZ = 0.0f;
            var accelReading = accelerometer?.GetCurrentReading();
            if (accelReading != null)
            {
                accelX = (float)accelReading.AccelerationX;
                accelY = (float)accelReading.AccelerationY;
                accelZ = (float)accelReading.AccelerationZ;
            }

            lock (sampleLock)
            {
                lastGyroTimestampTicksUtc = ticks;
                latestSample = new GyroSample(
                    (float)reading.AngularVelocityX,
                    (float)reading.AngularVelocityY,
                    (float)reading.AngularVelocityZ,
                    accelX,
                    accelY,
                    accelZ,
                    ticks);
                hasUnreadSample = true;
            }

            timestampTicksUtc = ticks;
            return true;
        }

        public bool TryGetLatestSample(out GyroSample sample)
        {
            sample = default;
            if (!started || gyrometer == null)
            {
                return false;
            }

            lock (sampleLock)
            {
                if (!hasUnreadSample)
                {
                    return false;
                }

                sample = latestSample;
                hasUnreadSample = false;
                return true;
            }
        }

        private void OnAccelerometerReadingChanged(Accelerometer sender, AccelerometerReadingChangedEventArgs args)
        {
            if (!started)
            {
                return;
            }

            try
            {
                var reading = args?.Reading;
                if (reading == null)
                {
                    return;
                }

                lock (sampleLock)
                {
                    latestAccelX = (float)reading.AccelerationX;
                    latestAccelY = (float)reading.AccelerationY;
                    latestAccelZ = (float)reading.AccelerationZ;
                    hasAccelSample = true;
                }
            }
            catch
            {
                // Ignore transient sensor callback failures.
            }
        }

        private void OnGyrometerReadingChanged(Gyrometer sender, GyrometerReadingChangedEventArgs args)
        {
            if (!started)
            {
                return;
            }

            try
            {
                var reading = args?.Reading;
                if (reading == null)
                {
                    return;
                }

                long timestampTicksUtc = reading.Timestamp.UtcDateTime.Ticks;
                if (timestampTicksUtc <= 0)
                {
                    timestampTicksUtc = DateTime.UtcNow.Ticks;
                }

                lock (sampleLock)
                {
                    if (timestampTicksUtc <= lastGyroTimestampTicksUtc)
                    {
                        return;
                    }

                    lastGyroTimestampTicksUtc = timestampTicksUtc;

                    float accelX = hasAccelSample ? latestAccelX : 0.0f;
                    float accelY = hasAccelSample ? latestAccelY : 0.0f;
                    float accelZ = hasAccelSample ? latestAccelZ : 0.0f;
                    latestSample = new GyroSample(
                        (float)reading.AngularVelocityX,
                        (float)reading.AngularVelocityY,
                        (float)reading.AngularVelocityZ,
                        accelX,
                        accelY,
                        accelZ,
                        timestampTicksUtc);
                    hasUnreadSample = true;
                }

                firstGyroSampleEvent?.Set();
            }
            catch
            {
                // Ignore transient sensor callback failures.
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }

    internal interface ILegionControllerGyroSource { }

    internal sealed class LegionControllerGyroSourceAdapter : IGyroSourceAdapter, ILegionControllerGyroSource
    {
        private readonly bool useLeftController;
        private bool started;

        public string Name => useLeftController ? "Legion Left Controller Gyro" : "Legion Right Controller Gyro";

        public LegionControllerGyroSourceAdapter(bool useLeftController)
        {
            this.useLeftController = useLeftController;
        }

        public bool Start()
        {
            started = true;
            return true;
        }

        public void Stop()
        {
            started = false;
        }

        public bool TryGetLatestSample(out GyroSample sample)
        {
            sample = default;
            if (!started)
            {
                return false;
            }

            if (!LegionButtonMonitor.TryGetLatestGyroSample(useLeftController, out LegionGyroSample parsed))
            {
                return false;
            }

            sample = new GyroSample(
                parsed.GyroXDegPerSecond,
                parsed.GyroYDegPerSecond,
                parsed.GyroZDegPerSecond,
                parsed.AccelXG,
                parsed.AccelYG,
                parsed.AccelZG,
                parsed.TimestampTicksUtc);
            return true;
        }

        public void Dispose()
        {
            Stop();
        }
    }

    /// <summary>
    /// Shared mirror-inversion helper. Lenovo's per-side parser pre-aligns byte offsets
    /// so left/right map to the same physical axis, but the two IMUs are sign-opposed on
    /// the X (pitch) and Y (yaw) axes for the same physical motion, while agreeing on Z
    /// (roll). Live capture 2026-05-27 (GYRODIAG samples, Legion Go 2 -> SteamDeck) proved
    /// this: during yaw L.Y=+58 / R.Y=-58, during pitch L.X=-94 / R.X=+96 (opposite), but
    /// during roll L.Z=+72 / R.Z=+72 (matching). With all-+1 constants the simple average
    /// cancelled pitch and yaw to ~0 while roll survived — the exact reported bug. Flipping
    /// the right X/Y signs brings the right chip into the left's frame so the average adds
    /// instead of cancelling; Z stays +1. (The earlier 2026-05-07 note claiming all axes
    /// matched in sign held only for the stick-gyro pitch test, not yaw or the native path.)
    /// </summary>
    internal static class LegionMixedGyroMerge
    {
        public const float RightGyroXSign = -1f;  // pitch: right is mirror-opposed to left
        public const float RightGyroYSign = -1f;  // yaw:   right is mirror-opposed to left
        public const float RightGyroZSign = +1f;  // roll:  both chips agree
        // Accel mirrors complementary to gyro: the two IMUs are mirror images across the
        // PCB (XY) plane, so the true-vector accel flips only the perpendicular axis (Z)
        // while X/Y agree. Confirmed 2026-05-27 from at-rest gravity in GYRODIAG: aL.Z=+0.11
        // vs aR.Z=-0.11 (opposed), aL.X/Y == aR.X/Y. With +1 the average cancelled accel Z
        // (gravity loss) in Mixed mode; -1 on Z brings the right chip into the left's frame.
        public const float RightAccelXSign = +1f;
        public const float RightAccelYSign = +1f;
        public const float RightAccelZSign = -1f;

        // Redundant-sensor outlier rejection. Angular velocity is identical at every point on
        // a rigid body, so after the mirror correction both chips MUST read the same gyro.
        // When they agree (within this threshold) we average — halving noise. When they
        // disagree by more than the threshold, one chip is glitching (a spike): we discard the
        // average (which would blend half the spike in) and instead take whichever sample is
        // closer to the PREVIOUS merged output, since real motion is continuous and a spike is
        // a sudden jump. The threshold is generous enough not to reject genuine fast motion:
        // the two chips sample up to ~8ms apart (separate 125Hz HID reports), so during a flick
        // they legitimately differ by angular_accel * timing_skew (tens of deg/s). Even if a
        // fast frame is misclassified, the continuity pick still returns a sample on the real
        // trajectory, so the cost of a false positive is negligible.
        private const float GyroAgreeThresholdDps = 60f;

        private static readonly object _mergeStateLock = new object();
        private static bool _hasPrevGyro;
        private static float _prevGyroX, _prevGyroY, _prevGyroZ;

        private static float MergeGyroAxis(float l, float rFlipped, float prev, bool hasPrev)
        {
            if (!hasPrev || Math.Abs(l - rFlipped) <= GyroAgreeThresholdDps)
            {
                return (l + rFlipped) * 0.5f;   // agree → average (noise reduction)
            }
            // disagree → one chip spiked; take the sample nearer the previous output
            return Math.Abs(l - prev) <= Math.Abs(rFlipped - prev) ? l : rFlipped;
        }

        /// <summary>
        /// Merge left and right gyro samples after pre-flipping the right side into the left's
        /// reference frame. Gyro uses agree-average / disagree-continuity-pick outlier rejection
        /// (see GyroAgreeThresholdDps); accel is straight-averaged (used only for gravity fusion,
        /// where per-chip spikes matter far less). Caller handles the single-side-only fallback.
        /// </summary>
        public static GyroSample Merge(LegionGyroSample left, LegionGyroSample right)
        {
            float rGyroX = right.GyroXDegPerSecond * RightGyroXSign;
            float rGyroY = right.GyroYDegPerSecond * RightGyroYSign;
            float rGyroZ = right.GyroZDegPerSecond * RightGyroZSign;
            float rAccelX = right.AccelXG * RightAccelXSign;
            float rAccelY = right.AccelYG * RightAccelYSign;
            float rAccelZ = right.AccelZG * RightAccelZSign;

            float mGyroX, mGyroY, mGyroZ;
            lock (_mergeStateLock)
            {
                bool hasPrev = _hasPrevGyro;
                mGyroX = MergeGyroAxis(left.GyroXDegPerSecond, rGyroX, _prevGyroX, hasPrev);
                mGyroY = MergeGyroAxis(left.GyroYDegPerSecond, rGyroY, _prevGyroY, hasPrev);
                mGyroZ = MergeGyroAxis(left.GyroZDegPerSecond, rGyroZ, _prevGyroZ, hasPrev);
                _prevGyroX = mGyroX;
                _prevGyroY = mGyroY;
                _prevGyroZ = mGyroZ;
                _hasPrevGyro = true;
            }

            return new GyroSample(
                mGyroX,
                mGyroY,
                mGyroZ,
                (left.AccelXG + rAccelX) * 0.5f,
                (left.AccelYG + rAccelY) * 0.5f,
                (left.AccelZG + rAccelZ) * 0.5f,
                Math.Max(left.TimestampTicksUtc, right.TimestampTicksUtc));
        }
    }

    internal sealed class LegionControllerMixedGyroSourceAdapter : IGyroSourceAdapter, ILegionControllerGyroSource
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // Detachable left and right controllers are mounted in a 180°-around-vertical
        // mirror configuration when attached to the handheld. Lenovo's parser remaps
        // byte offsets so left/right report against the same physical axes (X reads
        // from offset+7 on left, offset+9 on right, etc.), but the SIGNS were never
        // flipped for the mirror — Lenovo's official driver always uses one side at
        // a time, so they had no reason to. Naively averaging left + right then
        // produces ~0 on any axis the controllers disagree on.
        //
        // Empirical model for Legion Go / Go 2 (verified 2026-05-07):
        //   Lenovo's parser already remaps byte offsets so L and R report the
        //   same physical axis with the same sign. No per-axis flip is needed.
        //   The merge is a straight L+R average. Sign constants in
        //   LegionMixedGyroMerge default to +1 but stay in place so a hardware
        //   variant that genuinely mirrors an axis can be handled without code
        //   changes.
        //
        // Diagnostic: log raw left + raw right + merged values for the first
        // few samples each Start() so we can re-verify the assumption against
        // any future hardware variant. After this many samples, fall silent.
        private const int DiagSampleCount = 20;

        private bool started;
        private int diagLogged;

        public string Name => "Legion Controller Gyro (Mixed)";

        public bool Start()
        {
            started = true;
            diagLogged = 0;
            return true;
        }

        public void Stop()
        {
            started = false;
        }

        public bool TryGetLatestSample(out GyroSample sample)
        {
            sample = default;
            if (!started)
            {
                return false;
            }

            bool hasLeft = LegionButtonMonitor.TryGetLatestGyroSample(true, out LegionGyroSample left);
            bool hasRight = LegionButtonMonitor.TryGetLatestGyroSample(false, out LegionGyroSample right);

            if (hasLeft && hasRight)
            {
                sample = LegionMixedGyroMerge.Merge(left, right);

                if (diagLogged < DiagSampleCount)
                {
                    diagLogged++;
                    float rGyroX = right.GyroXDegPerSecond * LegionMixedGyroMerge.RightGyroXSign;
                    float rGyroY = right.GyroYDegPerSecond * LegionMixedGyroMerge.RightGyroYSign;
                    float rGyroZ = right.GyroZDegPerSecond * LegionMixedGyroMerge.RightGyroZSign;
                    Logger.Info(
                        $"GyroMix sample {diagLogged}/{DiagSampleCount}: " +
                        $"L=({left.GyroXDegPerSecond:F1},{left.GyroYDegPerSecond:F1},{left.GyroZDegPerSecond:F1}) " +
                        $"R=({right.GyroXDegPerSecond:F1},{right.GyroYDegPerSecond:F1},{right.GyroZDegPerSecond:F1}) " +
                        $"Rflipped=({rGyroX:F1},{rGyroY:F1},{rGyroZ:F1}) " +
                        $"Mixed=({sample.GyroXDegPerSecond:F1},{sample.GyroYDegPerSecond:F1},{sample.GyroZDegPerSecond:F1}) deg/s — " +
                        "ideally L and Rflipped should have matching signs while you're moving");
                }
                return true;
            }

            // Single-side fallback: pass the available side through untouched.
            // No inversion needed — there's nothing to combine with, so the
            // existing per-side conventions still apply downstream.
            LegionGyroSample source;
            if (hasLeft)
                source = left;
            else if (hasRight)
                source = right;
            else
                return false;

            sample = new GyroSample(
                source.GyroXDegPerSecond,
                source.GyroYDegPerSecond,
                source.GyroZDegPerSecond,
                source.AccelXG,
                source.AccelYG,
                source.AccelZG,
                source.TimestampTicksUtc);
            return true;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
