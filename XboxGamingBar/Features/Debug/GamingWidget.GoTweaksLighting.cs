using System;
using Windows.Foundation.Collections;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        // Suppress outbound sends while we populate controls from the helper's current values on
        // load (otherwise every programmatic SelectedIndex/Value change echoes back).
        private bool _goTweaksSyncing;

        // --- Haptics ------------------------------------------------------------------------

        private void GoTweaksHaptics_Changed(object sender, object e)
        {
            if (GoTweaksHapticsGroupsPanel != null && GoTweaksHapticsMasterToggle != null)
            {
                GoTweaksHapticsGroupsPanel.Visibility =
                    GoTweaksHapticsMasterToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
            }
            if (_goTweaksSyncing) return;
            SendHapticsConfig();
        }

        private void SendHapticsConfig()
        {
            try
            {
                int master = (GoTweaksHapticsMasterToggle?.IsOn ?? false) ? 1 : 0;
                int onRelease = (GoTweaksHapticsReleaseToggle?.IsOn ?? false) ? 1 : 0;
                int pulseMs = (int)(GoTweaksHapticsPulseSlider?.Value ?? 10);
                if (GoTweaksHapticsPulseValue != null) GoTweaksHapticsPulseValue.Text = $"{pulseMs} ms";
                string face = GroupSeg("face", GoTweaksHapticsFaceToggle, GoTweaksHapticsFaceSlider);
                string front = GroupSeg("front", GoTweaksHapticsFrontToggle, GoTweaksHapticsFrontSlider);
                string back = GroupSeg("back", GoTweaksHapticsBackToggle, GoTweaksHapticsBackSlider);
                string trigger = GroupSeg("trigger", GoTweaksHapticsTriggerToggle, GoTweaksHapticsTriggerSlider);

                string config = $"{master}|{face}|{front}|{back}|{trigger}|rel:{onRelease}|pulse:{pulseMs}";
                SendConfigToHelper(Shared.Enums.Function.GoTweaksHapticsConfig, config);
            }
            catch (Exception ex) { Logger.Warn($"SendHapticsConfig failed: {ex.Message}"); }
        }

        private static string GroupSeg(string name, ToggleSwitch toggle, Slider slider)
        {
            int on = (toggle?.IsOn ?? false) ? 1 : 0;
            int intensity = (int)(slider?.Value ?? 100);
            return $"{name}:{on},{intensity}";
        }

        // --- Shared helpers -----------------------------------------------------------------

        private static void SendConfigToHelper(Shared.Enums.Function fn, string value)
        {
            if (!App.IsConnected) return;
            var msg = new ValueSet
            {
                { "Command", (int)Shared.Enums.Command.Set },
                { "Function", (int)fn },
                { "Content", value }
            };
            App.PipeClient?.SendValueSet(msg);
        }

        private static void SendIntToHelper(Shared.Enums.Function fn, int value)
        {
            if (!App.IsConnected) return;
            var msg = new ValueSet
            {
                { "Command", (int)Shared.Enums.Command.Set },
                { "Function", (int)fn },
                { "Content", value }
            };
            App.PipeClient?.SendValueSet(msg);
        }

        // --- Controller auto-sleep timeout --------------------------------------------------

        private void LegionControllerSleepComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_goTweaksSyncing) return;
            try
            {
                string tag = (LegionControllerSleepComboBox?.SelectedItem as ComboBoxItem)?.Tag as string ?? "15";
                if (int.TryParse(tag, out int minutes))
                {
                    SendIntToHelper(Shared.Enums.Function.LegionControllerSleepMinutes, minutes);
                }
            }
            catch (Exception ex) { Logger.Warn($"LegionControllerSleepComboBox_SelectionChanged failed: {ex.Message}"); }
        }

        /// <summary>
        /// Request persisted haptics config from the helper and populate the controls.
        /// Guards sends with _goTweaksSyncing during the programmatic fill.
        /// </summary>
        internal async void SyncGoTweaksFeaturesFromHelper()
        {
            try
            {
                if (!App.IsConnected) return;
                string haptics = await RequestStringAsync(Shared.Enums.Function.GoTweaksHapticsConfig);
                string sleep = await RequestStringAsync(Shared.Enums.Function.LegionControllerSleepMinutes);

                _goTweaksSyncing = true;
                try
                {
                    if (!string.IsNullOrEmpty(haptics)) ApplyHapticsToUi(haptics);
                    if (!string.IsNullOrEmpty(sleep)) SelectComboByTag(LegionControllerSleepComboBox, sleep.Trim());
                }
                finally { _goTweaksSyncing = false; }
            }
            catch (Exception ex) { Logger.Warn($"SyncGoTweaksFeaturesFromHelper failed: {ex.Message}"); }
        }

        private static async System.Threading.Tasks.Task<string> RequestStringAsync(Shared.Enums.Function fn)
        {
            try
            {
                var request = new ValueSet
                {
                    { "Command", (int)Shared.Enums.Command.Get },
                    { "Function", (int)fn }
                };
                var resp = await App.PipeClient.SendRequestAsync(request);
                if (resp != null && resp.TryGetValue("Content", out object content) && content != null)
                {
                    // Content may be a string (config blobs) or a boxed int (sleep minutes).
                    return content as string ?? content.ToString();
                }
            }
            catch (Exception ex) { Logger.Warn($"RequestStringAsync {fn} failed: {ex.Message}"); }
            return null;
        }

        private void ApplyHapticsToUi(string config)
        {
            string[] parts = config.Split('|');
            if (GoTweaksHapticsMasterToggle != null)
            {
                GoTweaksHapticsMasterToggle.IsOn = parts.Length > 0 && parts[0].Trim() == "1";
            }
            for (int i = 1; i < parts.Length; i++)
            {
                string seg = parts[i].Trim();
                int colon = seg.IndexOf(':');
                if (colon <= 0) continue;
                string name = seg.Substring(0, colon).Trim().ToLowerInvariant();
                string[] kv = seg.Substring(colon + 1).Split(',');
                bool on = kv.Length > 0 && kv[0].Trim() == "1";
                int intensity = kv.Length > 1 && int.TryParse(kv[1].Trim(), out int v) ? v : 100;

                switch (name)
                {
                    case "face": SetGroupUi(GoTweaksHapticsFaceToggle, GoTweaksHapticsFaceSlider, on, intensity); break;
                    case "front": SetGroupUi(GoTweaksHapticsFrontToggle, GoTweaksHapticsFrontSlider, on, intensity); break;
                    case "back": SetGroupUi(GoTweaksHapticsBackToggle, GoTweaksHapticsBackSlider, on, intensity); break;
                    case "trigger": SetGroupUi(GoTweaksHapticsTriggerToggle, GoTweaksHapticsTriggerSlider, on, intensity); break;
                    case "rel": if (GoTweaksHapticsReleaseToggle != null) GoTweaksHapticsReleaseToggle.IsOn = on; break;
                    case "pulse":
                        if (GoTweaksHapticsPulseSlider != null && kv.Length > 0 && int.TryParse(kv[0].Trim(), out int pm))
                        {
                            GoTweaksHapticsPulseSlider.Value = Math.Min(Math.Max(pm, 4), 40);
                            if (GoTweaksHapticsPulseValue != null) GoTweaksHapticsPulseValue.Text = $"{(int)GoTweaksHapticsPulseSlider.Value} ms";
                        }
                        break;
                }
            }
            if (GoTweaksHapticsGroupsPanel != null && GoTweaksHapticsMasterToggle != null)
            {
                GoTweaksHapticsGroupsPanel.Visibility =
                    GoTweaksHapticsMasterToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private static void SetGroupUi(ToggleSwitch toggle, Slider slider, bool on, int intensity)
        {
            if (toggle != null) toggle.IsOn = on;
            if (slider != null) slider.Value = Math.Min(Math.Max(intensity, 0), 100);
        }

        private static void SelectComboByTag(ComboBox combo, string tag)
        {
            if (combo == null) return;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ComboBoxItem item && (item.Tag as string) == tag)
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }

    }
}
