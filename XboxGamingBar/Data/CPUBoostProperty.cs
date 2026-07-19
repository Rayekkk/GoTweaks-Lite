using System;
using Shared.Enums;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    internal class CPUBoostProperty : WidgetToggleProperty
    {
        public CPUBoostProperty(ToggleSwitch inUI, Page inOwner) : base(false, Function.CPUBoost, inUI, inOwner)
        {
        }

        // Profile edits use the explicit SetProfileField intent. Do not let the generic
        // bound-control path apply/send an optimistic value before helper validation.
        protected override void ToggleSwitch_ValueChanged(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
        }
    }
}
