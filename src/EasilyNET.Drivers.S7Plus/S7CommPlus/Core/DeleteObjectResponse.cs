// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

internal sealed class DeleteObjectResponse(byte protocolVersion, bool withIntegrityId) : IS7pResponse
{
    public byte TransportFlags { get; private set; }
    public ulong ReturnValue { get; private set; }
    public uint DeleteObjectId { get; private set; }

    public byte ProtocolVersion { get; set; } = protocolVersion;
    public ushort FunctionCode => Functioncode.DeleteObject;
    public ushort SequenceNumber { get; set; }
    public uint IntegrityId { get; set; }
    public bool WithIntegrityId { get; set; } = withIntegrityId; // When deleting the Sesssion Object-Id, there's no Integrity-Id!

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
        ret += S7p.DecodeUInt32(buffer, out var _deleteObjectId);
        DeleteObjectId = _deleteObjectId;
        if ((ReturnValue & 0x4000000000000000) > 0) // Error Extension
        {
            // Decode the error object, but don't use any informations from it. Must be processed on a higher level.
            var errorObject = new PObject();
            ret += S7p.DecodeObject(buffer, ref errorObject);
        }
        if (WithIntegrityId)
        {
            ret += S7p.DecodeUInt32Vlq(buffer, out var iid);
            IntegrityId = iid;
        }
        return ret;
    }

    public override string ToString()
    {
        var s = "";
        s += $"<DeleteObjectResponse>{Environment.NewLine}";
        s += $"<ProtocolVersion>{ProtocolVersion}</ProtocolVersion>{Environment.NewLine}";
        s += $"<SequenceNumber>{SequenceNumber}</SequenceNumber>{Environment.NewLine}";
        s += $"<TransportFlags>{TransportFlags}</TransportFlags>{Environment.NewLine}";
        s += $"<ResponseSet>{Environment.NewLine}";
        s += $"<ReturnValue>{ReturnValue}</ReturnValue>{Environment.NewLine}";
        s += $"<DeleteObjectId>{DeleteObjectId}</DeleteObjectId>{Environment.NewLine}";
        s += $"</ResponseSet>{Environment.NewLine}";
        s += $"<WithIntegrityId>{WithIntegrityId}</WithIntegrityId>{Environment.NewLine}";
        s += $"<IntegrityId>{IntegrityId}</IntegrityId>{Environment.NewLine}";
        s += $"</DeleteObjectResponse>{Environment.NewLine}";
        return s;
    }

    public static DeleteObjectResponse? DeserializeFromPdu(Stream pdu, bool withIntegrityId)
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
        if (function != Functioncode.DeleteObject)
        {
            return null;
        }
        var resp = new DeleteObjectResponse(protocolVersion, withIntegrityId);
        resp.Deserialize(pdu);
        return resp;
    }
}
