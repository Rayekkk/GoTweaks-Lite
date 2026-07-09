using System;
using System.Collections.Generic;
using System.Text;
using NLog;

namespace XboxGamingBarHelper.Services
{
    /// <summary>
    /// Detects environment problems that make GoTweaks look broken to a new
    /// user even though nothing crashed: currently, a missing driver on hardware
    /// that needs it. Inspired by Handheld Companion's Welcome-flow review.
    ///
    /// Evaluation is cheap (flags the callers already have), so callers run it
    /// on widget connect and on a slow timer, then push the JSON to the widget
    /// which renders a dismissible banner.
    ///
    /// NOTE (GoTweaks Lite): upstream also warns when Legion Space / LegionZone
    /// daemons are running. We deliberately DROP that check — on the Legion Go 2
    /// our settled guidance is the OPPOSITE (keeping the Legion Space background
    /// service running is one of the working ways to keep the standard gamepad
    /// alive), so nagging the user to close it would be actively misleading here.
    /// </summary>
    internal static class SetupHealthService
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// One evaluated warning. Id is stable across evaluations so the
        /// widget can key dismissals; Action is an optional machine hint the
        /// widget maps to a button ("pawnio" → Install PawnIO).
        /// </summary>
        internal sealed class Warning
        {
            public string Id;
            public string Message;
            public string Action;
        }

        /// <summary>
        /// Runs all checks and returns the warning list serialized as the
        /// JSON array the SetupWarnings pipe function carries. Returns "[]"
        /// when everything is healthy. Never throws.
        /// </summary>
        /// <param name="supportsControllerFeatures">Unused in the Lite build (kept for
        /// call-site parity); the Legion Space process check that used it was removed.</param>
        public static string EvaluateJson(bool isLegionDevice, bool supportsControllerFeatures, bool pawnIOInstalled,
                                          bool usbipNeeded, bool usbipInstalled)
        {
            try
            {
                var warnings = new List<Warning>();

                if (isLegionDevice && !pawnIOInstalled)
                {
                    warnings.Add(new Warning
                    {
                        Id = "pawnio",
                        Message = "PawnIO driver is not installed. Custom TDP and fan control need it.",
                        Action = "pawnio",
                    });
                }

                // VIIPER is the only emulation backend since the ViGEm retirement,
                // and it also serves the Legion-button Guide route; without
                // usbip-win2 both stay offline silently, so surface the
                // prerequisite prominently instead of relying on the user finding
                // the install card inside the CE tab.
                if (usbipNeeded && !usbipInstalled)
                {
                    warnings.Add(new Warning
                    {
                        Id = "usbip",
                        Message = "usbip-win2 driver is not installed. Controller emulation needs it (a reboot may be required after install).",
                        Action = "usbip",
                    });
                }

                return Serialize(warnings);
            }
            catch (Exception ex)
            {
                Logger.Warn($"SetupHealthService.EvaluateJson failed: {ex.Message}");
                return "[]";
            }
        }

        private static string Serialize(List<Warning> warnings)
        {
            // Hand-rolled to avoid a JSON dependency for three fields; message
            // text is our own constants so only quotes/backslashes need escaping.
            var sb = new StringBuilder("[");
            for (int i = 0; i < warnings.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":\"").Append(Escape(warnings[i].Id))
                  .Append("\",\"msg\":\"").Append(Escape(warnings[i].Message)).Append('"');
                if (!string.IsNullOrEmpty(warnings[i].Action))
                {
                    sb.Append(",\"action\":\"").Append(Escape(warnings[i].Action)).Append('"');
                }
                sb.Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string Escape(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
