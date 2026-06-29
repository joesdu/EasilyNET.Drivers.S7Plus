// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

internal sealed class CreateObjectResponse(byte protocolVersion) : IS7pResponse
{
    private byte _transportFlags;
    public byte TransportFlags { get => _transportFlags; set => _transportFlags = value; }
    private ulong _returnValue;
    public ulong ReturnValue { get => _returnValue; set => _returnValue = value; }
    private byte _objectIdCount;
    public byte ObjectIdCount { get => _objectIdCount; set => _objectIdCount = value; }
    public List<uint>? ObjectIds { get; private set; }
    public PObject? ResponseObject { get; private set; }

    public byte ProtocolVersion { get; set; } = protocolVersion;
    public ushort FunctionCode => Functioncode.CreateObject;
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
        ret += S7p.DecodeByte(buffer, out _objectIdCount);

        ObjectIds = [with(_objectIdCount)];
        for (var i = 0; i < _objectIdCount; i++)
        {
            ret += S7p.DecodeUInt32Vlq(buffer, out var object_id);
            ObjectIds?.Add(object_id);
        }
        PObject? _responseObject = null;
        ret += S7p.DecodeObject(buffer, ref _responseObject);
        ResponseObject = _responseObject;
        return ret;
    }

    public override string ToString()
    {
        var s = "";
        s += $"<CreateObjectResponse>{Environment.NewLine}";
        s += $"<ProtocolVersion>{ProtocolVersion}</ProtocolVersion>{Environment.NewLine}";
        s += $"<SequenceNumber>{SequenceNumber}</SequenceNumber>{Environment.NewLine}";
        s += $"<TransportFlags>{TransportFlags}</TransportFlags>{Environment.NewLine}";
        s += $"<ResponseSet>{Environment.NewLine}";
        s += $"<ReturnValue>{ReturnValue}</ReturnValue>{Environment.NewLine}";
        s += $"<ObjectIdCount>{ObjectIdCount}</ObjectIdCount>{Environment.NewLine}";
        if (ObjectIds != null)
        {
            foreach (var id in ObjectIds)
            {
                s += $"<ObjectId>{id}</ObjectId>{Environment.NewLine}";
            }
        }
        s += $"<ResponseObject>{ResponseObject?.ToString()}</ResponseObject>{Environment.NewLine}";
        s += $"</ResponseSet>{Environment.NewLine}";
        s += $"</CreateObjectResponse>{Environment.NewLine}";
        return s;
    }

    public static CreateObjectResponse? DeserializeFromPdu(Stream pdu)
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
        if (function != Functioncode.CreateObject)
        {
            return null;
        }
        var resp = new CreateObjectResponse(protocolVersion);
        resp.Deserialize(pdu);
        return resp;
    }
}
