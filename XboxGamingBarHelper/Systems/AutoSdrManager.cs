using NLog;
using System;
using System.Management;
using XboxGamingBarHelper.Windows;

namespace XboxGamingBarHelper.Systems
{
    /// <summary>
    /// Auto SDR white-level matching — the integrated core of the standalone Go2HDR app.
    ///
    /// When enabled AND HDR is active on the built-in panel, this continuously matches the
    /// SDR white level (paper-white nits) to the current screen brightness, so SDR content
    /// (desktop, most games/apps) does not look washed out or over-bright at any brightness
    /// while HDR is on. Windows leaves the SDR white level fixed while brightness varies
    /// 0–100 %; this closes that gap.
    ///
    /// Mechanics mirror Go2HDR: a WMI WmiMonitorBrightness event watcher fires on every
    /// brightness change (no polling), a fixed brightness→SDR curve (tuned for the Legion
    /// Go 2 panel) maps brightness % → an SDR 0–100 value, and nits = 80 + sdr*4 is written
    /// through the already-present User32.SetSdrWhiteLevelNits. HDR on/off transitions are
    /// driven externally by SystemManager (via SystemEvents.DisplaySettingsChanged / the
    /// HDREnabled property), so this class only owns the brightness side + the apply.
    /// </summary>
    internal sealed class AutoSdrManager : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // Below this brightness the SDR value is 0 (80 nits — the floor). Matches Go2HDR's
        // default minimum for the Legion Go 2 panel.
        private const int MinimumBrightness = 47;

        private readonly object _gate = new object();
        private ManagementEventWatcher _brightnessWatcher;
        private bool _enabled;
        private bool _hdrActive;
        private bool _disposed;

        // Default brightness(%) → SDR(0–100) curve, tuned for the Legion Go 2 OLED in HDR.
        // Piecewise-linear; below MinimumBrightness → 0. Lifted verbatim from Go2HDR's
        // AppSettings.DefaultCurve so behaviour matches the standalone app exactly.
        private static readonly (int Brightness, int Sdr)[] Curve =
        {
            (47, 0),  (48, 1),  (49, 2),  (50, 3),  (51, 4),
            (52, 5),  (53, 6),  (54, 7),  (55, 8),  (56, 9),
            (57, 10), (58, 12), (59, 13), (60, 14), (61, 15),
            (62, 17), (63, 18), (64, 20), (65, 21), (66, 23),
            (67, 25), (68, 27), (69, 28), (70, 30), (71, 32),
            (72, 34), (73, 35), (74, 37), (75, 39), (76, 41),
            (77, 43), (78, 45), (79, 47), (80, 49), (81, 51),
            (82, 53), (83, 55), (84, 58), (85, 60), (86, 62),
            (87, 65), (88, 67), (89, 69), (90, 72), (91, 75),
            (92, 77), (93, 80), (94, 83), (95, 85), (96, 88),
            (97, 91), (98, 94), (99, 97), (100, 100)
        };

        /// <summary>Map a brightness percentage to the target SDR white level in nits.</summary>
        public static int BrightnessToNits(int brightness)
        {
            int sdr = BrightnessToSdr(brightness);
            return 80 + sdr * 4;
        }

        private static int BrightnessToSdr(int brightness)
        {
            if (brightness < MinimumBrightness) return 0;
            if (brightness <= Curve[0].Brightness) return Curve[0].Sdr;
            if (brightness >= Curve[Curve.Length - 1].Brightness) return Curve[Curve.Length - 1].Sdr;

            for (int i = 1; i < Curve.Length; i++)
            {
                if (brightness <= Curve[i].Brightness)
                {
                    var p0 = Curve[i - 1];
                    var p1 = Curve[i];
                    double t = (double)(brightness - p0.Brightness) / (p1.Brightness - p0.Brightness);
                    return (int)Math.Round(p0.Sdr + t * (p1.Sdr - p0.Sdr));
                }
            }
            return Curve[Curve.Length - 1].Sdr;
        }

