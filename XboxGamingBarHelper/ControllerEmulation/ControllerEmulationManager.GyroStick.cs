using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Shared.Data;
using Shared.Enums;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.Devices;
using XboxGamingBarHelper.Devices.Libraries.GPD;
using XboxGamingBarHelper.Devices.Libraries.Legion;
using XboxGamingBarHelper.Labs;
using XboxGamingBarHelper.Settings;
using XboxGamingBarHelper.Windows;
using SharedDeviceType = Shared.Enums.DeviceType;

namespace XboxGamingBarHelper.ControllerEmulation
{
    internal partial class ControllerEmulationManager
    {

        private long stickTouchActiveTicks = 0;

        private void ApplyStickFromGyro(
            GyroSample sample,
            out short outputX,
            out short outputY)
        {
            // Default the physical-stick values to zero (no touch) — the new
            // overload below threads them in from the caller.
            ApplyStickFromGyro(sample, 0, 0, out outputX, out outputY);
        }

        private void ApplyStickFromGyro(
            GyroSample sample,
            short physicalStickX,
            short physicalStickY,
            out short outputX,
            out short outputY)
        {
            outputX = 0;
            outputY = 0;

            // Debug: log raw gyro axes periodically to diagnose axis mapping.
            // Gated by IsDebugEnabled so the string interpolation only runs when Debug logging is active.
            if (Logger.IsDebugEnabled
                && sample.TimestampTicksUtc - stickLastDiagLogTicksUtc > TimeSpan.TicksPerSecond)
            {
                stickLastDiagLogTicksUtc = sample.TimestampTicksUtc;
                float absX = Math.Abs(sample.GyroXDegPerSecond);
                float absY = Math.Abs(sample.GyroYDegPerSecond);
                float absZ = Math.Abs(sample.GyroZDegPerSecond);
                if (absX > 5 || absY > 5 || absZ > 5)
                {
                    Logger.Debug($"StickGyroRaw: X={sample.GyroXDegPerSecond:F1} Y={sample.GyroYDegPerSecond:F1} Z={sample.GyroZDegPerSecond:F1} conv={stickConversion} orient={stickOrientationV2}");
                }
            }

            // 0a. Sensor fusion update — push the RAW sample (pre-bias-correction)
            //     into JSL so its continuous-calibration "is steady" detector and
            //     internal gyro-bias estimator see actual sensor data rather than
            //     a sample we already corrected. Without raw input, JSL learns
            //     bias~=0 against an already-flat signal and the gravity vector
            //     drifts when our biasEstimator is wrong.
            long sampleTicks = sample.TimestampTicksUtc;
            float jslDeltaSeconds = stickGamepadMotionLastTicksUtc > 0 && sampleTicks > stickGamepadMotionLastTicksUtc
                ? (float)((sampleTicks - stickGamepadMotionLastTicksUtc) / (double)TimeSpan.TicksPerSecond)
                : DefaultDeltaSeconds;
            stickGamepadMotionLastTicksUtc = sampleTicks;

            // Always feed JSL the real accelerometer — see ViiperStickGyroProcessor
            // for full notes. The handheld override broke JSL's gravity tracking,
            // so we no longer override accel.
            stickGamepadMotion.Update(
                sample.GyroXDegPerSecond, sample.GyroYDegPerSecond, sample.GyroZDegPerSecond,
                sample.AccelXG, sample.AccelYG, sample.AccelZG,
                jslDeltaSeconds);

            // 0b. Read gyro from JSL's calibrated path for ALL conversion modes
            //     so Mode 0/1/2 share the same input feed as Player/World Space.
            //     Closes the felt-jitter gap between gravity-projected modes
            //     (which already used JSL state) and the raw-axis modes.
            stickGamepadMotion.GetCalibratedGyro(out float jslGyroX, out float jslGyroY, out float jslGyroZ);

            // Spike rejection: single-sample USB/EMI transmission glitches
            // occasionally produce one HID report with gyro tens of °/s
            // above the genuine motion baseline. The smoother dampens but
            // doesn't fully suppress them — they cross the anti-deadzone
            // threshold and produce a one-frame stick flicker. Detect by
            // comparing current raw sample magnitude vs the smoothed
            // (recent-history) magnitude: if recent history is near-idle
            // AND the current sample is suddenly large on any axis, reject
            // it. Genuine fast motion has a non-zero smoothed state already,
            // so this rejection only fires for true outliers.
            const float SpikeIdleFloorDps = 5.0f;
            const float SpikeMagnitudeDps = 60.0f;
            float curMaxAbs = Math.Max(Math.Abs(jslGyroX), Math.Max(Math.Abs(jslGyroY), Math.Abs(jslGyroZ)));
            float smoothedMaxAbs = Math.Max(Math.Abs(smoothedGyroXState), Math.Max(Math.Abs(smoothedGyroYState), Math.Abs(smoothedGyroZState)));
            if (smoothedGyroPrimed && smoothedMaxAbs < SpikeIdleFloorDps && curMaxAbs > SpikeMagnitudeDps)
            {
                jslGyroX = smoothedGyroXState;
                jslGyroY = smoothedGyroYState;
                jslGyroZ = smoothedGyroZState;
            }

            // Light EMA smoothing on the calibrated gyro stream. Even with a
            // calibrated bias offset, the per-sample IMU noise floor (~1°/s
            // peak-to-peak) shows up as visible stick jitter under the
            // sensitivity × curve × anti-deadzone pipeline. The smoother
            // averages each sample with prior state so high-frequency noise
            // dampens while genuine motion still passes through. Tunable
            // strength: 0 = off, 90 = heavy. Default 30 = ~5 ms half-life at
            // 64 Hz, no felt input lag.
            if (stickGyroSmoothing > 0)
            {
                float alpha = stickGyroSmoothing / 100.0f;
                if (!smoothedGyroPrimed)
                {
                    smoothedGyroXState = jslGyroX;
                    smoothedGyroYState = jslGyroY;
                    smoothedGyroZState = jslGyroZ;
                    smoothedGyroPrimed = true;
                }
                else
                {
                    smoothedGyroXState = alpha * smoothedGyroXState + (1.0f - alpha) * jslGyroX;
                    smoothedGyroYState = alpha * smoothedGyroYState + (1.0f - alpha) * jslGyroY;
                    smoothedGyroZState = alpha * smoothedGyroZState + (1.0f - alpha) * jslGyroZ;
                }
                jslGyroX = smoothedGyroXState;
                jslGyroY = smoothedGyroYState;
                jslGyroZ = smoothedGyroZState;
            }

            // Keep our biasEstimator running on the raw sample so its internal
            // "is stationary" state stays warm for any callers that still
            // consult it. Output unused.
            stickGyroBiasEstimator.Correct(sample);

            // 1. Orientation correction — SwapYawRoll:
            //    (X, Y, Z) → (X, -Z, -Y). The previous
            //    (X, Z, -Y) form had a sign error that pushed the user's actual
            //    yaw signal onto +gyroZ (unused by Mode 0) and noise floor onto
            //    +gyroY (Mode 0's horizontal) — that was the saturated-stutter
            //    cause in the regression logs.
            float gyroX = jslGyroX;
            float gyroY = jslGyroY;
            float gyroZ = jslGyroZ;

            if (stickOrientationV2 == 1)
            {
                float origY = gyroY;
                float origZ = gyroZ;
                gyroY = -origZ;
                gyroZ = -origY;
            }

            // 2. 3DOF-to-2D conversion
            float horizontal;
            float vertical;
            switch (stickConversion)
            {
                case 1: // Roll
                    horizontal = gyroZ;
                    vertical = gyroX;
                    break;
                case 2: // Yaw + Roll (averaged so horizontal stays magnitude-symmetric with vertical;
                        // summing the two horizontal sources gave horizontal an effective 2x boost
                        // from incidental wrist roll during pure-yaw motion, killing slow-end pitch
                        // accuracy. Sensitivity slider now applies to both axes equally.)
                    horizontal = (gyroY + gyroZ) * 0.5f;
                    vertical = gyroX;
                    break;
                case 3: // Player Space — gravity-aware projection. Good for
                        // device held flat; assumes pitch axis = device-X.
                    stickGamepadMotion.GetPlayerSpaceGyro(out horizontal, out vertical, gyroY, gyroX);
                    break;
                case 4: // World Space — re-derives pitch axis from gravity
                        // instead of assuming pitch = device-X. Better for
                        // tilted handhelds; identical output when held flat.
                    stickGamepadMotion.GetWorldSpaceGyro(out horizontal, out vertical, gyroY, gyroX);
                    break;
                default: // 0 = Yaw
                    horizontal = gyroY;
                    vertical = gyroX;
                    break;
            }

            // Bake in the Invert X preference observed across all gyro sources
            // and conv modes (Player/World Space's wrapper negation and our
            // parser flips kept canceling out at the apply layer). Inverting
            // horizontal at the output stage handles every conversion mode
            // uniformly — Yaw/Roll/Yaw+Roll/Player Space/World Space all flow
            // through this same point. The Invert X toggle remains available
            // as an in-game preference (will now revert to "raw" if user
            // explicitly wants the opposite direction in some game).
            horizontal = -horizontal;

            // 3. Invert axes (user-facing toggles, for in-game preference)
            if (stickInvertX) horizontal = -horizontal;
            if (stickInvertY) vertical = -vertical;

            // 4. Sensitivity curve + tightening + per-axis scale.
            //    Curve: user-selectable preset (Linear / Slow-and-precise / Snap-aim).
            //    Tightening: above the threshold, output gain ramps up linearly to
            //    the user-set "fast-zone" gain — lets users do precise slow aim
            //    AND quick whip-turns without changing the master slider.
            //    Per-axis: vertical sensitivity is scaled by stickGyroVerticalRatio
            //    (% of master) so users can tune yaw/pitch independently.
            float[] curveTable = SelectStickGyroCurve(stickGyroCurvePreset);
            float curveX = ApplyStickGyroCurve(horizontal, curveTable);
            float curveY = ApplyStickGyroCurve(vertical,   curveTable);
            float baseSens = Math.Max(0.01f, stickSensitivityV2 / 100.0f) * StickGyroHcSensScale;
            float vertSens = baseSens * Math.Max(10, Math.Min(200, stickGyroVerticalRatio)) / 100.0f;
            float tightenX = ComputeStickGyroTightenGain(horizontal, stickGyroTightenThreshold, stickGyroTightenGain);
            float tightenY = ComputeStickGyroTightenGain(vertical,   stickGyroTightenThreshold, stickGyroTightenGain);
            float scaledX = horizontal * curveX * baseSens * tightenX;
            float scaledY = vertical   * curveY * vertSens * tightenY;

            // Stick-touch deactivation: suppress gyro output when the user is
            // moving the physical stick past the configured threshold. After
            // the stick returns to center, wait the hold-off before resuming
            // so a stick flick doesn't immediately fight with restored gyro.
            if (stickGyroTouchDeactivateEnabled)
            {
                bool suppressed = UpdateStickTouchSuppression(physicalStickX, physicalStickY,
                    stickGyroTouchDeactivateThreshold, stickGyroTouchDeactivateHoldoff);
                if (suppressed) { scaledX = 0.0f; scaledY = 0.0f; }
            }

            // 5. Anti-deadzone for small-motion precision. Most games kill
            //    stick output below ~10–20% as built-in deadzone; without this
            //    rescale, a 2°/s aim adjustment produces ~6% deflection that
            //    the game silently swallows. Smooth rescale: any
            //    non-trivial input (>0.5°/s) is mapped from [0, max] to
            //    [adz, max], so tiny motions land just past typical deadzones
            //    while large motions still saturate cleanly. Default offset is
            //    ~0.10 of stick range.
            // Anti-deadzone uses the manager-level settings so the widget can
            // tune them live. UserPercent (0-30) maps linearly into [0, 0.30]
            // of int16 range; UserThreshold (0-50) maps linearly into [0, 5.0]
            // °/s. Both 0 disables (preserves the no-rescale path for users
            // who want true raw output).
            float adzInt16 = (Math.Max(0, Math.Min(30, stickGyroAntiDeadzone)) / 100.0f) * short.MaxValue;
            float adzThresholdDps = Math.Max(0, Math.Min(50, stickGyroAntiDeadzoneThreshold)) / 10.0f;
            scaledX = ApplyStickGyroAntiDeadzone(horizontal, scaledX, adzInt16, adzThresholdDps);
            scaledY = ApplyStickGyroAntiDeadzone(vertical,   scaledY, adzInt16, adzThresholdDps);

            outputX = ClampToInt16(scaledX);
            outputY = ClampToInt16(scaledY);

            // Live readings push for the widget visualizer (throttled to 5 Hz).
            PublishStickGyroLiveReadings(horizontal, vertical, 0.0f, outputX, outputY, true);
        }

