using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using NLog;

namespace XboxGamingBarHelper.ControllerEmulation.Viiper
{
    /// <summary>
    /// Removes ghost PnP entries left over from VIIPER hot-swaps. When the user
    /// switches from one virtual controller target to another, libviiper's
    /// RemoveDevice detaches the old USB device, but Windows keeps an
    /// "Present=False" entry in the PnP database for every (VID, PID, serial)
    /// it has ever seen. Steam's controller manager enumerates these ghosts
    /// and shows them as inactive controllers ("Switch Pro / PS4 separate
    /// from the one I'm emulating") — and worse, Steam Input can pin a game's
    /// rumble routing to the ghost's PnP path, so live rumble events stop
    /// reaching the active device.
    ///
    /// Strategy: maintain a static VID/PID map of every libviiper target we
    /// can produce. On helper start, sweep all known VID/PID combos for
    /// non-Present entries and remove them (cleans up ghosts that survived
    /// a previous helper session). On every hot-swap, after RemoveDevice
    /// succeeds, run the same sweep for the OLD target only (the one we
    /// just detached). This keeps the ghost list permanently empty going
    /// forward without affecting any currently-Present device.
    /// </summary>
    internal static class ViiperPnpCleanup
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// VID/PID combinations libviiper can present, indexed by the target
        /// alias the helper sends to AddDevice. Steam sub-device VIDs/PIDs
        /// (Valve 0x28DE / 0x12Fx range) are included so Steam-handheld
        /// hot-swaps clean their cached PIDs too.
        /// </summary>
        private static readonly Dictionary<string, (ushort vid, ushort pid)[]> TargetVidPids = new Dictionary<string, (ushort, ushort)[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["xbox360"]              = new[] { ((ushort)0x045E, (ushort)0x028E) },
            ["xboxelite2"]           = new[] { ((ushort)0x045E, (ushort)0x0B13) },
            ["xbox-one"]             = new[] { ((ushort)0x045E, (ushort)0x02D1) },
            ["xbox-elite"]           = new[] { ((ushort)0x045E, (ushort)0x0B00) },
            ["dualshock4"]           = new[] { ((ushort)0x054C, (ushort)0x05C4) },
            ["dualsense"]            = new[] { ((ushort)0x054C, (ushort)0x0CE6) },
            ["ds5"]                  = new[] { ((ushort)0x054C, (ushort)0x0CE6) },
            ["dualsenseedge"]        = new[] { ((ushort)0x054C, (ushort)0x0DF2) },
            ["dualsense-edge"]       = new[] { ((ushort)0x054C, (ushort)0x0DF2) },
            ["switchpro"]            = new[] { ((ushort)0x057E, (ushort)0x2009) },
            ["joycon-left"]          = new[] { ((ushort)0x057E, (ushort)0x2006) },
            ["joycon-right"]         = new[] { ((ushort)0x057E, (ushort)0x2007) },
            ["gordon"]               = new[] { ((ushort)0x28DE, (ushort)0x1102) },
            ["steam-controller"]     = new[] { ((ushort)0x28DE, (ushort)0x1102) },
            ["steam-controller-v1"]  = new[] { ((ushort)0x28DE, (ushort)0x1102) },
            ["steamcontroller-v1"]   = new[] { ((ushort)0x28DE, (ushort)0x1102) },
            // Steam-handheld family — all use Valve VID with handheld-specific PIDs.
            // steam-generic is the umbrella target; the sub-device PID overrides
            // narrow it down. We list every PID in the 0x12Fx range we ever
            // assign so a handheld hot-swap that changes only the PID also
            // cleans the previous handheld's ghost.
            ["steam-generic"]        = new[]
            {
                ((ushort)0x28DE, (ushort)0x12F0),   // generic
                ((ushort)0x28DE, (ushort)0x1205),   // steam-deck
                ((ushort)0x28DE, (ushort)0x12FA),   // msi-claw
                ((ushort)0x28DE, (ushort)0x12FB),   // legion-go-2
                ((ushort)0x28DE, (ushort)0x12FC),   // zotac-zone
                ((ushort)0x28DE, (ushort)0x12FD),   // rog-ally
                ((ushort)0x28DE, (ushort)0x12FE),   // legion-go
                ((ushort)0x28DE, (ushort)0x12FF),   // legion-go-s
            },
        };

