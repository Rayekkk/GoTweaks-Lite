using System;
using Shared.Data;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

namespace XboxGamingBar
{
    /// <summary>
    /// PadForge-inspired Sticks &amp; Triggers shaping UI for the VIIPER backend.
    /// Owns all the slider/combo event wiring + the bundle (re)serialization
    /// that pushes a single string through to the helper via
    /// <see cref="Shared.Enums.Function.Viiper_StickTriggerConfig"/>.
    /// </summary>
    public sealed partial class GamingWidget
    {
        // While we apply persisted values during init, suppress the change
        // handlers so they don't re-serialize-and-push on every control we
        // touch (or worse, push the partial state mid-init).
        private bool stickTriggerSuppressEvents;

        private void InitStickTriggerControls()
        {
            // Read whatever was persisted (or pushed from helper sync) and
            // populate every control. SuppressEvents keeps the ValueChanged /
            // SelectionChanged handlers quiet during the apply.
            stickTriggerSuppressEvents = true;
            try
            {
                var serialized = viiperStickTriggerConfig?.Value ?? string.Empty;
                var bundle = StickTriggerConfigBundle.Deserialize(serialized);
                ApplyBundleToControls(bundle);
            }
            finally
            {
                stickTriggerSuppressEvents = false;
            }

            // Wire each control's change event to RebuildAndPush.
            WireStick(ViiperLSShapeComboBox, ViiperLSDXSlider, ViiperLSDYSlider,
                ViiperLSADXSlider, ViiperLSADYSlider,
                ViiperLSCurveXComboBox, ViiperLSCurveYComboBox,
                ViiperLSDXValue, ViiperLSDYValue, ViiperLSADXValue, ViiperLSADYValue);
            WireStick(ViiperRSShapeComboBox, ViiperRSDXSlider, ViiperRSDYSlider,
                ViiperRSADXSlider, ViiperRSADYSlider,
                ViiperRSCurveXComboBox, ViiperRSCurveYComboBox,
                ViiperRSDXValue, ViiperRSDYValue, ViiperRSADXValue, ViiperRSADYValue);
            WireTrigger(ViiperLTDZSlider, ViiperLTRMSlider, ViiperLTADSlider, ViiperLTCurveComboBox,
                ViiperLTDZValue, ViiperLTRMValue, ViiperLTADValue);
            WireTrigger(ViiperRTDZSlider, ViiperRTRMSlider, ViiperRTADSlider, ViiperRTCurveComboBox,
                ViiperRTDZValue, ViiperRTRMValue, ViiperRTADValue);
        }

        private void WireStick(ComboBox shape, Slider dx, Slider dy, Slider adx, Slider ady,
            ComboBox cx, ComboBox cy, TextBlock dxValue, TextBlock dyValue, TextBlock adxValue, TextBlock adyValue)
        {
            if (shape != null) shape.SelectionChanged += StickTriggerChanged;
            HookSliderWithLabel(dx, dxValue);
            HookSliderWithLabel(dy, dyValue);
            HookSliderWithLabel(adx, adxValue);
            HookSliderWithLabel(ady, adyValue);
            if (cx != null) cx.SelectionChanged += StickTriggerChanged;
            if (cy != null) cy.SelectionChanged += StickTriggerChanged;
        }

        private void WireTrigger(Slider dz, Slider rm, Slider ad, ComboBox curve,
            TextBlock dzValue, TextBlock rmValue, TextBlock adValue)
        {
            HookSliderWithLabel(dz, dzValue);
            HookSliderWithLabel(rm, rmValue);
            HookSliderWithLabel(ad, adValue);
            if (curve != null) curve.SelectionChanged += StickTriggerChanged;
        }

        private void HookSliderWithLabel(Slider s, TextBlock label)
        {
            if (s == null) return;
            s.ValueChanged += (_, e) =>
            {
                if (label != null) label.Text = ((int)e.NewValue) + "%";
                StickTriggerChanged(null, null);
            };
        }

        private void StickTriggerChanged(object sender, object e)
        {
            if (stickTriggerSuppressEvents) return;
            try
            {
                var bundle = BuildBundleFromControls();
                viiperStickTriggerConfig?.SetValue(bundle.Serialize());
            }
            catch (Exception ex)
            {
                Logger.Warn($"StickTriggerChanged threw: {ex.Message}");
            }
        }

