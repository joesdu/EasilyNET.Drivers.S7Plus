// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

internal class GetMultiVariablesResponse(byte protocolVersion) : IS7pResponse
{
    public byte TransportFlags { get; private set; }
    public ulong ReturnValue { get; private set; }
    public Dictionary<uint, PValue> Values { get; private set; } = [];           // ItemNumber, Value
    public Dictionary<uint, ulong> ErrorValues { get; private set; } = [];      // ItemNumber, ReturnValue

    public byte ProtocolVersion { get; set; } = protocolVersion;
    public ushort FunctionCode => Functioncode.GetMultiVariables;
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

        // ValueList
        ret += S7p.DecodeUInt32Vlq(buffer, out var itemnr);
        while (itemnr > 0)
        {
            var v = PValue.Deserialize(buffer);
            Values.Add(itemnr, v);
            ret += S7p.DecodeUInt32Vlq(buffer, out itemnr);
        }

        // ErrorvalueList
        ret += S7p.DecodeUInt32Vlq(buffer, out itemnr);
        while (itemnr > 0)
        {
            ret += S7p.DecodeUInt64Vlq(buffer, out _);
            ErrorValues.Add(itemnr, 0);
            ret += S7p.DecodeUInt32Vlq(buffer, out itemnr);
        }
        ret += S7p.DecodeUInt32Vlq(buffer, out var iid);
        IntegrityId = iid;
        return ret;
    }

    public override string ToString()
    {
        var s = "";
        s += $"<GetMultiVariablesResponse>{Environment.NewLine}";
        s += $"<ProtocolVersion>{ProtocolVersion}</ProtocolVersion>{Environment.NewLine}";
        s += $"<SequenceNumber>{SequenceNumber}</SequenceNumber>{Environment.NewLine}";
        s += $"<TransportFlags>{TransportFlags}</TransportFlags>{Environment.NewLine}";
        s += $"<ResponseSet>{Environment.NewLine}";
        s += $"<ReturnValue>{ReturnValue}</ReturnValue>{Environment.NewLine}";

        s += $"<ValueList>{Environment.NewLine}";
        foreach (var value in Values)
        {
            s += $"<Value>{Environment.NewLine}";
            s += $"<ItemNr>{value.Key}</ItemNr>{Environment.NewLine}";
            s += value.Value.ToString();
            s += $"</Value>{Environment.NewLine}";
        }
        s += $"</ValueList>{Environment.NewLine}";

        s += $"<ErrorValueList>{Environment.NewLine}";
        foreach (var errval in ErrorValues)
        {
            s += $"<ErrorValue>{Environment.NewLine}";
            s += $"<ItemNr>{errval.Key}</ItemNr>{Environment.NewLine}";
            s += $"<ReturnValue>{errval.Value}</ReturnValue>{Environment.NewLine}";
            s += $"</ErrorValue>{Environment.NewLine}";
        }
        s += $"</ErrorValueList>{Environment.NewLine}";
        s += $"</ResponseSet>{Environment.NewLine}";
        s += $"<IntegrityId>{IntegrityId}</IntegrityId>{Environment.NewLine}";
        s += $"</GetMultiVariablesResponse>{Environment.NewLine}";
        return s;
    }

    public static GetMultiVariablesResponse? DeserializeFromPdu(Stream pdu)
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
        if (function != Functioncode.GetMultiVariables)
        {
            return null;
        }
        var resp = new GetMultiVariablesResponse(protocolVersion);
        resp.Deserialize(pdu);

        return resp;
    }
}
