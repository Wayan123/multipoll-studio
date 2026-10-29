using System;
using System.Collections.Generic;

namespace MultiPollStudio
{
    public enum FC : byte
    {
        ReadCoils = 1,
        ReadDiscreteInputs = 2,
        ReadHoldingRegisters = 3,
        ReadInputRegisters = 4,
        WriteSingleCoil = 5,
        WriteSingleRegister = 6,
        WriteMultipleCoils = 15,
        WriteMultipleRegisters = 16,
        MaskWriteRegister = 22,
        ReadWriteMultipleRegisters = 23,
    }

    /// <summary>Result codes. Numbers follow Modbus Poll's documented automation ReadResult values.</summary>
    public enum Status
    {
        Ok = 0,
        Timeout = 1,
        CrcError = 2,
        ResponseError = 3,
        WriteError = 4,
        ReadError = 5,
        NoConnection = 6,
        NotInitialized = 10,
        BytesMissing = 11,
        ByteCountError = 12,
        TransactionIdError = 13,
        UnknownException = 0x80,
        IllegalFunction = 0x81,
        IllegalDataAddress = 0x82,
        IllegalDataValue = 0x83,
        SlaveDeviceFailure = 0x84,
        Acknowledge = 0x85,
        SlaveDeviceBusy = 0x86,
        NegativeAcknowledge = 0x87,
        MemoryParityError = 0x88,
        GatewayPathUnavailable = 0x8A,
        GatewayTargetFailed = 0x8B,
    }

    public static class StatusInfo
    {
        static readonly Dictionary<Status, string> Texts = new Dictionary<Status, string>
        {
            { Status.Ok, "Response ok" },
            { Status.Timeout, "Timeout Error" },
            { Status.CrcError, "CRC Error" },
            { Status.ResponseError, "Response Error" },
            { Status.WriteError, "Write Error" },
            { Status.ReadError, "Read Error" },
            { Status.NoConnection, "No Connection" },
            { Status.NotInitialized, "Data Uninitialized" },
            { Status.BytesMissing, "Bytes Missing Error" },
            { Status.ByteCountError, "Byte Count Error" },
            { Status.TransactionIdError, "Transaction ID Error" },
            { Status.UnknownException, "Unknown Exception" },
            { Status.IllegalFunction, "Illegal Function" },
            { Status.IllegalDataAddress, "Illegal Data Address" },
            { Status.IllegalDataValue, "Illegal Data Value" },
            { Status.SlaveDeviceFailure, "Slave Device Failure" },
            { Status.Acknowledge, "Acknowledge" },
            { Status.SlaveDeviceBusy, "Slave Device Busy" },
            { Status.NegativeAcknowledge, "NAK-Negative Acknowledgment" },
            { Status.MemoryParityError, "Memory Parity Error" },
            { Status.GatewayPathUnavailable, "Gateway Path Unavailable" },
            { Status.GatewayTargetFailed, "Gateway Target Device Failed to Respond" },
        };

        public static string Text(this Status s)
        {
            string t;
            return Texts.TryGetValue(s, out t) ? t : s.ToString();
        }
    }

    public sealed class ModbusException : Exception
    {
        public Status Status { get; }
        public string Detail { get; }

        public ModbusException(Status status, string detail = "")
            : base(string.IsNullOrEmpty(detail) ? status.Text() : detail)
        {
            Status = status;
            Detail = detail ?? "";
        }

        /// <summary>Status text plus detail, e.g. "Illegal Data Address (exception code 02)".</summary>
        public string Describe()
        {
            return Detail.Length == 0 || Detail == Status.Text() ? Status.Text() : Status.Text() + " (" + Detail + ")";
        }
    }

    /// <summary>Raised when a write is refused locally because write protection is on. Nothing was sent.</summary>
    public sealed class WriteBlockedException : Exception
    {
        public WriteBlockedException() : base("Write protection is on: nothing was sent. Turn it off in the Connection menu to write.") { }
    }