        private StickTriggerConfigBundle BuildBundleFromControls()
        {
            return new StickTriggerConfigBundle
            {
                LeftStick = new StickConfig
                {
                    Shape = (DeadzoneShape)TagInt(ViiperLSShapeComboBox, 0),
                    DeadzoneX = PercentFromSlider(ViiperLSDXSlider),
                    DeadzoneY = PercentFromSlider(ViiperLSDYSlider),
                    AntiDeadzoneX = PercentFromSlider(ViiperLSADXSlider),
                    AntiDeadzoneY = PercentFromSlider(ViiperLSADYSlider),
                    CurveX = (SensitivityCurve)TagInt(ViiperLSCurveXComboBox, 0),
                    CurveY = (SensitivityCurve)TagInt(ViiperLSCurveYComboBox, 0),
                },
                RightStick = new StickConfig
                {
                    Shape = (DeadzoneShape)TagInt(ViiperRSShapeComboBox, 0),
                    DeadzoneX = PercentFromSlider(ViiperRSDXSlider),
                    DeadzoneY = PercentFromSlider(ViiperRSDYSlider),
                    AntiDeadzoneX = PercentFromSlider(ViiperRSADXSlider),
                    AntiDeadzoneY = PercentFromSlider(ViiperRSADYSlider),
                    CurveX = (SensitivityCurve)TagInt(ViiperRSCurveXComboBox, 0),
                    CurveY = (SensitivityCurve)TagInt(ViiperRSCurveYComboBox, 0),
                },
                LeftTrigger = new TriggerConfig
                {
                    DeadzoneStart = PercentFromSlider(ViiperLTDZSlider),
                    RangeMax = PercentFromSlider(ViiperLTRMSlider, 1f),
                    AntiDeadzone = PercentFromSlider(ViiperLTADSlider),
                    Curve = (SensitivityCurve)TagInt(ViiperLTCurveComboBox, 0),
                },
                RightTrigger = new TriggerConfig
                {
                    DeadzoneStart = PercentFromSlider(ViiperRTDZSlider),
                    RangeMax = PercentFromSlider(ViiperRTRMSlider, 1f),
                    AntiDeadzone = PercentFromSlider(ViiperRTADSlider),
                    Curve = (SensitivityCurve)TagInt(ViiperRTCurveComboBox, 0),
                },
            };
        }

        private void ApplyBundleToControls(StickTriggerConfigBundle b)
        {
            SetShapeCombo(ViiperLSShapeComboBox, (int)b.LeftStick.Shape);
            SetSlider(ViiperLSDXSlider, b.LeftStick.DeadzoneX);
            SetSlider(ViiperLSDYSlider, b.LeftStick.DeadzoneY);
            SetSlider(ViiperLSADXSlider, b.LeftStick.AntiDeadzoneX);
            SetSlider(ViiperLSADYSlider, b.LeftStick.AntiDeadzoneY);
            SetCurveCombo(ViiperLSCurveXComboBox, (int)b.LeftStick.CurveX);
            SetCurveCombo(ViiperLSCurveYComboBox, (int)b.LeftStick.CurveY);

            SetShapeCombo(ViiperRSShapeComboBox, (int)b.RightStick.Shape);
            SetSlider(ViiperRSDXSlider, b.RightStick.DeadzoneX);
            SetSlider(ViiperRSDYSlider, b.RightStick.DeadzoneY);
            SetSlider(ViiperRSADXSlider, b.RightStick.AntiDeadzoneX);
            SetSlider(ViiperRSADYSlider, b.RightStick.AntiDeadzoneY);
            SetCurveCombo(ViiperRSCurveXComboBox, (int)b.RightStick.CurveX);
            SetCurveCombo(ViiperRSCurveYComboBox, (int)b.RightStick.CurveY);

            SetSlider(ViiperLTDZSlider, b.LeftTrigger.DeadzoneStart);
            SetSlider(ViiperLTRMSlider, b.LeftTrigger.RangeMax);
            SetSlider(ViiperLTADSlider, b.LeftTrigger.AntiDeadzone);
            SetCurveCombo(ViiperLTCurveComboBox, (int)b.LeftTrigger.Curve);

            SetSlider(ViiperRTDZSlider, b.RightTrigger.DeadzoneStart);
            SetSlider(ViiperRTRMSlider, b.RightTrigger.RangeMax);
            SetSlider(ViiperRTADSlider, b.RightTrigger.AntiDeadzone);
            SetCurveCombo(ViiperRTCurveComboBox, (int)b.RightTrigger.Curve);

            UpdateAllLabels();
        }

