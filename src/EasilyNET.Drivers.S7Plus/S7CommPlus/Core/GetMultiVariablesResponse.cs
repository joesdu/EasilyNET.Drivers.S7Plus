// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

internal sealed class GetMultiVariablesResponse(byte protocolVersion) : IS7pResponse
{
    public byte TransportFlags { get; private set; }
    public ulong ReturnValue { get; private set; }
    public Dictionary<uint, PValue> Values { get; private set; } = [];           // ItemNumber, Value
    public Dictionary<uint, ulong> ErrorValues { get; private set; } = [];      // ItemNumber, ReturnValue

    public byte ProtocolVersion { get; set; } = protocolVersion;
    public ushort FunctionCode => Functioncode.GetMultiVariables;
    public ushort SequenceNumber { get; set; }
    public uint IntegrityId { get; set; }
    public bool WithIntegrityId { get; set; } = true;

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
        if ((ReturnValue & 0x4000000000000000) > 0) // Error Extension
        {
            // 消费掉错误对象以保持流对齐（内容留待上层处理），否则后续 ValueList 解码会错位
            var errorObject = new PObject();
            ret += S7p.DecodeObject(buffer, ref errorObject);
        }
        ErrorValues.Clear();

        // ValueList
        ret += S7p.DecodeUInt32Vlq(buffer, out var itemnr);
        while (itemnr > 0)
        {
            var v = PValue.Deserialize(buffer);
            Values.Add(itemnr, v);
            ret += S7p.DecodeUInt32Vlq(buffer, out itemnr);
        }

        // ErrorvalueList
        ret += S7p.DecodeUInt32Vlq(buffer, out itemnr);
        while (itemnr > 0)
        {
            // 每项的 64 位返回码必须保留（原实现误丢弃存 0，导致读失败的 tag 被当作成功）
            ret += S7p.DecodeUInt64Vlq(buffer, out var retval);
            ErrorValues.Add(itemnr, retval);
            ret += S7p.DecodeUInt32Vlq(buffer, out itemnr);
        }
        ret += S7p.DecodeUInt32Vlq(buffer, out var iid);
        IntegrityId = iid;
        return ret;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<GetMultiVariablesResponse>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<ProtocolVersion>{ProtocolVersion}</ProtocolVersion>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<SequenceNumber>{SequenceNumber}</SequenceNumber>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<TransportFlags>{TransportFlags}</TransportFlags>");
        sb.AppendLine("<ResponseSet>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<ReturnValue>{ReturnValue}</ReturnValue>");
        sb.AppendLine("<ValueList>");
        foreach (var value in Values)
        {
            sb.AppendLine("<Value>");
            sb.AppendLine(CultureInfo.InvariantCulture, $"<ItemNr>{value.Key}</ItemNr>");
            sb.AppendLine(value.Value.ToString());
            sb.AppendLine("</Value>");
        }
        sb.AppendLine("</ValueList>");
        sb.AppendLine("<ErrorValueList>");
        foreach (var errval in ErrorValues)
        {
            sb.AppendLine("<ErrorValue>");
            sb.AppendLine(CultureInfo.InvariantCulture, $"<ItemNr>{errval.Key}</ItemNr>");
            sb.AppendLine(CultureInfo.InvariantCulture, $"<ReturnValue>{errval.Value}</ReturnValue>");
            sb.AppendLine("</ErrorValue>");
        }
        sb.AppendLine("</ErrorValueList>");
        sb.AppendLine("</ResponseSet>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<IntegrityId>{IntegrityId}</IntegrityId>");
        sb.AppendLine("</GetMultiVariablesResponse>");
        return sb.ToString();
    }

    public static GetMultiVariablesResponse? DeserializeFromPdu(Stream pdu)
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
        if (function != Functioncode.GetMultiVariables)
        {
            return null;
        }
        var resp = new GetMultiVariablesResponse(protocolVersion);
        resp.Deserialize(pdu);

        return resp;
    }
}
