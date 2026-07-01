// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
using EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Alarming;

internal sealed class AlarmsDai
{
    public string? ObjectVariableTypeName { get; set; }

    public ulong CpuAlarmId { get; set; }
    public byte AllStatesInfo { get; set; }
    public ushort AlarmDomain { get; set; }
    public int MessageType { get; set; }
    public uint SequenceCounter { get; set; }
    public AlarmsAlarmTexts? AlarmTexts { get; set; }
    public AlarmsHmiInfo? HmiInfo { get; set; }
    public AlarmsAsCgs? AsCgs { get; set; }

    public override string ToString()
    {
        return $"""
            <AlarmsDai>
            <ObjectVariableTypeName>{ObjectVariableTypeName ?? string.Empty}</ObjectVariableTypeName>
            <CpuAlarmId>{CpuAlarmId}</CpuAlarmId>
            <AllStatesInfo>{AllStatesInfo}</AllStatesInfo>
            <AlarmDomain>{AlarmDomain}</AlarmDomain>
            <MessageType>{MessageType}</MessageType>
            <HmiInfo>{HmiInfo}</HmiInfo>
            <AsCgs>{AsCgs}</AsCgs>
            <SequenceCounter>{SequenceCounter}</SequenceCounter>
            <AlarmTexts>{AlarmTexts}</AlarmTexts>
            </AlarmsDai>
            """;
    }

    public static AlarmsDai? FromNotificationObject(PObject pobj, int alarmtextsLanguageId)
    {
        ArgumentNullException.ThrowIfNull(pobj, nameof(pobj));
        var dai = new AlarmsDai
        {
            ObjectVariableTypeName = ((ValueWString)pobj.GetAttribute(Ids.ObjectVariableTypeName)).Value,
            CpuAlarmId = ((ValueLWord)pobj.GetAttribute(Ids.DAI_CPUAlarmID)).Value,
            AllStatesInfo = ((ValueUSInt)pobj.GetAttribute(Ids.DAI_AllStatesInfo)).Value,
            AlarmDomain = ((ValueUInt)pobj.GetAttribute(Ids.DAI_AlarmDomain)).Value,
            MessageType = ((ValueDInt)pobj.GetAttribute(Ids.DAI_MessageType)).Value,
            HmiInfo = AlarmsHmiInfo.FromValueBlob((ValueBlob)pobj.GetAttribute(Ids.DAI_HmiInfo)),
            // TODO: Blob for additional values
            SequenceCounter = ((ValueUDInt)pobj.GetAttribute(Ids.DAI_SequenceCounter)).Value
        };
        ValueStruct? str = null;
        uint dai_id = 0;
        if (pobj.Attributes.ContainsKey(Ids.DAI_Coming))
        {
            str = (ValueStruct)pobj.GetAttribute(Ids.DAI_Coming);
            dai_id = Ids.DAI_Coming;
        }
        else if (pobj.Attributes.ContainsKey(Ids.DAI_Going))
        {
            str = (ValueStruct)pobj.GetAttribute(Ids.DAI_Going);
            dai_id = Ids.DAI_Going;
        }
        if (dai_id == 0)
        {
            return null;
        }
        dai.AsCgs = AlarmsAsCgs.FromValueStruct(str!);
        dai.AsCgs.SubtypeId = (SubtypeIds)dai_id;
        dai.AlarmTexts = AlarmsAlarmTexts.FromNotificationBlob((ValueBlobSparseArray)pobj.GetAttribute(Ids.DAI_AlarmTexts_Rid), alarmtextsLanguageId);
        return dai;
    }
}