using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Joy-Con Pair gyro routing. When on, each emulated Joy-Con half is driven by
    /// its matching physical controller's IMU (left half ← left controller, right
    /// half ← right controller). When off (default), both halves share the selected
    /// gyro source. Only relevant for the Joy-Con Pair target.
    /// </summary>
    internal class ViiperJoyconGyroPerHalfProperty : WidgetToggleProperty
    {
        public ViiperJoyconGyroPerHalfProperty(ToggleSwitch inUI, Page inOwner)
            : base(false, Function.Viiper_JoyconGyroPerHalf, inUI, inOwner)
        {
        }
    }
}
