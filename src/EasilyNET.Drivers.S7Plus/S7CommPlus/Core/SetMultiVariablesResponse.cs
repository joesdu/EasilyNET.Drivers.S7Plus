// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
using System.Text;

namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

internal sealed class SetMultiVariablesResponse(byte protocolVersion) : IS7pResponse
{
    public byte TransportFlags { get; private set; }
    public ulong ReturnValue { get; private set; }
    public Dictionary<uint, ulong> ErrorValues { get; private set; } = [];      // ItemNumber, ReturnValue

    public byte ProtocolVersion { get; set; } = protocolVersion;
    public ushort FunctionCode => Functioncode.SetMultiVariables;
    public ushort SequenceNumber { get; set; }
    public uint IntegrityId { get; set; }
    public bool WithIntegrityId { get; set; } = true;

    public int Deserialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.DecodeUInt16(buffer, out var seqnr);
        SequenceNumber = seqnr;
        ret += S7p.DecodeByte(buffer, out var _transportFlags);
        TransportFlags = _transportFlags;
        // Response Set
        ret += S7p.DecodeUInt64Vlq(buffer, out var _returnValue);
        ReturnValue = _returnValue;
        ErrorValues.Clear();
        ret += S7p.DecodeUInt32Vlq(buffer, out var itemnr);
        while (itemnr > 0)
        {
            ret += S7p.DecodeUInt64Vlq(buffer, out var retval);
            ErrorValues.Add(itemnr, retval);
            ret += S7p.DecodeUInt32Vlq(buffer, out itemnr); // TODO: Is this correct?
        }
        ret += S7p.DecodeUInt32Vlq(buffer, out var iid);
        IntegrityId = iid;
        return ret;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<SetMultiVariablesResponse>");
        sb.AppendLine($"<ProtocolVersion>{ProtocolVersion}</ProtocolVersion>");
        sb.AppendLine($"<SequenceNumber>{SequenceNumber}</SequenceNumber>");
        sb.AppendLine($"<TransportFlags>{TransportFlags}</TransportFlags>");
        sb.AppendLine("<ResponseSet>");
        sb.AppendLine($"<ReturnValue>{ReturnValue}</ReturnValue>");
        sb.AppendLine("<ErrorValueList>");
        foreach (var errval in ErrorValues)
        {
            sb.AppendLine("<ErrorValue>");
            sb.AppendLine($"<ItemNr>{errval.Key}</ItemNr>");
            sb.AppendLine($"<ReturnValue>{errval.Value}</ReturnValue>");
            sb.AppendLine("</ErrorValue>");
        }
        sb.AppendLine("</ErrorValueList>");
        sb.AppendLine("</ResponseSet>");
        sb.AppendLine($"<IntegrityId>{IntegrityId}</IntegrityId>");
        sb.AppendLine("</SetMultiVariablesResponse>");
        return sb.ToString();
    }

    public static SetMultiVariablesResponse? DeserializeFromPdu(Stream pdu)
    {
        // Special handling of ProtocolVersion, which is written to the stream before
        S7p.DecodeByte(pdu, out var protocolVersion);
        S7p.DecodeByte(pdu, out var opcode);
        if (opcode != Opcode.Response)
        {
            return null;
        }
        S7p.DecodeUInt16(pdu, out _);
        S7p.DecodeUInt16(pdu, out var function);
        S7p.DecodeUInt16(pdu, out _);
        if (function != Functioncode.SetMultiVariables)
        {
            return null;
        }
        var resp = new SetMultiVariablesResponse(protocolVersion);
        resp.Deserialize(pdu);

        return resp;
    }
}
