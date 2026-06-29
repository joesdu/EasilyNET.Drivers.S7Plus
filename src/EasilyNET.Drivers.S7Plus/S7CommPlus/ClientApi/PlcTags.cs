// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
using EasilyNET.Drivers.S7Plus.S7CommPlus.Core;
using Microsoft.Extensions.Logging;

namespace EasilyNET.Drivers.S7Plus.S7CommPlus.ClientApi;

internal static class PlcTags
{
    public static int ReadTags(this S7CommPlusConnection conn, List<PlcTag> plcTags)
    {
        var readlist = new List<ItemAddress>();
        ArgumentNullException.ThrowIfNull(conn, nameof(conn));
        ArgumentNullException.ThrowIfNull(plcTags, nameof(plcTags));
        readlist.AddRange(from tag in plcTags select tag.Address);
        var res = conn.ReadValues(readlist, out var values, out var errors);

        if (res == 0)
        {
            var idx = 0;
            foreach (var tag in plcTags)
            {
                tag.ProcessReadResult(values[idx], errors[idx]);
                idx++;
            }
        }
        else
        {
            S7Log.Instance?.LogDebug("ReadTags: Error res=" + res);
        }
        return res;
    }

    public static int WriteTags(this S7CommPlusConnection conn, List<PlcTag> plcTags)
    {
        ArgumentNullException.ThrowIfNull(conn, nameof(conn));
        ArgumentNullException.ThrowIfNull(plcTags, nameof(plcTags));
        var writelist = new List<ItemAddress>();
        var values = new List<PValue>();
        foreach (var tag in plcTags)
        {
            writelist.Add(tag.Address);
            values.Add(tag.GetWriteValue());
        }

        var res = conn.WriteValues(writelist, values, out var errors);

        if (res == 0)
        {
            var idx = 0;
            foreach (var tag in plcTags)
            {
                tag.ProcessWriteResult(errors[idx]);
                idx++;
            }
        }
        else
        {
            S7Log.Instance?.LogDebug("WriteTags: Error res=" + res);
        }
        return res;
    }