        private void UpdateAllLabels()
        {
            UpdateLabel(ViiperLSDXSlider, ViiperLSDXValue);
            UpdateLabel(ViiperLSDYSlider, ViiperLSDYValue);
            UpdateLabel(ViiperLSADXSlider, ViiperLSADXValue);
            UpdateLabel(ViiperLSADYSlider, ViiperLSADYValue);
            UpdateLabel(ViiperRSDXSlider, ViiperRSDXValue);
            UpdateLabel(ViiperRSDYSlider, ViiperRSDYValue);
            UpdateLabel(ViiperRSADXSlider, ViiperRSADXValue);
            UpdateLabel(ViiperRSADYSlider, ViiperRSADYValue);
            UpdateLabel(ViiperLTDZSlider, ViiperLTDZValue);
            UpdateLabel(ViiperLTRMSlider, ViiperLTRMValue);
            UpdateLabel(ViiperLTADSlider, ViiperLTADValue);
            UpdateLabel(ViiperRTDZSlider, ViiperRTDZValue);
            UpdateLabel(ViiperRTRMSlider, ViiperRTRMValue);
            UpdateLabel(ViiperRTADSlider, ViiperRTADValue);
        }
        private static void UpdateLabel(Slider s, TextBlock t)
        {
            if (s != null && t != null) t.Text = ((int)s.Value) + "%";
        }

        private static int TagInt(ComboBox cb, int fallback)
        {
            if (cb?.SelectedItem is ComboBoxItem item && item.Tag is string tag &&
                int.TryParse(tag, out var n)) return n;
            return fallback;
        }

        private static float PercentFromSlider(Slider s, float fallback = 0f)
        {
            if (s == null) return fallback;
            return (float)(s.Value / 100.0);
        }

        private static void SetSlider(Slider s, float normalized)
        {
            if (s == null) return;
            double pct = Math.Round(normalized * 100.0);
            if (pct < s.Minimum) pct = s.Minimum;
            if (pct > s.Maximum) pct = s.Maximum;
            s.Value = pct;
        }

        private static void SetShapeCombo(ComboBox cb, int tag) => SetComboByTag(cb, tag);
        private static void SetCurveCombo(ComboBox cb, int tag) => SetComboByTag(cb, tag);
        private static void SetComboByTag(ComboBox cb, int tag)
        {
            if (cb == null) return;
            string target = tag.ToString();
            for (int i = 0; i < cb.Items.Count; i++)
            {
                if (cb.Items[i] is ComboBoxItem item && (item.Tag as string) == target)
                {
                    if (cb.SelectedIndex != i) cb.SelectedIndex = i;
                    return;
                }
            }
            if (cb.Items.Count > 0 && cb.SelectedIndex < 0) cb.SelectedIndex = 0;
        }

        private void ViiperStickTriggerExpandButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViiperStickTriggerContent == null) return;
            bool collapsed = ViiperStickTriggerContent.Visibility == Visibility.Collapsed;
            ViiperStickTriggerContent.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
            // Tell the helper to start/stop the live-sample stream — kept off
            // by default so the input hot path doesn't push samples we'll just
            // throw away when the section is hidden.
            try { viiperStickTriggerPreviewEnabled?.SetValue(collapsed); }
            catch (Exception ex) { Logger.Warn($"toggle stick/trigger preview threw: {ex.Message}"); }

