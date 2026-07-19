using NLog;
using Shared.Enums;
using System;
using System.Collections.Generic;
using XboxGamingBarHelper.Power;

namespace XboxGamingBarHelper
{
    /// <summary>
    /// Helper-side AC/DC transition handling. The widget is UWP and stops receiving
    /// PowerManager.PowerSourceChanged callbacks while Game Bar is dismissed, so any
    /// AC↔DC transition that happens between Game Bar sessions used to be dropped
    /// (issue #72). The helper mirrors the three widget settings that drive its power-plan
    /// auto-switch decision, subscribes to SystemManager.PowerSourceChanged, and does the
    /// same work the widget would have done — independent of widget lifecycle.
    /// </summary>
    internal partial class Program
    {
        // [2.0 rebuild - AC/DC persistence] Slimmed to ONLY OSPowerMode. Every other field this
        // used to cache ephemerally (TDP triplet, CPUBoost/EPP/CPUState, FPSLimit) is now
        // persisted directly into the active GameProfile/GlobalProfile's base + _DC fields
        // (see ApplyPowerSourceProfileValues below) - real, durable storage instead of a
        // static in-memory cache lost on every helper restart. OSPowerMode stays ephemeral: it's
        // explicitly out of scope for the persistence migration (a direct Windows-OS-setting
        // mirror, not something needing helper-authoritative treatment).
        private static class PowerSourceProfileState
        {
            public static int? AcOsPowerMode = null;
            public static int? DcOsPowerMode = null;
        }

        // Resolves a DC-side value against its AC counterpart using the "null when equal"
        // convention already established by GameProfile's existing _DC fields: an explicit
        // override is only stored when the DC value genuinely differs from AC, so an unconfigured
        // _DC field means "use the AC value on battery too", not "explicitly forced equal".
        private static int? ResolveDcOverride(int? dc, int ac) => (dc.HasValue && dc.Value != ac) ? dc : (int?)null;
        private static bool? ResolveDcOverride(bool? dc, bool ac) => (dc.HasValue && dc.Value != ac) ? dc : (bool?)null;
        private static string ResolveDcOverride(string dc, string ac) => (!string.IsNullOrEmpty(dc) && dc != ac) ? dc : null;

        // Tracks last observed isOnAC so we can skip the (relatively expensive) plan-switch
        // and TDP-reapply work when SystemManager fires PowerSourceChanged for a status
        // transition that doesn't actually cross the AC/DC boundary. Real-world example
        // (Diego's box, build 2068): a flaky or underspec USB-C charger produces a torrent
        // of Inadequate ↔ Adequate transitions — both map to isOnAC=true, so re-pushing
        // TDP on each is wasted work and could fight the hardware. null on first call so
        // the first real transition always fires (initial seeding is done in SystemManager).
        private static bool? _lastIsOnAC;

        // [2.0 rebuild - AC/DC live-edit fix] Lets any live single-field save handler
        // (TDP_PropertyChanged, the AMD/CPU-cluster *_PropertyChanged handlers, etc.) find out
        // which power state to persist a live edit under - defaults to AC (true) when no real
        // transition has fired yet, matching the pre-existing "base field = AC" convention so an
        // early edit before the first PowerSourceChanged event lands where it always used to.
        internal static bool IsCurrentlyOnAC => _lastIsOnAC ?? true;