    public static PlcTag? TagFactory(string name, ItemAddress address, uint softdatatype, bool Is1Dim = false, ILogger? logger = null)
    {
        switch (softdatatype)
        {
            case Softdatatype.S7COMMP_SOFTDATATYPE_BOOL:
                if (Is1Dim)
                {
                    return new PlcTagBoolArray(name, address, softdatatype);
                }

                return new PlcTagBool(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_BYTE:
                if (Is1Dim)
                {
                    return new PlcTagByteArray(name, address, softdatatype);
                }

                return new PlcTagByte(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_CHAR:
                return new PlcTagChar(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_WORD:
                if (Is1Dim)
                {
                    return new PlcTagWordArray(name, address, softdatatype);
                }

                return new PlcTagWord(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_INT:
                if (Is1Dim)
                {
                    return new PlcTagIntArray(name, address, softdatatype);
                }

                return new PlcTagInt(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_DWORD:
                if (Is1Dim)
                {
                    return new PlcTagDWordArray(name, address, softdatatype);
                }

                return new PlcTagDWord(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_DINT:
                if (Is1Dim)
                {
                    return new PlcTagDIntArray(name, address, softdatatype);
                }

                return new PlcTagDInt(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_REAL:
                if (Is1Dim)
                {
                    return new PlcTagRealArray(name, address, softdatatype);
                }

                return new PlcTagReal(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_DATE:
                return new PlcTagDate(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_TIMEOFDAY:
                return new PlcTagTimeOfDay(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_TIME:
                return new PlcTagTime(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_S5TIME:
                return new PlcTagS5Time(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_DATEANDTIME:
                if (Is1Dim)
                {
                    return new PlcTagDateAndTimeArray(name, address, softdatatype);
                }

                return new PlcTagDateAndTime(name, address, softdatatype);

            case Softdatatype.S7COMMP_SOFTDATATYPE_STRING:
                if (Is1Dim)
                {
                    return new PlcTagStringArray(name, address, softdatatype);
                }

                return new PlcTagString(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_POINTER:
                return new PlcTagPointer(name, address, softdatatype);

            case Softdatatype.S7COMMP_SOFTDATATYPE_ANY:
                return new PlcTagAny(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_BLOCKFB:
                return new PlcTagUInt(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_BLOCKFC:
                return new PlcTagUInt(name, address, softdatatype);

            case Softdatatype.S7COMMP_SOFTDATATYPE_COUNTER:
                return new PlcTagUInt(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_TIMER:
                return new PlcTagUInt(name, address, softdatatype);

            case Softdatatype.S7COMMP_SOFTDATATYPE_BBOOL:
                if (Is1Dim)
                {
                    return new PlcTagBoolArray(name, address, softdatatype);
                }

                return new PlcTagBool(name, address, softdatatype);

            case Softdatatype.S7COMMP_SOFTDATATYPE_LREAL:
                return new PlcTagLReal(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_ULINT:
                return new PlcTagULInt(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_LINT:
                return new PlcTagLInt(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_LWORD:
                return new PlcTagLWord(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_USINT:
                if (Is1Dim)
                {
                    return new PlcTagUSIntArray(name, address, softdatatype);
                }

                return new PlcTagUSInt(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_UINT:
                if (Is1Dim)
                {
                    return new PlcTagUIntArray(name, address, softdatatype);
                }

                return new PlcTagUInt(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_UDINT:
                if (Is1Dim)
                {
                    return new PlcTagUDIntArray(name, address, softdatatype);
                }

                return new PlcTagUDInt(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_SINT:
                if (Is1Dim)
                {
                    return new PlcTagSIntArray(name, address, softdatatype);
                }

                return new PlcTagSInt(name, address, softdatatype);

            case Softdatatype.S7COMMP_SOFTDATATYPE_WCHAR:
                return new PlcTagWChar(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_WSTRING:
                return new PlcTagWString(name, address, softdatatype);
            //case Softdatatype.S7COMMP_SOFTDATATYPE_VARIANT:
            //-> Variant isn't added inside of the instance-db as a variable!
            case Softdatatype.S7COMMP_SOFTDATATYPE_LTIME:
                return new PlcTagLTime(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_LTOD:
                return new PlcTagLTOD(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_LDT:
                return new PlcTagLDT(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_DTL:
                return new PlcTagDTL(name, address, softdatatype);

            case Softdatatype.S7COMMP_SOFTDATATYPE_REMOTE:
                return new PlcTagAny(name, address, softdatatype);

            case Softdatatype.S7COMMP_SOFTDATATYPE_AOMIDENT:
                return new PlcTagDWord(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_EVENTANY:
                return new PlcTagDWord(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_EVENTATT:
                return new PlcTagDWord(name, address, softdatatype);

            case Softdatatype.S7COMMP_SOFTDATATYPE_AOMAID:
                return new PlcTagDWord(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_AOMLINK:
                return new PlcTagDWord(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_EVENTHWINT:
                return new PlcTagDWord(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_HWANY:
                return new PlcTagWord(name, address, softdatatype);

            case Softdatatype.S7COMMP_SOFTDATATYPE_HWIOSYSTEM:
                return new PlcTagWord(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_HWDPMASTER:
                return new PlcTagWord(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_HWDEVICE:
                return new PlcTagWord(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_HWDPSLAVE:
                return new PlcTagWord(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_HWIO:
                return new PlcTagWord(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_HWMODULE:
                return new PlcTagWord(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_HWSUBMODULE:
                return new PlcTagWord(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_HWHSC:
                return new PlcTagWord(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_HWPWM:
                return new PlcTagWord(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_HWPTO:
                return new PlcTagWord(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_HWINTERFACE:
                return new PlcTagWord(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_HWIEPORT:
                return new PlcTagWord(name, address, softdatatype);

            case Softdatatype.S7COMMP_SOFTDATATYPE_OBANY:
                return new PlcTagInt(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_OBDELAY:
                return new PlcTagInt(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_OBTOD:
                return new PlcTagInt(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_OBCYCLIC:
                return new PlcTagInt(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_OBATT:
                return new PlcTagInt(name, address, softdatatype);

            case Softdatatype.S7COMMP_SOFTDATATYPE_CONNANY:
                return new PlcTagWord(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_CONNPRG:
                return new PlcTagWord(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_CONNOUC:
                return new PlcTagWord(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_CONNRID:
                return new PlcTagDWord(name, address, softdatatype);

            case Softdatatype.S7COMMP_SOFTDATATYPE_PORT:
                return new PlcTagUInt(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_RTM:
                return new PlcTagUInt(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_PIP:
                return new PlcTagUInt(name, address, softdatatype);

            case Softdatatype.S7COMMP_SOFTDATATYPE_OBPCYCLE:
                return new PlcTagInt(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_OBHWINT:
                return new PlcTagInt(name, address, softdatatype);

            case Softdatatype.S7COMMP_SOFTDATATYPE_OBDIAG:
                return new PlcTagInt(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_OBTIMEERROR:
                return new PlcTagInt(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_OBSTARTUP:
                return new PlcTagInt(name, address, softdatatype);

            case Softdatatype.S7COMMP_SOFTDATATYPE_DBANY:
                return new PlcTagUInt(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_DBWWW:
                return new PlcTagUInt(name, address, softdatatype);
            case Softdatatype.S7COMMP_SOFTDATATYPE_DBDYN:
                return new PlcTagUInt(name, address, softdatatype);

            default:
                (logger ?? S7Log.Instance)?.LogDebug($"ERROR: Unknown softdatatype={softdatatype} for variable= {name}");
                return null;
        }
    }
}