        /// <summary>
        /// Enable/disable the feature. When turning on while HDR is already active, applies
        /// immediately and starts watching brightness; when turning off, stops watching.
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            lock (_gate)
            {
                if (_enabled == enabled) return;
                _enabled = enabled;
                Logger.Info($"AutoSDR: {(enabled ? "enabled" : "disabled")} (HDR active={_hdrActive}).");
                ReconcileLocked();
            }
        }

        /// <summary>
        /// Notify of an HDR on/off transition. Called by SystemManager whenever the helper's
        /// HDR-enabled state changes (display-settings change or a widget HDR toggle).
        /// </summary>
        public void OnHdrStateChanged(bool hdrActive)
        {
            lock (_gate)
            {
                if (_hdrActive == hdrActive) return;
                _hdrActive = hdrActive;
                Logger.Info($"AutoSDR: HDR {(hdrActive ? "active" : "inactive")} (enabled={_enabled}).");
                ReconcileLocked();
            }
        }

        // Decide watcher state + do an immediate apply. Caller holds _gate.
        private void ReconcileLocked()
        {
            if (_disposed) return;
            if (_enabled && _hdrActive)
            {
                StartWatchingLocked();
                ApplyLocked(GetCurrentBrightness());
            }
            else
            {
                StopWatchingLocked();
            }
        }

        private void ApplyLocked(int brightness)
        {
            if (!_enabled || !_hdrActive) return;
            int nits = BrightnessToNits(brightness);
            // SetSdrWhiteLevelNits already filters to the internal HDR-active panel and is a
            // no-op when no such target exists, so a brief HDR-off race is harmless.
            User32.SetSdrWhiteLevelNits(nits);
        }

        // ── Brightness watching (WmiMonitorBrightness) ───────────────────────────────

        private void StartWatchingLocked()
        {
            if (_brightnessWatcher != null) return;
            try
            {
                var watcher = new ManagementEventWatcher(
                    new ManagementScope(@"root\wmi"),
                    new WqlEventQuery(
                        "SELECT * FROM __InstanceModificationEvent WITHIN 1 " +
                        "WHERE TargetInstance ISA 'WmiMonitorBrightness'"));
                watcher.EventArrived += OnBrightnessEvent;
                watcher.Start();
                _brightnessWatcher = watcher;
                Logger.Info("AutoSDR: brightness watcher started.");
            }
            catch (Exception ex)
            {
                _brightnessWatcher = null;
                Logger.Warn($"AutoSDR: failed to start brightness watcher: {ex.Message}");
            }
        }

        private void StopWatchingLocked()
        {
            var watcher = _brightnessWatcher;
            _brightnessWatcher = null;
            if (watcher == null) return;
            try
            {
                watcher.EventArrived -= OnBrightnessEvent;
                watcher.Stop();
                watcher.Dispose();
                Logger.Info("AutoSDR: brightness watcher stopped.");
            }
            catch (Exception ex)
            {
                Logger.Debug($"AutoSDR: error stopping brightness watcher: {ex.Message}");
            }
        }

        private void OnBrightnessEvent(object sender, EventArrivedEventArgs e)
        {
            try
            {
                var target = (ManagementBaseObject)e.NewEvent["TargetInstance"];
                int brightness = Convert.ToInt32(target["CurrentBrightness"]);
                lock (_gate) { ApplyLocked(brightness); }
            }
            catch (Exception ex)
            {
                Logger.Debug($"AutoSDR: brightness event error: {ex.Message}");
            }
        }

        private static int GetCurrentBrightness()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightness"))
                using (var results = searcher.Get())
                {
                    foreach (ManagementBaseObject obj in results)
                    {
                        using (obj) return Convert.ToInt32(obj["CurrentBrightness"]);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"AutoSDR: GetCurrentBrightness failed: {ex.Message}");
            }
            return 50; // safe mid-range fallback
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                StopWatchingLocked();
            }
        }
    }
}
