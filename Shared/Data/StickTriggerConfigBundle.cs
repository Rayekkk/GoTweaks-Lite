using System;
using System.Globalization;
using System.Text;

namespace Shared.Data
{
    /// <summary>Shape of the deadzone applied to a stick input. See StickTriggerProcessor for math.</summary>
    public enum DeadzoneShape
    {
        ScaledRadial = 0,
        Radial = 1,
        Axial = 2,
    }

    /// <summary>Sensitivity / response curve applied to a normalized input.</summary>
    public enum SensitivityCurve
    {
        Linear = 0,
        Smooth = 1,
        Aggressive = 2,
        Instant = 3,
        SCurve = 4,
        Delay = 5,
    }

    /// <summary>Per-stick shaping config: shape, per-axis deadzones, anti-deadzones, curves.</summary>
    public struct StickConfig
    {
        public DeadzoneShape Shape;
        public float DeadzoneX;
        public float DeadzoneY;
        public float AntiDeadzoneX;
        public float AntiDeadzoneY;
        public SensitivityCurve CurveX;
        public SensitivityCurve CurveY;

        public static StickConfig Default => new StickConfig
        {
            Shape = DeadzoneShape.ScaledRadial,
            DeadzoneX = 0f, DeadzoneY = 0f,
            AntiDeadzoneX = 0f, AntiDeadzoneY = 0f,
            CurveX = SensitivityCurve.Linear, CurveY = SensitivityCurve.Linear,
        };
    }

    /// <summary>Per-trigger shaping config: deadzone start, range cap, anti-deadzone, curve.</summary>
    public struct TriggerConfig
    {
        public float DeadzoneStart;
        public float RangeMax;
        public float AntiDeadzone;
        public SensitivityCurve Curve;

        public static TriggerConfig Default => new TriggerConfig
        {
            DeadzoneStart = 0f, RangeMax = 1f, AntiDeadzone = 0f,
            Curve = SensitivityCurve.Linear,
        };
    }

    /// <summary>
    /// Serializable bundle of all four stick/trigger configurations (LS, RS,
    /// LT, RT). Persisted via the single <c>Viiper_StickTriggerConfig</c>
    /// property as a flat JSON-ish string. We hand-roll the serializer here
    /// to avoid pulling in Newtonsoft / System.Text.Json — it's 7 enum +
    /// float fields per config × 4 configs = a hundred-byte string.
    ///
    /// <para>Format: pipe-separated config blocks; each block is a
    /// semicolon-separated key=value list. Example (defaults shown):</para>
    /// <code>
    /// LS:Shape=0;DX=0;DY=0;ADX=0;ADY=0;CX=0;CY=0|RS:Shape=0;DX=0;DY=0;ADX=0;ADY=0;CX=0;CY=0|LT:DZ=0;RM=1;AD=0;C=0|RT:DZ=0;RM=1;AD=0;C=0
    /// </code>
    /// <para>Defaults round-trip to identity transformations
    /// (Linear curves, 0 deadzones, 0 anti-deadzones, full range), so
    /// missing/corrupt persisted values silently fall back to passthrough.</para>
    /// </summary>
    public struct StickTriggerConfigBundle
    {
        public StickConfig LeftStick;
        public StickConfig RightStick;
        public TriggerConfig LeftTrigger;
        public TriggerConfig RightTrigger;

        public static StickTriggerConfigBundle Default => new StickTriggerConfigBundle
        {
            LeftStick = StickConfig.Default,
            RightStick = StickConfig.Default,
            LeftTrigger = TriggerConfig.Default,
            RightTrigger = TriggerConfig.Default,
        };

        public string Serialize()
        {
            var sb = new StringBuilder(256);
            AppendStick(sb, "LS", LeftStick);
            sb.Append('|');
            AppendStick(sb, "RS", RightStick);
            sb.Append('|');
            AppendTrigger(sb, "LT", LeftTrigger);
            sb.Append('|');
            AppendTrigger(sb, "RT", RightTrigger);
            return sb.ToString();
        }

