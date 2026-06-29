// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
using EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Alarming;

internal enum SubtypeIds
{
    Coming = Ids.DAI_Coming,
    Going = Ids.DAI_Going,
    None = 0
}

internal sealed class AlarmsAsCgs
{
    public SubtypeIds SubtypeId { get; set; } // This is not part of the class, but is neccessary to store the information: 2673 = DAI.Coming, 2677 = DAI.Going

    public byte AllStatesInfo { get; set; }
    public DateTime Timestamp { get; set; }
    public AlarmsAssociatedValues? AssociatedValues { get; set; }
    public DateTime AckTimestamp { get; set; } = DateTime.Now;

    public override string ToString()
    {
        return $"""
            <AlarmsAsCgs>
            <SubtypeId>{SubtypeId}</SubtypeId>
            <SubtypeIdName>{SubtypeId}</SubtypeIdName>
            <AllStatesInfo>{AllStatesInfo}</AllStatesInfo>
            <AssociatedValues>{AssociatedValues}</AssociatedValues>
            <Timestamp>{Timestamp}</Timestamp>
            <AckTimestamp>{AckTimestamp}</AckTimestamp>
            </AlarmsAsCgs>
            """;
    }

    public static AlarmsAsCgs FromValueStruct(ValueStruct str)
    {
        ArgumentNullException.ThrowIfNull(str, nameof(str));
        var asCgs = new AlarmsAsCgs
        {
            AllStatesInfo = ((ValueUSInt)str.GetStructElement(Ids.AS_CGS_AllStatesInfo)).GetValue(),
            Timestamp = Utils.DtFromValueTimestamp(((ValueTimestamp)str.GetStructElement(Ids.AS_CGS_Timestamp)).GetValue()),
            AssociatedValues = AlarmsAssociatedValues.FromValueBlob((ValueBlobArray)str.GetStructElement(Ids.AS_CGS_AssociatedValues)),
            AckTimestamp = Utils.DtFromValueTimestamp(((ValueTimestamp)str.GetStructElement(Ids.AS_CGS_AckTimestamp)).GetValue())
        };
        return asCgs;
    }
}
