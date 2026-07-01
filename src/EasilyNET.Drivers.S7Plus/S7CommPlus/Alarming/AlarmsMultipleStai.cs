// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
using EasilyNET.Drivers.S7Plus.S7CommPlus.Core;
using System.Text;

namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Alarming;

internal sealed class AlarmsMultipleStai
{
    private ushort _alid;
    public ushort Alid
    {
        get => _alid; set => _alid = value;
    }
    private ushort _alarmDomain;
    /// <summary>
    /// 1=Systemdiagnose, 2=Security, 256..272 = UserClass_0..UserClass_16
    /// </summary>
    public ushort AlarmDomain
    {
        get => _alarmDomain; set => _alarmDomain = value;
    }
    private ushort _messageType;
    /// <summary>
    /// 1=Alarm AP, 2=Notify AP, 3=Info Report AP, 4=Event Ack AP
    /// </summary>
    public ushort MessageType
    {
        get => _messageType; set => _messageType = value;
    }

    private byte _alarmEnabled;
    /// <summary>
    /// 0=No, 1=Yes
    /// </summary>
    public byte AlarmEnabled
    {
        get => _alarmEnabled; set => _alarmEnabled = value;
    }

    private ushort _hmiInfoLength;
    public ushort HmiInfoLength
    {
        get => _hmiInfoLength; set => _hmiInfoLength = value;
    }
    public AlarmsHmiInfo? HmiInfo { get; set; }
    private ushort _lidCount;
    public ushort LidCount
    {
        get => _lidCount; set => _lidCount = value;
    }
    public List<uint>? Lids { get; private set; }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<AlarmsMultipleStai>");
        sb.AppendLine($"<Alid>{Alid}</Alid>");
        sb.AppendLine($"<AlarmDomain>{AlarmDomain}</AlarmDomain>");
        sb.AppendLine($"<MessageType>{MessageType}</MessageType>");
        sb.AppendLine($"<AlarmEnabled>{AlarmEnabled}</AlarmEnabled>");
        sb.AppendLine($"<HmiInfoLength>{HmiInfoLength}</HmiInfoLength>");
        sb.AppendLine($"<HmiInfo>{Environment.NewLine}{HmiInfo}{Environment.NewLine}</HmiInfo>");
        sb.AppendLine($"<LidCount>{LidCount}</LidCount>");
        if (Lids != null)
        {
            foreach (var li in Lids)
            {
                sb.AppendLine($"<Lid>{li}</Lid>");
            }
        }
        sb.AppendLine("</AlarmsMultipleStai>");
        return sb.ToString();
    }

    public int Deserialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.DecodeUInt16(buffer, out _alid);
        ret += S7p.DecodeUInt16(buffer, out _alarmDomain);
        ret += S7p.DecodeUInt16(buffer, out _messageType);
        ret += S7p.DecodeByte(buffer, out _alarmEnabled);
        ret += S7p.DecodeUInt16(buffer, out _hmiInfoLength);
        HmiInfo = new AlarmsHmiInfo();
        ret += HmiInfo.Deserialize(buffer);
        ret += S7p.DecodeUInt16(buffer, out _lidCount);
        Lids = [with(_lidCount)];
        for (var i = 0; i < _lidCount; i++)
        {
            ret += S7p.DecodeUInt32(buffer, out var lid);
            Lids[i] = lid;
        }
        return ret;
    }
}
