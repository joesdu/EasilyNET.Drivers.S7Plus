// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

internal sealed class ExploreRequest(byte protocolVersion) : IS7pRequest
{
    public byte TransportFlags { get; set; } = 0x34; // or 0x36???
    public uint ExploreId { get; set; }
    public uint ExploreRequestId { get; set; }
    public byte ExploreChildsRecursive { get; set; }
    public byte ExploreParents { get; set; }
    public ValueStruct? FilterData { get; set; }
    public List<uint> AddressList { get; private set; } = [];

    public uint SessionId { get; set; }
    public byte ProtocolVersion { get; set; } = protocolVersion;
    public ushort FunctionCode => Functioncode.Explore;
    public ushort SequenceNumber { get; set; }
    public uint IntegrityId { get; set; }
    public bool WithIntegrityId { get; set; } = true;

    public byte ProtocolVersion1 => ProtocolVersion;

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
        ret += S7p.EncodeUInt32(buffer, ExploreId);
        ret += S7p.EncodeUInt32Vlq(buffer, ExploreRequestId);
        ret += S7p.EncodeByte(buffer, ExploreChildsRecursive);
        ret += S7p.EncodeByte(buffer, 1);                                   // unknown 0 or 1?
        ret += S7p.EncodeByte(buffer, ExploreParents);

        if (FilterData != null)
        {
            ret += S7p.EncodeByte(buffer, 1); // 1 object / value

            // TODO / Experimental:
            // Not 100% sure about how this has to be used:
            // On a Struct, we don't write the datatypeflags into the stream.
            // Maybe the byte before are the flags (which is the way I have it in the Wireshark dissector so far, which may be wrong).
            // To get this working, the byte which gas given the number of addresses isn't written to the stream anymore.
            // ret += S7p.EncodeByte(buffer, 0); // 0 address
            ret += FilterData.Serialize(buffer);
        }

        ret += S7p.EncodeByte(buffer, 0);                                   // Number of following Objects / unknown

        ret += S7p.EncodeUInt32Vlq(buffer, (uint)AddressList.Count);      // in Wireshark Dissector only 1 Byte, but maybe a VLQ
        foreach (var id in AddressList)
        {
            ret += S7p.EncodeUInt32Vlq(buffer, id);
        }
        if (WithIntegrityId)
        {
            ret += S7p.EncodeUInt32Vlq(buffer, IntegrityId);
        }
        // Fill?
        ret += S7p.EncodeUInt32(buffer, 0);
        // Plcsim V13 with Integrity Id needs here 5 Bytes, with 4 doesn't work (not responding).
        // But with my old 1200er FW2.2 it's still working with 4.
        ret += S7p.EncodeByte(buffer, 0);

        return ret;
    }

    public override string ToString()
    {
        return $"""
            <ExploreRequest>
            <ProtocolVersion>{ProtocolVersion}</ProtocolVersion>
            <SequenceNumber>{SequenceNumber}</SequenceNumber>
            <SessionId>{SessionId}</SessionId>
            <TransportFlags>{TransportFlags}</TransportFlags>
            <RequestSet>
            <ExploreId>{ExploreId}</ExploreId>
            <ExploreRequestId>{ExploreRequestId}</ExploreRequestId>
            <ExploreChildsRecursive>{ExploreChildsRecursive}</ExploreChildsRecursive>
            <ExploreParents>{ExploreParents}</ExploreParents>
            <AddressList>
            {string.Join(Environment.NewLine, AddressList.Select(id => $"<Id>{id}</Id>"))}
            </AddressList>
            </RequestSet>
            </ExploreRequest>
            """;
    }
}
