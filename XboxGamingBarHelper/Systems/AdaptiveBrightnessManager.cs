using NLog;
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using Windows.Devices.Sensors;
using XboxGamingBarHelper.Settings;
using XboxGamingBarHelper.Sidebar;

namespace XboxGamingBarHelper.Systems
{
    /// <summary>
    /// Helper-managed adaptive brightness loop. Replacement for Windows ADAPTBRIGHT.
    /// The design goal is iOS-style behavior: rarely needs adjustment, doesn't twitch on
    /// transient flashes, smooth ramps, and learns from manual overrides.
    ///
    /// Pipeline (per ALS reading):
    ///   raw lux
    ///     -> One-Euro filter on log(1 + lux)   adaptive low-pass; tightens when stable,
    ///                                          opens when changing fast
    ///     -> log curve   base = min + (filteredLogLux / log(1+lux_ref)) * (max-min) * sensitivity
    ///     -> learned offset for current lux bucket  target = base + offset
    ///     -> sustained-change requirement (target must hold N ticks before we act)
    ///     -> direction-aware response (brighten quickly, dim slowly)
    ///     -> eased ramp (sub-target updates every InterpTickMs toward committed target)
    ///     -> lux-ratio override hold (slider settles -> learned; new commits paused
    ///                                until ambient changes by ≥ OverrideLuxRatio)
    ///
    /// Learning: when the user manually adjusts brightness, we wait until the slider
    /// settles (3 consecutive identical reads), compute final - base, and EWMA-blend
    /// that into the corresponding lux bucket's offset. Future tick predictions in
    /// that lux range bias toward what the user picked. Same idea as iOS — base curve
    /// from the sensor, learned per-environment offset on top.
    /// </summary>
    internal sealed class AdaptiveBrightnessManager : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // ---- Curve / range ----
        private const int MinBrightness = 5;
        private const int MaxBrightness = 100;
        private const double LuxReference = 1000.0;
        private const double Sensitivity = 1.0;

        // ---- One-Euro filter constants (operate on log(1+lux) for perceptual uniformity) ----
        // Lower MinCutoff = smoother but laggier; Beta scales how fast the cutoff opens
        // when the signal moves. Tuned against handheld ALS noise floor (~5–15% jitter
        // even in stable lighting): MinCutoff=0.30 Hz keeps idle drift < 1 brightness step,
        // Beta=0.40 lets a real lighting change land within ~600 ms.
        private const double OneEuroMinCutoff = 0.30;
        private const double OneEuroBeta = 0.40;
        private const double OneEuroDerivativeCutoff = 1.0;

        // ---- Sustained-change requirement (the iPhone "rarely needs adjustment" core) ----
        // Target must stay on the same side of current for this many evaluation ticks
        // before we commit a write. Higher = more inertial; smaller = more responsive.
        // Direction-asymmetric: brightening engages faster than dimming because users
        // notice "screen is too dark" immediately but "screen is slightly too bright"
        // is rarely worth a flicker.
        private const int SustainedTicksBrighten = 3;
        private const int SustainedTicksDim = 6;

        // ---- Action thresholds ----
        // Asymmetric — easier to brighten than to dim. Once the sustained-tick gate
        // opens, this is the |delta| (in brightness units) that must still hold to act.
        private const int BrightenThreshold = 3;
        private const int DimThreshold = 6;

        // ---- Eased ramp ----
        // Sub-tick updates that walk current -> committed target with cubic ease-out.
        // Single visible step every 1 s feels like a flash; ~250 ms ramp at 30 ms cadence
        // is below the "noticeable change" threshold for most users.
        private const int InterpTickMs = 30;
        private const int RampDurationMsBrighten = 200;
        private const int RampDurationMsDim = 600;