        public static StickTriggerConfigBundle Deserialize(string s)
        {
            var bundle = Default;
            if (string.IsNullOrWhiteSpace(s)) return bundle;
            try
            {
                foreach (var block in s.Split('|'))
                {
                    int colon = block.IndexOf(':');
                    if (colon <= 0) continue;
                    string key = block.Substring(0, colon);
                    string body = block.Substring(colon + 1);
                    switch (key)
                    {
                        case "LS": bundle.LeftStick = ParseStick(body); break;
                        case "RS": bundle.RightStick = ParseStick(body); break;
                        case "LT": bundle.LeftTrigger = ParseTrigger(body); break;
                        case "RT": bundle.RightTrigger = ParseTrigger(body); break;
                    }
                }
            }
            catch
            {
                // Corrupt persisted value — fall back to defaults so the
                // emulation path stays identity rather than throwing.
                return Default;
            }
            return bundle;
        }

        private static void AppendStick(StringBuilder sb, string key, StickConfig c)
        {
            sb.Append(key).Append(':');
            sb.Append("Shape=").Append((int)c.Shape).Append(';');
            sb.Append("DX=").Append(F(c.DeadzoneX)).Append(';');
            sb.Append("DY=").Append(F(c.DeadzoneY)).Append(';');
            sb.Append("ADX=").Append(F(c.AntiDeadzoneX)).Append(';');
            sb.Append("ADY=").Append(F(c.AntiDeadzoneY)).Append(';');
            sb.Append("CX=").Append((int)c.CurveX).Append(';');
            sb.Append("CY=").Append((int)c.CurveY);
        }

        private static void AppendTrigger(StringBuilder sb, string key, TriggerConfig c)
        {
            sb.Append(key).Append(':');
            sb.Append("DZ=").Append(F(c.DeadzoneStart)).Append(';');
            sb.Append("RM=").Append(F(c.RangeMax)).Append(';');
            sb.Append("AD=").Append(F(c.AntiDeadzone)).Append(';');
            sb.Append("C=").Append((int)c.Curve);
        }

        private static StickConfig ParseStick(string body)
        {
            var c = StickConfig.Default;
            foreach (var kv in body.Split(';'))
            {
                int eq = kv.IndexOf('=');
                if (eq <= 0) continue;
                string k = kv.Substring(0, eq);
                string v = kv.Substring(eq + 1);
                switch (k)
                {
                    case "Shape": c.Shape = (DeadzoneShape)IntOr(v, 0); break;
                    case "DX": c.DeadzoneX = FloatOr(v, 0f); break;
                    case "DY": c.DeadzoneY = FloatOr(v, 0f); break;
                    case "ADX": c.AntiDeadzoneX = FloatOr(v, 0f); break;
                    case "ADY": c.AntiDeadzoneY = FloatOr(v, 0f); break;
                    case "CX": c.CurveX = (SensitivityCurve)IntOr(v, 0); break;
                    case "CY": c.CurveY = (SensitivityCurve)IntOr(v, 0); break;
                }
            }
            return c;
        }

        private static TriggerConfig ParseTrigger(string body)
        {
            var c = TriggerConfig.Default;
            foreach (var kv in body.Split(';'))
            {
                int eq = kv.IndexOf('=');
                if (eq <= 0) continue;
                string k = kv.Substring(0, eq);
                string v = kv.Substring(eq + 1);
                switch (k)
                {
                    case "DZ": c.DeadzoneStart = FloatOr(v, 0f); break;
                    case "RM": c.RangeMax = FloatOr(v, 1f); break;
                    case "AD": c.AntiDeadzone = FloatOr(v, 0f); break;
                    case "C": c.Curve = (SensitivityCurve)IntOr(v, 0); break;
                }
            }
            return c;
        }

        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        private static int IntOr(string s, int fallback) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : fallback;
        private static float FloatOr(string s, float fallback) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : fallback;
    }
}