        /// <summary>
        /// Persists the widget's per-state (AC/DC) values into the currently-targeted profile's
        /// base + _DC fields (real, durable GameProfile/GlobalProfile storage - see
        /// ResolveDcOverride's doc comment for the null-means-no-override convention). Widget
        /// calls this whenever the active profile or its AC/DC sub-profile changes, or (debounced)
        /// after a live edit. OSPowerMode is the one exception, staying in the ephemeral
        /// PowerSourceProfileState cache (out of scope for persistence, see that class's comment).
        /// Each field group is only written when its AC-side key is present in the incoming JSON -
        /// a partial/legacy payload must never null out an already-persisted setting.
        /// </summary>
        internal static void ApplyPowerSourceProfileValues(string configJson)
        {
            try
            {
                var cfg = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(configJson);
                if (cfg == null) return;

                // 2.0 profile-edit contract.  Unlike the legacy AC/DC blob, this is an
                // explicit user intent: one field, one target profile, one power-state scope.
                // Keep the legacy parser below temporarily for older widgets during migration.
                if (cfg.TryGetValue("Intent", out var intent)
                    && intent.ValueKind == System.Text.Json.JsonValueKind.String
                    && intent.GetString() == "SetProfileField")
                {
                    ApplyProfileFieldIntent(cfg, out _);
                    return;
                }

                int? ParseInt(string key)
                {
                    if (!cfg.TryGetValue(key, out var el)) return null;
                    if (el.ValueKind == System.Text.Json.JsonValueKind.Null) return null;
                    if (el.ValueKind == System.Text.Json.JsonValueKind.Number && el.TryGetInt32(out int v)) return v;
                    return null;
                }
                bool? ParseBool(string key)
                {
                    if (!cfg.TryGetValue(key, out var el)) return null;
                    if (el.ValueKind == System.Text.Json.JsonValueKind.True) return true;
                    if (el.ValueKind == System.Text.Json.JsonValueKind.False) return false;
                    return null;
                }
                string ParseString(string key)
                {
                    if (!cfg.TryGetValue(key, out var el)) return null;
                    if (el.ValueKind != System.Text.Json.JsonValueKind.String) return null;
                    string s = el.GetString();
                    return string.IsNullOrEmpty(s) ? null : s;
                }

                // OSPowerMode: unchanged, stays ephemeral (out of scope for persistence).
                PowerSourceProfileState.AcOsPowerMode = ParseInt("AcOsPowerMode");
                PowerSourceProfileState.DcOsPowerMode = ParseInt("DcOsPowerMode");

                // LegionPerformanceMode (TDP Mode dropdown) - gated on the same TDP flag as the
                // triplet below, since it's the mode selector for the same feature.
                int? acMode = ParseInt("AcLegionPerformanceMode");
                if (acMode.HasValue)
                {
                    int? modeDcOverride = ResolveDcOverride(ParseInt("DcLegionPerformanceMode"), acMode.Value);
                    RouteProfileSave(ProfileSaveFlagsState.TDP, "PowerSourceProfileValues:LegionPerformanceMode",
                        cur => { cur.LegionPerformanceMode = acMode.Value; cur.LegionPerformanceMode_DC = modeDcOverride; },
                        glo => { glo.LegionPerformanceMode = acMode.Value; glo.LegionPerformanceMode_DC = modeDcOverride; });
                }

                // TDP triplet.
                int? acTdp = ParseInt("AcTdp");
                if (acTdp.HasValue)
                {
                    int? dcTdp = ParseInt("DcTdp");
                    int acFast = ParseInt("AcTdpFast") ?? acTdp.Value;
                    int? dcFast = ParseInt("DcTdpFast");
                    int acPeak = ParseInt("AcTdpPeak") ?? acTdp.Value;
                    int? dcPeak = ParseInt("DcTdpPeak");
                    int? tdpDcOverride = ResolveDcOverride(dcTdp, acTdp.Value);
                    int? fastDcOverride = ResolveDcOverride(dcFast, acFast);
                    int? peakDcOverride = ResolveDcOverride(dcPeak, acPeak);
                    RouteProfileSave(ProfileSaveFlagsState.TDP, "PowerSourceProfileValues:TDP",
                        cur => { cur.TDP = acTdp.Value; cur.TDP_DC = tdpDcOverride; cur.TDPFast = acFast; cur.TDPFast_DC = fastDcOverride; cur.TDPPeak = acPeak; cur.TDPPeak_DC = peakDcOverride; },
                        glo => { glo.TDP = acTdp.Value; glo.TDP_DC = tdpDcOverride; glo.TDPFast = acFast; glo.TDPFast_DC = fastDcOverride; glo.TDPPeak = acPeak; glo.TDPPeak_DC = peakDcOverride; });
                }

                // CPUBoost.
                bool? acCpuBoost = ParseBool("AcCpuBoost");
                if (acCpuBoost.HasValue)
                {
                    bool? dcOverride = ResolveDcOverride(ParseBool("DcCpuBoost"), acCpuBoost.Value);
                    RouteProfileSave(ProfileSaveFlagsState.CPUBoost, "PowerSourceProfileValues:CPUBoost",
                        cur => { cur.CPUBoost = acCpuBoost.Value; cur.CPUBoost_DC = dcOverride; },
                        glo => { glo.CPUBoost = acCpuBoost.Value; glo.CPUBoost_DC = dcOverride; });
                }

                // CPUEPP.
                int? acCpuEpp = ParseInt("AcCpuEpp");
                if (acCpuEpp.HasValue)
                {
                    int? dcOverride = ResolveDcOverride(ParseInt("DcCpuEpp"), acCpuEpp.Value);
                    RouteProfileSave(ProfileSaveFlagsState.CPUEPP, "PowerSourceProfileValues:CPUEPP",
                        cur => { cur.CPUEPP = acCpuEpp.Value; cur.CPUEPP_DC = dcOverride; },
                        glo => { glo.CPUEPP = acCpuEpp.Value; glo.CPUEPP_DC = dcOverride; });
                }

                // CPU state (Max + Min together, one flag).
                int? acMaxCpuState = ParseInt("AcMaxCpuState");
                int? acMinCpuState = ParseInt("AcMinCpuState");
                if (acMaxCpuState.HasValue || acMinCpuState.HasValue)
                {
                    int? maxOverride = acMaxCpuState.HasValue ? ResolveDcOverride(ParseInt("DcMaxCpuState"), acMaxCpuState.Value) : null;
                    int? minOverride = acMinCpuState.HasValue ? ResolveDcOverride(ParseInt("DcMinCpuState"), acMinCpuState.Value) : null;
                    RouteProfileSave(ProfileSaveFlagsState.CPUState, "PowerSourceProfileValues:CPUState",
                        cur =>
                        {
                            if (acMaxCpuState.HasValue) { cur.MaxCPUState = acMaxCpuState.Value; cur.MaxCPUState_DC = maxOverride; }
                            if (acMinCpuState.HasValue) { cur.MinCPUState = acMinCpuState.Value; cur.MinCPUState_DC = minOverride; }
                        },
                        glo =>
                        {
                            if (acMaxCpuState.HasValue) { glo.MaxCPUState = acMaxCpuState.Value; glo.MaxCPUState_DC = maxOverride; }
                            if (acMinCpuState.HasValue) { glo.MinCPUState = acMinCpuState.Value; glo.MinCPUState_DC = minOverride; }
                        });
                }

                // FPSLimit (0 = off, matches the wire's existing collapse of Enabled+Value).
                int? acFpsLimit = ParseInt("AcFpsLimit");
                if (acFpsLimit.HasValue)
                {
                    int? dcOverride = ResolveDcOverride(ParseInt("DcFpsLimit"), acFpsLimit.Value);
                    RouteProfileSave(ProfileSaveFlagsState.FPSLimit, "PowerSourceProfileValues:FPSLimit",
                        cur => { cur.FPSLimit = acFpsLimit.Value; cur.FPSLimit_DC = dcOverride; },
                        glo => { glo.FPSLimit = acFpsLimit.Value; glo.FPSLimit_DC = dcOverride; });
                }

                // HDR.
                bool? acHdr = ParseBool("AcHdrEnabled");
                if (acHdr.HasValue)
                {
                    bool? dcOverride = ResolveDcOverride(ParseBool("DcHdrEnabled"), acHdr.Value);
                    RouteProfileSave(ProfileSaveFlagsState.HDR, "PowerSourceProfileValues:HDR",
                        cur => { cur.HDREnabled = acHdr.Value; cur.HDREnabled_DC = dcOverride; },
                        glo => { glo.HDREnabled = acHdr.Value; glo.HDREnabled_DC = dcOverride; });
                }

                // Resolution.
                string acResolution = ParseString("AcResolution");
                if (acResolution != null)
                {
                    string dcOverride = ResolveDcOverride(ParseString("DcResolution"), acResolution);
                    RouteProfileSave(ProfileSaveFlagsState.Resolution, "PowerSourceProfileValues:Resolution",
                        cur => { cur.Resolution = acResolution; cur.Resolution_DC = dcOverride; },
                        glo => { glo.Resolution = acResolution; glo.Resolution_DC = dcOverride; });
                }

                // RefreshRate.
                int? acRefreshRate = ParseInt("AcRefreshRate");
                if (acRefreshRate.HasValue)
                {
                    int? dcOverride = ResolveDcOverride(ParseInt("DcRefreshRate"), acRefreshRate.Value);
                    RouteProfileSave(ProfileSaveFlagsState.RefreshRate, "PowerSourceProfileValues:RefreshRate",
                        cur => { cur.RefreshRate = acRefreshRate.Value; cur.RefreshRate_DC = dcOverride; },
                        glo => { glo.RefreshRate = acRefreshRate.Value; glo.RefreshRate_DC = dcOverride; });
                }

                // AMD Radeon (11 fields, single ProfileSaveFlagsState.AMDFeatures flag - same
                // one-flag-many-fields shape as ApplyAMDFeaturesFromProfile/GyroSettings). Gated
                // on FluidMotionFrames being present since the widget always sends the whole group
                // together in one blob (see SendPowerSourceProfileValuesToHelper).
                bool? acFmf = ParseBool("AcFluidMotionFrames");
                if (acFmf.HasValue)
                {
                    bool? fmfDc = ResolveDcOverride(ParseBool("DcFluidMotionFrames"), acFmf.Value);
                    bool? acRsr = ParseBool("AcRadeonSuperResolution");
                    bool? rsrDc = acRsr.HasValue ? ResolveDcOverride(ParseBool("DcRadeonSuperResolution"), acRsr.Value) : null;
                    int? acRsrSharp = ParseInt("AcRadeonSuperResolutionSharpness");
                    int? rsrSharpDc = acRsrSharp.HasValue ? ResolveDcOverride(ParseInt("DcRadeonSuperResolutionSharpness"), acRsrSharp.Value) : null;
                    bool? acRis = ParseBool("AcImageSharpening");
                    bool? risDc = acRis.HasValue ? ResolveDcOverride(ParseBool("DcImageSharpening"), acRis.Value) : null;
                    int? acRisSharp = ParseInt("AcImageSharpeningSharpness");
                    int? risSharpDc = acRisSharp.HasValue ? ResolveDcOverride(ParseInt("DcImageSharpeningSharpness"), acRisSharp.Value) : null;
                    bool? acAntiLag = ParseBool("AcRadeonAntiLag");
                    bool? antiLagDc = acAntiLag.HasValue ? ResolveDcOverride(ParseBool("DcRadeonAntiLag"), acAntiLag.Value) : null;
                    bool? acBoost = ParseBool("AcRadeonBoost");
                    bool? boostDc = acBoost.HasValue ? ResolveDcOverride(ParseBool("DcRadeonBoost"), acBoost.Value) : null;
                    int? acBoostRes = ParseInt("AcRadeonBoostResolution");
                    int? boostResDc = acBoostRes.HasValue ? ResolveDcOverride(ParseInt("DcRadeonBoostResolution"), acBoostRes.Value) : null;
                    bool? acChill = ParseBool("AcRadeonChill");
                    bool? chillDc = acChill.HasValue ? ResolveDcOverride(ParseBool("DcRadeonChill"), acChill.Value) : null;
                    int? acChillMin = ParseInt("AcRadeonChillMinFPS");
                    int? chillMinDc = acChillMin.HasValue ? ResolveDcOverride(ParseInt("DcRadeonChillMinFPS"), acChillMin.Value) : null;
                    int? acChillMax = ParseInt("AcRadeonChillMaxFPS");
                    int? chillMaxDc = acChillMax.HasValue ? ResolveDcOverride(ParseInt("DcRadeonChillMaxFPS"), acChillMax.Value) : null;

                    void ApplyAmd(Profile.GameProfileProperty cur)
                    {
                        cur.FluidMotionFrames = acFmf.Value; cur.FluidMotionFrames_DC = fmfDc;
                        if (acRsr.HasValue) { cur.RadeonSuperResolution = acRsr.Value; cur.RadeonSuperResolution_DC = rsrDc; }
                        if (acRsrSharp.HasValue) { cur.RadeonSuperResolutionSharpness = acRsrSharp.Value; cur.RadeonSuperResolutionSharpness_DC = rsrSharpDc; }
                        if (acRis.HasValue) { cur.ImageSharpening = acRis.Value; cur.ImageSharpening_DC = risDc; }
                        if (acRisSharp.HasValue) { cur.ImageSharpeningSharpness = acRisSharp.Value; cur.ImageSharpeningSharpness_DC = risSharpDc; }
                        if (acAntiLag.HasValue) { cur.RadeonAntiLag = acAntiLag.Value; cur.RadeonAntiLag_DC = antiLagDc; }
                        if (acBoost.HasValue) { cur.RadeonBoost = acBoost.Value; cur.RadeonBoost_DC = boostDc; }
                        if (acBoostRes.HasValue) { cur.RadeonBoostResolution = acBoostRes.Value; cur.RadeonBoostResolution_DC = boostResDc; }
                        if (acChill.HasValue) { cur.RadeonChill = acChill.Value; cur.RadeonChill_DC = chillDc; }
                        if (acChillMin.HasValue) { cur.RadeonChillMinFPS = acChillMin.Value; cur.RadeonChillMinFPS_DC = chillMinDc; }
                        if (acChillMax.HasValue) { cur.RadeonChillMaxFPS = acChillMax.Value; cur.RadeonChillMaxFPS_DC = chillMaxDc; }
                    }
                    void ApplyAmdGlobal(Shared.Data.GameProfile glo)
                    {
                        glo.FluidMotionFrames = acFmf.Value; glo.FluidMotionFrames_DC = fmfDc;
                        if (acRsr.HasValue) { glo.RadeonSuperResolution = acRsr.Value; glo.RadeonSuperResolution_DC = rsrDc; }
                        if (acRsrSharp.HasValue) { glo.RadeonSuperResolutionSharpness = acRsrSharp.Value; glo.RadeonSuperResolutionSharpness_DC = rsrSharpDc; }
                        if (acRis.HasValue) { glo.ImageSharpening = acRis.Value; glo.ImageSharpening_DC = risDc; }
                        if (acRisSharp.HasValue) { glo.ImageSharpeningSharpness = acRisSharp.Value; glo.ImageSharpeningSharpness_DC = risSharpDc; }
                        if (acAntiLag.HasValue) { glo.RadeonAntiLag = acAntiLag.Value; glo.RadeonAntiLag_DC = antiLagDc; }
                        if (acBoost.HasValue) { glo.RadeonBoost = acBoost.Value; glo.RadeonBoost_DC = boostDc; }
                        if (acBoostRes.HasValue) { glo.RadeonBoostResolution = acBoostRes.Value; glo.RadeonBoostResolution_DC = boostResDc; }
                        if (acChill.HasValue) { glo.RadeonChill = acChill.Value; glo.RadeonChill_DC = chillDc; }
                        if (acChillMin.HasValue) { glo.RadeonChillMinFPS = acChillMin.Value; glo.RadeonChillMinFPS_DC = chillMinDc; }
                        if (acChillMax.HasValue) { glo.RadeonChillMaxFPS = acChillMax.Value; glo.RadeonChillMaxFPS_DC = chillMaxDc; }
                    }
                    RouteProfileSave(ProfileSaveFlagsState.AMDFeatures, "PowerSourceProfileValues:AMDFeatures", ApplyAmd, ApplyAmdGlobal);
                }

                Logger.Info($"Applied PowerSourceProfileValues (AC: tdp={acTdp?.ToString() ?? "-"}W, "
                    + $"cpuBoost={acCpuBoost?.ToString() ?? "-"}, epp={acCpuEpp?.ToString() ?? "-"}, "
                    + $"cpuState={acMinCpuState?.ToString() ?? "-"}-{acMaxCpuState?.ToString() ?? "-"}, "
                    + $"fpsLimit={acFpsLimit?.ToString() ?? "-"}, hdr={acHdr?.ToString() ?? "-"}, "
                    + $"resolution={acResolution ?? "-"}, refreshRate={acRefreshRate?.ToString() ?? "-"}, "
                    + $"osMode={PowerSourceProfileState.AcOsPowerMode?.ToString() ?? "-"} [ephemeral])");
            }
            catch (Exception ex)
            {
                Logger.Error($"ApplyPowerSourceProfileValues: {ex.Message}");
            }
        }

