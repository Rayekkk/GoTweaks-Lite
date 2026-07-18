using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>Property for Legion M3 button remap (JSON ButtonMapping format).</summary>
    internal class LegionButtonM3Property : LegionButtonMappingProperty
    {
        public LegionButtonM3Property(ComboBox inUI, Page inOwner)
            : base(Function.LegionButtonM3, inUI, inOwner) { }
    }
}
