using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MultiPollStudio
{
    /// <summary>A one-click remedy the UI can run. Every fix only changes MultiPoll Studio's own settings or sends reads;
    /// none writes to the device.</summary>
    public enum FixKind
    {
        None,
        /// <summary>Scan baud/parity/ID combinations to find the device (read-only).</summary>
        FindDevice,
        /// <summary>Raise the response timeout to the value suggested for the baud rate and request size.</summary>
        RaiseTimeout,
        /// <summary>Switch FC03 ↔ FC04 (holding ↔ input registers).</summary>
        SwitchFunction,
        /// <summary>Probe each address of the window with single reads and shrink the window to the first readable run.</summary>
        ProbeRange,
        /// <summary>Poll the slave ID that actually answered.</summary>
        UseResponder,
        /// <summary>Raise the delay between polls and the scan rate.</summary>
        SlowDown,
        /// <summary>Turn on "Remove Echo" for adapters that echo transmitted bytes.</summary>
        RemoveEcho,
        /// <summary>Shift the start address by one (PLC base-1 vs protocol base-0 confusion).</summary>
        ShiftAddress,
        /// <summary>Re-open the connection.</summary>
        Reconnect,
    }

    public sealed class Advice
    {
        public string Title;
        /// <summary>Likely causes, most likely first.</summary>
        public string[] Causes;
        /// <summary>Things to check by hand, in order.</summary>
        public string[] Checks;
        public FixKind Fix;
        public string FixLabel = "";
        /// <summary>Parameter of the fix: responder ID, new timeout in ms, address delta.</summary>
        public int FixValue;
    }

    /// <summary>Maps a failed poll (status, detail, connection, definition) to causes, manual checks and safe fixes.
    /// Pure function so the self-test covers it; the same table feeds the Help → Troubleshooting guide.</summary>
    public static class Diagnosis
    {
        static readonly Regex Responder = new Regex(@"(?:slave|unit) (\d+) answered", RegexOptions.CultureInvariant);

        static Advice A(string title, string[] causes, string[] checks, FixKind fix = FixKind.None, string label = "", int value = 0)
        {
            return new Advice { Title = title, Causes = causes, Checks = checks, Fix = fix, FixLabel = label, FixValue = value };
        }

        /// <summary>Advice for one failure; first entry is the main explanation, later entries are extra fixes.</summary>
        public static List<Advice> For(Status status, string detail, ConnectionSettings c, PollDefinition d)
        {
            detail = detail ?? "";
            c = c ?? new ConnectionSettings();
            bool serial = c.Kind == ConnectionKind.Serial;
            bool rtuOverNet = c.Kind == ConnectionKind.SerialOverTcp || c.Kind == ConnectionKind.SerialOverUdp;
            var list = new List<Advice>();
            int function = d != null ? d.Function : 3;
            int quantity = d != null ? d.Quantity : 1;
            int suggested = serial ? SerialTiming.SuggestTimeoutMs(c.BaudRate, function, quantity, c.Mode) : Math.Max(1000, c.ResponseTimeoutMs * 2);
            switch (status)
            {
                case Status.NoConnection:
                    if (serial)
                    {
                        bool denied = detail.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool missing = detail.IndexOf("does not exist", StringComparison.OrdinalIgnoreCase) >= 0
                                       || detail.IndexOf("not available", StringComparison.OrdinalIgnoreCase) >= 0 && !denied;
                        list.Add(A(denied ? "The COM port is in use by another program" : missing ? "The COM port was not found" : "The COM port could not be opened",
                            denied ? new[] { "Another program (vendor tool, terminal, other MultiPoll Studio window, Modbus Poll) holds " + c.Port + ". A COM port has one owner at a time." }
                                   : new[] { c.Port + " is not present: the USB-RS485 adapter is unplugged, has another COM number, or its driver (CH340/CH343, FTDI, CP210x) is missing." },
                            denied ? new[] { "Close the other program or disconnect it from " + c.Port + ".", "Unplug and replug the adapter if the port stays locked." }
                                   : new[] { "Open Device Manager → Ports (COM & LPT) and note the adapter's COM number.", "Pick that port in Connection → Connect (F3).", "Install the adapter driver if it shows as an unknown device." },
                            FixKind.Reconnect, "Try again"));
                    }
                    else
                    {
                        bool timeout = detail.IndexOf("Timeout", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool refused = detail.IndexOf("refused", StringComparison.OrdinalIgnoreCase) >= 0 || detail.IndexOf("actively", StringComparison.OrdinalIgnoreCase) >= 0;
                        list.Add(A(refused ? "The host refused the connection" : timeout ? "The host did not answer the connection request" : "The network connection failed",
                            refused ? new[] { "Nothing listens on " + c.Host + ":" + c.TcpPort + " (Modbus TCP is usually port 502; RTU-over-TCP gateways often use 4001 or 8899).",
                                              "The gateway accepts only one master at a time and another one is connected." }
                                    : new[] { "Wrong IP address, the device is off, or it is on another subnet.", "A firewall blocks port " + c.TcpPort + "." },
                            new[] { "Ping " + c.Host + " from a command prompt.", "Check IP, port and connection type (Modbus TCP vs RTU over TCP) in the gateway's web page.",
                                    "Allow MultiPoll Studio in Windows Firewall, or try from a PC on the same switch." },
                            FixKind.Reconnect, "Try again"));
                    }
                    break;
                case Status.Timeout:
                    list.Add(A("The device did not answer",
                        serial || rtuOverNet
                            ? new[] { "Slave ID, baud rate or parity differ from the device.", "RS-485 A/B wires are swapped, or GND/power is missing.",
                                      "The device does not exist at this ID (scan) or is still starting up.", "The response timeout is shorter than the device needs." }
                            : new[] { "The gateway forwards to a serial line where nothing answers at this unit ID.", "Wrong unit ID (many devices answer only 1 or 255)." },
                        serial || rtuOverNet
                            ? new[] { "Check the device's default settings in its manual (often 9600 8N1, ID 1).", "Swap A and B; check the adapter's TX/RX LEDs blink on each poll.",
                                      "Use one device on the bus while searching, then add the others.", "Add a 120 Ω terminator at both ends on long cables." }
                            : new[] { "Try unit ID 1 and 255.", "Check the gateway's serial settings match the device." },
                        FixKind.FindDevice, "Find device (scan baud / parity / ID)"));
                    if (c.ResponseTimeoutMs < suggested)
                        list.Add(A("Response timeout may be too short", new[] { "Timeout " + c.ResponseTimeoutMs + " ms; " + suggested + " ms is suggested for this baud and request size." },
                                   new string[0], FixKind.RaiseTimeout, "Set response timeout to " + suggested + " ms", suggested));
                    break;
                case Status.CrcError:
                    list.Add(A("Garbled reply (CRC/LRC check failed)",
                        new[] { "Baud rate or parity differ slightly: bytes arrive but are wrong.", "Electrical noise, missing termination or a missing GND reference.",
                                "Two masters on the same bus, or two devices with the same ID answering together." },
                        new[] { "Find the device's exact baud/parity.", "Use twisted-pair cable, add 120 Ω terminators, connect GND.", "Make sure only one master (this PC) polls the bus." },
                        FixKind.FindDevice, "Find device (scan baud / parity / ID)"));
                    break;
                case Status.BytesMissing:
                    list.Add(A("The reply stopped part-way",
                        new[] { "The response timeout ends before the whole frame arrives (slow baud, large quantity, USB latency).", "The device sends shorter replies than the protocol requires." },
                        new[] { "Raise the response timeout or read fewer registers per window." },
                        FixKind.RaiseTimeout, "Set response timeout to " + suggested + " ms", suggested));
                    break;
                case Status.ResponseError:
                {
                    Match m = Responder.Match(detail);
                    if (m.Success)
                    {
                        int id = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                        list.Add(A("Another slave ID answered (" + id + ")",
                            new[] { "The device's ID is " + id + ", or two devices share the bus and the other one answered.", "A device answering to a general address (0xF8/0xFE) replies with its real ID." },
                            new[] { "Give every device on the bus a unique ID (Device → Device Settings)." },
                            FixKind.UseResponder, "Poll slave " + id + " instead", id));
                    }
                    else
                    {
                        list.Add(A("The reply does not match the request",
                            serial ? new[] { "The adapter echoes what it sends (2-wire adapters without echo suppression).", "Noise or a second master on the bus." }
                                   : new[] { "The gateway mixes up replies, or the connection type is wrong (Modbus TCP vs RTU over TCP)." },
                            serial ? new[] { "Turn on Remove Echo for adapters that echo; check for a second master." } : new[] { "Check the connection type in Connection → Connect." },
                            serial && !c.RemoveEcho ? FixKind.RemoveEcho : FixKind.None, serial && !c.RemoveEcho ? "Turn on Remove Echo" : ""));
                    }
                    break;
                }
                case Status.IllegalFunction:
                    list.Add(A("The device does not support function " + function.ToString("D2"),
                        new[] { function == 3 ? "The values are input registers (FC04), not holding registers (FC03)." : function == 4 ? "The values are holding registers (FC03), not input registers (FC04)." : "The device implements only some function codes.",
                                "Some meters (e.g. Eastron) accept only FC03/04/16 and refuse FC06." },
                        new[] { "Check which function codes the manual lists for these registers." },
                        function == 3 || function == 4 ? FixKind.SwitchFunction : FixKind.None, function == 3 ? "Read with FC04 (input registers)" : function == 4 ? "Read with FC03 (holding registers)" : ""));
                    break;
                case Status.IllegalDataAddress:
                    list.Add(A("Some of the addresses do not exist in the device",
                        new[] { "The window reads past the end of the register map, or across a gap.", "The manual lists PLC addresses (40001 = protocol 0): the start is off by one.",
                                "Meters that need even start addresses and even counts (32-bit values) refuse odd ones." },
                        new[] { "Compare the start address and quantity with the manual's register table.", "Toggle Display → PLC Addresses to see both numbering schemes." },
                        FixKind.ProbeRange, "Probe each address and keep the readable part"));
                    if (d != null && d.Address > 0)
                        list.Add(A("Try the address one lower", new[] { "Manuals that count from 1 make the protocol address one smaller." }, new string[0],
                                   FixKind.ShiftAddress, "Start at " + (d.Address - 1) + " instead of " + d.Address, -1));
                    break;
                case Status.IllegalDataValue:
                    list.Add(A("The device rejected the request's value or quantity",
                        new[] { "The quantity is larger than the device accepts per request.", "For a write: the value is outside the allowed range." },
                        new[] { "Read fewer registers at a time; check value limits in the manual." },
                        FixKind.ProbeRange, "Probe each address and keep the readable part"));
                    break;
                case Status.SlaveDeviceFailure:
                    list.Add(A("The device reported an internal failure",
                        new[] { "The sensor element is faulty or not ready (warming up).", "The register exists but the value cannot be produced now." },
                        new[] { "Power-cycle the device; check the sensor probe; read again after its stabilisation time." }));
                    break;
                case Status.SlaveDeviceBusy:
                case Status.Acknowledge:
                    list.Add(A("The device is busy",
                        new[] { "It is still processing the previous request, saving settings or restarting." },
                        new[] { "Poll less often; wait a few seconds after changing settings." },
                        FixKind.SlowDown, "Poll slower (scan rate ×2, delay +50 ms)"));
                    break;
                case Status.GatewayPathUnavailable:
                case Status.GatewayTargetFailed:
                    list.Add(A(status == Status.GatewayTargetFailed ? "The gateway got no answer from the serial device" : "The gateway has no route for this unit ID",
                        new[] { "Unit ID differs from the serial device's ID.", "The gateway's serial baud/parity differ from the device.", "Wiring between gateway and device." },
                        new[] { "Open the gateway's configuration page and compare its serial settings with the device.", "Try the ID scan through the gateway." },
                        FixKind.FindDevice, "Scan unit IDs through the gateway"));
                    break;
                case Status.TransactionIdError:
                    list.Add(A("A late reply to an earlier request arrived",
                        new[] { "The device or gateway answers slower than the response timeout." },
                        new[] { "Raise the response timeout and the delay between polls." },
                        FixKind.RaiseTimeout, "Set response timeout to " + suggested + " ms", suggested));
                    break;
                case Status.ByteCountError:
                    list.Add(A("The reply has the wrong length",
                        new[] { "Non-standard device firmware, or two devices answering together." },
                        new[] { "Read smaller blocks; make sure IDs are unique." },
                        FixKind.ProbeRange, "Probe each address and keep the readable part"));
                    break;
                case Status.WriteError:
                case Status.ReadError:
                    list.Add(A("The port stopped working",
                        new[] { "The USB adapter was unplugged or its driver reset.", "The PC went to sleep." },
                        new[] { "Replug the adapter; disable USB selective suspend for long logging runs." },
                        FixKind.Reconnect, "Reconnect"));
                    break;
                case Status.MemoryParityError:
                case Status.NegativeAcknowledge:
                case Status.UnknownException:
                    list.Add(A("The device returned exception " + ((int)status & 0x7F).ToString("X2"),
                        new[] { "Vendor-specific refusal; some meters only accept setting writes in a front-panel set-up mode." },
                        new[] { "Check the manual's exception list." }));
                    break;
                default:
                    list.Add(A(status.Text(), new[] { "No specific advice for this status." }, new string[0]));
                    break;
            }
            return list;
        }

        /// <summary>Statuses covered by the troubleshooting guide, in display order.</summary>
        public static readonly Status[] GuideStatuses =
        {
            Status.NoConnection, Status.Timeout, Status.CrcError, Status.BytesMissing, Status.ResponseError, Status.IllegalFunction,
            Status.IllegalDataAddress, Status.IllegalDataValue, Status.SlaveDeviceFailure, Status.SlaveDeviceBusy,
            Status.GatewayPathUnavailable, Status.GatewayTargetFailed, Status.TransactionIdError, Status.ByteCountError, Status.WriteError,
        };

        /// <summary>Plain-text guide: every status with causes and checks for a serial and a TCP connection.</summary>
        public static string Guide()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("MultiPoll Studio troubleshooting guide").AppendLine();
            var serial = new ConnectionSettings { Kind = ConnectionKind.Serial, Port = "COMx" };
            var tcp = new ConnectionSettings { Kind = ConnectionKind.Tcp, Host = "<IP>" };
            var d = new PollDefinition { Address = 1 };
            foreach (Status s in GuideStatuses)
            {
                sb.AppendLine("== " + s.Text() + " ==");
                var seen = new HashSet<string>();
                foreach (var c in new[] { serial, tcp })
                    foreach (Advice a in For(s, "", c, d))
                    {
                        if (!seen.Add(a.Title)) continue;
                        sb.AppendLine(a.Title + (c == tcp && s == Status.NoConnection ? " (network)" : c == serial && s == Status.NoConnection ? " (serial)" : ""));
                        foreach (string cause in a.Causes) sb.AppendLine("  cause: " + cause);
                        foreach (string check in a.Checks) sb.AppendLine("  check: " + check);
                        if (a.Fix != FixKind.None) sb.AppendLine("  one-click fix: " + a.FixLabel);
                    }
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
