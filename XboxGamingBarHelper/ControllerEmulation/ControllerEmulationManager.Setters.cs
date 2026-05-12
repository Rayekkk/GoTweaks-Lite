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

        public void SetGyroSource(int source)
        {
            int normalized = NormalizeGyroSource(source);
            if (gyroSource == normalized)
            {
                return;
            }

            gyroSource = normalized;
            SaveSettings();
            ApplyCurrentConfiguration("gyro source changed");
        }

        public void SetMode(int inMode)
        {
            int normalized = NormalizeMode(inMode);
            if (mode == normalized)
            {
                return;
            }

            mode = normalized;
            SaveSettings();
            ApplyCurrentConfiguration("mode changed");
        }

        public void SetRumbleProfile(int value)
        {
            int normalized = NormalizeRumbleProfile(value);
            if (rumbleProfile == normalized)
            {
                return;
            }

            rumbleProfile = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation rumble profile set to {rumbleProfile}");
        }

        public void SetGyroActivationMode(int value)
        {
            int normalized = NormalizeGyroActivationMode(value);
            if (gyroActivationMode == normalized)
            {
                return;
            }

            gyroActivationMode = normalized;
            ResetGyroActivationRuntimeState();
            SaveSettings();
            Logger.Info($"Controller emulation gyro activation mode set to {gyroActivationMode}");
        }

        public void SetGyroActivationButton(int value)
        {
            int normalized = NormalizeGyroActivationButton(value);
            if (gyroActivationButton == normalized)
            {
                return;
            }

            gyroActivationButton = normalized;
            ResetGyroActivationRuntimeState();
            SaveSettings();
            Logger.Info($"Controller emulation gyro activation button set to {gyroActivationButton}");
        }

        public void SetDs4Orientation(int value)
        {
            int normalized = NormalizeDs4Orientation(value);
            if (ds4Orientation == normalized)
            {
                return;
            }

            ds4Orientation = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation DS4 motion orientation set to {ds4Orientation}");
        }

        public void SetPs4TouchpadEnabled(bool value)
        {
            if (ps4TouchpadEnabled == value)
            {
                return;
            }

            ps4TouchpadEnabled = value;
            SaveSettings();
            Logger.Info($"Controller emulation PS4 touchpad forwarding set to {ps4TouchpadEnabled}");
        }

        public void SetLedForwardingEnabled(bool value)
        {
            if (ledForwardingEnabled == value)
            {
                return;
            }

            ledForwardingEnabled = value;
            SaveSettings();
            Logger.Info($"Controller emulation LED forwarding set to {ledForwardingEnabled}");

            if (!ledForwardingEnabled && hasForwardedLed)
            {
                RevertLegionLed();
            }
        }

        public void CalibrateGyro()
        {
            if (deviceType != SharedDeviceType.LegionGo && deviceType != SharedDeviceType.LegionGo2)
            {
                Logger.Warn("Calibrate gyro: not supported on this device");
                return;
            }

            // CE Calibrate Gyro = JSL software-only. The firmware-level Legion
            // HID gyro calibration (which takes 5–10s per controller) is exposed
            // separately in the Legion tab — chaining it here just made the CE
            // button block for ~15s for no extra benefit (firmware bias was
            // already known fresh). Calibration is JSL-only.
            //
            // Progress is pushed back to the widget via
            // ControllerEmulationCalibrateGyroStatus (a JSON string property)
            // so the UI can render a live countdown + the captured offset.
            System.Threading.Tasks.Task.Run(() =>
            {
                const int GracePeriodMs = 3000;
                const int CalibrationDurationMs = 5000;
                try
                {
                    // Grace period: give the user time to set the device down
                    // / let go of the controller after clicking. Publish a
                    // countdown so the UI shows when actual capture begins.
                    Logger.Info($"JSL gyro calibration: place device flat — capture starts in {GracePeriodMs / 1000}s...");
                    var graceSw = System.Diagnostics.Stopwatch.StartNew();
                    while (graceSw.ElapsedMilliseconds < GracePeriodMs)
                    {
                        int secondsLeft = (int)Math.Ceiling((GracePeriodMs - graceSw.ElapsedMilliseconds) / 1000.0);
                        PublishCalibrationStatus("preparing", Math.Max(0, secondsLeft), 0, 0, 0, 0);
                        System.Threading.Thread.Sleep(200);
                    }

                    Logger.Info("JSL gyro calibration: capturing now, hold still...");
                    bool jslOk = RunActiveJslCalibrationWithProgress(
                        CalibrationDurationMs,
                        out float xOffset, out float yOffset, out float zOffset, out int weight);

                    PersistJslCalibrationOffset(xOffset, yOffset, zOffset, weight, jslOk);
                    PublishCalibrationStatus(jslOk ? "done" : "low_confidence", 0, xOffset, yOffset, zOffset, weight);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"JSL gyro calibration failed: {ex.Message}");
                    PublishCalibrationStatus("error", 0, 0, 0, 0, 0);
                }
            });
        }

        /// <summary>
        /// Calibration with per-second progress callbacks. Wraps the existing
        /// RunActiveJslCalibration logic but emits a status update every
        /// ~500ms so the widget UI can render a countdown.
        /// </summary>
        private bool RunActiveJslCalibrationWithProgress(
            int totalMs,
            out float xOffset, out float yOffset, out float zOffset, out int weight)
        {
            xOffset = 0; yOffset = 0; zOffset = 0; weight = 0;

            // Pick the active pipeline. Same as RunActiveJslCalibration —
            // prefer the Viiper processor when present (it has the live
            // sample stream during normal use), fall back to legacy CE.
            var viiperProc = Viiper.ViiperInputForwarder.ActiveInstance?.StickGyroProcessor;
            int legacyPumpSampleCount = 0;
            try
            {
                if (viiperProc != null)
                {
                    viiperProc.BeginJslCalibration();
                }
                else
                {
                    // Legacy CE fallback: same manual-continuous-calibration
                    // approach (just average every sample we feed during the
                    // window). Stillness auto-cal mode never converges fast
                    // enough on the BMI260 within 5s.
                    stickGamepadMotion.ResetContinuousCalibration();
                    stickGamepadMotion.StartContinuousCalibration();
                }

                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < totalMs)
                {
                    // Feed JSL fresh samples from the active gyro source.
                    // The normal apply path is gated on the user's activation
                    // mode (Always-On / Hold / Toggle button) and stick-gyro
                    // enable flag — calibration must work regardless of those.
                    // While IsCalibrating is true (set by BeginJslCalibration),
                    // every sample fed via ProcessMotion gets pushed straight
                    // into JSL's GyroCalibration buffer (PushSensorSamples);
                    // no stillness detection / threshold checks. Run the full
                    // window so we accumulate enough samples to average.
                    if (viiperProc != null)
                    {
                        viiperProc.PumpJslSampleForCalibration();
                    }
                    else
                    {
                        // Legacy CE has its own JSL handle but no pump path.
                        // Pull samples through the normal stick-gyro tick by
                        // calling the helper directly (no gate). Counts how
                        // many were actually fed.
                        if (PumpLegacyStickGyroCalibration())
                        {
                            legacyPumpSampleCount++;
                        }
                    }

                    int secondsLeft = (int)Math.Ceiling((totalMs - sw.ElapsedMilliseconds) / 1000.0);
                    PublishCalibrationStatus("running", Math.Max(0, secondsLeft), 0, 0, 0, 0);
                    System.Threading.Thread.Sleep(50);
                }

                if (viiperProc != null)
                {
                    viiperProc.EndJslCalibration(out xOffset, out yOffset, out zOffset, out weight);
                    stickGamepadMotion.SetCalibrationOffset(xOffset, yOffset, zOffset, weight);
                }
                else
                {
                    stickGamepadMotion.PauseContinuousCalibration();
                    stickGamepadMotion.GetCalibrationOffset(out xOffset, out yOffset, out zOffset);
                    weight = legacyPumpSampleCount >= 50 ? 10 : (legacyPumpSampleCount / 5);
                    if (weight < 0) weight = 0; if (weight > 10) weight = 10;
                    stickGamepadMotion.SetCalibrationOffset(xOffset, yOffset, zOffset, weight);
                }

                Logger.Info($"JSL calibration done: offset=({xOffset:F3}, {yOffset:F3}, {zOffset:F3}) weight={weight} elapsed={sw.ElapsedMilliseconds}ms legacyPump={legacyPumpSampleCount}");
                return weight > 0;
            }
            catch (Exception ex)
            {
                Logger.Warn($"RunActiveJslCalibrationWithProgress failed: {ex.Message}");
                return false;
            }
        }

        private long legacyPumpLastTicksUtc;
        /// <summary>
        /// Pump one IMU sample directly into the legacy stickGamepadMotion
        /// JSL handle, bypassing the activation gate. Mirrors the Viiper
        /// processor's PumpJslSampleForCalibration but for the legacy CE path.
        /// </summary>
        private bool PumpLegacyStickGyroCalibration()
        {
            try
            {
                // Use whichever Legion sample is freshest. Prefers the gyro
                // source the user has selected by routing through the same
                // adapter the legacy pipeline uses.
                if (Program.legionButtonMonitor == null) return false;
                bool hasLeft = Labs.LegionButtonMonitor.TryGetLatestGyroSample(true, out XboxGamingBarHelper.Labs.LegionGyroSample left);
                bool hasRight = Labs.LegionButtonMonitor.TryGetLatestGyroSample(false, out XboxGamingBarHelper.Labs.LegionGyroSample right);
                if (!hasLeft && !hasRight) return false;

                // Single-side passthrough — same simple averaging the Mixed
                // adapter does, but skipping the abstraction since this is
                // a one-off calibration sample. Prefer left if both present.
                var s = hasLeft ? left : right;
                long sampleTicks = s.TimestampTicksUtc;
                float dt = legacyPumpLastTicksUtc > 0 && sampleTicks > legacyPumpLastTicksUtc
                    ? (float)((sampleTicks - legacyPumpLastTicksUtc) / (double)TimeSpan.TicksPerSecond)
                    : 1.0f / 250.0f;
                legacyPumpLastTicksUtc = sampleTicks;
                stickGamepadMotion.Update(s.GyroXDegPerSecond, s.GyroYDegPerSecond, s.GyroZDegPerSecond,
                    s.AccelXG, s.AccelYG, s.AccelZG, dt);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn($"PumpLegacyStickGyroCalibration failed: {ex.Message}");
                return false;
            }
        }

        private void PublishCalibrationStatus(string phase, int secondsLeft, float xOffset, float yOffset, float zOffset, int weight)
        {
            try
            {
                // Compact JSON: phase + countdown + offset triplet + confidence weight.
                // Widget parses and displays. Plain string to keep the pipe simple.
                string json = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{{\"phase\":\"{0}\",\"secondsLeft\":{1},\"offset\":[{2:F3},{3:F3},{4:F3}],\"weight\":{5}}}",
                    phase, secondsLeft, xOffset, yOffset, zOffset, weight);
                ControllerEmulationCalibrateGyroStatus?.SetValue(json);
            }
            catch (Exception ex)
            {
                Logger.Warn($"PublishCalibrationStatus failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Run JSL calibration on whichever pipelines are alive. Always
        /// calibrates the legacy <c>stickGamepadMotion</c> (it lives on the
        /// manager). If the Viiper backend is the active one, also calibrates
        /// its processor — both pipelines should agree on the bias because
        /// they consume the same physical IMU. The Viiper-side capture wins
        /// when present (more samples flowing through it during normal use);
        /// otherwise the legacy one is used.
        /// </summary>
        private bool RunActiveJslCalibration(
            out float xOffset, out float yOffset, out float zOffset, out int weight)
        {
            xOffset = 0; yOffset = 0; zOffset = 0; weight = 0;

            var viiperProc = Viiper.ViiperInputForwarder.ActiveInstance?.StickGyroProcessor;
            bool viiperOk = false;
            if (viiperProc != null)
            {
                viiperOk = viiperProc.RunJslCalibration(5000,
                    out float vx, out float vy, out float vz, out int vw);
                if (viiperOk)
                {
                    xOffset = vx; yOffset = vy; zOffset = vz; weight = vw;
                    // Mirror to legacy so both backends are in sync after a calibration.
                    stickGamepadMotion.SetCalibrationOffset(vx, vy, vz, vw);
                    return true;
                }
            }

            // Fallback: calibrate the legacy CE-side JSL directly. Same flow.
            try
            {
                stickGamepadMotion.ResetContinuousCalibration();
                stickGamepadMotion.SetCalibrationMode(GamepadMotion.JslCalibrationStillnessFusion);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                float confidence = 0.0f;
                while (sw.ElapsedMilliseconds < 5000)
                {
                    confidence = stickGamepadMotion.GetAutoCalibrationConfidence();
                    if (confidence >= 1.0f) break;
                    System.Threading.Thread.Sleep(50);
                }
                stickGamepadMotion.GetCalibrationOffset(out xOffset, out yOffset, out zOffset);
                weight = (int)Math.Round(confidence * 10.0f);
                stickGamepadMotion.SetCalibrationOffset(xOffset, yOffset, zOffset, weight);
                stickGamepadMotion.SetCalibrationMode(GamepadMotion.JslCalibrationManual);
                if (viiperProc != null)
                {
                    viiperProc.ApplyJslCalibrationOffset(xOffset, yOffset, zOffset, weight);
                }
                Logger.Info($"Legacy CE JSL calibration: confidence={confidence:F2} offset=({xOffset:F3}, {yOffset:F3}, {zOffset:F3}) weight={weight} elapsed={sw.ElapsedMilliseconds}ms");
                return confidence > 0.0f;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Legacy CE JSL calibration failed: {ex.Message}");
                return false;
            }
        }

        // Persisted to LocalSettings under these keys; loaded by
        // LoadJslCalibrationOffset() on helper startup.
        private const string JslCalibKeyX = "ControllerEmulationGyroBiasX";
        private const string JslCalibKeyY = "ControllerEmulationGyroBiasY";
        private const string JslCalibKeyZ = "ControllerEmulationGyroBiasZ";
        private const string JslCalibKeyW = "ControllerEmulationGyroBiasWeight";

        private void PersistJslCalibrationOffset(float xOffset, float yOffset, float zOffset, int weight, bool ok)
        {
            try
            {
                LocalSettingsHelper.SetValue(JslCalibKeyX, xOffset);
                LocalSettingsHelper.SetValue(JslCalibKeyY, yOffset);
                LocalSettingsHelper.SetValue(JslCalibKeyZ, zOffset);
                LocalSettingsHelper.SetValue(JslCalibKeyW, weight);
                Logger.Info($"JSL gyro calibration {(ok ? "complete" : "captured low-confidence result")}: " +
                            $"offset=({xOffset:F3}, {yOffset:F3}, {zOffset:F3}) weight={weight} — persisted.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"PersistJslCalibrationOffset failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Load the persisted JSL gyro bias offset (if any) and apply to both
        /// pipelines. Called from EnsureSettingsLoaded after the JSL handles exist.
        /// </summary>
        public void LoadJslCalibrationOffset()
        {
            try
            {
                bool hasX = LocalSettingsHelper.TryGetValue(JslCalibKeyX, out float xOffset);
                bool hasY = LocalSettingsHelper.TryGetValue(JslCalibKeyY, out float yOffset);
                bool hasZ = LocalSettingsHelper.TryGetValue(JslCalibKeyZ, out float zOffset);
                LocalSettingsHelper.TryGetValue(JslCalibKeyW, out int weight);
                if (!hasX || !hasY || !hasZ)
                {
                    Logger.Info("JSL gyro calibration: no saved offset — JSL runs uncorrected until user calibrates.");
                    return;
                }

                stickGamepadMotion.SetCalibrationOffset(xOffset, yOffset, zOffset, weight);
                stickGamepadMotion.SetCalibrationMode(GamepadMotion.JslCalibrationManual);
                Viiper.ViiperInputForwarder.ActiveInstance?.StickGyroProcessor?.ApplyJslCalibrationOffset(xOffset, yOffset, zOffset, weight);
                Logger.Info($"JSL gyro calibration loaded: offset=({xOffset:F3}, {yOffset:F3}, {zOffset:F3}) weight={weight}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"LoadJslCalibrationOffset failed: {ex.Message}");
            }
        }

        public void SetEnabled(bool value)
        {
            if (enabled == value)
            {
                return;
            }

            enabled = value;
            SaveSettings();
            ApplyCurrentConfiguration(enabled ? "enabled changed: on" : "enabled changed: off");
            RaiseEmulationEnabledChanged();
        }

        /// <summary>
        /// Runtime mutual-exclusion with the VIIPER backend. When set to true, this manager
        /// stops forwarding immediately; when cleared, it re-applies its persisted configuration.
        /// The user's saved <see cref="SetEnabled"/> value is untouched either way.
        /// </summary>
        public void SetSuppressedByViiper(bool value)
        {
            if (suppressedByViiper == value) return;
            suppressedByViiper = value;
            ApplyCurrentConfiguration(value ? "suppressed by VIIPER" : "VIIPER suppression cleared");
        }

        public void SetHideStockController(bool value)
        {
            if (hideStockController == value)
            {
                return;
            }

            hideStockController = value;
            SaveSettings();
            ApplySuppressionConfiguration(hideStockController ? "hide stock controller changed: on" : "hide stock controller changed: off");
        }

        public void SetHideTarget(int value)
        {
            int normalized = NormalizeHideTarget(value);
            if (hideTarget == normalized)
            {
                return;
            }

            hideTarget = normalized;
            SaveSettings();
            ApplySuppressionConfiguration($"hide target changed: {hideTarget}");
        }

        public void SetImprovedInputRead(bool value)
        {
            if (improvedInputRead == value)
            {
                return;
            }

            improvedInputRead = value;
            SaveSettings();
            ApplyCurrentConfiguration(improvedInputRead ? "improved input changed: on" : "improved input changed: off");
        }

        public void SetMouseSensitivity(int value)
        {
            int normalized = NormalizeMouseSensitivity(value);
            if (mouseSensitivity == normalized)
            {
                return;
            }

            mouseSensitivity = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation mouse sensitivity set to {mouseSensitivity}");
        }

        public void SetMouseThreshold(int value)
        {
            int normalized = NormalizeMouseThreshold(value);
            if (mouseThreshold == normalized)
            {
                return;
            }

            mouseThreshold = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation mouse threshold set to {mouseThreshold}");
        }

        public void SetMouseAxis(int value)
        {
            int normalized = NormalizeMouseAxis(value);
            if (mouseAxis == normalized)
            {
                return;
            }

            mouseAxis = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation mouse axis set to {mouseAxis}");
        }

        public void SetMouseInvertX(bool value)
        {
            if (mouseInvertX == value)
            {
                return;
            }

            mouseInvertX = value;
            SaveSettings();
            Logger.Info($"Controller emulation mouse invert X set to {mouseInvertX}");
        }

        public void SetMouseInvertY(bool value)
        {
            if (mouseInvertY == value)
            {
                return;
            }

            mouseInvertY = value;
            SaveSettings();
            Logger.Info($"Controller emulation mouse invert Y set to {mouseInvertY}");
        }

        public void SetMouseGainX(int value)
        {
            int normalized = NormalizeMouseGain(value);
            if (mouseGainX == normalized)
            {
                return;
            }

            mouseGainX = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation mouse gain X set to {mouseGainX}");
        }

        public void SetMouseGainY(int value)
        {
            int normalized = NormalizeMouseGain(value);
            if (mouseGainY == normalized)
            {
                return;
            }

            mouseGainY = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation mouse gain Y set to {mouseGainY}");
        }

        public void SetStickSensitivity(int value)
        {
            int normalized = NormalizeStickSensitivity(value);
            if (stickSensitivity == normalized)
            {
                return;
            }

            stickSensitivity = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick sensitivity set to {stickSensitivity}");
        }

        public void SetStickThreshold(int value)
        {
            int normalized = NormalizeStickThreshold(value);
            if (stickThreshold == normalized)
            {
                return;
            }

            stickThreshold = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick threshold set to {stickThreshold}");
        }

        public void SetStickAxis(int value)
        {
            int normalized = NormalizeStickAxis(value);
            if (stickAxis == normalized)
            {
                return;
            }

            stickAxis = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick axis set to {stickAxis}");
        }

        public void SetStickInvertX(bool value)
        {
            if (stickInvertX == value)
            {
                return;
            }

            stickInvertX = value;
            SaveSettings();
            Logger.Info($"Controller emulation stick invert X set to {stickInvertX}");
        }

        public void SetStickInvertY(bool value)
        {
            if (stickInvertY == value)
            {
                return;
            }

            stickInvertY = value;
            SaveSettings();
            Logger.Info($"Controller emulation stick invert Y set to {stickInvertY}");
        }

        public void SetStickGainX(int value)
        {
            int normalized = NormalizeStickGain(value);
            if (stickGainX == normalized)
            {
                return;
            }

            stickGainX = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick gain X set to {stickGainX}");
        }

        public void SetStickGainY(int value)
        {
            int normalized = NormalizeStickGain(value);
            if (stickGainY == normalized)
            {
                return;
            }

            stickGainY = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick gain Y set to {stickGainY}");
        }

        public void SetStickSelect(int value)
        {
            int normalized = NormalizeStickSelect(value);
            if (stickSelect == normalized)
            {
                return;
            }

            stickSelect = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick select set to {stickSelect}");
        }

        public void SetStickExcessMove(bool value)
        {
            if (stickExcessMove == value)
            {
                return;
            }

            stickExcessMove = value;
            SaveSettings();
            Logger.Info($"Controller emulation stick excess move set to {stickExcessMove}");
        }

        public void SetStickRange(int value)
        {
            int normalized = NormalizeStickRange(value);
            if (stickRange == normalized)
            {
                return;
            }

            stickRange = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick range set to {stickRange}");
        }

        public void SetStickOnlyJoystickData(bool value)
        {
            if (stickOnlyJoystickData == value)
            {
                return;
            }

            stickOnlyJoystickData = value;
            SaveSettings();
            Logger.Info($"Controller emulation stick only joystick data set to {stickOnlyJoystickData}");
        }

        public void SetStickMinGyroSpeed(int value)
        {
            int normalized = Math.Max(0, Math.Min(100, value));
            if (stickMinGyroSpeed == normalized) return;
            stickMinGyroSpeed = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick min gyro speed set to {stickMinGyroSpeed}");
        }

        public void SetStickMaxGyroSpeed(int value)
        {
            int normalized = Math.Max(50, Math.Min(720, value));
            if (stickMaxGyroSpeed == normalized) return;
            stickMaxGyroSpeed = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick max gyro speed set to {stickMaxGyroSpeed}");
        }

        public void SetStickMinOutput(int value)
        {
            int normalized = Math.Max(0, Math.Min(100, value));
            if (stickMinOutput == normalized) return;
            stickMinOutput = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick min output set to {stickMinOutput}");
        }

        public void SetStickMaxOutput(int value)
        {
            int normalized = Math.Max(1, Math.Min(100, value));
            if (stickMaxOutput == normalized) return;
            stickMaxOutput = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick max output set to {stickMaxOutput}");
        }

        public void SetStickPowerCurve(int value)
        {
            int normalized = Math.Max(10, Math.Min(400, value));
            if (stickPowerCurve == normalized) return;
            stickPowerCurve = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick power curve set to {stickPowerCurve}");
        }

        public void SetStickSensitivityV2(int value)
        {
            int normalized = Math.Max(1, Math.Min(400, value));
            if (stickSensitivityV2 == normalized) return;
            stickSensitivityV2 = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick sensitivity v2 set to {stickSensitivityV2}");
        }

        public void SetStickDeadzone(int value)
        {
            int normalized = Math.Max(0, Math.Min(50, value));
            if (stickDeadzone == normalized) return;
            stickDeadzone = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick deadzone set to {stickDeadzone}");
        }

        public void SetStickPrecisionSpeed(int value)
        {
            int normalized = Math.Max(0, Math.Min(100, value));
            if (stickPrecisionSpeed == normalized) return;
            stickPrecisionSpeed = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick precision speed set to {stickPrecisionSpeed}");
        }

        public void SetStickOutputMix(int value)
        {
            int normalized = Math.Max(-100, Math.Min(100, value));
            if (stickOutputMix == normalized) return;
            stickOutputMix = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick output mix set to {stickOutputMix}");
        }

        public void SetStickOrientationV2(int value)
        {
            int normalized = (value == 1) ? 1 : 0;
            if (stickOrientationV2 == normalized) return;
            stickOrientationV2 = normalized;
            stickFilterInitialized = false;
            SaveSettings();
            Logger.Info($"Controller emulation stick orientation v2 set to {stickOrientationV2}");
        }

        public void SetStickConversion(int value)
        {
            // 0=Yaw, 1=Roll, 2=Yaw+Roll, 3=Player Space, 4=World Space (both
            // JSL-fused; World Space re-derives the pitch axis from gravity so
            // tilted handhelds feel correct).
            int normalized = Math.Max(0, Math.Min(4, value));
            if (stickConversion == normalized) return;
            stickConversion = normalized;
            stickFilterInitialized = false;
            SaveSettings();
            Logger.Info($"Controller emulation stick conversion set to {stickConversion}");
        }

        public void SetStickGyroAntiDeadzone(int value)
        {
            int normalized = Math.Max(0, Math.Min(30, value));
            if (stickGyroAntiDeadzone == normalized) return;
            stickGyroAntiDeadzone = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick-gyro anti-deadzone set to {stickGyroAntiDeadzone}% of stick range");
        }

        public void SetStickGyroAntiDeadzoneThreshold(int value)
        {
            int normalized = Math.Max(0, Math.Min(50, value));
            if (stickGyroAntiDeadzoneThreshold == normalized) return;
            stickGyroAntiDeadzoneThreshold = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation stick-gyro anti-deadzone threshold set to {stickGyroAntiDeadzoneThreshold / 10.0f:F1}°/s");
        }

        public void SetVirtualABXYLayout(int value)
        {
            int normalized = NormalizeVirtualAbxyLayout(value);
            if (virtualAbxyLayout == normalized)
            {
                return;
            }

            virtualAbxyLayout = normalized;
            SaveSettings();
            Logger.Info($"Controller emulation virtual ABXY layout set to {virtualAbxyLayout}");
        }

    }
}