        // ---- Override hold ----
        // After the user adjusts the slider, hold the new value until ambient lux changes
        // by at least this ratio (or its reciprocal). 2x covers most "I moved into a new
        // room" transitions while ignoring small idle drift (cloud passing, monitor flicker).
        private const double OverrideLuxRatio = 2.0;

        // ---- Tick + safety ----
        private const int EvalTickMs = 250;
        private static readonly TimeSpan UserOverrideMaxHold = TimeSpan.FromHours(8);
        private const int SettleSamples = 3;

        // ---- Learning ----
        // 6 buckets: [0,2) [2,10) [10,50) [50,200) [200,1000) [1000,inf).
        private static readonly double[] BucketUpper = { 2.0, 10.0, 50.0, 200.0, 1000.0, double.PositiveInfinity };
        private const double LearningAlpha = 0.3;
        private const int OffsetClamp = 40;
        private const string OffsetsSettingsKey = "HelperAdaptiveBrightnessOffsets";

        // ---- State ----
        private LightSensor sensor;
        private Timer evalTimer;
        private Timer rampTimer;

        // One-Euro filter state (tracking log(1+lux)).
        private double oeLastLogLux = double.NaN;
        private double oeLastDerivative;
        private long oeLastTimestamp;
        private bool oeInitialized;

        // Sustained-change accumulator.
        private int sustainedDir;          // -1, 0, +1
        private int sustainedCount;
        private int lastTargetSeen;

        // Eased ramp.
        private int rampCurrentBrightness;
        private int rampTargetBrightness = -1;
        private long rampStartTicks;
        private int rampDurationMs;

        // Override / learning.
        private int lastWrittenBrightness = -1;
        private bool overrideActive;
        private double overrideLuxAtCommit;
        private DateTime overrideUntilUtc;
        private bool overridePending;
        private int overrideLastSample = -1;
        private int overrideStableCount;
        private double overrideLuxAtDetection;

        private readonly double[] offsets = new double[BucketUpper.Length];

        public AdaptiveBrightnessManager()
        {
            LoadOffsets();
        }

