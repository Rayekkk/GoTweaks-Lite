using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Widget toggle for Auto SDR white-level matching (Go2HDR integration). The helper is
    /// authoritative for the persisted state, so this just reflects/sends the bool; flipping
    /// the switch sends a Set, and the helper's persisted value syncs back into the UI on
    /// startup via BatchGet.
    /// </summary>
    internal class AutoSdrEnabledProperty : WidgetToggleProperty
    {
        public AutoSdrEnabledProperty(ToggleSwitch inUI, Page inOwner)
            : base(false, Function.AutoSdrEnabled, inUI, inOwner)
        {
        }
    }
}