    /// <summary>Modbus PDU encoding and response validation (Modbus Application Protocol v1.1b3).</summary>
    public static class Pdu
    {
        public const int MaxReadBits = 2000;
        public const int MaxReadRegisters = 125;
        public const int MaxWriteBits = 1968;
        public const int MaxWriteRegisters = 123;
        public const int MaxRwReadRegisters = 125;
        public const int MaxRwWriteRegisters = 121;

        public static bool IsRead(int fc) { return fc >= 1 && fc <= 4; }
        public static bool IsBits(int fc) { return fc == 1 || fc == 2; }

        /// <summary>Writes may be broadcast to slave 0 on serial lines; reads and FC23 may not.</summary>
        public static bool IsBroadcastable(byte[] pdu)
        {
            int fc = pdu[0];
            return fc == 5 || fc == 6 || fc == 15 || fc == 16 || fc == 22;
        }

        static void Range(string name, int value, int low, int high)
        {
            if (value < low || value > high)
                throw new ArgumentException(name + " must be " + low + ".." + high + ", got " + value);
        }

        static void Span(int address, int quantity)
        {
            Range("address", address, 0, 0xFFFF);
            if (address + quantity > 0x10000)
                throw new ArgumentException("address " + address + " + quantity " + quantity + " exceeds 65536");
        }

        static void Put16(List<byte> b, int v)
        {
            b.Add((byte)(v >> 8));
            b.Add((byte)v);
        }

        public static int Get16(byte[] b, int offset) { return (b[offset] << 8) | b[offset + 1]; }

        public static byte[] ReadRequest(int fc, int address, int quantity)
        {
            if (!IsRead(fc)) throw new ArgumentException("function " + fc + " is not a read function");
            Range("quantity", quantity, 1, IsBits(fc) ? MaxReadBits : MaxReadRegisters);
            Span(address, quantity);
            var b = new List<byte> { (byte)fc };
            Put16(b, address);
            Put16(b, quantity);
            return b.ToArray();
        }

        public static byte[] WriteSingleCoil(int address, bool on)
        {
            Range("address", address, 0, 0xFFFF);
            var b = new List<byte> { (byte)FC.WriteSingleCoil };
            Put16(b, address);
            Put16(b, on ? 0xFF00 : 0x0000);
            return b.ToArray();
        }

        public static byte[] WriteSingleRegister(int address, int value)
        {
            Range("address", address, 0, 0xFFFF);
            Range("value", value, 0, 0xFFFF);
            var b = new List<byte> { (byte)FC.WriteSingleRegister };
            Put16(b, address);
            Put16(b, value);
            return b.ToArray();
        }

        public static byte[] PackBits(IList<int> bits)
        {
            var output = new byte[(bits.Count + 7) / 8];
            for (int i = 0; i < bits.Count; i++)
                if (bits[i] != 0) output[i / 8] |= (byte)(1 << (i % 8));
            return output;
        }

        public static int[] UnpackBits(byte[] data, int offset, int count)
        {
            var output = new int[count];
            for (int i = 0; i < count; i++) output[i] = (data[offset + i / 8] >> (i % 8)) & 1;
            return output;
        }

        public static byte[] WriteMultipleCoils(int address, IList<int> bits)
        {
            Range("quantity", bits.Count, 1, MaxWriteBits);
            Span(address, bits.Count);
            byte[] payload = PackBits(bits);
            var b = new List<byte> { (byte)FC.WriteMultipleCoils };
            Put16(b, address);
            Put16(b, bits.Count);
            b.Add((byte)payload.Length);
            b.AddRange(payload);
            return b.ToArray();
        }

        static void PutRegisters(List<byte> b, IList<int> values)
        {
            foreach (int v in values)
            {
                Range("register value", v, 0, 0xFFFF);
                Put16(b, v);
            }
        }

