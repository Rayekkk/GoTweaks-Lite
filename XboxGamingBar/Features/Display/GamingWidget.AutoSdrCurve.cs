using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        // In-memory copy of the Custom preset's curve, ascending by brightness. Mirrors the
        // helper's AutoSdrManager custom curve; kept here so the chart + row list can redraw
        // without a round trip. Empty until the first AutoSdrCustomCurve push arrives (startup
        // BatchGet or a helper-driven change).
        private readonly List<(int Brightness, int Sdr)> autoSdrCurve = new List<(int, int)>();
        private int autoSdrPresetValue;
        private Windows.UI.Xaml.DispatcherTimer autoSdrCurveSaveDebounceTimer;
        private const int AutoSdrCurveSaveDebounceMs = 400;
        private bool autoSdrCanvasSizeHooked;

        private void AutoSdrExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            bool expanded = AutoSdrExpandToggle?.IsChecked ?? false;
            if (AutoSdrCurvePanel != null)
                AutoSdrCurvePanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            if (AutoSdrExpandIcon != null)
                AutoSdrExpandIcon.Glyph = expanded ? "" : ""; // ChevronUp / ChevronDown
        }

        /// <summary>
        /// Shows/hides the Custom-only chart+list content and (re)draws it when shown. Called
        /// from AutoSdrPresetProperty whenever the preset value changes, from either direction
        /// (user picking a ComboBox item, or a helper-driven sync/import).
        /// </summary>
        internal void ApplyAutoSdrPreset(int preset)
        {
            autoSdrPresetValue = preset;
            bool isCustom = preset == 1;
            if (AutoSdrCustomContent != null)
                AutoSdrCustomContent.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;

            // The Canvas can still report ActualWidth/Height == 0 the first time this runs
            // (Visibility just flipped to Visible; layout hasn't passed yet), so a redraw hook
            // on SizeChanged catches that first real layout instead of drawing an empty chart.
            if (AutoSdrCurveCanvas != null && !autoSdrCanvasSizeHooked)
            {
                autoSdrCanvasSizeHooked = true;
                AutoSdrCurveCanvas.SizeChanged += (s, e) => DrawAutoSdrCurveChart();
            }

            if (isCustom)
            {
                DrawAutoSdrCurveChart();
                RebuildAutoSdrCurveRows();
            }
        }

        /// <summary>
        /// Parses a Go2HDR-compatible curve JSON push from the helper into the local cache and
        /// redraws the chart/list if the Custom content is currently shown. Called from
        /// AutoSdrCustomCurveProperty on every value change (startup sync, helper-driven change,
        /// or the echo of our own edits).
        /// </summary>
        internal void ApplyAutoSdrCustomCurve(string json)
        {
            var parsed = ParseAutoSdrCurveJson(json);
            autoSdrCurve.Clear();
            autoSdrCurve.AddRange(parsed);

            if (autoSdrPresetValue == 1 && AutoSdrCustomContent?.Visibility == Visibility.Visible)
            {
                DrawAutoSdrCurveChart();
                RebuildAutoSdrCurveRows();
            }
        }

        // ── Chart (read-only) ──────────────────────────────────────────────────────────────
        // X axis: 100% brightness on the left down to 40% on the right (matches Go2HDR's own
        // layout). Y axis: SDR 0 at the bottom, 100 at the top. Points below the curve's own
        // minimum brightness draw flat at SDR 0, same floor rule the helper applies at runtime.

        private const int AutoSdrChartMaxBrightness = 100;
        private const int AutoSdrChartMinBrightness = 40;

        // Reserve space inside the canvas for the tick labels drawn along each axis, so the
        // plotted curve itself doesn't overlap them.
        private const double AutoSdrChartLeftPad = 24;
        private const double AutoSdrChartBottomPad = 14;

        private void DrawAutoSdrCurveChart()
        {
            if (AutoSdrCurveCanvas == null) return;
            AutoSdrCurveCanvas.Children.Clear();

            double totalWidth = AutoSdrCurveCanvas.ActualWidth;
            double totalHeight = AutoSdrCurveCanvas.ActualHeight;
            if (totalWidth <= 0 || totalHeight <= 0 || autoSdrCurve.Count == 0) return;

            double plotLeft = AutoSdrChartLeftPad;
            double plotWidth = Math.Max(1, totalWidth - AutoSdrChartLeftPad);
            double plotHeight = Math.Max(1, totalHeight - AutoSdrChartBottomPad);

            double XFor(int brightness)
            {
                double t = (double)(AutoSdrChartMaxBrightness - brightness) / (AutoSdrChartMaxBrightness - AutoSdrChartMinBrightness);
                return plotLeft + Clamp01(t) * plotWidth;
            }
            double YFor(int sdr) => Clamp01(1 - sdr / 100.0) * plotHeight;

            var axisBrush = new SolidColorBrush(Color.FromArgb(255, 0x55, 0x59, 0x60));
            var tickBrush = new SolidColorBrush(Color.FromArgb(255, 0x88, 0x88, 0x88));

            // Axis lines
            AutoSdrCurveCanvas.Children.Add(new Line { X1 = plotLeft, Y1 = 0, X2 = plotLeft, Y2 = plotHeight, Stroke = axisBrush, StrokeThickness = 1 });
            AutoSdrCurveCanvas.Children.Add(new Line { X1 = plotLeft, Y1 = plotHeight, X2 = totalWidth, Y2 = plotHeight, Stroke = axisBrush, StrokeThickness = 1 });

            // Y ticks: 100 at top, 0 at bottom (SDR Level)
            AddAutoSdrAxisLabel("100", 0, 0, tickBrush);
            AddAutoSdrAxisLabel("0", 6, plotHeight - 9, tickBrush);

            // X ticks: 100% at the plot's left edge, 40% at the canvas' right edge (Screen Brightness)
            AddAutoSdrAxisLabel("100%", plotLeft + 1, plotHeight + 1, tickBrush);
            AddAutoSdrAxisLabel("40%", totalWidth - 24, plotHeight + 1, tickBrush);

            int minCurveBrightness = autoSdrCurve[0].Brightness;

            var points = new PointCollection();
            // Flat floor segment from the chart's left-most defined brightness (usually the
            // curve's own minimum) down to the chart's 40% edge, at SDR 0.
            points.Add(new Windows.Foundation.Point(XFor(minCurveBrightness), YFor(0)));
            points.Add(new Windows.Foundation.Point(XFor(AutoSdrChartMinBrightness), YFor(0)));
            for (int i = 0; i < autoSdrCurve.Count; i++)
            {
                points.Add(new Windows.Foundation.Point(XFor(autoSdrCurve[i].Brightness), YFor(autoSdrCurve[i].Sdr)));
            }

            var polyline = new Polyline
            {
                Points = points,
                Stroke = new SolidColorBrush(Color.FromArgb(255, 0x4C, 0xC2, 0xFF)),
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round,
            };
            AutoSdrCurveCanvas.Children.Add(polyline);
        }

        private void AddAutoSdrAxisLabel(string text, double x, double y, SolidColorBrush brush)
        {
            var label = new TextBlock { Text = text, FontSize = 9, Foreground = brush };
            Canvas.SetLeft(label, x);
            Canvas.SetTop(label, y);
            AutoSdrCurveCanvas.Children.Add(label);
        }

        private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

        // ── Editable row list ──────────────────────────────────────────────────────────────
        // One row per curve point, listed from 100% brightness down to the lowest defined
        // point (matching the chart's left-to-right direction). Brightness is read-only; SDR
        // Level has a value plus small up/down buttons that step it by 1.

        private void RebuildAutoSdrCurveRows()
        {
            if (AutoSdrCurveRowsPanel == null) return;
            AutoSdrCurveRowsPanel.Children.Clear();

            var orderedPoints = autoSdrCurve.OrderByDescending(p => p.Brightness).ToList();
            var separatorBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
            // Same shade as CardBackgroundBrush - one step lighter than the table's own
            // PageBackgroundBrush background, so the SDR value pill reads as a distinct chip.
            var pillBrush = new SolidColorBrush(Color.FromArgb(255, 0x35, 0x39, 0x3F));

            for (int rowIndex = 0; rowIndex < orderedPoints.Count; rowIndex++)
            {
                var point = orderedPoints[rowIndex];
                int brightness = point.Brightness; // captured per-row for the click closures below

                // Two equal-width columns, each centering its own content - not a full-width
                // stretch (Brightness pinned left, SDR pinned right), which read as two
                // disconnected edges rather than an aligned table.
                var row = new Grid { Margin = new Thickness(0, 6, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var brightnessText = new TextBlock
                {
                    Text = $"{brightness}%",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 0xB9, 0xC0, 0xCC)),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                };
                Grid.SetColumn(brightnessText, 0);
                row.Children.Add(brightnessText);

                var sdrPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };

                var sdrText = new TextBlock
                {
                    Text = point.Sdr.ToString(CultureInfo.InvariantCulture),
                    FontSize = 12,
                    FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Colors.White),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Width = 22,
                    TextAlignment = TextAlignment.Center,
                    Tag = brightness, // AdjustAutoSdrCurvePoint looks the row's text back up by this
                };
                var sdrPill = new Border
                {
                    Background = pillBrush,
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(10, 3, 10, 3),
                    Child = sdrText,
                    Margin = new Thickness(8, 0, 8, 0),
                };

                // Down (decrease) on the left, up (increase) on the right - a normal
                // stepper layout, not the previous stacked up/down pair.
                var downButton = MakeAutoSdrSpinnerButton(false, () => AdjustAutoSdrCurvePoint(brightness, -1, sdrText));
                var upButton = MakeAutoSdrSpinnerButton(true, () => AdjustAutoSdrCurvePoint(brightness, +1, sdrText));

                sdrPanel.Children.Add(downButton);
                sdrPanel.Children.Add(sdrPill);
                sdrPanel.Children.Add(upButton);
                Grid.SetColumn(sdrPanel, 1);
                row.Children.Add(sdrPanel);

                AutoSdrCurveRowsPanel.Children.Add(row);

                if (rowIndex < orderedPoints.Count - 1)
                {
                    AutoSdrCurveRowsPanel.Children.Add(new Border { Height = 1, Background = separatorBrush, Margin = new Thickness(4, 0, 4, 0) });
                }
            }
        }

        // Large, normal-sized stepper button (not the previous tiny stacked pair). isUp picks
        // ChevronUp ("") vs ChevronDown ("") - the glyph literal lives here, as an
        // escaped literal, specifically so call sites never need to embed the raw glyph
        // character themselves.
        private static Button MakeAutoSdrSpinnerButton(bool isUp, Action onClick)
        {
            var button = new Button
            {
                Content = new FontIcon { Glyph = isUp ? "" : "", FontSize = 12 },
                Width = 30,
                Height = 30,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Color.FromArgb(255, 0x35, 0x39, 0x3F)),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(6),
            };
            button.Click += (s, e) => onClick();
            return button;
        }

        /// <summary>
        /// Steps one curve point's SDR value by +/-1 (clamped 0-100), updates that row's
        /// TextBlock immediately, redraws the chart, and debounce-pushes the new curve to the
        /// helper so a burst of clicks doesn't spam the pipe.
        /// </summary>
        private void AdjustAutoSdrCurvePoint(int brightness, int delta, TextBlock sdrText)
        {
            int index = autoSdrCurve.FindIndex(p => p.Brightness == brightness);
            if (index < 0) return;

            int newSdr = Math.Max(0, Math.Min(100, autoSdrCurve[index].Sdr + delta));
            if (newSdr == autoSdrCurve[index].Sdr) return;

            autoSdrCurve[index] = (brightness, newSdr);
            if (sdrText != null) sdrText.Text = newSdr.ToString(CultureInfo.InvariantCulture);
            DrawAutoSdrCurveChart();

            if (autoSdrCurveSaveDebounceTimer == null)
            {
                autoSdrCurveSaveDebounceTimer = new Windows.UI.Xaml.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(AutoSdrCurveSaveDebounceMs)
                };
                autoSdrCurveSaveDebounceTimer.Tick += (s, e) =>
                {
                    autoSdrCurveSaveDebounceTimer.Stop();
                    autoSdrCustomCurve?.ForceSetValue(SerializeAutoSdrCurveJson(autoSdrCurve));
                };
            }
            autoSdrCurveSaveDebounceTimer.Stop();
            autoSdrCurveSaveDebounceTimer.Start();
        }

        // ── Import / Export ────────────────────────────────────────────────────────────────

        private async void AutoSdrExportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileSavePicker();
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
                picker.SuggestedFileName = "sdr_curve";
                picker.FileTypeChoices.Add("JSON files", new List<string> { ".json" });

                var file = await picker.PickSaveFileAsync();
                if (file == null) return;

                SetAutoSdrCurveStatus("Exporting...");
                if (!App.IsConnected)
                {
                    SetAutoSdrCurveStatus("Helper not connected.");
                    return;
                }

                var request = new Windows.Foundation.Collections.ValueSet();
                request.Add("ExportAutoSdrCurve", file.Path);
                var response = await App.SendMessageAsync(request);
                bool success = response != null && response.TryGetValue("Success", out var s) && s is bool b && b;
                SetAutoSdrCurveStatus(success ? "Curve exported." : GetPipeErrorMessage(response, "Export failed."));
            }
            catch (Exception ex)
            {
                Logger.Warn($"AutoSdrExportButton_Click failed: {ex.Message}");
                SetAutoSdrCurveStatus($"Export failed: {ex.Message}");
            }
        }

        private async void AutoSdrImportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
                picker.FileTypeFilter.Add(".json");
                picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;

                var file = await picker.PickSingleFileAsync();
                if (file == null) return;

                SetAutoSdrCurveStatus("Importing...");
                if (!App.IsConnected)
                {
                    SetAutoSdrCurveStatus("Helper not connected.");
                    return;
                }

                var request = new Windows.Foundation.Collections.ValueSet();
                request.Add("ImportAutoSdrCurve", file.Path);
                var response = await App.SendMessageAsync(request);
                bool success = response != null && response.TryGetValue("Success", out var s) && s is bool b && b;
                // On success, the helper ForceSetValue's AutoSdrCustomCurve + AutoSdrPreset back
                // to us, which redraws the chart/list - no local re-render needed here.
                SetAutoSdrCurveStatus(success ? "Curve imported." : GetPipeErrorMessage(response, "Import failed."));
            }
            catch (Exception ex)
            {
                Logger.Warn($"AutoSdrImportButton_Click failed: {ex.Message}");
                SetAutoSdrCurveStatus($"Import failed: {ex.Message}");
            }
        }

        private static string GetPipeErrorMessage(Windows.Foundation.Collections.ValueSet response, string fallback)
        {
            if (response != null && response.TryGetValue("Error", out var err) && err is string errStr && !string.IsNullOrWhiteSpace(errStr))
                return errStr;
            return fallback;
        }

        private void SetAutoSdrCurveStatus(string text)
        {
            if (AutoSdrCurveStatusText == null) return;
            AutoSdrCurveStatusText.Text = text;
            AutoSdrCurveStatusText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        // ── JSON (Go2HDR-compatible flat array: [{"brightness":N,"sdrValue":N}, ...]) ────────

        private static readonly Regex AutoSdrCurveObjectRegex = new Regex(@"\{[^{}]*\}", RegexOptions.Compiled);
        private static readonly Regex AutoSdrBrightnessFieldRegex = new Regex(@"""brightness""\s*:\s*(-?\d+(?:\.\d+)?)", RegexOptions.Compiled);
        private static readonly Regex AutoSdrSdrFieldRegex = new Regex(@"""sdrValue""\s*:\s*(-?\d+(?:\.\d+)?)", RegexOptions.Compiled);

        private static List<(int Brightness, int Sdr)> ParseAutoSdrCurveJson(string json)
        {
            var result = new List<(int, int)>();
            if (string.IsNullOrWhiteSpace(json)) return result;

            foreach (Match obj in AutoSdrCurveObjectRegex.Matches(json))
            {
                var bMatch = AutoSdrBrightnessFieldRegex.Match(obj.Value);
                var sMatch = AutoSdrSdrFieldRegex.Match(obj.Value);
                if (!bMatch.Success || !sMatch.Success) continue;
                if (!double.TryParse(bMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var bVal)) continue;
                if (!double.TryParse(sMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var sVal)) continue;
                result.Add(((int)Math.Round(bVal), (int)Math.Round(sVal)));
            }
            return result.OrderBy(p => p.Item1).ToList();
        }

        private static string SerializeAutoSdrCurveJson(List<(int Brightness, int Sdr)> curve)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < curve.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"brightness\":").Append(curve[i].Brightness.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"sdrValue\":").Append(curve[i].Sdr.ToString(CultureInfo.InvariantCulture)).Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }
    }
}
