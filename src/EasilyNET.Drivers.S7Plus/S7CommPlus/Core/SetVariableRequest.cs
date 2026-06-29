// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

internal sealed class SetVariableRequest(byte protocolVersion) : IS7pRequest
{
    public byte TransportFlags { get; set; } = 0x34;

    public uint InObjectId { get; set; }

    public uint Address { get; set; }
    public required PValue Value { get; set; }

    public uint SessionId { get; set; }
    public byte ProtocolVersion { get; set; } = protocolVersion;
    public ushort FunctionCode => Functioncode.SetVariable;
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
        ret += S7p.EncodeUInt32Vlq(buffer, 1); // Always 1 (?)
        ret += S7p.EncodeUInt32Vlq(buffer, Address);
        ret += Value.Serialize(buffer);

        ret += S7p.EncodeObjectQualifier(buffer);
        // 1 Byte unknown
        ret += S7p.EncodeByte(buffer, 0x00);

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
            <SetVariableRequest>
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
            <Value>{Value}</Value>
            </ValueList>
            </RequestSet>
            </SetVariableRequest>
            """;
    }
}
