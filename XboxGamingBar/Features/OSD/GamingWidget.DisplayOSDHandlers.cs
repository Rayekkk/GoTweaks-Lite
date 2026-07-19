using Microsoft.Gaming.XboxGameBar;
using Microsoft.Gaming.XboxGameBar.Input;
using Microsoft.UI.Xaml.Controls;
using NLog;
using Shared.Data;
using Shared.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.System.Power;
using Windows.Storage;
using Windows.System;
using Windows.UI.Xaml.Input;
using System.Runtime.InteropServices;
using Windows.UI;
using XboxGamingBar.Data;
using XboxGamingBar.Event;
using XboxGamingBar.IPC;
using XboxGamingBar.QuickSettings;
using Shared.Enums;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async void AdaptiveBrightnessToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (isLoadingOLEDSettings) return;
            adaptiveBrightnessEnabled = AdaptiveBrightnessToggle.IsOn;
            SaveDisplayOSDSettingsToStorage();
            await SendDisplayOSDConfigToHelper();
        }

        // Chevron expander for the Adaptive Brightness card — keeps the card compact
        // by default; the mode combo + description live inside.
        // Chevron glyph: E70D = down (collapsed), E70E = up (expanded).
        private void AdaptiveBrightnessExpanderToggle_Click(object sender, RoutedEventArgs e)
        {
            if (AdaptiveBrightnessPanel == null || AdaptiveBrightnessExpanderToggle == null) return;
            bool open = AdaptiveBrightnessExpanderToggle.IsChecked == true;
            AdaptiveBrightnessPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            if (AdaptiveBrightnessExpanderIcon != null)
            {
                AdaptiveBrightnessExpanderIcon.Glyph = open ? "\uE70E" : "\uE70D";
            }
        }

        private async void OSDPositionShiftToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (isLoadingOLEDSettings) return;
            osdPositionShiftEnabled = OSDPositionShiftToggle.IsOn;
            SaveDisplayOSDSettingsToStorage();
            await SendDisplayOSDConfigToHelper();
        }

        private void FrametimeGraphPinnedToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (isLoadingOSDConfig) return;
            frametimeGraphPinned = FrametimeGraphPinnedToggle.IsOn;
            SaveOSDConfigToStorage();
            SendOSDConfigToHelper();
        }

        private void SaveDisplayOSDSettingsToStorage()
        {
            // Persisted by helper after it accepts OLEDConfig.
        }

        private void LoadDisplayOSDSettingsFromStorage()
        {
            isLoadingOLEDSettings = true;
            try
            {
                // Update UI
                if (AdaptiveBrightnessToggle != null) AdaptiveBrightnessToggle.IsOn = adaptiveBrightnessEnabled;
                if (OSDPositionShiftToggle != null) OSDPositionShiftToggle.IsOn = osdPositionShiftEnabled;
                if (OSDOpacitySlider != null) OSDOpacitySlider.Value = osdOpacity;
                if (OSDOpacityValue != null) OSDOpacityValue.Text = $"{osdOpacity}%";
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading display/OSD settings: {ex.Message}");
            }
            finally
            {
                isLoadingOLEDSettings = false;
            }
        }

        private async Task SendDisplayOSDConfigToHelper()
        {
            try
            {
                if (!App.IsConnected) return;

                var configString = $"AdaptiveBrightness:{(adaptiveBrightnessEnabled ? 1 : 0)};" +
                                   $"PositionShift:{(osdPositionShiftEnabled ? 1 : 0)}";

                var request = new Windows.Foundation.Collections.ValueSet
                {
                    { "Command", (int)Shared.Enums.Command.Set },
                    { "Function", (int)Shared.Enums.Function.OLEDConfig },
                    { "Content", configString },
                    { "UpdatedTime", DateTimeOffset.Now.Ticks }
                };
                await App.SendMessageAsync(request);

                Logger.Info($"Display/OSD config sent to helper: {configString}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending display/OSD config to helper: {ex.Message}");
            }
        }

        private async Task RequestDisplayOSDConfigFromHelperAsync()
        {
            if (!App.IsConnected) return;
            try
            {
                var response = await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet { { "Command", (int)Shared.Enums.Command.Get }, { "Function", (int)Shared.Enums.Function.OLEDConfig } });
                if (response == null || !response.TryGetValue("Content", out object content) || string.IsNullOrWhiteSpace(content?.ToString())) return;
                isLoadingOLEDSettings = true;
                foreach (var part in content.ToString().Split(';'))
                {
                    var pair = part.Split(':');
                    if (pair.Length != 2) continue;
                    if (pair[0] == "AdaptiveBrightness") adaptiveBrightnessEnabled = pair[1] == "1";
                    if (pair[0] == "PositionShift") osdPositionShiftEnabled = pair[1] == "1";
                }
                if (AdaptiveBrightnessToggle != null) AdaptiveBrightnessToggle.IsOn = adaptiveBrightnessEnabled;
                if (OSDPositionShiftToggle != null) OSDPositionShiftToggle.IsOn = osdPositionShiftEnabled;
            }
            catch (Exception ex) { Logger.Error($"Failed to render helper display OSD configuration: {ex.Message}"); }
            finally { isLoadingOLEDSettings = false; }
        }

    }
}