        // Smooth anti-deadzone: the hard "below threshold → output 0" cutoff
        // killed precision aim. We now treat the user-set threshold as the
        // *top* of a ramp zone whose bottom is threshold/2 — below threshold/2
        // it's a true dead zone, between threshold/2 and threshold the
        // anti-deadzone offset ramps in linearly, above threshold it's at
        // full strength. End result for slow pitch motion: continuous output
        // from ~0 up through the full stick range, no felt step.
        private static float ApplyStickGyroAntiDeadzone(float gyroDps, float scaledOutput, float adzInt16, float adzThresholdDps)
        {
            if (adzInt16 <= 0.0f) return scaledOutput;
            float absGyro = Math.Abs(gyroDps);
            float deadFloor = adzThresholdDps * 0.5f;
            if (absGyro < deadFloor) return 0.0f;
            float ramp = adzThresholdDps > deadFloor
                ? Math.Min(1.0f, (absGyro - deadFloor) / (adzThresholdDps - deadFloor))
                : 1.0f;
            float sign = scaledOutput >= 0.0f ? 1.0f : -1.0f;
            float absScaled = Math.Abs(scaledOutput);
            float effectiveAdz = adzInt16 * ramp;
            float remap = effectiveAdz + absScaled * (1.0f - effectiveAdz / short.MaxValue);
            return sign * remap;
        }

