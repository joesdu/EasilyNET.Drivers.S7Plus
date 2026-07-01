// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

internal sealed class CreateObjectRequest(byte protocolVersion, ushort seqNum, bool withIntegrityId) : IS7pRequest
{
    public byte TransportFlags { get; set; } = 0x36;
    public uint RequestId { get; set; }
    public PValue? RequestValue { get; set; }
    public PObject? RequestObject { get; set; }

    public uint SessionId { get; set; }
    public byte ProtocolVersion { get; set; } = protocolVersion;
    public ushort FunctionCode => Functioncode.CreateObject;
    public ushort SequenceNumber { get; set; } = seqNum;
    public uint IntegrityId { get; set; }
    public bool WithIntegrityId { get; set; } = withIntegrityId;

    public void SetRequestIdValue(uint requestId, PValue requestValue)
    {
        RequestId = requestId;
        RequestValue = requestValue;
    }

    public void SetRequestObject(PObject requestObject)
    {
        RequestObject = requestObject;
    }

    public void SetNullServerSessionData()
    {
        // Initializes the data for a Nullserver Session on connection setup.
        // SessionId is set automatically to Ids.ObjectNullServerSession when this object is sent, if there's no session Id.
        TransportFlags = 0x36;
        RequestId = Ids.ObjectServerSessionContainer;
        RequestValue = new ValueUDInt(0);
        RequestObject = new PObject(RID: Ids.GetNewRIDOnServer, CLSID: Ids.ClassServerSession, AID: Ids.None);
        RequestObject.AddAttribute(Ids.ServerSessionClientRID, new ValueRID(0x80c3c901));
        RequestObject.AddObject(new PObject(RID: Ids.GetNewRIDOnServer, CLSID: Ids.ClassSubscriptions, AID: Ids.None));
    }

    public byte ProtocolVersion1 => ProtocolVersion;

    public int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, Opcode.Request);
        ret += S7p.EncodeUInt16(buffer, 0);
        ret += S7p.EncodeUInt16(buffer, FunctionCode);
        ret += S7p.EncodeUInt16(buffer, 0);
        ret += S7p.EncodeUInt16(buffer, SequenceNumber);
        ret += S7p.EncodeUInt32(buffer, SessionId);
        ret += S7p.EncodeByte(buffer, TransportFlags);

        // Request set
        ret += S7p.EncodeUInt32(buffer, RequestId);
        ret += RequestValue?.Serialize(buffer) ?? 0;
        ret += S7p.EncodeUInt32(buffer, 0); // Unknown value 1

        if (WithIntegrityId)
        {
            ret += S7p.EncodeUInt32Vlq(buffer, IntegrityId);
        }

        // Object 
        ret += RequestObject?.Serialize(buffer) ?? 0;

        // Fill?
        ret += S7p.EncodeUInt32(buffer, 0);
        return ret;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<CreateObjectRequest>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<ProtocolVersion>{ProtocolVersion}</ProtocolVersion>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<SequenceNumber>{SequenceNumber}</SequenceNumber>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<SessionId>{SessionId}</SessionId>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<TransportFlags>{TransportFlags}</TransportFlags>");
        sb.AppendLine("<RequestSet>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<RequestId>{RequestId}</RequestId>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<RequestValue>{RequestValue}</RequestValue>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<RequestObject>{RequestObject}</RequestObject>");
        sb.AppendLine("</RequestSet>");
        sb.AppendLine("</CreateObjectRequest>");
        return sb.ToString();
    }
}