        public static byte[] WriteMultipleRegisters(int address, IList<int> values)
        {
            Range("quantity", values.Count, 1, MaxWriteRegisters);
            Span(address, values.Count);
            var b = new List<byte> { (byte)FC.WriteMultipleRegisters };
            Put16(b, address);
            Put16(b, values.Count);
            b.Add((byte)(values.Count * 2));
            PutRegisters(b, values);
            return b.ToArray();
        }

        public static byte[] MaskWriteRegister(int address, int andMask, int orMask)
        {
            Range("address", address, 0, 0xFFFF);
            Range("AND mask", andMask, 0, 0xFFFF);
            Range("OR mask", orMask, 0, 0xFFFF);
            var b = new List<byte> { (byte)FC.MaskWriteRegister };
            Put16(b, address);
            Put16(b, andMask);
            Put16(b, orMask);
            return b.ToArray();
        }

        public static byte[] ReadWriteRegisters(int readAddress, int readQuantity, int writeAddress, IList<int> values)
        {
            Range("read quantity", readQuantity, 1, MaxRwReadRegisters);
            Range("write quantity", values.Count, 1, MaxRwWriteRegisters);
            Span(readAddress, readQuantity);
            Span(writeAddress, values.Count);
            var b = new List<byte> { (byte)FC.ReadWriteMultipleRegisters };
            Put16(b, readAddress);
            Put16(b, readQuantity);
            Put16(b, writeAddress);
            Put16(b, values.Count);
            b.Add((byte)(values.Count * 2));
            PutRegisters(b, values);
            return b.ToArray();
        }

        /// <summary>Validate a response PDU against its request. Returns read values, or null for writes.</summary>
        /// <exception cref="ModbusException">carrying the matching Status on any mismatch.</exception>
        public static int[] ParseResponse(byte[] request, byte[] response)
        {
            if (response == null || response.Length == 0)
                throw new ModbusException(Status.ResponseError, "empty response PDU");
            int fc = request[0];
            int rfc = response[0];
            if (rfc == (fc | 0x80))
            {
                if (response.Length != 2) throw new ModbusException(Status.ResponseError, "malformed exception response");
                int code = response[1];
                var status = (Status)(0x80 | code);
                if (!Enum.IsDefined(typeof(Status), status)) status = Status.UnknownException;
                throw new ModbusException(status, "exception code " + code.ToString("X2"));
            }
            if (rfc != fc)
                throw new ModbusException(Status.ResponseError,
                    "function " + rfc.ToString("X2") + " does not match request " + fc.ToString("X2"));

            if (IsRead(fc) || fc == (int)FC.ReadWriteMultipleRegisters)
            {
                int quantity = Get16(request, 3);
                bool bits = IsBits(fc);
                int expected = bits ? (quantity + 7) / 8 : 2 * quantity;
                if (response.Length < 2 || response[1] != expected || response.Length != 2 + expected)
                {
                    int got = response.Length >= 2 ? response[1] : 0;
                    throw new ModbusException(Status.ByteCountError, "byte count " + got + ", expected " + expected);
                }
                if (bits) return UnpackBits(response, 2, quantity);
                var values = new int[quantity];
                for (int i = 0; i < quantity; i++) values[i] = Get16(response, 2 + 2 * i);
                return values;
            }

            if (fc == 5 || fc == 6 || fc == 15 || fc == 16)
            {
                if (!Same(response, request, 5)) throw new ModbusException(Status.ResponseError, "write echo does not match request");
                return null;
            }
            if (fc == 22)
            {
                if (!Same(response, request, request.Length))
                    throw new ModbusException(Status.ResponseError, "mask write echo does not match request");
                return null;
            }
            throw new ModbusException(Status.ResponseError, "unsupported function " + fc.ToString("X2"));
        }

        static bool Same(byte[] response, byte[] request, int length)
        {
            if (response.Length != length || request.Length < length) return false;
            for (int i = 0; i < length; i++)
                if (response[i] != request[i]) return false;
            return true;
        }
    }
}
