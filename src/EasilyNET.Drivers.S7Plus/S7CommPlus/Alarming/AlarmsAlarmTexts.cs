// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
using EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Alarming;

internal sealed class AlarmsAlarmTexts
{
    public int LanguageId { get; set; }
    public string Infotext { get; set; } = string.Empty;
    public string AlarmText { get; set; } = string.Empty;
    public string AdditionalText1 { get; set; } = string.Empty;
    public string AdditionalText2 { get; set; } = string.Empty;
    public string AdditionalText3 { get; set; } = string.Empty;
    public string AdditionalText4 { get; set; } = string.Empty;
    public string AdditionalText5 { get; set; } = string.Empty;
    public string AdditionalText6 { get; set; } = string.Empty;
    public string AdditionalText7 { get; set; } = string.Empty;
    public string AdditionalText8 { get; set; } = string.Empty;
    public string AdditionalText9 { get; set; } = string.Empty;

    // These two values we get in addition when browsing for the alarmtexts
    // Don't know if they are useful for something.
    public ushort UnknownValue1 { get; set; }
    public ushort UnknownValue2 { get; set; }

    public static AlarmsAlarmTexts FromNotificationBlob(ValueBlobSparseArray blob, int languageId)
    {
        ArgumentNullException.ThrowIfNull(blob, nameof(blob));
        var at = new AlarmsAlarmTexts();
        string s;
        int lcid;
        int textid;
        at.LanguageId = languageId;
        foreach (var v in blob.Value)
        {
            s = Utils.GetUtfString(v.Value.Value, 0, (uint)v.Value.Value.Length);
            // Values in older CPUs, from: 0xa09c8001..0xa09c800b (2694610945..2694610955)
            // Current CPUs use:           0x04070001..0x0407000b (  67567617..  67567627)
            // Where the left word is the language ID, 0x0407 = 1031, and the right word is the text id.
            // The blob may contain several languages. If you need them all, you need to call this multiple times.
            lcid = (int)(v.Key >> 16);
            textid = (int)(v.Key & 0xffff);
            if (lcid == languageId)
            {
                switch (textid)
                {
                    case 1:
                        at.Infotext = s;
                        break;
                    case 2:
                        at.AlarmText = s;
                        break;
                    case 3:
                        at.AdditionalText1 = s;
                        break;
                    case 4:
                        at.AdditionalText2 = s;
                        break;
                    case 5:
                        at.AdditionalText3 = s;
                        break;
                    case 6:
                        at.AdditionalText4 = s;
                        break;
                    case 7:
                        at.AdditionalText5 = s;
                        break;
                    case 8:
                        at.AdditionalText6 = s;
                        break;
                    case 9:
                        at.AdditionalText7 = s;
                        break;
                    case 10:
                        at.AdditionalText8 = s;
                        break;
                    case 11:
                        at.AdditionalText9 = s;
                        break;
                    default:
                        break;
                }
            }
        }
        return at;
    }

    public override string ToString()
    {
        return $"""
            <AlarmsAlarmTexts LanguageId="{LanguageId}">
            <Infotext>{Infotext}</Infotext>
            <AlarmText>{AlarmText}</AlarmText>
            <AdditionalText1>{AdditionalText1}</AdditionalText1>
            <AdditionalText2>{AdditionalText2}</AdditionalText2>
            <AdditionalText3>{AdditionalText3}</AdditionalText3>
            <AdditionalText4>{AdditionalText4}</AdditionalText4>
            <AdditionalText5>{AdditionalText5}</AdditionalText5>
            <AdditionalText6>{AdditionalText6}</AdditionalText6>
            <AdditionalText7>{AdditionalText7}</AdditionalText7>
            <AdditionalText8>{AdditionalText8}</AdditionalText8>
            <AdditionalText9>{AdditionalText9}</AdditionalText9>
            </AlarmsAlarmTexts>
            """;
    }
}
