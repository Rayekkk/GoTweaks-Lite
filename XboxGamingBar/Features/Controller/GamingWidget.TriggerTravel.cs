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
using Shared.Enums;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private void LegionHairTriggers_Toggled(object sender, RoutedEventArgs e)
        {
            if (isLoadingControllerProfile)
                return;

            bool enabled = LegionHairTriggersToggle?.IsOn ?? false;

            // [2.0 rebuild - slice 3] Slider enablement is a view concern - always follow the toggle
            // state, including when the helper pushes it (HairTriggers is helper-authoritative now).
            UpdateTriggerSlidersEnabled(!enabled);

            // Applying the hair-trigger travel preset (0/99) and saving is a USER action. On a helper
            // push the helper already sent the correct trigger travel values, so don't overwrite them
            // with the preset or re-save/echo. Just reflect the toggle + enablement (done above).
            //
            // [full-audit fix, 2026-07-20 — B2] This used to guard on isApplyingHelperUpdate, which
            // PipeClient has already RESET by the time this handler runs: the toggle reflect happens
            // inside WidgetToggleProperty.NotifyPropertyChanged's QUEUED dispatcher lambda (a later
            // pass), so isApplyingHelperUpdate is false and the preset branch fired on a helper push
            // - zeroing the four travel sliders whose 500ms debounce then overwrote the profile's
            // saved custom travel. IsUpdatingUI / HelperSyncCount ARE true here, because the
            // synchronous Toggled fires DURING that lambda's UI.IsOn write (inside the
            // HelperSyncCount++/IsUpdatingUI=true bracket) - the correct guard.
            if (legionHairTriggers?.IsUpdatingUI == true || WidgetSliderProperty.HelperSyncCount > 0)
            {
                Logger.Info($"Hair Triggers reflected from helper: {enabled}");
                return;
            }

            if (enabled)
            {
                // Hair triggers: Start=0 (no dead zone), End=99 (full press at 1% travel)
                // HID command end% is offset from 100%, so end=99 means trigger fully pressed at 1% travel
                if (LegionLeftTriggerStartSlider != null)
                {
                    LegionLeftTriggerStartSlider.Value = 0;
                    if (LegionLeftTriggerStartValue != null)
                        LegionLeftTriggerStartValue.Text = "0%";
                }
                if (LegionLeftTriggerEndSlider != null)
                {
                    LegionLeftTriggerEndSlider.Value = 99;
                    if (LegionLeftTriggerEndValue != null)
                        LegionLeftTriggerEndValue.Text = "99%";
                }
                if (LegionRightTriggerStartSlider != null)
                {
                    LegionRightTriggerStartSlider.Value = 0;
                    if (LegionRightTriggerStartValue != null)
                        LegionRightTriggerStartValue.Text = "0%";
                }
                if (LegionRightTriggerEndSlider != null)
                {
                    LegionRightTriggerEndSlider.Value = 99;
                    if (LegionRightTriggerEndValue != null)
                        LegionRightTriggerEndValue.Text = "99%";
                }
            }
            else
            {
                // Disable hair triggers: Reset to full travel (0% for all = full trigger press required)
                if (LegionLeftTriggerStartSlider != null)
                {
                    LegionLeftTriggerStartSlider.Value = 0;
                    if (LegionLeftTriggerStartValue != null)
                        LegionLeftTriggerStartValue.Text = "0%";
                }
                if (LegionLeftTriggerEndSlider != null)
                {
                    LegionLeftTriggerEndSlider.Value = 0;
                    if (LegionLeftTriggerEndValue != null)
                        LegionLeftTriggerEndValue.Text = "0%";
                }
                if (LegionRightTriggerStartSlider != null)
                {
                    LegionRightTriggerStartSlider.Value = 0;
                    if (LegionRightTriggerStartValue != null)
                        LegionRightTriggerStartValue.Text = "0%";
                }
                if (LegionRightTriggerEndSlider != null)
                {
                    LegionRightTriggerEndSlider.Value = 0;
                    if (LegionRightTriggerEndValue != null)
                        LegionRightTriggerEndValue.Text = "0%";
                }
            }

            // Enable/disable sliders based on hair triggers state
            UpdateTriggerSlidersEnabled(!enabled);

            Logger.Info($"Hair Triggers toggled: {enabled}");

            // Save the profile
            ControllerSettingChanged(sender, e);
        }

        private void UpdateTriggerSlidersEnabled(bool enabled)
        {
            if (LegionLeftTriggerStartSlider != null)
                LegionLeftTriggerStartSlider.IsEnabled = enabled;
            if (LegionLeftTriggerEndSlider != null)
                LegionLeftTriggerEndSlider.IsEnabled = enabled;
            if (LegionRightTriggerStartSlider != null)
                LegionRightTriggerStartSlider.IsEnabled = enabled;
            if (LegionRightTriggerEndSlider != null)
                LegionRightTriggerEndSlider.IsEnabled = enabled;
        }

    }
}
