// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

internal sealed class GetVarSubstreamedResponse(byte protocolVersion) : IS7pResponse
{
    public byte TransportFlags { get; private set; }
    public ulong ReturnValue { get; private set; }
    public PValue? Value { get; private set; }

    public byte ProtocolVersion { get; set; } = protocolVersion;
    public ushort FunctionCode => Functioncode.SetVariable;
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

        ret += S7p.DecodeByte(buffer, out _);

        Value = PValue.Deserialize(buffer);

        ret += S7p.DecodeUInt32Vlq(buffer, out var iid);
        IntegrityId = iid;
        return ret;
    }

    public override string ToString()
    {
        var s = "";
        s += $"<GetVarSubstreamedResponse>{Environment.NewLine}";
        s += $"<ProtocolVersion>{ProtocolVersion}</ProtocolVersion>{Environment.NewLine}";
        s += $"<SequenceNumber>{SequenceNumber}</SequenceNumber>{Environment.NewLine}";
        s += $"<TransportFlags>{TransportFlags}</TransportFlags>{Environment.NewLine}";
        s += $"<ResponseSet>{Environment.NewLine}";
        s += $"<ReturnValue>{ReturnValue}</ReturnValue>{Environment.NewLine}";
        s += $"</ResponseSet>{Environment.NewLine}";
        s += $"<IntegrityId>{IntegrityId}</IntegrityId>{Environment.NewLine}";
        s += $"</GetVarSubstreamedResponse>{Environment.NewLine}";
        return s;
    }

    public static GetVarSubstreamedResponse? DeserializeFromPdu(Stream pdu)
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
        if (function != Functioncode.GetVarSubStreamed)
        {
            return null;
        }
        var resp = new GetVarSubstreamedResponse(protocolVersion);
        resp.Deserialize(pdu);

        return resp;
    }
}