            if (ViiperStickTriggerExpandIcon != null)
            {
                // E70D when collapsed, E70E when expanded.
                ViiperStickTriggerExpandIcon.Glyph = collapsed ? "" : "";
            }
        }

        private void ViiperLSExpandButton_Click(object sender, RoutedEventArgs e)
            => ToggleSubSection(ViiperLSContent, ViiperLSExpandIcon);
        private void ViiperRSExpandButton_Click(object sender, RoutedEventArgs e)
            => ToggleSubSection(ViiperRSContent, ViiperRSExpandIcon);
        private void ViiperLTExpandButton_Click(object sender, RoutedEventArgs e)
            => ToggleSubSection(ViiperLTContent, ViiperLTExpandIcon);
        private void ViiperRTExpandButton_Click(object sender, RoutedEventArgs e)
            => ToggleSubSection(ViiperRTContent, ViiperRTExpandIcon);

        /// <summary>
        /// True while the Sticks &amp; Triggers panel is expanded. Used by
        /// the Page-level PreviewKeyDown handler to swallow LT/RT presses
        /// without advancing tabs, so the user can pull the triggers to
        /// verify their shaping curve without being kicked off the tab.
        /// </summary>
        internal bool IsStickTriggerPreviewOpen
            => ViiperStickTriggerContent != null && ViiperStickTriggerContent.Visibility == Visibility.Visible;

        private static void ToggleSubSection(Windows.UI.Xaml.UIElement content, FontIcon icon)
        {
            if (content == null) return;
            bool collapsed = content.Visibility == Visibility.Collapsed;
            content.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
            if (icon != null) icon.Glyph = collapsed ? "" : "";
        }

        private void ViiperStickGyroExpandButton_Click(object sender, RoutedEventArgs e)
            => ToggleSubSection(ViiperStickGyroContent, ViiperStickGyroExpandIcon);

        private void ViiperStickTriggerResetButton_Click(object sender, RoutedEventArgs e)
        {
            stickTriggerSuppressEvents = true;
            try
            {
                ApplyBundleToControls(StickTriggerConfigBundle.Default);
            }
            finally
            {
                stickTriggerSuppressEvents = false;
            }
            // Single push for the entire reset.
            viiperStickTriggerConfig?.SetValue(StickTriggerConfigBundle.Default.Serialize());
        }

        /// <summary>
        /// Wires the live-sample subscription. The helper streams raw frames
        /// at ~30Hz while viiperStickTriggerPreviewEnabled is true; the
        /// widget runs the same StickTriggerProcessor locally to derive
        /// shaped values so the canvas always reflects the current bundle —
        /// no extra pipe traffic when the user is adjusting a slider.
        /// </summary>
        private void InitStickTriggerPreview()
        {
            if (viiperStickTriggerLiveSample == null) return;
            viiperStickTriggerLiveSample.PropertyChanged += (s, e) => OnStickTriggerLiveSample();
        }

        private async void OnStickTriggerLiveSample()
        {
            if (ViiperStickTriggerContent == null || ViiperStickTriggerContent.Visibility != Visibility.Visible) return;
            if (Dispatcher == null) return;
            var payload = viiperStickTriggerLiveSample?.Value;
            if (string.IsNullOrEmpty(payload)) return;

            short lx, ly, rx, ry; byte lt, rt;
            if (!TryParseSample(payload, out lx, out ly, out rx, out ry, out lt, out rt)) return;

            var bundle = BuildBundleFromControls();
            StickTriggerProcessor.TransformStick(lx, ly, bundle.LeftStick, out var slx, out var sly);
            StickTriggerProcessor.TransformStick(rx, ry, bundle.RightStick, out var srx, out var sry);
            var slt = StickTriggerProcessor.TransformTrigger(lt, bundle.LeftTrigger);
            var srt = StickTriggerProcessor.TransformTrigger(rt, bundle.RightTrigger);

            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                PlaceStickDot(ViiperLSRawDot, lx, ly);
                PlaceStickDot(ViiperLSShapedDot, slx, sly);
                PlaceStickDot(ViiperRSRawDot, rx, ry);
                PlaceStickDot(ViiperRSShapedDot, srx, sry);
                UpdateTriggerBars(ViiperLTRawBar, ViiperLTShapedBar, lt, slt);
                UpdateTriggerBars(ViiperRTRawBar, ViiperRTShapedBar, rt, srt);
            });
        }

        private static bool TryParseSample(string s,
            out short lx, out short ly, out short rx, out short ry, out byte lt, out byte rt)
        {
            lx = ly = rx = ry = 0; lt = rt = 0;
            if (string.IsNullOrEmpty(s)) return false;
            var parts = s.Split(',');
            if (parts.Length != 6) return false;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var ns = System.Globalization.NumberStyles.Integer;
            return short.TryParse(parts[0], ns, inv, out lx)
                && short.TryParse(parts[1], ns, inv, out ly)
                && short.TryParse(parts[2], ns, inv, out rx)
                && short.TryParse(parts[3], ns, inv, out ry)
                && byte.TryParse(parts[4], ns, inv, out lt)
                && byte.TryParse(parts[5], ns, inv, out rt);
        }

        // Canvas is 80×80, dots are 8×8 → usable travel is ±36 px from center.
        // XInput Y grows up; Canvas Y grows down — flip on render.
        private const double StickCanvasSize = 80.0;
        private const double StickDotSize = 8.0;

        private static void PlaceStickDot(Windows.UI.Xaml.Shapes.Ellipse dot, short x, short y)
        {
            if (dot == null) return;
            double nx = x / 32768.0;
            double ny = -(y / 32768.0);
            if (nx < -1.0) nx = -1.0; else if (nx > 1.0) nx = 1.0;
            if (ny < -1.0) ny = -1.0; else if (ny > 1.0) ny = 1.0;
            double half = (StickCanvasSize - StickDotSize) / 2.0;
            Windows.UI.Xaml.Controls.Canvas.SetLeft(dot, half + nx * half);
            Windows.UI.Xaml.Controls.Canvas.SetTop(dot, half + ny * half);
        }

        private static void UpdateTriggerBars(Windows.UI.Xaml.Shapes.Rectangle raw,
            Windows.UI.Xaml.Shapes.Rectangle shaped, byte rawValue, byte shapedValue)
        {
            double maxW = 0.0;
            if (raw?.Parent is Windows.UI.Xaml.FrameworkElement parent)
            {
                maxW = parent.ActualWidth;
            }
            if (maxW <= 0) maxW = 100;
            if (raw != null) raw.Width = (rawValue / 255.0) * maxW;
            if (shaped != null) shaped.Width = (shapedValue / 255.0) * maxW;
        }
    }
}
