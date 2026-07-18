using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>Property for Legion Desktop button remap (JSON ButtonMapping format).</summary>
    internal class LegionButtonDesktopProperty : LegionButtonMappingProperty
    {
        public LegionButtonDesktopProperty(ComboBox inUI, Page inOwner)
            : base(Function.LegionButtonDesktop, inUI, inOwner) { }
    }
}
