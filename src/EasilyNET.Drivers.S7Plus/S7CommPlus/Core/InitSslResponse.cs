// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

internal sealed class InitSslResponse(byte protocolVersion) : IS7pResponse
{
    public byte TransportFlags;
    public ulong ReturnValue;

    public byte ProtocolVersion { get; set; } = protocolVersion;
    public ushort FunctionCode => Functioncode.InitSsl;
    public ushort SequenceNumber { get; set; }
    public uint IntegrityId { get; set; }
    public bool WithIntegrityId { get; set; }

    public int Deserialize(Stream buffer)
    {
        var ret = 0;

        ret += S7p.DecodeUInt16(buffer, out var seqnr);
        SequenceNumber = seqnr;
        ret += S7p.DecodeByte(buffer, out TransportFlags);

        // Response Set
        ret += S7p.DecodeUInt64Vlq(buffer, out ReturnValue);
        if ((ReturnValue & 0x4000000000000000) > 0) // Error Extension
        {
            // Decode the error object, but don't use any informations from it. Must be processed on a higher level.
            var errorObject = new PObject();
            ret += S7p.DecodeObject(buffer, ref errorObject);
        }

        return ret;
    }

    public override string ToString()
    {
        return $"""
            <InitSslResponse>
            <ProtocolVersion>{ProtocolVersion}</ProtocolVersion>
            <SequenceNumber>{SequenceNumber}</SequenceNumber>
            <TransportFlags>{TransportFlags}</TransportFlags>
            <ResponseSet>
            <ReturnValue>{ReturnValue}</ReturnValue>
            </ResponseSet>
            </InitSslResponse>
            """;
    }

    public static InitSslResponse? DeserializeFromPdu(Stream pdu)
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
        if (function != Functioncode.InitSsl)
        {
            return null;
        }
        var resp = new InitSslResponse(protocolVersion);
        resp.Deserialize(pdu);

        return resp;
    }
}