        private static bool ApplyProfileFieldIntent(Dictionary<string, System.Text.Json.JsonElement> cfg, out string reason)
        {
            reason = null;
            string field = cfg.TryGetValue("Field", out var fieldElement) ? fieldElement.GetString() : null;
            string power = cfg.TryGetValue("Power", out var powerElement) ? powerElement.GetString() : null;
            if (string.IsNullOrEmpty(field) || (power != "AC" && power != "DC")
                || !cfg.TryGetValue("Value", out var value))
            {
                reason = "missing or invalid Field, Power, or Value";
                Logger.Warn("Rejected SetProfileField: " + reason);
                return false;
            }

            var profile = profileManager?.CurrentProfile;
            if (profile == null)
            {
                reason = "profile manager not ready";
                Logger.Warn($"Rejected SetProfileField({field}): {reason}");
                return false;
            }

            bool dc = power == "DC";
            if (field == "TDP" && value.TryGetInt32(out int watts))
            {
                if (watts < 5 || watts > 50)
                {
                    reason = "TDP outside 5-50W";
                    Logger.Warn($"Rejected SetProfileField(TDP): {watts}W outside 5-50W");
                    return false;
                }
                if (dc) profile.TDP_DC = watts;
                else profile.TDP = watts;
            }
            else if (field == "CPUEPP" && value.TryGetInt32(out int epp))
            {
                if (epp < 0 || epp > 100) { reason = "CPUEPP outside 0-100"; Logger.Warn($"Rejected SetProfileField(CPUEPP): {epp}"); return false; }
                if (dc) profile.CPUEPP_DC = epp;
                else profile.CPUEPP = epp;
            }
            else if (field == "CPUBoost" && (value.ValueKind == System.Text.Json.JsonValueKind.True || value.ValueKind == System.Text.Json.JsonValueKind.False))
            {
                bool enabled = value.GetBoolean();
                if (dc) profile.CPUBoost_DC = enabled;
                else profile.CPUBoost = enabled;
            }
            else
            {
                reason = "unsupported field or value type";
                Logger.Warn($"Rejected SetProfileField: unsupported field '{field}' or value type");
                return false;
            }

            Logger.Info($"Applied SetProfileField intent: {field} ({power}) to {profile.GameId.Name}");
            return true;
        }

