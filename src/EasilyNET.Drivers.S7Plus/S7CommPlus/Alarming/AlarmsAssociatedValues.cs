// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
using EasilyNET.Drivers.S7Plus.S7CommPlus.Core;
using System.Globalization;
using System.Text;

namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Alarming;

internal sealed class AlarmsAssociatedValues
{
    public AssociatedValue? SD_1 { get; set; }
    public AssociatedValue? SD_2 { get; set; }
    public AssociatedValue? SD_3 { get; set; }
    public AssociatedValue? SD_4 { get; set; }
    public AssociatedValue? SD_5 { get; set; }
    public AssociatedValue? SD_6 { get; set; }
    public AssociatedValue? SD_7 { get; set; }
    public AssociatedValue? SD_8 { get; set; }
    public AssociatedValue? SD_9 { get; set; }
    public AssociatedValue? SD_10 { get; set; }

    public override string ToString()
    {
        return $"""
            <AlarmsAssociatedValues>
            <SD_1>{(SD_1 is null ? string.Empty : SD_1.ToString())}</SD_1>
            <SD_2>{(SD_2 is null ? string.Empty : SD_2.ToString())}</SD_2>
            <SD_3>{(SD_3 is null ? string.Empty : SD_3.ToString())}</SD_3>
            <SD_4>{(SD_4 is null ? string.Empty : SD_4.ToString())}</SD_4>
            <SD_5>{(SD_5 is null ? string.Empty : SD_5.ToString())}</SD_5>
            <SD_6>{(SD_6 is null ? string.Empty : SD_6.ToString())}</SD_6>
            <SD_7>{(SD_7 is null ? string.Empty : SD_7.ToString())}</SD_7>
            <SD_8>{(SD_8 is null ? string.Empty : SD_8.ToString())}</SD_8>
            <SD_9>{(SD_9 is null ? string.Empty : SD_9.ToString())}</SD_9>
            <SD_10>{(SD_10 is null ? string.Empty : SD_10.ToString())}</SD_10>
            </AlarmsAssociatedValues>
            """;
    }

    public static AlarmsAssociatedValues FromValueBlob(ValueBlobArray blob)
    {
        ArgumentNullException.ThrowIfNull(blob, nameof(blob));
        var av = new AlarmsAssociatedValues();
        var blobs = blob.Value;
        // Comes as Array[17], with indices:
        // 0 = Unknown Typeinformation, 4 Bytes
        // 1..10 = SD_1..SD_10
        //
        // The typeinformation at index 0 has a BlobRootId of 3476 = AS_CGS.AssociatedValues
        // When browsing 0x2000113 we get the result:
        // Type   Name
        // ---------------
        // UInt   Syntax
        // Byte   Aap
        var i = 0;
        AssociatedValue pv;
        foreach (var b in blobs)
        {
            var bytes = b.Value;
            switch (b.BlobRootId)
            {
                case Ids.TI_BOOL:
                    pv = new AssociatedValue(b.BlobRootId);
                    pv.SetBool(bytes[0] != 0);
                    av.SetSDValue(pv, i);
                    break;
                case Ids.TI_BYTE:
                    pv = new AssociatedValue(b.BlobRootId);
                    pv.SetInt(bytes[0]);
                    av.SetSDValue(pv, i);
                    break;
                case Ids.TI_CHAR:
                    pv = new AssociatedValue(b.BlobRootId);
                    pv.SetString(Encoding.GetEncoding("ISO-8859-1").GetString(bytes, 0, 1));
                    av.SetSDValue(pv, i);
                    break;
                case Ids.TI_WORD:
                    pv = new AssociatedValue(b.BlobRootId);
                    pv.SetInt(Utils.GetUInt16(bytes, 0));
                    av.SetSDValue(pv, i);
                    break;
                case Ids.TI_INT:
                    pv = new AssociatedValue(b.BlobRootId);
                    pv.SetInt(Utils.GetInt16(bytes, 0));
                    av.SetSDValue(pv, i);
                    break;
                case Ids.TI_DWORD:
                    pv = new AssociatedValue(b.BlobRootId);
                    pv.SetInt(Utils.GetUInt32(bytes, 0));
                    av.SetSDValue(pv, i);
                    break;
                case Ids.TI_DINT:
                    pv = new AssociatedValue(b.BlobRootId);
                    pv.SetInt(Utils.GetInt32(bytes, 0));
                    av.SetSDValue(pv, i);
                    break;
                case Ids.TI_REAL:
                    pv = new AssociatedValue(b.BlobRootId);
                    pv.SetReal(Utils.GetFloat(bytes, 0));
                    av.SetSDValue(pv, i);
                    break;
                case Ids.TI_LREAL:
                    pv = new AssociatedValue(b.BlobRootId);
                    pv.SetReal(Utils.GetDouble(bytes, 0));
                    av.SetSDValue(pv, i);
                    break;
                case Ids.TI_USINT:
                    pv = new AssociatedValue(b.BlobRootId);
                    pv.SetInt(bytes[0]);
                    av.SetSDValue(pv, i);
                    break;
                case Ids.TI_UINT:
                    pv = new AssociatedValue(b.BlobRootId);
                    pv.SetInt(Utils.GetUInt16(bytes, 0));
                    av.SetSDValue(pv, i);
                    break;
                case Ids.TI_UDINT:
                    pv = new AssociatedValue(b.BlobRootId);
                    pv.SetInt(Utils.GetUInt32(bytes, 0));
                    av.SetSDValue(pv, i);
                    break;
                case Ids.TI_SINT:
                    pv = new AssociatedValue(b.BlobRootId);
                    pv.SetInt((sbyte)bytes[0]);
                    av.SetSDValue(pv, i);
                    break;
                case Ids.TI_WCHAR:
                    pv = new AssociatedValue(b.BlobRootId);
                    pv.SetString(((char)Utils.GetUInt16(bytes, 0)).ToString());
                    av.SetSDValue(pv, i);
                    break;
                default:
                    if (b.BlobRootId is > Ids.TI_STRING_START and <= Ids.TI_STRING_END)
                    {
                        //byte s_maxlen = bytes[0]; // Don't need this value
                        var s_actlen = bytes[1];
                        pv = new AssociatedValue(Ids.TI_STRING);
                        pv.SetString(Encoding.GetEncoding("ISO-8859-1").GetString(bytes, 2, s_actlen));
                        av.SetSDValue(pv, i);
                    }
                    else if (b.BlobRootId is > Ids.TI_WSTRING_START and <= Ids.TI_WSTRING_END)
                    {
                        //int ws_maxlen = Utils.GetUInt16(bytes, 0); // Don't need this value
                        int ws_actlen = Utils.GetUInt16(bytes, 2);
                        pv = new AssociatedValue(Ids.TI_WSTRING);
                        pv.SetString(Encoding.BigEndianUnicode.GetString(bytes, 4, ws_actlen * 2));
                        av.SetSDValue(pv, i);
                    }
                    break;
            }
            i++;
            // All other elements have no value
            if (i > 10)
            {
                break;
            }
        }
        return av;
    }

