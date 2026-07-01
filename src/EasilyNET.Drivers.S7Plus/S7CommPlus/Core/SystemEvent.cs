// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

/// <summary>
/// These extended keep-alive telegrams came up with TIA V14, and are sent from the PLC or HMI.
/// There is a version of 16 bytes length and another with 22 bytes length.
/// The 22 byte version may contain a string like "LOGOUT", but this only after a DeleteObject.
/// In contrast to all other protocol functions, the values don't use the VLQ encoding!
/// </summary>
internal sealed class SystemEvent(byte protocolVersion)
{
    public byte TransportFlags { get; set; }
    public ulong ReturnValue { get; set; }
    public uint Reserved1 { get; private set; }
    public uint ConfirmedBytes { get; private set; }
    public uint Reserved2 { get; private set; }
    public uint Reserved3 { get; private set; }
    public bool IsData { get; set; }
    public PValue? Data { get; set; }
    public bool IsMessage { get; private set; }
    public string Message { get; private set; } = string.Empty;

    public byte ProtocolVersion { get; set; } = protocolVersion;

    public int Deserialize(Stream buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var ret = 0;

        S7p.DecodeUInt32(buffer, out var reserved1);
        Reserved1 = reserved1;
        S7p.DecodeUInt32(buffer, out var confirmedBytes);
        ConfirmedBytes = confirmedBytes;
        S7p.DecodeUInt32(buffer, out var reserved2);
        Reserved2 = reserved2;
        S7p.DecodeUInt32(buffer, out var reserved3);
        Reserved3 = reserved3;

        // If's possible that this is the end of the dataset, without data value or message string.
        var remaining_length = buffer.Length - buffer.Position;
        if (remaining_length >= 4)
        {
            // Heuristic check if next is a string or a struct
            S7p.DecodeUInt32(buffer, out var peekType);
            buffer.Position -= 4; // set position back
            if (remaining_length >= 4 && peekType == Datatype.Struct)
            {
                // binary coded
                // This seems to work like ordinary values, but without that VLQ encoding.
                // So a UDINT is always 4 bytes long.
                IsData = true;
                IsMessage = false;
                Data = PValue.Deserialize(buffer, disableVlq: true);
            }
        }
        if (!IsData && remaining_length > 0)
        {
            IsMessage = true;
            // raw string without header or end termination.
            S7p.DecodeWString(buffer, (int)remaining_length, out var message);
            Message = message;
        }
        return ret;
    }

    public bool IsFatalError()
    {
        // If we don't have a Data struct at all, then this is possibly just a kind of notification
        if (Data != null)
        {
            try
            {
                // We excpect Data is of type ValueStruct, and has a structmember "ReturnValue" 40305 of Type LInt
                var str = (ValueStruct)Data;
                var retval = (ValueLInt)str.GetStructElement(Ids.ReturnValue);
                // It's just guess that if the value is negative, then it's a fatal error and we need to disconnect
                return retval.Value < 0;
            }
            catch
            {
                // 解析失败（Data 非预期结构 / 缺少 ReturnValue 成员）不应判为致命而触发断连，按良性系统事件处理
                return false;
            }
        }
        return false;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<SystemEvent>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<ProtocolVersion>{ProtocolVersion}</ProtocolVersion>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<Reserved1>{Reserved1}</Reserved1>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<ConfirmedBytes>{ConfirmedBytes}</ConfirmedBytes>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<Reserved2>{Reserved2}</Reserved2>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<Reserved3>{Reserved3}</Reserved3>");
        if (IsData)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"<Data>{Data}</Data>");
            sb.AppendLine($"<Message></Message>");
        }
        else if (IsMessage)
        {
            sb.AppendLine($"<Data></Data>");
            sb.AppendLine(CultureInfo.InvariantCulture, $"<Message>{Message}</Message>");
        }
        else
        {
            sb.AppendLine($"<Data></Data>");
            sb.AppendLine($"<Message></Message>");
        }
        sb.AppendLine("</SystemEvent>");
        return sb.ToString();
    }

    public static SystemEvent? DeserializeFromPdu(Stream pdu)
    {
        // Special handling of ProtocolVersion, which is written to the stream before
        S7p.DecodeByte(pdu, out var protocolVersion);
        if (protocolVersion != Core.ProtocolVersion.SystemEvent)
        {
            return null;
        }
        var sysevt = new SystemEvent(protocolVersion);
        sysevt.Deserialize(pdu);
        return sysevt;
    }
}