        public bool Start()
        {
            if (evalTimer != null) return true;
            try
            {
                sensor = LightSensor.GetDefault();
                if (sensor == null)
                {
                    Logger.Warn("AdaptiveBrightness: no LightSensor available on this device");
                    return false;
                }
                sensor.ReportInterval = Math.Max(sensor.MinimumReportInterval, 250);
                sensor.ReadingChanged += OnReading;
                evalTimer = new Timer(OnEvalTick, null, EvalTickMs, EvalTickMs);
                Logger.Info($"AdaptiveBrightness: started (ALS report interval {sensor.ReportInterval} ms, eval cadence {EvalTickMs} ms, learned offsets [{string.Join(",", offsets.Select(o => o.ToString("F1", CultureInfo.InvariantCulture)))}])");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"AdaptiveBrightness: start failed: {ex.Message}");
                Stop();
                return false;
            }
        }

        public void Stop()
        {
            try { if (sensor != null) sensor.ReadingChanged -= OnReading; } catch { }
            sensor = null;
            try { evalTimer?.Dispose(); } catch { }
            evalTimer = null;
            try { rampTimer?.Dispose(); } catch { }
            rampTimer = null;
            oeInitialized = false;
            oeLastLogLux = double.NaN;
            sustainedDir = 0;
            sustainedCount = 0;
            rampTargetBrightness = -1;
            lastWrittenBrightness = -1;
            overridePending = false;
            overrideActive = false;
            Logger.Info("AdaptiveBrightness: stopped");
        }

        // --------------------------------------------------------------
        // ALS reading: feed One-Euro filter on log(1+lux)
        // --------------------------------------------------------------
        private void OnReading(LightSensor s, LightSensorReadingChangedEventArgs e)
        {
            try
            {
                double lux = e.Reading.IlluminanceInLux;
                if (lux < 0) return;
                double logLux = Math.Log(1.0 + lux);
                long now = DateTime.UtcNow.Ticks;

                if (!oeInitialized)
                {
                    oeLastLogLux = logLux;
                    oeLastDerivative = 0.0;
                    oeLastTimestamp = now;
                    oeInitialized = true;
                    return;
                }

                double dt = Math.Max(0.001, (now - oeLastTimestamp) / (double)TimeSpan.TicksPerSecond);
                oeLastTimestamp = now;

                // Derivative low-pass.
                double rawDerivative = (logLux - oeLastLogLux) / dt;
                double dAlpha = OneEuroAlpha(OneEuroDerivativeCutoff, dt);
                double smoothedDerivative = oeLastDerivative + (rawDerivative - oeLastDerivative) * dAlpha;
                oeLastDerivative = smoothedDerivative;

                // Adaptive cutoff: opens proportionally to |derivative|. Stable signal -> tight
                // cutoff (smooth, idle-stable). Fast change -> high cutoff (fast tracking).
                double dynamicCutoff = OneEuroMinCutoff + OneEuroBeta * Math.Abs(smoothedDerivative);
                double vAlpha = OneEuroAlpha(dynamicCutoff, dt);
                oeLastLogLux = oeLastLogLux + (logLux - oeLastLogLux) * vAlpha;
            }
            catch (Exception ex)
            {
                Logger.Warn($"AdaptiveBrightness: reading handler: {ex.Message}");
            }
        }

        private static double OneEuroAlpha(double cutoff, double dt)
        {
            double tau = 1.0 / (2.0 * Math.PI * Math.Max(0.001, cutoff));
            return 1.0 / (1.0 + tau / dt);
        }

        // --------------------------------------------------------------
        // Eval tick: decide and commit a new target
        // --------------------------------------------------------------
        private void OnEvalTick(object _)
        {
            try
            {
                if (!oeInitialized) return;
                double luxFiltered = Math.Exp(oeLastLogLux) - 1.0;
                if (luxFiltered < 0) luxFiltered = 0;

                int currentBrightness = BrightnessManager.GetBrightness();

                // 1) Override detection: someone moved the slider.
                if (lastWrittenBrightness >= 0
                    && Math.Abs(currentBrightness - lastWrittenBrightness) > 1
                    && !overridePending)
                {
                    overridePending = true;
                    overrideLastSample = currentBrightness;
                    overrideStableCount = 1;
                    overrideLuxAtDetection = luxFiltered;
                    Logger.Info($"AdaptiveBrightness: override start (slider {currentBrightness} vs lastWrite {lastWrittenBrightness}, lux {luxFiltered:F0})");
                }

                // 2) While override is pending, wait for the slider to settle, then learn.
                if (overridePending)
                {
                    if (currentBrightness == overrideLastSample) overrideStableCount++;
                    else { overrideLastSample = currentBrightness; overrideStableCount = 1; }

                    if (overrideStableCount >= SettleSamples)
                    {
                        TrainOffset(overrideLuxAtDetection, currentBrightness);
                        lastWrittenBrightness = currentBrightness;
                        overridePending = false;

                        // Hold this brightness until ambient lux changes by OverrideLuxRatio.
                        overrideActive = true;
                        overrideLuxAtCommit = overrideLuxAtDetection;
                        overrideUntilUtc = DateTime.UtcNow + UserOverrideMaxHold;
                        rampTargetBrightness = -1;
                    }
                    return;
                }

                // 3) Lux-ratio override hold. Walk away from it only when ambient has actually
                //    moved meaningfully. Prevents the helper from undoing the user's nudge in
                //    the same lighting environment.
                if (overrideActive)
                {
                    bool expired = DateTime.UtcNow > overrideUntilUtc;
                    bool ratioBroken =
                        overrideLuxAtCommit > 0
                        && (luxFiltered / overrideLuxAtCommit >= OverrideLuxRatio
                            || overrideLuxAtCommit / Math.Max(1.0, luxFiltered) >= OverrideLuxRatio);
                    if (!expired && !ratioBroken)
                    {
                        sustainedDir = 0;
                        sustainedCount = 0;
                        return;
                    }
                    overrideActive = false;
                    Logger.Info($"AdaptiveBrightness: override released (luxAtCommit={overrideLuxAtCommit:F0} now={luxFiltered:F0} expired={expired} ratioBroken={ratioBroken})");
                }

                int target = ComputeTarget(luxFiltered);
                int delta = target - currentBrightness;
                int absDelta = Math.Abs(delta);
                int dir = Math.Sign(delta);

                int threshold = dir > 0 ? BrightenThreshold : DimThreshold;
                int sustainNeeded = dir > 0 ? SustainedTicksBrighten : SustainedTicksDim;

                if (absDelta < threshold || dir == 0)
                {
                    // Inside the dead-band — don't act, and decay any in-flight sustain
                    // counter. Avoids reacting to brief lux excursions.
                    sustainedDir = 0;
                    sustainedCount = 0;
                    return;
                }

                if (dir != sustainedDir)
                {
                    sustainedDir = dir;
                    sustainedCount = 1;
                    lastTargetSeen = target;
                    return;
                }

                // Same direction as last tick. Stop the sustain progress if the magnitude
                // shrank back inside the dead-band on this side; otherwise tick up.
                sustainedCount++;
                lastTargetSeen = target;
                if (sustainedCount < sustainNeeded) return;

                // Sustained gate cleared — commit the ramp toward this target.
                StartRampTo(currentBrightness, target, dir);
                sustainedDir = 0;
                sustainedCount = 0;
            }
            catch (Exception ex)
            {
                Logger.Warn($"AdaptiveBrightness: eval tick: {ex.Message}");
            }
        }

        // --------------------------------------------------------------
        // Eased ramp: cubic ease-out from current to committed target
        // --------------------------------------------------------------
        private void StartRampTo(int from, int to, int dir)
        {
            if (to == from) return;
            if (to < MinBrightness) to = MinBrightness;
            if (to > MaxBrightness) to = MaxBrightness;

            rampCurrentBrightness = from;
            rampTargetBrightness = to;
            rampStartTicks = DateTime.UtcNow.Ticks;
            rampDurationMs = dir > 0 ? RampDurationMsBrighten : RampDurationMsDim;

            if (rampTimer == null)
            {
                rampTimer = new Timer(OnRampTick, null, InterpTickMs, InterpTickMs);
            }
            Logger.Info($"AdaptiveBrightness: ramp {from} -> {to} ({rampDurationMs} ms)");
        }

        private void OnRampTick(object _)
        {
            try
            {
                int target = rampTargetBrightness;
                if (target < 0) return;
                int from = rampCurrentBrightness;

                long elapsedMs = (DateTime.UtcNow.Ticks - rampStartTicks) / TimeSpan.TicksPerMillisecond;
                double t = rampDurationMs <= 0 ? 1.0 : Math.Min(1.0, elapsedMs / (double)rampDurationMs);
                double eased = 1.0 - Math.Pow(1.0 - t, 3.0); // cubic ease-out
                int next = (int)Math.Round(from + (target - from) * eased);

                if (next != lastWrittenBrightness)
                {
                    BrightnessManager.SetBrightness(next);
                    lastWrittenBrightness = next;
                }

                if (t >= 1.0)
                {
                    rampTargetBrightness = -1;
                    try { rampTimer?.Dispose(); } catch { }
                    rampTimer = null;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"AdaptiveBrightness: ramp tick: {ex.Message}");
            }
        }

        // --------------------------------------------------------------
        // Curve + learned-offset application
        // --------------------------------------------------------------
        private int ComputeTarget(double lux)
        {
            int baseTarget = ComputeBaseTarget(lux);
            double offset = InterpolateOffset(lux);
            int adjusted = baseTarget + (int)Math.Round(offset);
            if (adjusted < MinBrightness) adjusted = MinBrightness;
            if (adjusted > MaxBrightness) adjusted = MaxBrightness;
            return adjusted;
        }

        private static int ComputeBaseTarget(double lux)
        {
            double scale = Math.Log(1.0 + lux) / Math.Log(1.0 + LuxReference);
            scale *= Sensitivity;
            if (scale > 1.0) scale = 1.0;
            if (scale < 0.0) scale = 0.0;
            int range = MaxBrightness - MinBrightness;
            return MinBrightness + (int)Math.Round(scale * range);
        }

        private double InterpolateOffset(double lux)
        {
            int idx = BucketIndex(lux);
            int prev = idx == 0 ? 0 : idx - 1;
            int next = idx == BucketUpper.Length - 1 ? idx : idx + 1;

            double centerThis = BucketCenterLog(idx);
            double centerPrev = BucketCenterLog(prev);
            double centerNext = BucketCenterLog(next);
            double l = Math.Log(1.0 + lux);

            double otherCenter = (Math.Abs(l - centerPrev) < Math.Abs(l - centerNext)) ? centerPrev : centerNext;
            int otherIdx = (otherCenter == centerPrev) ? prev : next;

            if (Math.Abs(otherCenter - centerThis) < 1e-9) return offsets[idx];

            double t = (l - centerThis) / (otherCenter - centerThis);
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return offsets[idx] * (1 - t) + offsets[otherIdx] * t;
        }

        private void TrainOffset(double luxAtDetection, int finalBrightness)
        {
            int idx = BucketIndex(luxAtDetection);
            int baseTarget = ComputeBaseTarget(luxAtDetection);
            double sampleOffset = finalBrightness - baseTarget;
            double updated = LearningAlpha * sampleOffset + (1 - LearningAlpha) * offsets[idx];
            if (updated > OffsetClamp) updated = OffsetClamp;
            if (updated < -OffsetClamp) updated = -OffsetClamp;
            offsets[idx] = updated;
            SaveOffsets();
            Logger.Info($"AdaptiveBrightness: learned bucket[{idx}] (lux≈{luxAtDetection:F0}) base={baseTarget} user={finalBrightness} sampleΔ={sampleOffset:F1} -> offset={offsets[idx]:F1}");
        }

        private static int BucketIndex(double lux)
        {
            for (int i = 0; i < BucketUpper.Length; i++)
                if (lux < BucketUpper[i]) return i;
            return BucketUpper.Length - 1;
        }

        private static double BucketCenterLog(int idx)
        {
            double lower = idx == 0 ? 0.0 : BucketUpper[idx - 1];
            double upper = double.IsPositiveInfinity(BucketUpper[idx]) ? lower * 5.0 + 1.0 : BucketUpper[idx];
            return (Math.Log(1.0 + lower) + Math.Log(1.0 + upper)) * 0.5;
        }

        private void LoadOffsets()
        {
            try
            {
                if (!LocalSettingsHelper.TryGetValue<string>(OffsetsSettingsKey, out var s) || string.IsNullOrEmpty(s)) return;
                var parts = s.Split(',');
                int n = Math.Min(parts.Length, offsets.Length);
                for (int i = 0; i < n; i++)
                {
                    if (double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) offsets[i] = v;
                }
            }
            catch (Exception ex) { Logger.Warn($"AdaptiveBrightness: LoadOffsets: {ex.Message}"); }
        }

        private void SaveOffsets()
        {
            try
            {
                var s = string.Join(",", offsets.Select(o => o.ToString("F2", CultureInfo.InvariantCulture)));
                LocalSettingsHelper.SetValue(OffsetsSettingsKey, s);
            }
            catch (Exception ex) { Logger.Warn($"AdaptiveBrightness: SaveOffsets: {ex.Message}"); }
        }

        public void Dispose() => Stop();
    }
}
