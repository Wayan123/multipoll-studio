using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MultiPollStudio
{
    public enum DataType
    {
        Int16, UInt16, Hex16, Binary16, Bcd16, Int32, UInt32, Float32, Bcd32, UnixTime32, Int64, UInt64, Float64, String, BitField,
    }

    /// <summary>Byte order of a multi-byte value on the wire, letters = bytes of the value from most significant (A).
    /// ABCD big-endian (Modbus standard), CDAB word swap (Modbus Poll "Inverse"), BADC byte swap inside each register,
    /// DCBA little-endian. For one-register values only the byte swap matters, so CDAB folds into ABCD and DCBA into BADC.</summary>
    public enum ByteOrder { ABCD, CDAB, BADC, DCBA }

    /// <summary>How the registers of one cell are interpreted: data type, byte order and, for strings, the length in
    /// registers or, for bit fields, the bit range of the register. Immutable with value equality. The ten formats of
    /// Modbus Poll's Display menu are <see cref="Presets"/> and keep their original file keys ("float_inverse").</summary>
    public sealed class DisplayFormat : IEquatable<DisplayFormat>
    {
        public const int MaxStringRegisters = 64;

        public readonly DataType Type;
        public readonly ByteOrder Order;
        /// <summary>Registers of a <see cref="DataType.String"/>; 0 for other types.</summary>
        public readonly int Length;
        /// <summary>Bit range of a <see cref="DataType.BitField"/> (bit 0 = least significant); 0 for other types.</summary>
        public readonly int BitStart, BitWidth;

        public DisplayFormat(DataType type, ByteOrder order = ByteOrder.ABCD, int length = 0, int bitStart = 0, int bitWidth = 1)
        {
            Type = type;
            if (type == DataType.String)
            {
                if (length < 1 || length > MaxStringRegisters)
                    throw new FormatException("String length must be 1.." + MaxStringRegisters + " registers");
                Length = length;
            }
            if (type == DataType.BitField)
            {
                if (bitStart < 0 || bitStart > 15 || bitWidth < 1 || bitStart + bitWidth > 16)
                    throw new FormatException("Bit field must lie inside bits 0..15");
                BitStart = bitStart;
                BitWidth = bitWidth;
                order = ByteOrder.ABCD; // bits are numbered in the register value as received
            }
            if (Width == 1) order = order == ByteOrder.CDAB ? ByteOrder.ABCD : order == ByteOrder.DCBA ? ByteOrder.BADC : order;
            Order = order;
        }

        public static readonly DisplayFormat Signed = new DisplayFormat(DataType.Int16);
        public static readonly DisplayFormat Unsigned = new DisplayFormat(DataType.UInt16);
        public static readonly DisplayFormat Hex = new DisplayFormat(DataType.Hex16);
        public static readonly DisplayFormat Binary = new DisplayFormat(DataType.Binary16);
        public static readonly DisplayFormat Long = new DisplayFormat(DataType.Int32);
        public static readonly DisplayFormat LongInverse = new DisplayFormat(DataType.Int32, ByteOrder.CDAB);
        public static readonly DisplayFormat Float = new DisplayFormat(DataType.Float32);
        public static readonly DisplayFormat FloatInverse = new DisplayFormat(DataType.Float32, ByteOrder.CDAB);
        public static readonly DisplayFormat Double = new DisplayFormat(DataType.Float64);
        public static readonly DisplayFormat DoubleInverse = new DisplayFormat(DataType.Float64, ByteOrder.CDAB);

        /// <summary>Common display presets in menu order.</summary>
        public static readonly DisplayFormat[] Presets =
        {
            Signed, Unsigned, Hex, Binary, Long, LongInverse, Float, FloatInverse, Double, DoubleInverse,
        };

        static readonly string[] PresetLabels =
        {
            "Signed 16-bit", "Unsigned 16-bit", "Hex 16-bit", "Binary 16-bit",
            "Signed 32-bit ABCD", "Signed 32-bit CDAB", "Float 32-bit ABCD", "Float 32-bit CDAB",
            "Float 64-bit (normal words)", "Float 64-bit (reversed words)",
        };

        static readonly string[] PresetKeys =
        {
            "signed", "unsigned", "hex", "binary", "long", "long_inverse", "float", "float_inverse", "double", "double_inverse",
        };

        static readonly string[] TypeKeys =
        {
            "i16", "u16", "hex16", "bin16", "bcd16", "i32", "u32", "f32", "bcd32", "unix32", "i64", "u64", "f64", "str", "bits",
        };

        static readonly string[] ShortLabels =
        {
            "Signed", "Unsigned", "Hex", "Binary", "BCD16", "Int32", "UInt32", "Float32", "BCD32", "Unix time", "Int64", "UInt64", "Float64", "String", "Bits",
        };

        /// <summary>Type names for pickers, indexed by <see cref="DataType"/>.</summary>
        public static readonly string[] TypeLabels =
        {
            "Signed 16-bit", "Unsigned 16-bit", "Hex 16-bit", "Binary 16-bit", "BCD 16-bit (4 digits)", "Signed 32-bit", "Unsigned 32-bit",
            "Float 32-bit", "BCD 32-bit (8 digits)", "Unix time 32-bit (UTC)", "Signed 64-bit", "Unsigned 64-bit", "Float 64-bit",
            "String (ASCII)", "Bit field",
        };

        public static readonly string[] OrderLabels =
        {
            "ABCD (big-endian)", "CDAB (word swap)", "BADC (byte swap)", "DCBA (little-endian)",
        };

        /// <summary>Registers one value occupies.</summary>
        public int Width
        {
            get
            {
                switch (Type)
                {
                    case DataType.Int32: case DataType.UInt32: case DataType.Float32: case DataType.Bcd32: case DataType.UnixTime32:
                        return 2;
                    case DataType.Int64: case DataType.UInt64: case DataType.Float64:
                        return 4;
                    case DataType.String:
                        return Length;
                    default:
                        return 1;
                }
            }
        }

        public bool IsFloat { get { return Type == DataType.Float32 || Type == DataType.Float64; } }

        /// <summary>True when tag scaling, units and alarms apply (not hex/binary, time stamps or text).</summary>
        public bool Scalable
        {
            get { return Type != DataType.Hex16 && Type != DataType.Binary16 && Type != DataType.UnixTime32 && Type != DataType.String; }
        }

        string OrderSuffix(bool key)
        {
            if (Width == 1) return Order == ByteOrder.BADC ? (key ? "_ba" : " BA") : "";
            if (key) return Order == ByteOrder.ABCD ? "" : "_" + Order.ToString().ToLowerInvariant();
            return " " + Order;
        }

        public string Label
        {
            get
            {
                int p = Array.IndexOf(Presets, this);
                if (p >= 0) return PresetLabels[p];
                if (Type == DataType.BitField) return BitWidth == 1 ? "Bit " + BitStart : "Bits " + BitStart + "-" + (BitStart + BitWidth - 1);
                string t = Type == DataType.String ? "String " + 2 * Length + " ch" : ShortLabels[(int)Type];
                return t + OrderSuffix(false);
            }
        }

        /// <summary>Type and order spelled out ("Float32 ABCD", "Unsigned BA"), also for presets.</summary>
        public string Spec
        {
            get
            {
                if (Type == DataType.BitField) return Label;
                string t = Type == DataType.String ? "String " + 2 * Length + " ch" : ShortLabels[(int)Type];
                return t + " " + (Width == 1 ? (Order == ByteOrder.BADC ? "BA" : "AB") : Order.ToString());
            }
        }

        /// <summary>File key: a preset key, or type plus an order suffix when not ABCD: "u32", "f32_badc", "str8_cdab",
        /// "u16_ba", "bits4.3" (first bit 4, 3 bits). <see cref="FromKey"/> also accepts an explicit "_abcd".</summary>
        public string Key
        {
            get
            {
                int p = Array.IndexOf(Presets, this);
                if (p >= 0) return PresetKeys[p];
                if (Type == DataType.BitField) return "bits" + BitStart + "." + BitWidth;
                string t = Type == DataType.String ? "str" + Length : TypeKeys[(int)Type];
                return t + OrderSuffix(true);
            }
        }

        public static DisplayFormat FromKey(string key)
        {
            string k = (key ?? "").Trim().ToLowerInvariant();
            int p = Array.IndexOf(PresetKeys, k);
            if (p >= 0) return Presets[p];
            try
            {
                string typePart = k, orderPart = null;
                int us = k.LastIndexOf('_');
                if (us > 0)
                {
                    typePart = k.Substring(0, us);
                    orderPart = k.Substring(us + 1);
                }
                ByteOrder order = ByteOrder.ABCD;
                switch (orderPart)
                {
                    case null: case "ab": case "abcd": break;
                    case "ba": case "badc": order = ByteOrder.BADC; break;
                    case "cdab": order = ByteOrder.CDAB; break;
                    case "dcba": order = ByteOrder.DCBA; break;
                    default: throw new FormatException();
                }
                if (typePart.StartsWith("bits") && orderPart == null)
                {
                    string[] parts = typePart.Substring(4).Split('.');
                    if (parts.Length != 2) throw new FormatException();
                    return new DisplayFormat(DataType.BitField, bitStart: ParseSmall(parts[0]), bitWidth: ParseSmall(parts[1]));
                }
                if (typePart.StartsWith("str"))
                    return new DisplayFormat(DataType.String, order, ParseSmall(typePart.Substring(3)));
                int t = Array.IndexOf(TypeKeys, typePart);
                if (t < 0 || t == (int)DataType.String || t == (int)DataType.BitField) throw new FormatException();
                return new DisplayFormat((DataType)t, order);
            }
            catch (FormatException)
            {
                throw new FormatException("unknown display format '" + key + "'");
            }
        }

        static int ParseSmall(string s)
        {
            int v;
            if (s.Length == 0 || s.Length > 3 || !int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out v)) throw new FormatException();
            return v;
        }

        public bool Equals(DisplayFormat o)
        {
            return !ReferenceEquals(o, null) && Type == o.Type && Order == o.Order && Length == o.Length && BitStart == o.BitStart && BitWidth == o.BitWidth;
        }

        public override bool Equals(object obj) { return Equals(obj as DisplayFormat); }

        public override int GetHashCode() { return ((((int)Type * 4 + (int)Order) * 128 + Length) * 16 + BitStart) * 17 + BitWidth; }

        public static bool operator ==(DisplayFormat a, DisplayFormat b) { return ReferenceEquals(a, b) || (!ReferenceEquals(a, null) && a.Equals(b)); }

        public static bool operator !=(DisplayFormat a, DisplayFormat b) { return !(a == b); }

        public override string ToString() { return Label; }
    }

    /// <summary>Conversions between registers and text or numbers for every <see cref="DisplayFormat"/>. Text paths are
    /// exact for 64-bit integers; <see cref="Number"/> returns a double for scaling, alarms and trends.</summary>
    public static class Formats
    {
        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        static bool ReverseWords(ByteOrder o) { return o == ByteOrder.CDAB || o == ByteOrder.DCBA; }
        static bool SwapBytes(ByteOrder o) { return o == ByteOrder.BADC || o == ByteOrder.DCBA; }

        /// <summary>The cell's registers as bytes in value order (most significant first; for strings, text order).</summary>
        public static byte[] ToBytes(IList<int> words, int offset, DisplayFormat f)
        {
            int n = f.Width;
            bool rev = ReverseWords(f.Order), swap = SwapBytes(f.Order);
            var b = new byte[2 * n];
            for (int i = 0; i < n; i++)
            {
                int w = words[offset + (rev ? n - 1 - i : i)] & 0xFFFF;
                byte hi = (byte)(w >> 8), lo = (byte)w;
                b[2 * i] = swap ? lo : hi;
                b[2 * i + 1] = swap ? hi : lo;
            }
            return b;
        }

        /// <summary>Inverse of <see cref="ToBytes"/>: value-order bytes to registers in wire order.</summary>
        public static int[] FromBytes(byte[] b, DisplayFormat f)
        {
            int n = b.Length / 2;
            bool rev = ReverseWords(f.Order), swap = SwapBytes(f.Order);
            var w = new int[n];
            for (int i = 0; i < n; i++)
            {
                int hi = swap ? b[2 * i + 1] : b[2 * i], lo = swap ? b[2 * i] : b[2 * i + 1];
                w[rev ? n - 1 - i : i] = (hi << 8) | lo;
            }
            return w;
        }

        static ulong ToBits(byte[] b)
        {
            ulong v = 0;
            foreach (byte x in b) v = (v << 8) | x;
            return v;
        }

        static byte[] FromBits(ulong v, int count)
        {
            var b = new byte[count];
            for (int i = count - 1; i >= 0; i--)
            {
                b[i] = (byte)v;
                v >>= 8;
            }
            return b;
        }

        static float SingleFromBits(uint bits) { return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0); }
        static uint SingleToBits(float v) { return BitConverter.ToUInt32(BitConverter.GetBytes(v), 0); }

        /// <summary>Mask of a bit field inside its register.</summary>
        public static int BitMask(DisplayFormat f) { return ((1 << f.BitWidth) - 1) << f.BitStart; }

        static int BitValue(int word, DisplayFormat f) { return (word & BitMask(f)) >> f.BitStart; }

        static long? FromBcd(ulong v, int digits)
        {
            long r = 0;
            for (int i = digits - 1; i >= 0; i--)
            {
                int d = (int)((v >> (4 * i)) & 0xF);
                if (d > 9) return null;
                r = r * 10 + d;
            }
            return r;
        }

        static ulong ToBcd(ulong v)
        {
            ulong r = 0;
            for (int shift = 0; v > 0; shift += 4, v /= 10) r |= (v % 10) << shift;
            return r;
        }

        /// <summary>Display text of the value starting at offset; needs f.Width registers.</summary>
        public static string Format(IList<int> words, int offset, DisplayFormat f)
        {
            if (f.Type == DataType.BitField) return BitValue(words[offset], f).ToString(CultureInfo.InvariantCulture);
            byte[] b = ToBytes(words, offset, f);
            if (f.Type == DataType.String) return Text(b);
            ulong u = ToBits(b);
            switch (f.Type)
            {
                case DataType.Int16: return unchecked((short)u).ToString(CultureInfo.InvariantCulture);
                case DataType.UInt16: case DataType.UInt32: case DataType.UInt64: return u.ToString(CultureInfo.InvariantCulture);
                case DataType.Hex16: return "0x" + u.ToString("X4", CultureInfo.InvariantCulture);
                case DataType.Binary16:
                {
                    string s = Convert.ToString((int)u, 2).PadLeft(16, '0');
                    return s.Substring(0, 4) + " " + s.Substring(4, 4) + " " + s.Substring(8, 4) + " " + s.Substring(12, 4);
                }
                case DataType.Int32: return unchecked((int)(uint)u).ToString(CultureInfo.InvariantCulture);
                case DataType.Int64: return unchecked((long)u).ToString(CultureInfo.InvariantCulture);
                case DataType.Float32: return FormatFloat(SingleFromBits((uint)u), 7);
                case DataType.Float64: return FormatFloat(BitConverter.Int64BitsToDouble(unchecked((long)u)), 15);
                case DataType.Bcd16: case DataType.Bcd32:
                {
                    int digits = f.Type == DataType.Bcd16 ? 4 : 8;
                    long? v = FromBcd(u, digits);
                    return v.HasValue ? v.Value.ToString(CultureInfo.InvariantCulture) : "BCD? 0x" + u.ToString("X" + digits, CultureInfo.InvariantCulture);
                }
                case DataType.UnixTime32: return Epoch.AddSeconds((uint)u).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
                default: throw new ArgumentException("unknown type " + f.Type);
            }
        }

        /// <summary>Numeric value for scaling, alarms and trends; null for strings and invalid BCD. 64-bit integers
        /// beyond 2^53 lose precision here (the grid text is exact).</summary>
        public static double? Number(IList<int> words, int offset, DisplayFormat f)
        {
            if (f.Type == DataType.BitField) return BitValue(words[offset], f);
            if (f.Type == DataType.String) return null;
            ulong u = ToBits(ToBytes(words, offset, f));
            switch (f.Type)
            {
                case DataType.Int16: return unchecked((short)u);
                case DataType.Int32: return unchecked((int)(uint)u);
                case DataType.Int64: return unchecked((long)u);
                case DataType.Float32: return SingleFromBits((uint)u);
                case DataType.Float64: return BitConverter.Int64BitsToDouble(unchecked((long)u));
                case DataType.Bcd16: return FromBcd(u, 4);
                case DataType.Bcd32: return FromBcd(u, 8);
                default: return u; // unsigned, hex, binary, unix seconds
            }
        }

        /// <summary>Printable ASCII up to the first NUL; other bytes show as '·'; trailing spaces trimmed.</summary>
        static string Text(byte[] b)
        {
            var sb = new StringBuilder(b.Length);
            foreach (byte x in b)
            {
                if (x == 0) break;
                sb.Append(x >= 0x20 && x <= 0x7E ? (char)x : '·');
            }
            return sb.ToString().TrimEnd(' ');
        }

        static string FormatFloat(double v, int digits)
        {
            if (double.IsNaN(v)) return "NaN";
            if (double.IsPositiveInfinity(v)) return "Inf";
            if (double.IsNegativeInfinity(v)) return "-Inf";
            return v.ToString("G" + digits, CultureInfo.InvariantCulture);
        }

        /// <summary>User text to registers. Integers accept a 0x prefix meaning the raw bit pattern (0xFFFF as Signed
        /// is -1), hex/binary accept spaces and 0x/0b, decimals accept '.' or ',', Unix time accepts seconds or an
        /// ISO-8601 date (UTC unless an offset is given), strings are printable ASCII padded with NUL.</summary>
        public static int[] Parse(string text, DisplayFormat f)
        {
            if (f.Type == DataType.String) return EncodeString(text ?? "", f);
            string t = (text ?? "").Trim().Replace(" ", "").Replace("_", "");
            if (t.Length == 0) throw new FormatException("empty value");
            switch (f.Type)
            {
                case DataType.Float32: case DataType.Float64:
                    return Encode(ParseNumber(t), f);
                case DataType.Binary16:
                    return EncodePattern(Pattern(t.StartsWith("0b", StringComparison.OrdinalIgnoreCase) ? t.Substring(2) : t, 2, text, f), f);
                case DataType.Hex16:
                    return EncodePattern(Pattern(t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? t.Substring(2) : t, 16, text, f), f);
                case DataType.UnixTime32:
                    if (!IsDigits(t))
                    {
                        DateTimeOffset when;
                        if (!DateTimeOffset.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out when))
                            throw Invalid(text, f);
                        return EncodeInteger(when.ToUnixTimeSeconds(), f);
                    }
                    break;
            }
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && f.Type != DataType.BitField)
                return EncodePattern(Pattern(t.Substring(2), 16, text, f), f);
            decimal d;
            if (!decimal.TryParse(t, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out d)) throw Invalid(text, f);
            return EncodeInteger(d, f);
        }

        static FormatException Invalid(string text, DisplayFormat f)
        {
            return new FormatException("'" + text + "' is not a valid " + f.Label + " value");
        }

        static ulong Pattern(string digits, int radix, string text, DisplayFormat f)
        {
            try { return Convert.ToUInt64(digits, radix); }
            catch (OverflowException) { throw new FormatException("'" + text + "' is out of range for " + f.Label); }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException) { throw Invalid(text, f); }
        }

        static bool IsDigits(string s)
        {
            foreach (char c in s) if (c < '0' || c > '9') return false;
            return s.Length > 0;
        }

        /// <summary>Decimal number as typed by a user ('.' or ',' as decimal separator).</summary>
        public static double ParseNumber(string text)
        {
            string t = (text ?? "").Trim().Replace(" ", "").Replace(',', '.');
            double v;
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                throw new FormatException("'" + text + "' is not a number");
            return v;
        }

        /// <summary>Number to registers (engineering writes compute the raw value as a double). Integer types require an
        /// integral value in range; strings are rejected.</summary>
        public static int[] Encode(double value, DisplayFormat f)
        {
            switch (f.Type)
            {
                case DataType.Float32:
                    if (!double.IsNaN(value) && !double.IsInfinity(value) && Math.Abs(value) > float.MaxValue)
                        throw new FormatException(value.ToString(CultureInfo.InvariantCulture) + " does not fit a 32-bit float");
                    return EncodePattern(SingleToBits((float)value), f);
                case DataType.Float64:
                    return EncodePattern(unchecked((ulong)BitConverter.DoubleToInt64Bits(value)), f);
                case DataType.String:
                    throw new FormatException("a string cell needs text, not a number");
            }
            if (double.IsNaN(value) || double.IsInfinity(value) || value != Math.Floor(value))
                throw new FormatException(value.ToString(CultureInfo.InvariantCulture) + " is not an integer");
            decimal d;
            try { d = (decimal)value; }
            catch (OverflowException) { throw new FormatException(value.ToString(CultureInfo.InvariantCulture) + " is out of range for " + f.Label); }
            return EncodeInteger(d, f);
        }

        static void Range(decimal v, decimal low, decimal high)
        {
            if (v < low || v > high)
                throw new FormatException(v.ToString(CultureInfo.InvariantCulture) + " is outside "
                                          + low.ToString(CultureInfo.InvariantCulture) + ".." + high.ToString(CultureInfo.InvariantCulture));
        }

        static int[] EncodeInteger(decimal v, DisplayFormat f)
        {
            ulong pattern;
            switch (f.Type)
            {
                case DataType.Int16: Range(v, short.MinValue, short.MaxValue); pattern = unchecked((ushort)(short)v); break;
                case DataType.UInt16: case DataType.Hex16: case DataType.Binary16: Range(v, 0, ushort.MaxValue); pattern = (ulong)v; break;
                case DataType.Int32: Range(v, int.MinValue, int.MaxValue); pattern = unchecked((uint)(int)v); break;
                case DataType.UInt32: case DataType.UnixTime32: Range(v, 0, uint.MaxValue); pattern = (ulong)v; break;
                case DataType.Int64: Range(v, long.MinValue, long.MaxValue); pattern = unchecked((ulong)(long)v); break;
                case DataType.UInt64: Range(v, 0, ulong.MaxValue); pattern = (ulong)v; break;
                case DataType.Bcd16: Range(v, 0, 9999); pattern = ToBcd((ulong)v); break;
                case DataType.Bcd32: Range(v, 0, 99999999); pattern = ToBcd((ulong)v); break;
                case DataType.BitField:
                    Range(v, 0, (1 << f.BitWidth) - 1);
                    return new[] { (int)v << f.BitStart };
                default: throw new FormatException(f.Label + " is not an integer format");
            }
            return EncodePattern(pattern, f);
        }

        /// <summary>Raw bit pattern (value order) to registers; rejects patterns wider than the format.</summary>
        static int[] EncodePattern(ulong pattern, DisplayFormat f)
        {
            int bytes = 2 * f.Width;
            if (bytes < 8 && pattern >> (8 * bytes) != 0)
                throw new FormatException("0x" + pattern.ToString("X", CultureInfo.InvariantCulture) + " does not fit " + f.Width * 16 + " bits");
            return FromBytes(FromBits(pattern, bytes), f);
        }

        static int[] EncodeString(string s, DisplayFormat f)
        {
            int max = 2 * f.Length;
            if (s.Length > max) throw new FormatException("text is " + s.Length + " characters; this cell holds " + max);
            var b = new byte[max];
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] < 0x20 || s[i] > 0x7E) throw new FormatException("only printable ASCII characters can be written");
                b[i] = (byte)s[i];
            }
            return FromBytes(b, f);
        }

        public static int ParseCoil(string text)
        {
            string t = (text ?? "").Trim().ToLowerInvariant();
            if (t == "1" || t == "on" || t == "true") return 1;
            if (t == "0" || t == "off" || t == "false") return 0;
            throw new FormatException("coil value must be 0 or 1, got '" + text + "'");
        }

        /// <summary>Every numeric type × byte order that fits the registers, plus an ASCII reading, for the Interpret As
        /// panel. One-register types show two orders (AB, BA), wider types all four.</summary>
        public static List<KeyValuePair<DisplayFormat, string>> Interpretations(IList<int> words)
        {
            var rows = new List<KeyValuePair<DisplayFormat, string>>();
            if (words == null || words.Count == 0) return rows;
            DataType[] types =
            {
                DataType.Int16, DataType.UInt16, DataType.Hex16, DataType.Bcd16, DataType.Int32, DataType.UInt32, DataType.Float32,
                DataType.Bcd32, DataType.UnixTime32, DataType.Int64, DataType.UInt64, DataType.Float64,
            };
            foreach (DataType t in types)
                foreach (ByteOrder o in Enum.GetValues(typeof(ByteOrder)))
                {
                    var f = new DisplayFormat(t, o);
                    if (f.Order != o || f.Width > words.Count) continue; // one-register types: AB and BA only
                    rows.Add(new KeyValuePair<DisplayFormat, string>(f, Format(words, 0, f)));
                }
            int n = Math.Min(words.Count, 8);
            foreach (ByteOrder o in new[] { ByteOrder.ABCD, ByteOrder.BADC })
            {
                var f = new DisplayFormat(DataType.String, o, n);
                rows.Add(new KeyValuePair<DisplayFormat, string>(f, "\"" + Format(words, 0, f) + "\""));
            }
            return rows;
        }
    }
}