        // Per-axis sensitivity scale: default 1.0,
        // multiplied by 1000 to convert deg/s into stick units.
        private const float StickGyroHcSensScale = 1000.0f;
        private const float StickGyroHcThresholdDegPerSec = 124.0f;
        private const int StickGyroHcCurveNodeCount = 49;
        private static readonly float[] StickGyroHcCurve = MakeStickGyroFlatCurve(0.5f);

        // Three curve presets (49-node lookup tables). Each value × 2 is the
        // effective multiplier at that gyro rate. Linear = flat 0.5 everywhere
        // (always 1.0× multiplier). Slow-and-precise = low values reduced so
        // slow motion produces less stick output (precision aim feel).
        // Snap-aim = low values reduced AND high values boosted so users get
        // precise slow aim plus quick whip-turns from the same slider.
        private static readonly float[] StickGyroCurveLinear = MakeStickGyroFlatCurve(0.5f);
        private static readonly float[] StickGyroCurveSlow = MakeStickGyroSlowPreciseCurve();
        private static readonly float[] StickGyroCurveSnap = MakeStickGyroSnapAimCurve();

        private static float[] SelectStickGyroCurve(int preset)
        {
            switch (preset)
            {
                case 1: return StickGyroCurveSlow;
                case 2: return StickGyroCurveSnap;
                default: return StickGyroCurveLinear;
            }
        }

