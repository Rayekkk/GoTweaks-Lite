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
        private async void PowerManager_PowerSourceChanged(object sender, object e)
        {
            if (isUnloading) return;

            // Small delay to allow system to update power status
            await System.Threading.Tasks.Task.Delay(100);

            if (isUnloading) return;

            // Update the active profile indicator when power source changes
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (isUnloading) return;

                var batteryStatus = PowerManager.BatteryStatus;
                var powerSupplyStatus = PowerManager.PowerSupplyStatus;

                Logger.Info($"Power source event - Battery: {batteryStatus}, PowerSupply: {powerSupplyStatus}");

                UpdateActiveProfileIndicator();

                // [2.0 rebuild - widget-pure-display] This used to schedule a 5-second-delayed
                // generic "ReapplyTDP" pipe poke here (in Custom mode, or when a Power Source
                // Profile split is enabled) as a widget-side safety net for "the hardware might not
                // have settled yet". That's now fully redundant: Program.PowerSourceHandler.cs
                // (helper-side) already reapplies the correct Custom TDP triplet - and the other
                // AC/DC-mirrored fields (CPUBoost/CPUEPP/CPUState/OSPowerMode/FPSLimit) - IMMEDIATELY
                // and correctly on this same OS event, independently of widget lifecycle. A widget
                // timer nudging the helper to redo its own job on its own schedule is exactly the
                // "widget doing something on its own" pattern the 2.0 architecture rules out (see
                // memory: widget-pure-display-principle). Removed entirely, along with
                // SchedulePowerSourceTdpReapply/powerSourceTdpReapplyTimer.
            });
        }

    }
}
