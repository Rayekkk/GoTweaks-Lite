using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>Property for Legion Y2 button remap (JSON ButtonMapping format).</summary>
    internal class LegionButtonY2Property : LegionButtonMappingProperty
    {
        public LegionButtonY2Property(ComboBox inUI, Page inOwner)
            : base(Function.LegionButtonY2, inUI, inOwner) { }
    }
}