        /// <summary>
        /// Removes any Present=False PnP entries matching the VID/PID set
        /// associated with the given target alias. Runs on a background
        /// thread; safe to call from hot-swap or startup code paths. No-ops
        /// if the helper isn't elevated (pnputil exits with access denied)
        /// or if there's nothing to clean.
        /// </summary>
        public static void CleanupGhostsForTarget(string targetAlias)
        {
            if (string.IsNullOrEmpty(targetAlias)) return;
            if (!TargetVidPids.TryGetValue(targetAlias, out var pairs)) return;

            System.Threading.Tasks.Task.Run(() =>
            {
                try { RunCleanup(pairs); }
                catch (Exception ex) { Logger.Debug($"ViiperPnpCleanup({targetAlias}) threw: {ex.Message}"); }
            });
        }

        /// <summary>
        /// Full sweep across every known VID/PID combination — used at helper
        /// startup to clear any ghost left over from a previous session.
        /// </summary>
        public static void CleanupAllKnownGhosts()
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                var all = new HashSet<(ushort vid, ushort pid)>();
                foreach (var kvp in TargetVidPids)
                {
                    foreach (var pair in kvp.Value) all.Add(pair);
                }
                try { RunCleanup(all); }
                catch (Exception ex) { Logger.Debug($"ViiperPnpCleanup startup sweep threw: {ex.Message}"); }
            });
        }

        private static void RunCleanup(IEnumerable<(ushort vid, ushort pid)> pairs)
        {
            // /disconnected restricts the listing to Present=False entries —
            // exactly the ghosts we want to clean. Avoids the cost of parsing
            // every HID device on the system and avoids the risk of removing
            // a Present device by mistake (Status line is omitted in the bulk
            // enumeration for some Present entries, so filtering by status
            // string is unreliable; the /disconnected filter is the canonical
            // way to scope to ghosts).
            string raw = RunPnputil("/enum-devices /class HIDClass /disconnected");
            if (string.IsNullOrEmpty(raw)) return;

            var targetVidPids = new HashSet<string>();
            foreach (var (vid, pid) in pairs)
            {
                targetVidPids.Add($"VID_{vid:X4}&PID_{pid:X4}".ToUpperInvariant());
            }
            var instanceIdRegex = new Regex(@"Instance ID:\s+(\S+)", RegexOptions.IgnoreCase);

            var toRemove = new List<string>();
            foreach (Match m in instanceIdRegex.Matches(raw))
            {
                string instanceId = m.Groups[1].Value.Trim();
                string upperId = instanceId.ToUpperInvariant();
                foreach (var prefix in targetVidPids)
                {
                    if (upperId.Contains(prefix)) { toRemove.Add(instanceId); break; }
                }
            }

            if (toRemove.Count == 0) return;
            Logger.Info($"ViiperPnpCleanup: removing {toRemove.Count} ghost PnP entr{(toRemove.Count == 1 ? "y" : "ies")}");
            foreach (var id in toRemove)
            {
                string output = RunPnputil($"/remove-device \"{id}\"");
                Logger.Info($"  pnputil /remove-device {id} → {(output ?? string.Empty).TrimEnd()}");
            }
        }

        private static string RunPnputil(string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "pnputil.exe",
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using (var proc = Process.Start(psi))
                {
                    if (proc == null) return string.Empty;
                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit(15000);
                    if (proc.ExitCode != 0 && !string.IsNullOrEmpty(stderr))
                    {
                        Logger.Debug($"pnputil exit={proc.ExitCode}: {stderr.TrimEnd()}");
                    }
                    return stdout;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"pnputil exec failed: {ex.Message}");
                return string.Empty;
            }
        }
    }
}
