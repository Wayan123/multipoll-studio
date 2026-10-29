using System;
using System.Collections.Generic;
using System.Globalization;

namespace MultiPollStudio
{
    /// <summary>What one poll window reads.</summary>
    public sealed class PollDefinition
    {
        public int SlaveId = 1;
        public int Function = (int)FC.ReadHoldingRegisters;
        public int Address;          // protocol address (base 0)
        public int Quantity = 10;
        public int ScanRateMs = 1000;
        public bool Enabled = true;

        public bool IsBits { get { return Pdu.IsBits(Function); } }
        public bool Writable { get { return Function == (int)FC.ReadCoils || Function == (int)FC.ReadHoldingRegisters; } }

        public PollDefinition Clone() { return (PollDefinition)MemberwiseClone(); }

        public void Validate()
        {
            if (SlaveId < 0 || SlaveId > 255) throw new FormatException("Slave ID must be 0..255");
            if (!Pdu.IsRead(Function)) throw new FormatException("Function must be 01, 02, 03 or 04");
            int limit = IsBits ? Pdu.MaxReadBits : Pdu.MaxReadRegisters;
            if (Quantity < 1 || Quantity > limit) throw new FormatException("Quantity must be 1.." + limit + " for this function");
            if (Address < 0 || Address > 0xFFFF || Address + Quantity > 0x10000)
                throw new FormatException("Address + quantity must stay within 0..65535");
            if (ScanRateMs < 1 || ScanRateMs > 3600000) throw new FormatException("Scan rate must be 1..3600000 ms");
        }
    }

    public sealed class ViewOptions
    {
        public static readonly int[] RowChoices = { 10, 20, 50, 100 };

        public DisplayFormat Display = DisplayFormat.Signed;
        public bool PlcAddresses;
        public int Rows = 10;
        public bool HideAlias;
        public bool AddressInCell;
        public Dictionary<int, DisplayFormat> CellFormats = new Dictionary<int, DisplayFormat>();

        public DisplayFormat FormatAt(int offset)
        {
            DisplayFormat f;
            return CellFormats.TryGetValue(offset, out f) ? f : Display;
        }
    }

    public sealed class PollDocument
    {
        public PollDefinition Definition = new PollDefinition();
        public ViewOptions View = new ViewOptions();
        public Dictionary<int, string> Aliases = new Dictionary<int, string>();
        /// <summary>Engineering metadata (scaling, unit, alarms, write limits) per offset.</summary>
        public Dictionary<int, Tag> Tags = new Dictionary<int, Tag>();
        /// <summary>Own connection for this window; null = the main connection (Connection → Connect).</summary>
        public ConnectionSettings Connection;
        /// <summary>Device profile this window was created from ("" = none); enables Device Settings.</summary>
        public string ProfileId = "";
        /// <summary>Window caption chosen by the user or the profile ("" = file name / generated poll name).</summary>
        public string Title = "";
        public Tag TagAt(int offset)
        {
            Tag t;
            return Tags.TryGetValue(offset, out t) ? t : null;
        }
    }

    public static class Addressing
    {
        public static readonly string[] FunctionLabels =
        {
            "01 Read Coils (0x)", "02 Read Discrete Inputs (1x)", "03 Read Holding Registers (4x)", "04 Read Input Registers (3x)",
        };

        /// <summary>PLC reference prefix per read function: 0xxxx coils, 1xxxx inputs, 3xxxx input regs, 4xxxx holding.</summary>
        public static int PlcPrefix(int function)
        {
            switch (function)
            {
                case 1: return 0;
                case 2: return 1;
                case 4: return 3;
                case 3: return 4;
                default: throw new ArgumentException("not a read function");
            }
        }

        /// <summary>Address text: "40001" (PLC, base 1) or "00000" (protocol, base 0).</summary>
        public static string Label(int function, int address, bool plc)
        {
            if (!plc) return address.ToString("D5", CultureInfo.InvariantCulture);
            int number = address + 1;
            return PlcPrefix(function).ToString(CultureInfo.InvariantCulture)
                   + number.ToString(number <= 9999 ? "D4" : "D5", CultureInfo.InvariantCulture);
        }
    }
}