        private static float[] MakeStickGyroSlowPreciseCurve()
        {
            // Concave: nodes 0..5 = 0.20, ramp linearly up to 0.5 by node 15,
            // stay flat at 0.5 for the rest. Slow motion → less stick output.
            var arr = new float[StickGyroHcCurveNodeCount];
            for (int i = 0; i < arr.Length; i++)
            {
                if (i <= 5) arr[i] = 0.20f;
                else if (i <= 15) arr[i] = 0.20f + (0.5f - 0.20f) * (i - 5) / 10.0f;
                else arr[i] = 0.5f;
            }
            return arr;
        }

        private static float[] MakeStickGyroSnapAimCurve()
        {
            // Slow region trimmed (precision) + fast region boosted (snap).
            // Nodes 0..3 = 0.20, ramp to 0.5 by node 10, stay 0.5 to node 25,
            // ramp up to 0.85 by node 40+ (high end = strong boost for whip-turns).
            var arr = new float[StickGyroHcCurveNodeCount];
            for (int i = 0; i < arr.Length; i++)
            {
                if (i <= 3) arr[i] = 0.20f;
                else if (i <= 10) arr[i] = 0.20f + (0.5f - 0.20f) * (i - 3) / 7.0f;
                else if (i <= 25) arr[i] = 0.5f;
                else if (i <= 40) arr[i] = 0.5f + (0.85f - 0.5f) * (i - 25) / 15.0f;
                else arr[i] = 0.85f;
            }
            return arr;
        }

        private static float ApplyStickGyroCurve(float value, float[] curve)
        {
            if (curve == null || curve.Length == 0) return 1.0f;
            float position = Math.Abs(value) / StickGyroHcThresholdDegPerSec;
            if (position >= curve.Length - 1) return curve[curve.Length - 1] * 2.0f;
            int lo = (int)Math.Floor(position);
            int hi = lo + 1;
            float t = position - lo;
            return ((1.0f - t) * curve[lo] + t * curve[hi]) * 2.0f;
        }

