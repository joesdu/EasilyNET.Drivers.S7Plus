// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
using EasilyNET.Drivers.S7Plus.S7CommPlus.ClientApi;

namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

internal sealed class GetMultiVariablesRequest(byte protocolVersion) : IS7pRequest
{
    public byte TransportFlags { get; set; } = 0x34;
    public uint LinkId { get; set; }       // for reading variables, this should be 0
    public List<ItemAddress> AddressList { get; private set; } = [];

    public uint SessionId { get; set; }
    public byte ProtocolVersion { get; set; } = protocolVersion;
    public ushort FunctionCode => Functioncode.GetMultiVariables;
    public ushort SequenceNumber { get; set; }
    public uint IntegrityId { get; set; }
    public bool WithIntegrityId { get; set; } = true;

    public int Serialize(Stream buffer)
    {
        var ret = 0;
        uint fieldCount = 0;
        ret += S7p.EncodeByte(buffer, Opcode.Request);
        ret += S7p.EncodeUInt16(buffer, 0);                               // Reserved
        ret += S7p.EncodeUInt16(buffer, FunctionCode);
        ret += S7p.EncodeUInt16(buffer, 0);                               // Reserved
        ret += S7p.EncodeUInt16(buffer, SequenceNumber);
        ret += S7p.EncodeUInt32(buffer, SessionId);
        ret += S7p.EncodeByte(buffer, TransportFlags);

        // Request set
        ret += S7p.EncodeUInt32(buffer, LinkId);
        ret += S7p.EncodeUInt32Vlq(buffer, (uint)AddressList.Count);
        foreach (var adr in AddressList)
        {
            fieldCount += adr.NumberOfFields;
        }
        ret += S7p.EncodeUInt32Vlq(buffer, fieldCount);

        foreach (var adr in AddressList)
        {
            ret += adr.Serialize(buffer);
        }
        ret += S7p.EncodeObjectQualifier(buffer);

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
            <GetMultiVariablesRequest>
            <ProtocolVersion>{ProtocolVersion}</ProtocolVersion>
            <SequenceNumber>{SequenceNumber}</SequenceNumber>
            <SessionId>{SessionId}</SessionId>
            <TransportFlags>{TransportFlags}</TransportFlags>
            <RequestSet>
            <LinkId>{LinkId}</LinkId>
            <ItemCount>{AddressList.Count}</ItemCount>
            <NumberOfFields>{AddressList.Sum(c => c.NumberOfFields)}</NumberOfFields>
            <AddressList>
            {string.Join(Environment.NewLine, AddressList.Select(adr => adr.ToString()))}
            </AddressList>
            </RequestSet>
            </GetMultiVariablesRequest>
            """;
    }
}
