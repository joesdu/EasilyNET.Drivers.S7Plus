// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

internal sealed class ExploreResponse(byte protocolVersion) : IS7pResponse
{
    private byte _transportFlags;
    public byte TransportFlags { get => _transportFlags; set => _transportFlags = value; }
    private ulong _returnValue;
    public ulong ReturnValue { get => _returnValue; set => _returnValue = value; }
    private uint _exploreId;
    public uint ExploreId { get => _exploreId; set => _exploreId = value; }
    private List<PObject> _objects = [];
    public List<PObject> Objects { get => _objects; private set => _objects = value; }

    public byte ProtocolVersion { get; set; } = protocolVersion;
    public ushort FunctionCode => Functioncode.Explore;
    public ushort SequenceNumber { get; set; }
    public uint IntegrityId { get; set; }
    public bool WithIntegrityId { get; set; }

    public int Deserialize(Stream buffer)
    {
        var ret = 0;

        ret += S7p.DecodeUInt16(buffer, out var seqnr);
        SequenceNumber = seqnr;
        ret += S7p.DecodeByte(buffer, out _transportFlags);

        // Response Set
        ret += S7p.DecodeUInt64Vlq(buffer, out _returnValue);
        ret += S7p.DecodeUInt32(buffer, out _exploreId);

        if (WithIntegrityId)
        {
            ret += S7p.DecodeUInt32Vlq(buffer, out var iid);
            IntegrityId = iid;
        }

        // This is a List of objects
        Objects = [];
        ret += S7p.DecodeObjectList(buffer, ref _objects);
        return ret;
    }

    public override string ToString()
    {
        return $"""
            <ExploreResponse>
            <ProtocolVersion>{ProtocolVersion}</ProtocolVersion>
            <SequenceNumber>{SequenceNumber}</SequenceNumber>
            <TransportFlags>{TransportFlags}</TransportFlags>
            <ResponseSet>
            <ReturnValue>{ReturnValue}</ReturnValue>
            <ExploreId>{ExploreId}</ExploreId>
            <Objects>
            {string.Join(Environment.NewLine, Objects.Select(obj => obj.ToString()))}
            </Objects>
            </ResponseSet>
            </ExploreResponse>
            """;
    }

    public static ExploreResponse? DeserializeFromPdu(Stream pdu, bool withIntegrityId)
    {
        // Special handling of ProtocolVersion, which is written to the stream before
        S7p.DecodeByte(pdu, out var protocolVersion);
        S7p.DecodeByte(pdu, out var opcode);
        if (opcode != Opcode.Response)
        {
            return null;
        }
        S7p.DecodeUInt16(pdu, out var _);
        S7p.DecodeUInt16(pdu, out var function);
        S7p.DecodeUInt16(pdu, out _);
        if (function != Functioncode.Explore)
        {
            return null;
        }
        var resp = new ExploreResponse(protocolVersion)
        {
            WithIntegrityId = withIntegrityId
        };
        resp.Deserialize(pdu);

        return resp;
    }
}
