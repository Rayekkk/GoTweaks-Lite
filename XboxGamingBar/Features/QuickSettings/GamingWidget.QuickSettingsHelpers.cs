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
using XboxGamingBar.IPC;
using Shared.Enums;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private void StartScreenSaverCountdown()
        {
            if (screenSaverCountdownTimer == null)
            {
                screenSaverCountdownTimer = new DispatcherTimer();
                screenSaverCountdownTimer.Interval = TimeSpan.FromSeconds(1);
                screenSaverCountdownTimer.Tick += ScreenSaverCountdownTimer_Tick;
            }
            screenSaverCountdownTimer.Start();
            UpdateScreenSaverTileCountdown();
        }

        private void StopScreenSaverCountdown()
        {
            screenSaverCountdownTimer?.Stop();
            UpdateQuickSettingsTileStates();
        }

        private void ScreenSaverCountdownTimer_Tick(object sender, object e)
        {
            if (!screenSaverEnabled)
            {
                screenSaverCountdownTimer?.Stop();
                return;
            }
            UpdateScreenSaverTileCountdown();
        }

        private void UpdateScreenSaverTileCountdown()
        {
            if (qsTileMap == null || !qsTileMap.TryGetValue("ScreenSaver", out var tile) || tile.StateText == null)
                return;

            try
            {
                var lastInput = new LASTINPUTINFO();
                lastInput.cbSize = (uint)Marshal.SizeOf(lastInput);

                if (GetLastInputInfo(ref lastInput))
                {
                    uint idleMs = (uint)Environment.TickCount - lastInput.dwTime;
                    int remaining = Math.Max(0, ScreenSaverTimeoutSeconds - (int)(idleMs / 1000));
                    tile.StateText.Text = $"{remaining}s";
                }
            }
            catch
            {
                tile.StateText.Text = $"{ScreenSaverTimeoutSeconds}s";
            }
        }

    }
}
