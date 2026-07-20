using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for FPS Limit (RTSS). 0 = unlimited.
    ///
    /// [full-audit fix, 2026-07-20 — B8] Headless (no bound control). Without OnValueSyncedFromHelper
    /// a live Function.FPSLimit push from the helper (e.g. a per-game profile applied on game launch)
    /// updated only Value and the Quick tile, never the Performance-tab FPSLimitToggle/Slider - so
    /// those showed a stale state and the user's next interaction sent an intent computed from it.
    /// This reflects the pushed value into those controls via GamingWidget.ReflectFPSLimitFromHelper.
    /// </summary>
    internal class FPSLimitProperty : WidgetProperty<int>
    {
        private readonly GamingWidget owner;

        public FPSLimitProperty(GamingWidget inOwner = null) : base(0, null, Function.FPSLimit)
        {
            owner = inOwner;
        }

        protected override void OnValueSyncedFromHelper()
        {
            owner?.ReflectFPSLimitFromHelper();
        }
    }
}
