// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
using EasilyNET.Drivers.S7Plus.S7CommPlus.Core;
using System.Text;

namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Alarming;

//  7813 = DAI.HmiInfo
internal sealed class AlarmsHmiInfo
{
    private ushort _syntaxId;
    public ushort SyntaxId
    {
        get => _syntaxId; set => _syntaxId = value;
    }
    private ushort _version;
    public ushort Version
    {
        get => _version; set => _version = value;
    }
    private uint _clientAlarmId;
    public uint ClientAlarmId
    {
        get => _clientAlarmId; set => _clientAlarmId = value;
    }
    private byte _priority;
    public byte Priority
    {
        get => _priority; set => _priority = value;
    }
    private byte _reserved1;
    public byte Reserved1
    {
        get => _reserved1; set => _reserved1 = value;
    }
    private byte _reserved2;
    public byte Reserved2
    {
        get => _reserved2; set => _reserved2 = value;
    }
    private byte _reserved3;
    public byte Reserved3
    {
        get => _reserved3; set => _reserved3 = value;
    }
    private ushort _alarmClass;
    public ushort AlarmClass
    {
        get => _alarmClass; set => _alarmClass = value;
    }
    private byte _producer;
    public byte Producer
    {
        get => _producer; set => _producer = value;
    }
    private byte _groupId;
    public byte GroupId
    {
        get => _groupId; set => _groupId = value;
    }
    private byte _flags;
    public byte Flags
    {
        get => _flags; set => _flags = value;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<AlarmsHmiInfo>");
        sb.AppendLine($"<SyntaxId>{SyntaxId}</SyntaxId>");
        sb.AppendLine($"<Version>{Version}</Version>");
        sb.AppendLine($"<ClientAlarmId>{ClientAlarmId}</ClientAlarmId>");
        sb.AppendLine($"<Priority>{Priority}</Priority>");
        if (SyntaxId >= 257)
        {
            sb.AppendLine($"<Reserved1>{Reserved1}</Reserved1>");
            sb.AppendLine($"<Reserved2>{Reserved2}</Reserved2>");
            sb.AppendLine($"<Reserved3>{Reserved3}</Reserved3>");
            if (SyntaxId >= 258)
            {
                sb.AppendLine($"<AlarmClass>{AlarmClass}</AlarmClass>");
                sb.AppendLine($"<Producer>{Producer}</Producer>");
                sb.AppendLine($"<GroupId>{GroupId}</GroupId>");
                sb.AppendLine($"<Flags>{Flags}</Flags>");
            }
        }
        sb.AppendLine("</AlarmsHmiInfo>");
        return sb.ToString();
    }

    public int Deserialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.DecodeUInt16(buffer, out _syntaxId);
        ret += S7p.DecodeUInt16(buffer, out _version);
        ret += S7p.DecodeUInt32(buffer, out _clientAlarmId);
        ret += S7p.DecodeByte(buffer, out _priority);
        if (SyntaxId >= 257)
        {
            ret += S7p.DecodeByte(buffer, out _reserved1);
            ret += S7p.DecodeByte(buffer, out _reserved2);
            ret += S7p.DecodeByte(buffer, out _reserved3);
            if (SyntaxId >= 258)
            {
                ret += S7p.DecodeUInt16(buffer, out _alarmClass);
                ret += S7p.DecodeByte(buffer, out _producer);
                ret += S7p.DecodeByte(buffer, out _groupId);
                ret += S7p.DecodeByte(buffer, out _flags);
            }
        }
        return ret;
    }

    public static AlarmsHmiInfo FromValueBlob(ValueBlob blob)
    {
        ArgumentNullException.ThrowIfNull(blob);
        var hmiinfo = new AlarmsHmiInfo();
        var barr = blob.Value;
        uint pos = 0;
        hmiinfo.SyntaxId = Utils.GetUInt16(barr, pos);
        pos += 2;
        hmiinfo.Version = Utils.GetUInt16(barr, pos);
        pos += 2;
        hmiinfo.ClientAlarmId = Utils.GetUInt32(barr, pos);
        pos += 4;
        hmiinfo.Priority = Utils.GetUInt8(barr, pos);
        pos += 1;
        if (hmiinfo.SyntaxId >= 257)
        {
            hmiinfo.Reserved1 = Utils.GetUInt8(barr, pos);
            pos += 1;
            hmiinfo.Reserved2 = Utils.GetUInt8(barr, pos);
            pos += 1;
            hmiinfo.Reserved3 = Utils.GetUInt8(barr, pos);
            pos += 1;
            if (hmiinfo.SyntaxId >= 258)
            {
                hmiinfo.AlarmClass = Utils.GetUInt16(barr, pos);
                pos += 2;
                hmiinfo.Producer = Utils.GetUInt8(barr, pos);
                pos += 1;
                hmiinfo.GroupId = Utils.GetUInt8(barr, pos);
                pos += 1;
                hmiinfo.Flags = Utils.GetUInt8(barr, pos);
            }
        }
        return hmiinfo;
    }
}
