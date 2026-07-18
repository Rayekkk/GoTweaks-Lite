using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Minimum CPU State percentage (0-100).
    ///
    /// [2.0 rebuild - Faza C-CPU] Headless (no bound control) - see MaxCPUStateProperty's comment.
    /// </summary>
    internal class MinCPUStateProperty : WidgetProperty<int>
    {
        private readonly GamingWidget owner;

        public MinCPUStateProperty(GamingWidget inOwner) : base(5, null, Function.MinCPUState)
        {
            owner = inOwner;
        }

        protected override void OnValueSyncedFromHelper()
        {
            if (owner == null) return;
            int value = Value;
            var ignore = owner.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
                () => owner.ApplyCPUStateFromHelper(isMax: false, value));
        }
    }
}