    private void SetSDValue(AssociatedValue v, int index)
    {
        switch (index)
        {
            case 1: SD_1 = v; break;
            case 2: SD_2 = v; break;
            case 3: SD_3 = v; break;
            case 4: SD_4 = v; break;
            case 5: SD_5 = v; break;
            case 6: SD_6 = v; break;
            case 7: SD_7 = v; break;
            case 8: SD_8 = v; break;
            case 9: SD_9 = v; break;
            case 10: SD_10 = v; break;
            default:
                break;
        }
    }
}

internal class AssociatedValue(uint typeinfo)
{
    private bool ValueBool { get; set; }
    private long ValueInt { get; set; }
    private double ValueReal { get; set; }
    private string? ValueString { get; set; }

    private readonly uint TypeInfo = typeinfo;

    public void SetBool(bool value)
    {
        ValueBool = value;
    }

    public void SetInt(long value)
    {
        ValueInt = value;
    }

    public void SetReal(double value)
    {
        ValueReal = value;
    }

    public void SetString(string value)
    {
        ValueString = value;
    }

    public override string? ToString()
    {
        return TypeInfo switch
        {
            Ids.TI_BOOL => ValueBool.ToString(),
            Ids.TI_BYTE => ValueInt.ToString(CultureInfo.InvariantCulture),
            Ids.TI_CHAR => ValueString,
            Ids.TI_WORD => ValueInt.ToString(CultureInfo.InvariantCulture),
            Ids.TI_INT => ValueInt.ToString(CultureInfo.InvariantCulture),
            Ids.TI_DWORD => ValueInt.ToString(CultureInfo.InvariantCulture),
            Ids.TI_DINT => ValueInt.ToString(CultureInfo.InvariantCulture),
            Ids.TI_REAL => ValueReal.ToString(CultureInfo.InvariantCulture),
            Ids.TI_LREAL => ValueReal.ToString(CultureInfo.InvariantCulture),
            Ids.TI_USINT => ValueInt.ToString(CultureInfo.InvariantCulture),
            Ids.TI_UINT => ValueInt.ToString(CultureInfo.InvariantCulture),
            Ids.TI_UDINT => ValueInt.ToString(CultureInfo.InvariantCulture),
            Ids.TI_SINT => ValueInt.ToString(CultureInfo.InvariantCulture),
            Ids.TI_WCHAR => ValueString,
            Ids.TI_STRING => ValueString,
            Ids.TI_WSTRING => ValueString,
            _ => $"Unknown TypeInfo {TypeInfo}",
        };
    }
}
