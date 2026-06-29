// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

internal sealed class InitSslRequest(byte protocolVersion, ushort seqNum, uint sessionId) : IS7pRequest
{
    private byte TransportFlags { get; set; } = 0x30;

    public uint SessionId { get; set; } = sessionId;
    public byte ProtocolVersion { get; set; } = protocolVersion;
    public ushort FunctionCode => Functioncode.InitSsl;
    public ushort SequenceNumber { get; set; } = seqNum;
    public uint IntegrityId { get; set; }
    public bool WithIntegrityId { get; set; }

    public int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, Opcode.Request);
        ret += S7p.EncodeUInt16(buffer, 0);                               // Reserved
        ret += S7p.EncodeUInt16(buffer, FunctionCode);
        ret += S7p.EncodeUInt16(buffer, 0);                               // Reserved
        ret += S7p.EncodeUInt16(buffer, SequenceNumber);
        ret += S7p.EncodeUInt32(buffer, SessionId);
        ret += S7p.EncodeByte(buffer, TransportFlags);

        // Fill?
        ret += S7p.EncodeUInt32(buffer, 0);

        return ret;
    }

    public override string ToString()
    {
        return $"""
            <InitSslRequest>
            <ProtocolVersion>{ProtocolVersion}</ProtocolVersion>
            <SequenceNumber>{SequenceNumber}</SequenceNumber>
            <SessionId>{SessionId}</SessionId>
            <TransportFlags>{TransportFlags}</TransportFlags>
            </InitSslRequest>
            """;
    }
}
