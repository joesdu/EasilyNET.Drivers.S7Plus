// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

internal sealed class SetVariableResponse(byte protocolVersion) : IS7pResponse
{
    public byte TransportFlags { get; private set; }
    public ulong ReturnValue { get; private set; }

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
        ret += S7p.DecodeUInt32Vlq(buffer, out var iid);
        IntegrityId = iid;
        return ret;
    }

    public override string ToString()
    {
        return $"""
            <SetVariableResponse>
            <ProtocolVersion>{ProtocolVersion}</ProtocolVersion>
            <SequenceNumber>{SequenceNumber}</SequenceNumber>
            <TransportFlags>{TransportFlags}</TransportFlags>
            <ResponseSet>
            <ReturnValue>{ReturnValue}</ReturnValue>
            </ResponseSet>
            <IntegrityId>{IntegrityId}</IntegrityId>
            </SetVariableResponse>
            """;
    }

    public static SetVariableResponse? DeserializeFromPdu(Stream pdu)
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
        if (function != Functioncode.SetVariable)
        {
            return null;
        }
        var resp = new SetVariableResponse(protocolVersion);
        resp.Deserialize(pdu);

        return resp;
    }
}
