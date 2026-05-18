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

        // Last tag the USER actually picked (via click, Space/Enter, gamepad A, D-pad/arrow
        // nav within the bar, LT/RT trigger nav, or a programmatic IsChecked from code we
        // know to be user-driven like the QuickDriverUpdatesTile shortcut). Used to revert
        // focus-restoration-driven Checked events that otherwise change the tab when the
        // widget regains window focus and UWP RadioButton group's selection-follows-focus
        // hijacks the previously-checked state. Default matches GamingWidget_Loaded's
        // initial QuickNavItem.IsChecked=true so the very first NavRadioButton_Checked
        // is treated as the user-intended starting tab, not a drift.
        private string lastUserNavTag = "Quick";
        // Monotonic timestamp of the most-recent user-intent signal on a nav RadioButton.
        // A Checked event fired without a recent matching signal is treated as focus drift.
        private long lastUserNavInteractionTicks = DateTime.UtcNow.Ticks;
        // Widening this beyond the Checked event's dispatch latency would let a single
        // user click bleed into accepting subsequent focus drifts. 350 ms is empirically
        // enough cover for the Loaded→Checked round-trip and Pointer→Checked latency on
        // Game Bar's overlay-host while staying tight enough to reject focus restoration
        // that happens 1–2 s after the window regains focus.
        private static readonly TimeSpan UserNavInteractionWindow = TimeSpan.FromMilliseconds(350);

        /// <summary>
        /// Mark a user-intent signal on the nav bar. Call from any code path that
        /// legitimately changes the active tab so the NavRadioButton_Checked guard
        /// accepts the resulting Checked event instead of reverting it as focus drift.
        /// </summary>
        internal void MarkUserNavInteraction()
        {
            lastUserNavInteractionTicks = DateTime.UtcNow.Ticks;
        }

        /// <summary>
        /// Wired once at widget init. Adds Pointer/Tap/KeyDown listeners on every nav
        /// RadioButton so any genuine user-driven activation marks the user-intent
        /// timestamp before the resulting Checked event fires.
        /// </summary>
        internal void AttachNavInteractionTracking()
        {
            foreach (var child in MainNavPanel.Children)
            {
                if (child is RadioButton rb)
                {
                    rb.PointerPressed += (s, e) => MarkUserNavInteraction();
                    rb.Tapped += (s, e) => MarkUserNavInteraction();
                    rb.KeyDown += NavRadio_KeyDown_TrackIntent;
                }
            }
        }

        private void NavRadio_KeyDown_TrackIntent(object sender, KeyRoutedEventArgs e)
        {
            // Space, Enter, and the gamepad A button are the explicit "click" keys
            // for a RadioButton in WinUI. Arrow keys / D-pad / left-stick directional
            // input are how the user navigates WITHIN the nav bar — UWP's RadioButton
            // group selection-follows-focus reacts to them on purpose, so we want to
            // accept those Checked events as user-driven and not revert them.
            switch (e.Key)
            {
                case VirtualKey.Space:
                case VirtualKey.Enter:
                case VirtualKey.GamepadA:
                case VirtualKey.Left:
                case VirtualKey.Right:
                case VirtualKey.Up:
                case VirtualKey.Down:
                case VirtualKey.GamepadDPadLeft:
                case VirtualKey.GamepadDPadRight:
                case VirtualKey.GamepadDPadUp:
                case VirtualKey.GamepadDPadDown:
                case VirtualKey.GamepadLeftThumbstickLeft:
                case VirtualKey.GamepadLeftThumbstickRight:
                case VirtualKey.GamepadLeftThumbstickUp:
                case VirtualKey.GamepadLeftThumbstickDown:
                    MarkUserNavInteraction();
                    break;
            }
        }

        private void NavRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton selectedItem)
            {
                string tag = selectedItem.Tag?.ToString() ?? "";

                // Focus-drift suppression. When the widget's window regains focus, UWP
                // can land focus on a nav RadioButton other than the currently-checked
                // one — and the RadioButton group's selection-follows-focus default then
                // auto-Checks it, firing this handler with no user input. Reject those
                // Checked events: if the tag differs from the last user-selected tag AND
                // no user-intent signal arrived in the recent past, revert by re-checking
                // the original tag's button.
                bool recentUserIntent = (DateTime.UtcNow - new DateTime(lastUserNavInteractionTicks, DateTimeKind.Utc))
                                          < UserNavInteractionWindow;
                if (!recentUserIntent && tag != lastUserNavTag)
                {
                    Logger.Info($"Nav drift suppressed: focus-driven Checked='{tag}' but no recent user intent — reverting to '{lastUserNavTag}'");
                    var revert = MainNavPanel.Children.OfType<RadioButton>()
                                              .FirstOrDefault(rb => string.Equals((rb.Tag as string) ?? string.Empty, lastUserNavTag, StringComparison.Ordinal));
                    if (revert != null && revert.IsChecked != true)
                    {
                        // Re-checking will fire NavRadioButton_Checked again with the
                        // original tag matching lastUserNavTag, so the guard passes and
                        // the content stays where the user left it.
                        revert.IsChecked = true;
                    }
                    return;
                }
                lastUserNavTag = tag;

                // Hide all sections
                QuickSettingsScrollViewer.Visibility = Visibility.Collapsed;
                PerformanceScrollViewer.Visibility = Visibility.Collapsed;
                GameScrollViewer.Visibility = Visibility.Collapsed;
                AMDScrollViewer.Visibility = Visibility.Collapsed;
                ScalingScrollViewer.Visibility = Visibility.Collapsed;
                LegionScrollViewer.Visibility = Visibility.Collapsed;
                GPDScrollViewer.Visibility = Visibility.Collapsed;
                SystemScrollViewer.Visibility = Visibility.Collapsed;

                // Stop fan curve updates when leaving Legion tab (will be re-enabled if Legion is selected)
                legionFanCurveVisible?.SetVisible(false);

                // Stop DAService status polling when leaving Legion tab
                daServiceStatusTimer?.Stop();

                // Show selected section and scroll to top
                switch (tag)
                {
                    case "Quick":
                        QuickSettingsScrollViewer.Visibility = Visibility.Visible;
                        QuickSettingsScrollViewer.ChangeView(null, 0, null, true);
                        UpdateQuickSettingsTileStates();
                        break;
                    case "Performance":
                        PerformanceScrollViewer.Visibility = Visibility.Visible;
                        PerformanceScrollViewer.ChangeView(null, 0, null, true);
                        break;
                    case "Game":
                        GameScrollViewer.Visibility = Visibility.Visible;
                        GameScrollViewer.ChangeView(null, 0, null, true);
                        break;
                    case "AMD":
                        AMDScrollViewer.Visibility = Visibility.Visible;
                        AMDScrollViewer.ChangeView(null, 0, null, true);
                        break;
                    case "Scaling":
                        ScalingScrollViewer.Visibility = Visibility.Visible;
                        ScalingScrollViewer.ChangeView(null, 0, null, true);
                        UpdateLosslessScalingStatus();
                        break;
                    case "Legion":
                        LegionScrollViewer.Visibility = Visibility.Visible;
                        LegionScrollViewer.ChangeView(null, 0, null, true);
                        // Update fan curve visibility when switching to Legion tab
                        legionFanCurveVisible?.SetVisible(isFanCurveExpanded);
                        // Start DAService status polling when on Legion tab
                        if (daServiceStatusTimer != null)
                        {
                            UpdateDAServiceStatus(); // Immediate update
                            daServiceStatusTimer.Start();
                        }
                        // Request ViGEmBus status for button remap section
                        RequestViGEmBusStatus();
                        // Force remap UI refresh when Legion tab becomes active.
                        RefreshLegionEnhancedRemapUi();
                        break;
                    case "GPD":
                        GPDScrollViewer.Visibility = Visibility.Visible;
                        GPDScrollViewer.ChangeView(null, 0, null, true);
                        break;
                    case "System":
                        SystemScrollViewer.Visibility = Visibility.Visible;
                        SystemScrollViewer.ChangeView(null, 0, null, true);
                        RequestControllerEmulationDriverStatus();
                        break;
                }

                // Re-apply theme to newly visible tab (StaticResources don't update dynamically)
                // Defer with delay to ensure visual tree is fully loaded
                if (currentThemeName != "Default")
                {
                    _ = ApplyThemeToCurrentTabAsync();
                }
            }
        }

        private async Task ApplyThemeToCurrentTabAsync()
        {
            // Wait for visual tree to fully load
            await Task.Delay(50);
            ApplyThemeToCurrentTab();
        }

        private void ApplyThemeToCurrentTab()
        {
            if (!WidgetThemes.TryGetValue(currentThemeName, out var theme)) return;

            var cardBgBrush = new SolidColorBrush(theme.CardBackground);
            var cardBorderBrush = new SolidColorBrush(theme.CardBorder);
            var accentBrush = new SolidColorBrush(theme.AccentColor);
            var textSecondaryBrush = new SolidColorBrush(theme.TextSecondary);

            // Apply to all scroll viewers (only visible ones will have loaded content)
            ApplyThemeToVisualTree(QuickSettingsScrollViewer, theme, cardBgBrush, cardBorderBrush, accentBrush, textSecondaryBrush);
            ApplyThemeToVisualTree(PerformanceScrollViewer, theme, cardBgBrush, cardBorderBrush, accentBrush, textSecondaryBrush);
            ApplyThemeToVisualTree(GameScrollViewer, theme, cardBgBrush, cardBorderBrush, accentBrush, textSecondaryBrush);
            ApplyThemeToVisualTree(AMDScrollViewer, theme, cardBgBrush, cardBorderBrush, accentBrush, textSecondaryBrush);
            ApplyThemeToVisualTree(ScalingScrollViewer, theme, cardBgBrush, cardBorderBrush, accentBrush, textSecondaryBrush);
            ApplyThemeToVisualTree(LegionScrollViewer, theme, cardBgBrush, cardBorderBrush, accentBrush, textSecondaryBrush);
            ApplyThemeToVisualTree(SystemScrollViewer, theme, cardBgBrush, cardBorderBrush, accentBrush, textSecondaryBrush);
        }

        // Trigger-press edge tracking for tab navigation. Holding LT/RT would otherwise
        // auto-repeat (WasKeyDown=true) and cycle tabs continuously. We require the user
        // to release the trigger before accepting another press, and also apply a small
        // minimum interval as a belt-and-suspenders debounce.
        private bool ltTriggerHeld;
        private bool rtTriggerHeld;
        private DateTime lastTriggerNavigateUtc = DateTime.MinValue;
        private static readonly TimeSpan TriggerNavigateDebounce = TimeSpan.FromMilliseconds(150);

        private void GamingWidget_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            // Handle LT (Left Trigger) and RT (Right Trigger) for tab navigation.
            // Using PreviewKeyDown to intercept before ScrollViewer handles it.
            // Skip when the key is auto-repeating while still held — only the initial
            // press-edge should advance a tab. One press == one tab.
            if (e.Key == VirtualKey.GamepadLeftTrigger)
            {
                if (!ltTriggerHeld && !e.KeyStatus.WasKeyDown
                    && (DateTime.UtcNow - lastTriggerNavigateUtc) >= TriggerNavigateDebounce)
                {
                    ltTriggerHeld = true;
                    lastTriggerNavigateUtc = DateTime.UtcNow;
                    NavigateToPreviousTab();
                }
                e.Handled = true;
                return;
            }
            else if (e.Key == VirtualKey.GamepadRightTrigger)
            {
                if (!rtTriggerHeld && !e.KeyStatus.WasKeyDown
                    && (DateTime.UtcNow - lastTriggerNavigateUtc) >= TriggerNavigateDebounce)
                {
                    rtTriggerHeld = true;
                    lastTriggerNavigateUtc = DateTime.UtcNow;
                    NavigateToNextTab();
                }
                e.Handled = true;
                return;
            }
            // Handle D-pad down from nav items to focus content area (for overflow menu items)
            else if (e.Key == VirtualKey.GamepadDPadDown)
            {
                var focusedElement = FocusManager.GetFocusedElement() as FrameworkElement;
                // Check if focus is on a nav RadioButton or within the nav area
                if (focusedElement != null && IsInNavigationArea(focusedElement))
                {
                    // Mark as handled immediately to prevent default XY navigation
                    e.Handled = true;

                    // Use TryMoveFocus to move to the first focusable element downward
                    FocusManager.TryMoveFocus(FocusNavigationDirection.Down);
                }
            }
        }

        private void GamingWidget_PreviewKeyUp(object sender, KeyRoutedEventArgs e)
        {
            // Clear the press-edge state so the next LT/RT press advances exactly one
            // tab. Without this, auto-repeat during a held trigger would cycle through
            // the entire tab strip.
            if (e.Key == VirtualKey.GamepadLeftTrigger)
            {
                ltTriggerHeld = false;
            }
            else if (e.Key == VirtualKey.GamepadRightTrigger)
            {
                rtTriggerHeld = false;
            }
        }

        /// <summary>
        /// Forcibly clears any held LT/RT press-edge state. Called when the widget gains
        /// focus or when VIIPER/controller emulation toggles. HidHide CyclePort on the
        /// physical pad during emulation setup can leave the OS believing RT/LT is
        /// stuck-down (no KeyUp arrives because the device disappeared between events),
        /// which would otherwise leave tab nav wedged until the user gets a fresh KeyUp.
        /// Resetting here lets the very next physical press act as a clean press-edge.
        /// </summary>
        internal void ResetTriggerTabNavState()
        {
            if (ltTriggerHeld || rtTriggerHeld)
            {
                Logger.Info("Clearing stuck LT/RT tab-nav state (focus/emulation transition)");
            }
            ltTriggerHeld = false;
            rtTriggerHeld = false;
            lastTriggerNavigateUtc = DateTime.MinValue;
        }

        private bool IsInNavigationArea(FrameworkElement element)
        {
            // Check if any of our nav items has focus
            // This works regardless of whether the item is in the main bar or overflow menu
            if (QuickNavItem.FocusState != FocusState.Unfocused) return true;
            if (PerformanceNavItem.FocusState != FocusState.Unfocused) return true;
            if (ProfilesNavItem.FocusState != FocusState.Unfocused) return true;
            if (GraphicsNavItem.FocusState != FocusState.Unfocused) return true;
            if (ScalingNavItem.FocusState != FocusState.Unfocused) return true;
            if (LegionNavItem.FocusState != FocusState.Unfocused) return true;
            if (GPDNavItem.FocusState != FocusState.Unfocused) return true;
            if (SystemNavItem.FocusState != FocusState.Unfocused) return true;

            // Fallback: walk visual tree for other nav-related elements
            var current = element;
            while (current != null)
            {
                // Check if we're in the nav panel
                if (current == MainNavPanel)
                    return true;
                current = VisualTreeHelper.GetParent(current) as FrameworkElement;
            }
            return false;
        }

        private void NavigateToPreviousTab()
        {
            var visibleItems = GetVisibleNavigationItems();
            if (visibleItems.Count == 0) return;

            // Find currently checked item
            var currentItem = visibleItems.FirstOrDefault(rb => rb.IsChecked == true);
            int currentIndex = currentItem != null ? visibleItems.IndexOf(currentItem) : 0;

            // LT trigger nav is explicit user intent — mark before IsChecked= so the
            // resulting NavRadioButton_Checked guard accepts the tab change.
            MarkUserNavInteraction();
            if (currentIndex > 0)
            {
                visibleItems[currentIndex - 1].IsChecked = true;
            }
            else
            {
                // Wrap around to last tab
                visibleItems[visibleItems.Count - 1].IsChecked = true;
            }
        }

        private void NavigateToNextTab()
        {
            var visibleItems = GetVisibleNavigationItems();
            if (visibleItems.Count == 0) return;

            // Find currently checked item
            var currentItem = visibleItems.FirstOrDefault(rb => rb.IsChecked == true);
            int currentIndex = currentItem != null ? visibleItems.IndexOf(currentItem) : 0;

            MarkUserNavInteraction();
            if (currentIndex < visibleItems.Count - 1)
            {
                visibleItems[currentIndex + 1].IsChecked = true;
            }
            else
            {
                // Wrap around to first tab
                visibleItems[0].IsChecked = true;
            }
        }

        private List<RadioButton> GetVisibleNavigationItems()
        {
            var visibleItems = new List<RadioButton>();
            foreach (var item in MainNavPanel.Children)
            {
                if (item is RadioButton radioButton && radioButton.Visibility == Visibility.Visible)
                {
                    visibleItems.Add(radioButton);
                }
            }
            return visibleItems;
        }

    }
}
