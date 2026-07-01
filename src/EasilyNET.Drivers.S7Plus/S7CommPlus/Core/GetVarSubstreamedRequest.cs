// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

internal sealed class GetVarSubstreamedRequest(byte protocolVersion) : IS7pRequest
{
    public byte TransportFlags { get; private set; } = 0x34;

    public uint InObjectId { get; set; }

    public ushort Address { get; set; }

    public uint SessionId { get; set; }
    public byte ProtocolVersion { get; set; } = protocolVersion;
    public ushort FunctionCode => Functioncode.GetVarSubStreamed;
    public ushort SequenceNumber { get; set; }
    public uint IntegrityId { get; set; }
    public bool WithIntegrityId { get; set; } = true;

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

        // Request set
        ret += S7p.EncodeUInt32(buffer, InObjectId);
        ret += S7p.EncodeByte(buffer, 0x20); // Addressarray
        ret += S7p.EncodeByte(buffer, Datatype.UDInt);
        ret += S7p.EncodeByte(buffer, 1); // Array size
        ret += S7p.EncodeUInt32Vlq(buffer, Address);

        ret += S7p.EncodeObjectQualifier(buffer);
        // 2 Bytes unknown
        ret += S7p.EncodeUInt16(buffer, 0x0001);

        if (WithIntegrityId)
        {
            ret += S7p.EncodeUInt32Vlq(buffer, IntegrityId);
        }

        // Fill?
        ret += S7p.EncodeUInt32(buffer, 0);

        return ret;
    }

    public override string ToString()
    {
        return $"""
            <GetVarSubstreamedRequest>
            <ProtocolVersion>{ProtocolVersion}</ProtocolVersion>
            <SequenceNumber>{SequenceNumber}</SequenceNumber>
            <SessionId>{SessionId}</SessionId>
            <TransportFlags>{TransportFlags}</TransportFlags>
            <RequestSet>
            <InObjectId>{InObjectId}</InObjectId>
            <AddressList>
            <Id>{Address}</Id>
            </AddressList>
            <ValueList>
            </ValueList>
            </RequestSet>
            </GetVarSubstreamedRequest>
            """;
    }
}
