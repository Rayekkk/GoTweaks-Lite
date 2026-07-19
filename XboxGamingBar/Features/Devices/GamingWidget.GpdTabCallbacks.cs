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
        /// <summary>
        /// Sets GPD tab visibility based on device detection.
        /// </summary>
        private void SetGPDTabVisibility(bool visible)
        {
            if (GPDNavItem != null)
            {
                GPDNavItem.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                Logger.Info($"GPD tab visibility set to: {visible}");
            }

            // Update connection status text
            if (GPDConnectionStatusText != null)
            {
                GPDConnectionStatusText.Text = visible ? "Connected" : "Detecting...";
                GPDConnectionStatusText.Foreground = new SolidColorBrush(visible ?
                    Windows.UI.Color.FromArgb(255, 76, 175, 80) :  // Green
                    Windows.UI.Color.FromArgb(255, 136, 136, 136)); // Gray
            }

            UpdateSystemControllerEmulationNavigation();
        }

        /// <summary>
        /// Sets the GPD device name text from the helper.
        /// </summary>
        private void SetGPDDeviceName(string name)
        {
            if (GPDDeviceNameText != null && !string.IsNullOrEmpty(name))
            {
                GPDDeviceNameText.Text = name;
                Logger.Info($"GPD device name set to: {name}");
            }
        }

        /// <summary>
        /// Sets visibility of fan control section based on device capability.
        /// Fan control uses EC commands, independent of HID controller connection.
        /// </summary>
        private void SetGPDFanControlVisibility(bool supported)
        {
            if (GPDFanControlSection != null)
            {
                GPDFanControlSection.Visibility = supported ? Visibility.Visible : Visibility.Collapsed;
                Logger.Info($"GPD fan control section visibility set to: {supported}");
            }

            // The helper owns and persists the enabled state. Render the last confirmed value;
            // never resurrect a stale widget LocalSettings value when the section becomes visible.
            if (supported)
                ApplyConfirmedGPDFanCurveEnabled(gpdFanCurveEnabled?.Value == true);
        }

        /// <summary>
        /// Sets visibility of button remapping section based on HID controller connection.
        /// Button remapping requires HID connection to the Win 5 controller.
        /// </summary>
        private void SetGPDButtonRemapVisibility(bool connected)
        {
            if (GPDButtonRemapSection != null)
            {
                GPDButtonRemapSection.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
                Logger.Info($"GPD button remap section visibility set to: {connected}");
            }

            if (GPDApplyMappingsButton != null)
            {
                GPDApplyMappingsButton.IsEnabled = connected;
            }

            UpdateSystemControllerEmulationNavigation();
        }

        /// <summary>
        /// Controls visibility and enabled state for the handheld-agnostic Controller Emulation card.
        /// </summary>
        private void SetControllerEmulationAvailability(bool available)
        {
            controllerEmulationSupported = available;

            if (ControllerEmulationCard != null)
            {
                ControllerEmulationCard.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            }

            if (ControllerEmulationEnabledToggle != null)
            {
                ControllerEmulationEnabledToggle.IsEnabled = available;
            }

            UpdateControllerEmulationControlState();
            UpdateControllerEmulationStatusText();
            UpdateControllerEmulationPrereqGate();
            Logger.Info($"Controller emulation availability set to: {available}");
            RefreshLegionEnhancedRemapUi();
            UpdateSystemControllerEmulationNavigation();

            if (available)
            {
                RequestControllerEmulationDriverStatus();
            }

            // Quick tab tile visibility is gated on availability — refresh the grid so
            // the Controller tile appears/disappears when the helper reports support.
            RefreshQuickSettingsForControllerEmulation();
        }

        private void RefreshQuickSettingsForControllerEmulation()
        {
            if (!quickSettingsInitialized) return;
            try
            {
                RebuildQuickSettingsTiles();
                BuildSortableGrid();
                UpdateQuickSettingsTileStates();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error refreshing Quick Settings for Controller Emulation: {ex.Message}");
            }
        }

        private void UpdateControllerEmulationControlState()
        {
            // The legacy ControllerEmulationContent body (mode combo, mouse/DS4/rumble
            // settings, hide-stock-controller toggle, etc.) is permanently collapsed now
            // that VIIPER is the only emulation backend (see CLAUDE.md SS21) - those
            // controls were removed. What's left here is the "Gyro -> Stick" mirror bank
            // (GyroActivationContent / JoystickOutputContent), which stays invisible and
            // is driven programmatically by WireViiperStickGyroMirror(); it just needs to
            // track the card's enabled state so mirrored values stay consistent.
            bool enabled = controllerEmulationSupported &&
                           ControllerEmulationEnabledToggle != null &&
                           ControllerEmulationEnabledToggle.IsOn;

            bool isAlwaysOnActivation = ControllerEmulationGyroActivationModeComboBox == null ||
                                        ControllerEmulationGyroActivationModeComboBox.SelectedIndex <= 0;
            if (ControllerEmulationGyroActivationModeComboBox != null)
            {
                ControllerEmulationGyroActivationModeComboBox.IsEnabled = enabled;
            }

            if (ControllerEmulationGyroActivationButtonComboBox != null)
            {
                ControllerEmulationGyroActivationButtonComboBox.IsEnabled = enabled && !isAlwaysOnActivation;
            }

            if (ControllerEmulationStickSelectComboBox != null)
                ControllerEmulationStickSelectComboBox.IsEnabled = enabled;
            if (StickConversionComboBox != null)
                StickConversionComboBox.IsEnabled = enabled;
            if (StickOrientationV2ComboBox != null)
                StickOrientationV2ComboBox.IsEnabled = enabled;
            if (ControllerEmulationStickInvertXToggle != null)
                ControllerEmulationStickInvertXToggle.IsEnabled = enabled;
            if (ControllerEmulationStickInvertYToggle != null)
                ControllerEmulationStickInvertYToggle.IsEnabled = enabled;
            if (StickSensitivityV2Slider != null)
                StickSensitivityV2Slider.IsEnabled = enabled;

            // Keep Legion remap advanced controls aligned with current emulation toggles
            // even when startup/property sync order suppresses Toggle events.
            RefreshLegionEnhancedRemapUi();
        }

        private void RefreshLegionEnhancedRemapUi()
        {
            foreach (string buttonName in LegionRemapButtonNames)
            {
                UpdateButtonGamepadComboControls(buttonName);
            }
        }

        private void UpdateControllerEmulationStatusText()
        {
            if (ControllerEmulationStatusText != null)
            {
                bool enabled = controllerEmulationSupported &&
                               ControllerEmulationEnabledToggle != null &&
                               ControllerEmulationEnabledToggle.IsOn;

                if (!controllerEmulationSupported)
                {
                    ControllerEmulationStatusText.Text = "Controller emulation is not available on this handheld.";
                    ControllerEmulationStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 136, 136, 136));
                }
                else if (!enabled)
                {
                    ControllerEmulationStatusText.Text = "Controller emulation is disabled.";
                    ControllerEmulationStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 210, 170, 90));
                }
                else
                {
                    ControllerEmulationStatusText.Text = "Controller emulation is enabled.";
                    ControllerEmulationStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 120, 200, 120));
                }
            }
        }

        private void ControllerEmulationEnabledToggle_Toggled(object sender, RoutedEventArgs e)
        {
            UpdateControllerEmulationControlState();
            UpdateControllerEmulationStatusText();
            UpdateSystemControllerEmulationNavigation();
            // Keep the Quick tab Controller tile in sync with the System-tab toggle
            // and any helper-driven changes (e.g. ControllerEmulationAvailable arrives
            // late and needs to flip the tile from "N/A" to its actual state).
            UpdateQuickSettingsTileStates();
        }

        private void ControllerEmulationGyroActivationModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateControllerEmulationControlState();
            UpdateSystemControllerEmulationNavigation();
        }

        /// <summary>
        /// Keeps System tab D-pad/keyboard navigation valid when Controller Emulation card visibility/enabled state changes.
        /// </summary>
        private void UpdateSystemControllerEmulationNavigation()
        {
            if (HotkeysExpandButton == null || DebugExpandButton == null)
            {
                return;
            }

            bool emulationCardVisible =
                ControllerEmulationCard != null &&
                ControllerEmulationCard.Visibility == Visibility.Visible &&
                ControllerEmulationExpandButton != null;

            // The legacy ControllerEmulationContent body is permanently collapsed now
            // that VIIPER is the only emulation backend (CLAUDE.md SS21) - only the VIIPER
            // body needs the ExpandButton -> EnabledToggle -> first-body-item chain.
            bool emulationCardExpanded =
                emulationCardVisible &&
                isControllerEmulationExpanded &&
                ViiperEmulationContent != null &&
                ViiperEmulationContent.Visibility == Visibility.Visible;

            if (emulationCardVisible)
            {
                HotkeysExpandButton.XYFocusDown = ControllerEmulationExpandButton;
                ControllerEmulationExpandButton.XYFocusUp = HotkeysExpandButton;

                if (!emulationCardExpanded)
                {
                    ControllerEmulationExpandButton.XYFocusDown = DebugExpandButton;
                    DebugExpandButton.XYFocusUp = ControllerEmulationExpandButton;
                    return;
                }

                if (ControllerEmulationEnabledToggle != null)
                {
                    ControllerEmulationExpandButton.XYFocusDown = ControllerEmulationEnabledToggle;
                    ControllerEmulationEnabledToggle.XYFocusUp = ControllerEmulationExpandButton;
                }
                else
                {
                    ControllerEmulationExpandButton.XYFocusDown = DebugExpandButton;
                    DebugExpandButton.XYFocusUp = ControllerEmulationExpandButton;
                    return;
                }

                // Wire the entry/exit explicitly; auto XY traversal handles internal
                // navigation between VIIPER controls (sub-device combo, toggles, sliders)
                // since they sit in a straightforward vertical stack.
                if (ViiperDeviceTypeComboBox != null)
                {
                    ControllerEmulationEnabledToggle.XYFocusDown = ViiperDeviceTypeComboBox;
                    ViiperDeviceTypeComboBox.XYFocusUp = ControllerEmulationEnabledToggle;
                    DebugExpandButton.XYFocusUp = ViiperDeviceTypeComboBox;
                }
                else
                {
                    DebugExpandButton.XYFocusUp = ControllerEmulationEnabledToggle;
                }
            }
            else
            {
                HotkeysExpandButton.XYFocusDown = DebugExpandButton;
                DebugExpandButton.XYFocusUp = HotkeysExpandButton;
            }
        }

        /// <summary>
        /// Updates the fan RPM display.
        /// </summary>
        private void UpdateGPDFanRPM(int rpm)
        {
            if (GPDFanRPMText != null)
            {
                GPDFanRPMText.Text = rpm > 0 ? $"{rpm} RPM" : "-- RPM";
            }
        }

        /// <summary>
        /// Updates the fan mode UI.
        /// </summary>
        private void UpdateGPDFanMode(int mode)
        {
            bool isManual = mode == 1;

            if (GPDFanModeToggle != null)
            {
                // Temporarily remove handler to avoid triggering property update
                GPDFanModeToggle.Toggled -= GPDFanModeToggle_Toggled;
                GPDFanModeToggle.IsOn = isManual;
                GPDFanModeToggle.Toggled += GPDFanModeToggle_Toggled;
            }

            if (GPDFanModeText != null)
            {
                GPDFanModeText.Text = isManual ? "Manual" : "Auto";
            }

            if (GPDFanSpeedSection != null)
            {
                GPDFanSpeedSection.Visibility = isManual ? Visibility.Visible : Visibility.Collapsed;
            }
        }

    }
}
