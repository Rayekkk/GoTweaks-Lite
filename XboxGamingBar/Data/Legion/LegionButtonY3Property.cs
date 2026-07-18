using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>Property for Legion Y3 button remap (JSON ButtonMapping format).</summary>
    internal class LegionButtonY3Property : LegionButtonMappingProperty
    {
        public LegionButtonY3Property(ComboBox inUI, Page inOwner)
            : base(Function.LegionButtonY3, inUI, inOwner) { }
    }
}
