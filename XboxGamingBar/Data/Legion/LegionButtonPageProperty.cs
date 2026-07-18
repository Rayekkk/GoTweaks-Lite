using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>Property for Legion Page button remap (JSON ButtonMapping format).</summary>
    internal class LegionButtonPageProperty : LegionButtonMappingProperty
    {
        public LegionButtonPageProperty(ComboBox inUI, Page inOwner)
            : base(Function.LegionButtonPage, inUI, inOwner) { }
    }
}