        private static void SystemManager_PowerSourceChanged(object sender, global::Windows.System.Power.PowerSupplyStatus newStatus)
        {
            // Match the widget's interpretation (GamingWidget.PowerSourceEvents.cs:70 and
            // GamingWidget.xaml.cs:2742, :2777): "Inadequate" means a charger is connected
            // but can't keep up with current load — common with the Legion Go's stock
            // USB-C charger under heavy draw. Treat that as AC, not battery, so we match
            // what the user expects when they have something physically plugged in.
            // Only NotPresent (truly unplugged) means DC.
            bool isOnAC = newStatus != global::Windows.System.Power.PowerSupplyStatus.NotPresent;

            // Short-circuit when isOnAC didn't actually change. SystemManager fires
            // PowerSourceChanged on every PowerSupplyStatus transition (Adequate ↔
            // Inadequate ↔ NotPresent), but our work below — power plan switch and
            // TDP reapply — only depends on the AC/DC boolean. On a flaky charger
            // (Inadequate ↔ Adequate flapping) we'd otherwise re-push TDP dozens of
            // times in a few minutes for no behavioral change.
            if (_lastIsOnAC == isOnAC)
            {
                Logger.Debug($"Helper-side AC/DC handler: status {newStatus} but isOnAC unchanged ({isOnAC}); skipping plan/TDP work");
                return;
            }
            _lastIsOnAC = isOnAC;

            // Apply per-state values from the currently-targeted profile's persisted base/_DC
            // fields (real GameProfile/GlobalProfile storage - see ApplyPowerSourceProfileValues
            // above - not the old ephemeral cache, which is now OSPowerMode-only).
            //
            // Layered gating, in order of how much they should restrict the work:
            //   - performanceManager/profileManager null → skip everything (no manager to call).
            //   - TDP-specific gate (Legion Custom mode): only blocks TDP/TDPBoost reapply.
            //     In Legion preset modes the system manages TDP itself, so pushing our
            //     value would fight the preset (and preset modes have no manual TDP).
            //   - Extended fields (CPUBoost / CPUEPP / CPUState / OSPowerMode / FPSLimit /
            //     AMD / HDR / Resolution / RefreshRate) are NOT gated by Legion mode — those
            //     settings work regardless of preset mode, and the per-property equality
            //     check below no-ops when AC and DC resolve to the same value.
            //
            // Net effect: helper-side AC/DC apply does useful work whenever the user has
            // configured ANY AC/DC differences for the active profile.
            try
            {
                if (performanceManager == null || profileManager == null)
                {
                    return;
                }

                // [bug fix, found on-device 2026-07-18: AMD/CPU-cluster settings were being
                // corrupted on every AC/DC transition] Applying a resolved value below via
                // powerManager.X.SetValue()/amdManager.X.SetValue()/systemManager.X.SetValue()
                // fires that property's own NotifyPropertyChanged - which is ALSO wired to the
                // existing live single-field save handler for that same setting (e.g.
                // RadeonAntiLag_PropertyChanged), since these are the SAME properties the user's
                // live UI edits go through. Without this guard, reapplying (say) the DC-resolved
                // RadeonAntiLag value to hardware would immediately trigger that handler to SAVE
                // whatever was just applied back into the profile's BASE (AC) field via
                // RouteProfileSave - silently overwriting the user's configured AC value with the
                // DC one on every single transition. lock+isApplyingProfile is the existing,
                // already-proven guard every other profile-apply site
                // (RunningGame_PropertyChanged / RestoreGlobalProfileSettings /
                // CurrentProfile_PropertyChanged) uses for exactly this reason - all the affected
                // single-field handlers already check it. TDP was unaffected by this bug because
                // LegionManager.SetCustomTDP updates its own cache via SetValueSilent (no
                // NotifyPropertyChanged, so no re-save loop) - everything else in this method
                // goes through a normal SetValue and needed this same protection.
                lock (profileApplicationLock)
                {
                    if (isApplyingProfile)
                    {
                        Logger.Debug("Helper-side AC/DC handler: skipping - already applying a profile elsewhere");
                        return;
                    }

                    try
                    {
                        isApplyingProfile = true;
                        ApplyPowerSourceChangeInternal(isOnAC);
                    }
                    finally
                    {
                        isApplyingProfile = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Helper-side AC/DC handler: reapply TDP threw: {ex.Message}");
            }
        }

        private static void ApplyPowerSourceChangeInternal(bool isOnAC)
        {
            try
            {
                // Reading .Value takes a STRUCT COPY of the live profile - safe to read fields
                // from freely (getters have no side effect), but must never assign into this
                // copy (GameProfile's setters call Save(), which would debounce-write a bogus
                // in-between value from a throwaway copy).
                Shared.Data.GameProfile profile = profileManager.CurrentProfile.Value;
                string state = isOnAC ? "AC" : "DC";

                // [2.0 rebuild - AC/DC persistence follow-up] LegionPerformanceMode (TDP Mode)
                // itself now switches on a real AC/DC transition too, same as every other
                // TDP/CPU/AMD field - e.g. "Custom on AC, Balanced on DC" now actually applies.
                // null means "not configured for this profile" (its own pre-existing doc comment:
                // "don't change on profile switch") - only switch when a real value is set.
                if (legionManager != null)
                {
                    int? targetMode = isOnAC ? profile.LegionPerformanceMode : (profile.LegionPerformanceMode_DC ?? profile.LegionPerformanceMode);
                    if (targetMode.HasValue && targetMode.Value != legionManager.LegionPerformanceMode.Value)
                    {
                        Logger.Info($"Helper-side AC/DC handler: switching LegionPerformanceMode to {targetMode.Value} from persisted {state} profile");
                        legionManager.LegionPerformanceMode.SetValue(targetMode.Value);
                    }
                }

                bool isLegionCustomMode = legionManager != null && legionManager.CurrentPerformanceMode == 255;

                // 2a) TDP triplet — gated by Legion Custom mode.
                if (!isLegionCustomMode)
                {
                    Logger.Debug("Helper-side AC/DC handler: skipping TDP/TDPBoost reapply — not in Legion Custom mode (extended fields below still apply)");
                }
                else
                {
                    // Resolve against the persisted profile first (isOnAC ? base : (_DC ?? base) -
                    // same convention as GameProfile.TDPFast/TDPPeak's own "fall back to SPL"
                    // getters). Only fall back further to the runtime cache / flat property if the
                    // persisted read is somehow unusable (<= 0) - a defensive safety net for a
                    // profile that was never actually configured, kept from the pre-persistence
                    // version of this method rather than removed.
                    var (cachedSlow, cachedFast, cachedPeak) = legionManager.GetCurrentTDPValues();

                    int targetTdp = isOnAC ? profile.TDP : (profile.TDP_DC ?? profile.TDP);
                    if (targetTdp <= 0) targetTdp = cachedSlow ?? performanceManager.TDP.Value;

                    int targetTdpFast = isOnAC ? profile.TDPFast : (profile.TDPFast_DC ?? profile.TDPFast);
                    if (targetTdpFast <= 0) targetTdpFast = cachedFast ?? targetTdp;

                    int targetTdpPeak = isOnAC ? profile.TDPPeak : (profile.TDPPeak_DC ?? profile.TDPPeak);
                    if (targetTdpPeak <= 0) targetTdpPeak = cachedPeak ?? targetTdp;

                    if (targetTdp > 0)
                    {
                        Logger.Info($"Helper-side AC/DC handler: applying Custom TDP {targetTdp}/{targetTdpFast}/{targetTdpPeak}W from persisted {state} profile (legionCustom={isLegionCustomMode})");
                        // [TDP Custom-triplet AC/DC fix] The flat PerformanceManager.SetTDP used
                        // here previously hit ApplyTDPInternal's IsInCustomMode branch, which calls
                        // LegionManager.ReassertCustomTDP - that IGNORES the passed value and just
                        // re-pushes whatever's cached, so the per-state SPPT/FPPT split was never
                        // actually applied on an AC/DC transition (only ever the flat SPL, and even
                        // that only indirectly via the cache). Push the full triplet directly via
                        // SetCustomTDP instead - same method the widget's sliders and the
                        // profile-switch fix (commit 9e878f9) already use, with its own built-in
                        // mode-switch safety (pending-mode flush, hardware-confirm poll, atomic
                        // apply+rollback).
                        legionManager.SetCustomTDP(targetTdp, targetTdpFast, targetTdpPeak);

                        // Keep the flat TDP property's cache in sync too (SetValueSilent - do NOT
                        // use SetValue/SetProfileValue here). On-device testing of an earlier
                        // version of this fix (2026-07-18) caught a real race: TDPProperty.
                        // NotifyPropertyChanged unconditionally calls Manager.SetTDP(Value), which
                        // in Custom mode re-enters ApplyTDPInternal -> ReassertCustomTDP - a
                        // SECOND, fully independent apply of whatever's cached at that instant.
                        // Since ApplyTDPValues above writes SPL/SPPT/FPPT one at a time (each a
                        // separate slow WMI call) and only updates the cache at the very end, a
                        // SetValue call here could observe/reassert a stale cache mid-flight and
                        // (if a mode-switch happens to be pending) finish LAST, overwriting the
                        // just-applied correct values. Custom mode has no UI bound to the flat TDP
                        // property (removed per the master-slider cleanup) - SetCustomTDP above
                        // already syncs the properties the Custom TDP sliders actually read
                        // (LegionCustomTDPSlow/Fast/Peak) - so this is cache bookkeeping only, no
                        // hardware re-apply needed or wanted.
                        performanceManager.TDP.SetValueSilent(targetTdp);
                    }

                    // TDP Boost removed — boost is always on (SPPT/FPPT applied directly), so there's
                    // no per-state boost flag to reapply on AC/DC transitions anymore.
                }

                // 2b) Extended fields: CPUBoost / CPUEPP / CPUState (Min+Max) / FPSLimit. Apply
                // unconditionally — these settings aren't managed by Legion preset modes, so
                // they're safe to reapply anytime the resolved value differs from current.
                bool targetCpuBoost = isOnAC ? profile.CPUBoost : (profile.CPUBoost_DC ?? profile.CPUBoost);
                if (powerManager?.CPUBoost != null && targetCpuBoost != powerManager.CPUBoost.Value)
                {
                    Logger.Info($"Helper-side AC/DC handler: applying CPUBoost={targetCpuBoost} from persisted {state} profile");
                    powerManager.CPUBoost.SetValue(targetCpuBoost);
                }

                int targetCpuEpp = isOnAC ? profile.CPUEPP : (profile.CPUEPP_DC ?? profile.CPUEPP);
                if (powerManager?.CPUEPP != null && targetCpuEpp != powerManager.CPUEPP.Value)
                {
                    Logger.Info($"Helper-side AC/DC handler: applying CPUEPP={targetCpuEpp} from persisted {state} profile");
                    powerManager.CPUEPP.SetValue(targetCpuEpp);
                }

                int targetMaxCpuState = isOnAC ? profile.MaxCPUState : (profile.MaxCPUState_DC ?? profile.MaxCPUState);
                if (powerManager?.MaxCPUState != null && targetMaxCpuState != powerManager.MaxCPUState.Value)
                {
                    Logger.Info($"Helper-side AC/DC handler: applying MaxCPUState={targetMaxCpuState}% from persisted {state} profile");
                    powerManager.MaxCPUState.SetValue(targetMaxCpuState);
                }

                int targetMinCpuState = isOnAC ? profile.MinCPUState : (profile.MinCPUState_DC ?? profile.MinCPUState);
                if (powerManager?.MinCPUState != null && targetMinCpuState != powerManager.MinCPUState.Value)
                {
                    Logger.Info($"Helper-side AC/DC handler: applying MinCPUState={targetMinCpuState}% from persisted {state} profile");
                    powerManager.MinCPUState.SetValue(targetMinCpuState);
                }

                int? targetFpsLimit = isOnAC ? profile.FPSLimit : (profile.FPSLimit_DC ?? profile.FPSLimit);
                if (targetFpsLimit.HasValue && rtssManager?.FPSLimit != null && targetFpsLimit.Value != rtssManager.FPSLimit.Value)
                {
                    Logger.Info($"Helper-side AC/DC handler: applying FPSLimit={targetFpsLimit.Value} from persisted {state} profile");
                    rtssManager.FPSLimit.SetValue(targetFpsLimit.Value);
                }

                // OSPowerMode: unchanged, still reads the ephemeral (out-of-scope) cache.
                int? perStateOsMode = isOnAC ? PowerSourceProfileState.AcOsPowerMode : PowerSourceProfileState.DcOsPowerMode;
                if (perStateOsMode.HasValue && powerManager?.OSPowerMode != null
                    && perStateOsMode.Value != powerManager.OSPowerMode.Value)
                {
                    Logger.Info($"Helper-side AC/DC handler: applying OSPowerMode={perStateOsMode.Value} from per-state {state} profile");
                    powerManager.OSPowerMode.SetValue(perStateOsMode.Value);
                }

                // 2c) AMD Radeon features (11 fields) — reuses ApplyAMDFeaturesFromProfile's
                // existing mutual-exclusion correction (RSR/RIS, Chill/AntiLag/Boost), now AC/DC-aware.
                if (amdManager != null)
                {
                    ApplyAMDFeaturesFromProfile(profile, isOnAC);
                }

                // 2d) HDR / Resolution / RefreshRate — same HasValue/IsNullOrEmpty-gated shape
                // RunningGame_PropertyChanged's own profile-switch block already uses.
                bool? targetHdr = isOnAC ? profile.HDREnabled : (profile.HDREnabled_DC ?? profile.HDREnabled);
                if (targetHdr.HasValue && systemManager?.HDREnabled != null && targetHdr.Value != systemManager.HDREnabled.Value)
                {
                    Logger.Info($"Helper-side AC/DC handler: applying HDREnabled={targetHdr.Value} from persisted {state} profile");
                    systemManager.HDREnabled.SetValue(targetHdr.Value);
                }

                string targetResolution = isOnAC ? profile.Resolution : (profile.Resolution_DC ?? profile.Resolution);
                if (!string.IsNullOrEmpty(targetResolution) && systemManager?.Resolution != null && targetResolution != systemManager.Resolution.Value)
                {
                    Logger.Info($"Helper-side AC/DC handler: applying Resolution={targetResolution} from persisted {state} profile");
                    systemManager.Resolution.SetValue(targetResolution);
                }

                int? targetRefreshRate = isOnAC ? profile.RefreshRate : (profile.RefreshRate_DC ?? profile.RefreshRate);
                if (targetRefreshRate.HasValue && systemManager?.RefreshRate != null && targetRefreshRate.Value != systemManager.RefreshRate.Value)
                {
                    Logger.Info($"Helper-side AC/DC handler: applying RefreshRate={targetRefreshRate.Value} from persisted {state} profile");
                    systemManager.RefreshRate.SetValue(targetRefreshRate.Value);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Helper-side AC/DC handler: reapply TDP threw: {ex.Message}");
            }
        }
    }
}
