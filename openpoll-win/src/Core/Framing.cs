using System;
using System.Text;

namespace MultiPollStudio
{
    /// <summary>ADU framing: Modbus RTU (CRC-16), ASCII (LRC) and TCP (MBAP).</summary>
    public static class Framing
    {
        static readonly ushort[] CrcTable = BuildCrcTable();

        static ushort[] BuildCrcTable()
        {
            var table = new ushort[256];
            for (int b = 0; b < 256; b++)
            {
                int crc = b;
                for (int i = 0; i < 8; i++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xA001 : crc >> 1;
                table[b] = (ushort)crc;
            }
            return table;
        }

        /// <summary>Modbus CRC-16 (reflected poly 0xA001, init 0xFFFF). Sent low byte first.</summary>
        public static int Crc16(byte[] data, int offset, int count)
        {
            int crc = 0xFFFF;
            for (int i = offset; i < offset + count; i++) crc = (crc >> 8) ^ CrcTable[(crc ^ data[i]) & 0xFF];
            return crc;
        }

        public static int Crc16(byte[] data) { return Crc16(data, 0, data.Length); }

        /// <summary>Modbus ASCII LRC: two's complement of the 8-bit sum.</summary>
        public static int Lrc(byte[] data, int offset, int count)
        {
            int sum = 0;
            for (int i = offset; i < offset + count; i++) sum += data[i];
            return (-sum) & 0xFF;
        }

        public static byte[] Concat(byte first, byte[] rest)
        {
            var r = new byte[rest.Length + 1];
            r[0] = first;
            Buffer.BlockCopy(rest, 0, r, 1, rest.Length);
            return r;
        }

        // --- RTU ---------------------------------------------------------------

        public static byte[] RtuFrame(int slave, byte[] pdu)
        {
            byte[] body = Concat((byte)slave, pdu);
            int crc = Crc16(body);
            var frame = new byte[body.Length + 2];
            Buffer.BlockCopy(body, 0, frame, 0, body.Length);
            frame[body.Length] = (byte)crc;
            frame[body.Length + 1] = (byte)(crc >> 8);
            return frame;
        }

        /// <summary>Total RTU frame length implied by the first bytes (slave, function, [byte count]).
        /// Returns -1 when more bytes are needed.</summary>
        public static int RtuResponseLength(byte[] header, int count)
        {
            if (count < 2) return -1;
            int fc = header[1];
            if ((fc & 0x80) != 0) return 5;
            switch (fc)
            {
                case 1: case 2: case 3: case 4: case 23:
                    return count < 3 ? -1 : 3 + header[2] + 2;
                case 5: case 6: case 15: case 16:
                    return 8;
                case 22:
                    return 10;
                case 0x2B:
                    return 0; // FC43 has no byte count: the frame ends at line silence (caller handles it)
                case 0x42:
                    return 4; // Peacefair PZEM reset energy: slave, 0x42, CRC
                default:
                    throw new ModbusException(Status.ResponseError, "unexpected function " + fc.ToString("X2") + " in RTU response");
            }
        }

        /// <param name="slave">Expected slave; -1 accepts any (address-recovery queries such as 0xFE, where the device
        /// answers with its real address).</param>
        public static byte[] RtuUnframe(byte[] frame, int slave)
        {
            if (frame.Length < 4) throw new ModbusException(Status.BytesMissing, "RTU frame of " + frame.Length + " bytes");
            int body = frame.Length - 2;
            int crc = frame[body] | (frame[body + 1] << 8);
            int computed = Crc16(frame, 0, body);
            if (crc != computed)
                throw new ModbusException(Status.CrcError, "CRC " + crc.ToString("X4") + ", computed " + computed.ToString("X4"));
            if (slave >= 0 && frame[0] != slave)
                throw new ModbusException(Status.ResponseError, "slave " + frame[0] + " answered, expected " + slave);
            var pdu = new byte[body - 1];
            Buffer.BlockCopy(frame, 1, pdu, 0, pdu.Length);
            return pdu;
        }

        // --- ASCII -------------------------------------------------------------

        public static byte[] AsciiFrame(int slave, byte[] pdu)
        {
            byte[] body = Concat((byte)slave, pdu);
            var sb = new StringBuilder(":");
            foreach (byte b in body) sb.Append(b.ToString("X2"));
            sb.Append(Lrc(body, 0, body.Length).ToString("X2")).Append("\r\n");
            return Encoding.ASCII.GetBytes(sb.ToString());
        }

        public static byte[] AsciiUnframe(byte[] frame, int slave)
        {
            string text = Encoding.ASCII.GetString(frame).Trim();
            if (!text.StartsWith(":")) throw new ModbusException(Status.ResponseError, "ASCII frame missing ':'");
            byte[] raw;
            try { raw = Hex.Parse(text.Substring(1)); }
            catch (FormatException ex) { throw new ModbusException(Status.ResponseError, "ASCII frame is not hex: " + ex.Message); }
            if (raw.Length < 3) throw new ModbusException(Status.BytesMissing, "ASCII frame of " + raw.Length + " bytes");
            int lrc = Lrc(raw, 0, raw.Length - 1);
            if (lrc != raw[raw.Length - 1])
                throw new ModbusException(Status.CrcError, "LRC " + raw[raw.Length - 1].ToString("X2") + ", computed " + lrc.ToString("X2"));
            if (slave >= 0 && raw[0] != slave) throw new ModbusException(Status.ResponseError, "slave " + raw[0] + " answered, expected " + slave);
            var pdu = new byte[raw.Length - 2];
            Buffer.BlockCopy(raw, 1, pdu, 0, pdu.Length);
            return pdu;
        }

        // --- TCP (MBAP) --------------------------------------------------------

        public const int MbapLength = 7;

        public static byte[] MbapFrame(int transaction, int unit, byte[] pdu)
        {
            var frame = new byte[MbapLength + pdu.Length];
            frame[0] = (byte)(transaction >> 8);
            frame[1] = (byte)transaction;
            int length = pdu.Length + 1;
            frame[4] = (byte)(length >> 8);
            frame[5] = (byte)length;
            frame[6] = (byte)unit;
            Buffer.BlockCopy(pdu, 0, frame, MbapLength, pdu.Length);
            return frame;
        }

        public static byte[] MbapUnframe(byte[] frame, int transaction, int unit)
        {
            if (frame.Length < MbapLength + 1) throw new ModbusException(Status.BytesMissing, "MBAP frame of " + frame.Length + " bytes");
            int tid = Pdu.Get16(frame, 0), proto = Pdu.Get16(frame, 2), length = Pdu.Get16(frame, 4), uid = frame[6];
            if (tid != (transaction & 0xFFFF))
                throw new ModbusException(Status.TransactionIdError, "transaction " + tid + ", expected " + (transaction & 0xFFFF));
            if (proto != 0) throw new ModbusException(Status.ResponseError, "protocol id " + proto);
            if (length != frame.Length - 6)
                throw new ModbusException(Status.BytesMissing, "MBAP length " + length + ", received " + (frame.Length - 6));
            if (unit >= 0 && uid != unit) throw new ModbusException(Status.ResponseError, "unit " + uid + " answered, expected " + unit);
            var pdu = new byte[frame.Length - MbapLength];
            Buffer.BlockCopy(frame, MbapLength, pdu, 0, pdu.Length);
            return pdu;
        }
    }

    public static class Hex
    {
        public static string Format(byte[] data)
        {
            var sb = new StringBuilder(data.Length * 3);
            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(data[i].ToString("X2"));
            }
            return sb.ToString();
        }

        /// <summary>Parse contiguous hex digits ("0103AB").</summary>
        public static byte[] Parse(string s)
        {
            if (s.Length % 2 != 0) throw new FormatException("odd number of hex digits");
            var r = new byte[s.Length / 2];
            for (int i = 0; i < r.Length; i++) r[i] = Convert.ToByte(s.Substring(2 * i, 2), 16);
            return r;
        }

        /// <summary>Parse bytes separated by ',', '.', or whitespace (Frame Console input rules).</summary>
        public static byte[] ParseSeparated(string text)
        {
            var tokens = text.Replace(',', ' ').Replace('.', ' ').Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            var r = new byte[tokens.Length];
            for (int i = 0; i < tokens.Length; i++)
            {
                string t = tokens[i];
                if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t.Substring(2);
                if (t.Length < 1 || t.Length > 2) throw new FormatException("'" + tokens[i] + "' is not a byte");
                r[i] = Convert.ToByte(t, 16);
            }
            if (r.Length == 0) throw new FormatException("no bytes entered");
            return r;
        }
    }
}
