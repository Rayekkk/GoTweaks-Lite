using NLog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
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
    /// brightness change (no polling), a brightness→SDR curve maps brightness % → an SDR
    /// 0–100 value, and nits = 80 + sdr*4 is written through the already-present
    /// User32.SetSdrWhiteLevelNits. HDR on/off transitions are driven externally by
    /// SystemManager (via SystemEvents.DisplaySettingsChanged / the HDREnabled property), so
    /// this class only owns the brightness side + the apply.
    ///
    /// Two curve presets, matching Go2HDR's own preset model: LegionGo2 (fixed, the default
    /// curve below) and Custom (editable, seeded from the same default the first time it's
    /// selected). SystemManager owns persistence of the preset choice + custom curve; this
    /// class only holds the active runtime state and applies it.
    /// </summary>
    internal sealed class AutoSdrManager : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        internal enum CurvePreset { LegionGo2 = 0, Custom = 1 }

        // Below this brightness the SDR value is 0 (80 nits — the floor). Matches Go2HDR's
        // default minimum for the Legion Go 2 panel.
        private const int MinimumBrightness = 47;

        private readonly object _gate = new object();
        private ManagementEventWatcher _brightnessWatcher;
        private bool _enabled;
        private bool _hdrActive;
        private bool _disposed;
        private CurvePreset _preset = CurvePreset.LegionGo2;
        private (int Brightness, int Sdr)[] _customCurve = CloneDefaultCurve();

        // Default brightness(%) → SDR(0–100) curve, tuned for the Legion Go 2 OLED in HDR.
        // Piecewise-linear; below MinimumBrightness → 0. Lifted verbatim from Go2HDR's
        // AppSettings.DefaultCurve so behaviour matches the standalone app exactly.
        private static readonly (int Brightness, int Sdr)[] DefaultCurve =
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

        private static (int Brightness, int Sdr)[] CloneDefaultCurve() => DefaultCurve.ToArray();

        /// <summary>Map a brightness percentage to the target SDR white level in nits.</summary>
        public int BrightnessToNits(int brightness)
        {
            int sdr = BrightnessToSdr(brightness);
            return 80 + sdr * 4;
        }

        private (int Brightness, int Sdr)[] ActiveCurve => _preset == CurvePreset.Custom && _customCurve.Length >= 2
            ? _customCurve
            : DefaultCurve;

        private int BrightnessToSdr(int brightness)
        {
            var curve = ActiveCurve;
            if (brightness < MinimumBrightness) return 0;
            if (brightness <= curve[0].Brightness) return curve[0].Sdr;
            if (brightness >= curve[curve.Length - 1].Brightness) return curve[curve.Length - 1].Sdr;

            for (int i = 1; i < curve.Length; i++)
            {
                if (brightness <= curve[i].Brightness)
                {
                    var p0 = curve[i - 1];
                    var p1 = curve[i];
                    double t = (double)(brightness - p0.Brightness) / (p1.Brightness - p0.Brightness);
                    return (int)Math.Round(p0.Sdr + t * (p1.Sdr - p0.Sdr));
                }
            }
            return curve[curve.Length - 1].Sdr;
        }

        // ── Preset + custom curve management ──────────────────────────────────────

        /// <summary>Current preset as its raw int value (0=LegionGo2, 1=Custom).</summary>
        public int GetPreset() => (int)_preset;

        /// <summary>
        /// Switches the active preset. Does not touch the stored custom curve - SystemManager
        /// is responsible for seeding it (via SetCustomCurveFromJson) the first time Custom is
        /// selected with nothing saved yet.
        /// </summary>
        public void SetPreset(int preset)
        {
            lock (_gate)
            {
                var next = preset == (int)CurvePreset.Custom ? CurvePreset.Custom : CurvePreset.LegionGo2;
                if (_preset == next) return;
                _preset = next;
                Logger.Info($"AutoSDR: preset changed to {_preset}.");
                if (_enabled && _hdrActive) ApplyLocked(GetCurrentBrightness());
            }
        }

        /// <summary>The custom curve as Go2HDR-compatible JSON, ascending by brightness.</summary>
        public string GetCustomCurveJson() => SerializeCurveJson(_customCurve);

        /// <summary>The currently ACTIVE curve (whichever preset is selected) as JSON, for export.</summary>
        public string GetActiveCurveJson() => SerializeCurveJson(ActiveCurve);

        /// <summary>
        /// Replaces the custom curve from Go2HDR-compatible JSON (an array of
        /// {"brightness":N,"sdrValue":N} objects). Requires at least 2 points; brightness is
        /// clamped to 1-100 and sdrValue to 0-100, then sorted ascending by brightness and
        /// de-duplicated (last value wins for a repeated brightness). Returns false with an
        /// error message on malformed input instead of throwing.
        /// </summary>
        public bool SetCustomCurveFromJson(string json, out string error)
        {
            if (!TryParseCurveJson(json, out var points, out error)) return false;

            lock (_gate)
            {
                _customCurve = points;
                if (_enabled && _hdrActive && _preset == CurvePreset.Custom) ApplyLocked(GetCurrentBrightness());
            }
            return true;
        }

        /// <summary>
        /// Seeds the custom curve to a copy of the Legion Go 2 default curve if nothing has
        /// been set yet (i.e. still at the built-in initial value). Called once by SystemManager
        /// on first startup / first switch to Custom so a fresh custom curve isn't empty.
        /// </summary>
        public void SeedCustomCurveFromDefaultIfUnset()
        {
            lock (_gate)
            {
                if (_customCurve == null || _customCurve.Length < 2)
                {
                    _customCurve = CloneDefaultCurve();
                }
            }
        }

        // ── JSON (Go2HDR-compatible flat array, hand-rolled to match the project's existing
        //    no-JSON-dependency convention for small payloads) ──────────────────────────────

        private static string SerializeCurveJson((int Brightness, int Sdr)[] curve)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < curve.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"brightness\":").Append(curve[i].Brightness.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"sdrValue\":").Append(curve[i].Sdr.ToString(CultureInfo.InvariantCulture)).Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static readonly Regex CurveObjectRegex = new Regex(@"\{[^{}]*\}", RegexOptions.Compiled);
        private static readonly Regex BrightnessFieldRegex = new Regex(@"""brightness""\s*:\s*(-?\d+(?:\.\d+)?)", RegexOptions.Compiled);
        private static readonly Regex SdrFieldRegex = new Regex(@"""sdrValue""\s*:\s*(-?\d+(?:\.\d+)?)", RegexOptions.Compiled);

        internal static bool TryParseCurveJson(string json, out (int Brightness, int Sdr)[] points, out string error)
        {
            points = null;
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Empty curve data.";
                return false;
            }

            var raw = new List<(int Brightness, int Sdr)>();
            foreach (Match obj in CurveObjectRegex.Matches(json))
            {
                var bMatch = BrightnessFieldRegex.Match(obj.Value);
                var sMatch = SdrFieldRegex.Match(obj.Value);
                if (!bMatch.Success || !sMatch.Success) continue;

                if (!double.TryParse(bMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var bVal)) continue;
                if (!double.TryParse(sMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var sVal)) continue;

                int brightness = Math.Max(1, Math.Min(100, (int)Math.Round(bVal)));
                int sdr = Math.Max(0, Math.Min(100, (int)Math.Round(sVal)));
                raw.Add((brightness, sdr));
            }

            if (raw.Count < 2)
            {
                error = "Curve needs at least 2 points.";
                return false;
            }

            // Sort ascending by brightness, de-duplicating (last value for a repeated brightness wins).
            points = raw
                .GroupBy(p => p.Brightness)
                .Select(g => g.Last())
                .OrderBy(p => p.Brightness)
                .ToArray();
            return true;
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