        // Tightening: at zero deg/s, gain = 1.0× (no boost). Above the user
        // threshold, gain ramps linearly to the configured fast-zone gain.
        // Plateau at the threshold itself = 1.0× (no jump); ramp completes
        // over the next 2× threshold's worth of input so the bonus fades in
        // naturally with motion intensity. gain%=100 disables (no boost).
        private static float ComputeStickGyroTightenGain(float gyroDps, int thresholdDps, int gainPercent)
        {
            if (thresholdDps <= 0 || gainPercent <= 100) return 1.0f;
            float absGyro = Math.Abs(gyroDps);
            if (absGyro <= thresholdDps) return 1.0f;
            float rampWidth = thresholdDps * 2.0f;
            float ramp = Math.Min(1.0f, (absGyro - thresholdDps) / rampWidth);
            float maxBoost = gainPercent / 100.0f;
            return 1.0f + (maxBoost - 1.0f) * ramp;
        }

        // Returns true while gyro output should be suppressed due to physical
        // stick touch. Threshold is % of int16 stick range. Hold-off prevents
        // gyro from "snapping back" the moment the user releases the stick.
        private bool UpdateStickTouchSuppression(short physicalX, short physicalY, int thresholdPct, int holdoffMs)
        {
            float magPct = (float)Math.Sqrt(physicalX * (double)physicalX + physicalY * (double)physicalY) / short.MaxValue * 100.0f;
            long now = DateTime.UtcNow.Ticks;
            if (magPct >= thresholdPct)
            {
                stickTouchActiveTicks = now;
                return true;
            }
            long heldOffTicks = holdoffMs * TimeSpan.TicksPerMillisecond;
            return stickTouchActiveTicks > 0 && (now - stickTouchActiveTicks) < heldOffTicks;
        }

        private static float[] MakeStickGyroFlatCurve(float v)
        {
            var arr = new float[StickGyroHcCurveNodeCount];
            for (int i = 0; i < arr.Length; i++) arr[i] = v;
            return arr;
        }

        private static float HcApplyCustomSensitivity(float value)
        {
            if (StickGyroHcThresholdDegPerSec <= 0.0f) return 1.0f;
            float position = Math.Abs(value) / StickGyroHcThresholdDegPerSec;
            if (position >= StickGyroHcCurve.Length - 1)
            {
                return StickGyroHcCurve[StickGyroHcCurve.Length - 1] * 2.0f;
            }
            int lo = (int)Math.Floor(position);
            int hi = lo + 1;
            float t = position - lo;
            return ((1.0f - t) * StickGyroHcCurve[lo] + t * StickGyroHcCurve[hi]) * 2.0f;
        }

        private void ApplyDs4Orientation(
            ref float gyroX,
            ref float gyroY,
            ref float gyroZ,
            ref float accelX,
            ref float accelY,
            ref float accelZ)
        {
            if (ds4Orientation != 1)
            {
                return;
            }

            // Orthogonal mode rotates around X so DS4 motion orientation matches
            // users holding the handheld in a perpendicular posture.
            // Swaps Y↔Z (yaw↔roll) with sign flip.
            float originalGyroY = gyroY;
            float originalGyroZ = gyroZ;
            gyroY = originalGyroZ;
            gyroZ = -originalGyroY;

            float originalAccelY = accelY;
            float originalAccelZ = accelZ;
            accelY = originalAccelZ;
            accelZ = -originalAccelY;
        }

        private static short ConvertNormalizedToInt16(float normalized)
        {
            float clamped = Math.Max(-1.0f, Math.Min(1.0f, normalized));
            return (short)Math.Round(clamped * short.MaxValue);
        }

        private static void MergeStickVectors(
            short physicalX,
            short physicalY,
            short gyroX,
            short gyroY,
            out short mergedX,
            out short mergedY)
        {
            float sumX = physicalX + gyroX;
            float sumY = physicalY + gyroY;
            float magnitude = (float)Math.Sqrt((sumX * sumX) + (sumY * sumY));
            if (magnitude > short.MaxValue && magnitude > 0.0f)
            {
                float scale = short.MaxValue / magnitude;
                sumX *= scale;
                sumY *= scale;
            }

            mergedX = ClampToInt16(sumX);
            mergedY = ClampToInt16(sumY);
        }

        private static short ClampToInt16(float value)
        {
            if (value > short.MaxValue)
            {
                return short.MaxValue;
            }

            if (value < short.MinValue)
            {
                return short.MinValue;
            }

            return (short)Math.Round(value);
        }

    }
}
