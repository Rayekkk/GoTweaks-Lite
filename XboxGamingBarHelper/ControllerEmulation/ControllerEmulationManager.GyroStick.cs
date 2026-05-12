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

        private void ApplyStickFromGyro(
            GyroSample sample,
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

            // No EMA smoother — JSL auto-calibration (Stillness | SensorFusion)
            // handles bias drift, so no pre-output smoothing is needed.

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

            // 4. Sensitivity curve + clamp. Direct mirror of the
            //    Viiper-side processor; both feed into the same default 0.5-flat
            //    lookup table with a 124°/s threshold and a 1000× per-axis scale.
            float curveX = HcApplyCustomSensitivity(horizontal);
            float curveY = HcApplyCustomSensitivity(vertical);
            float perAxisSens = Math.Max(0.01f, stickSensitivityV2 / 100.0f) * StickGyroHcSensScale;
            float scaledX = horizontal * curveX * perAxisSens;
            float scaledY = vertical   * curveY * perAxisSens;

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
