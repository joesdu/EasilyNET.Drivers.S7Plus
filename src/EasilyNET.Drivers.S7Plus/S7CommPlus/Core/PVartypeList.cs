// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

internal sealed class PVartypeList
{
    public List<PVartypeListElement> Elements { get; private set; } = [];
    public uint FirstId;

    public int Deserialize(Stream buffer)
    {
        var ret = 0;
        int maxret;
        Elements = [];

        ret += S7p.DecodeUInt16(buffer, out var blocklen);
        maxret = ret + blocklen;
        // This ID occurs only on the first block.
        ret += S7p.DecodeUInt32LE(buffer, out FirstId);

        while (blocklen > 0)
        {
            do
            {
                var elem = new PVartypeListElement();
                ret += elem.Deserialize(buffer);
                Elements.Add(elem);
            } while (ret < maxret);
            ret += S7p.DecodeUInt16(buffer, out blocklen);
            maxret = ret + blocklen;
        }
        return ret;
    }

    public override string ToString()
    {
        var s = "";
        s += "<VartypeList>" + Environment.NewLine;
        s += $"<FirstId>{FirstId}</FirstId>" + Environment.NewLine;
        var i = 1;
        foreach (var elem in Elements)
        {
            s += $"<Element index=\"{i}\">" + Environment.NewLine;
            s += elem.ToString();
            s += "</Element>" + Environment.NewLine;
            i++;
        }
        s += "</VartypeList>" + Environment.NewLine;
        return s;
    }
}

internal sealed class PVartypeListElement
{
    /* flags in tag description for 1500 */
    private const int S7COMMP_TAGDESCR_ATTRIBUTE2_OFFSETINFOTYPE = 0xf000;      /* Bits 13..16 */
    private const int S7COMMP_TAGDESCR_ATTRIBUTE2_HMIVISIBLE = 0x0800;          /* Bit 12 */
    private const int S7COMMP_TAGDESCR_ATTRIBUTE2_HMIREADONLY = 0x0400;         /* Bit 11 */
    private const int S7COMMP_TAGDESCR_ATTRIBUTE2_HMIACCESSIBLE = 0x0200;       /* Bit 10 */
    private const int S7COMMP_TAGDESCR_ATTRIBUTE2_BIT09 = 0x0100;               /* Bit 09 */
    private const int S7COMMP_TAGDESCR_ATTRIBUTE2_OPTIMIZEDACCESS = 0x0080;     /* Bit 08 */
    private const int S7COMMP_TAGDESCR_ATTRIBUTE2_SECTION = 0x0070;             /* Bits 05..07 */
    private const int S7COMMP_TAGDESCR_ATTRIBUTE2_BIT04 = 0x0008;               /* Bit 04 */
    private const int S7COMMP_TAGDESCR_ATTRIBUTE2_BITOFFSET = 0x0007;           /* Bits 01..03 */

    /* Offsetinfo type for tag description (S7-1500) */
    private const int S7COMMP_TAGDESCR_OFFSETINFOTYPE2_FB_ARRAY = 0;
    private const int S7COMMP_TAGDESCR_OFFSETINFOTYPE2_STRUCTELEM_STD = 1;
    private const int S7COMMP_TAGDESCR_OFFSETINFOTYPE2_STRUCTELEM_STRING = 2;
    private const int S7COMMP_TAGDESCR_OFFSETINFOTYPE2_STRUCTELEM_ARRAY1DIM = 3;
    private const int S7COMMP_TAGDESCR_OFFSETINFOTYPE2_STRUCTELEM_ARRAYMDIM = 4;
    private const int S7COMMP_TAGDESCR_OFFSETINFOTYPE2_STRUCTELEM_STRUCT = 5;
    private const int S7COMMP_TAGDESCR_OFFSETINFOTYPE2_STRUCTELEM_STRUCT1DIM = 6;
    private const int S7COMMP_TAGDESCR_OFFSETINFOTYPE2_STRUCTELEM_STRUCTMDIM = 7;
    private const int S7COMMP_TAGDESCR_OFFSETINFOTYPE2_STD = 8;
    private const int S7COMMP_TAGDESCR_OFFSETINFOTYPE2_STRING = 9;
    private const int S7COMMP_TAGDESCR_OFFSETINFOTYPE2_ARRAY1DIM = 10;
    private const int S7COMMP_TAGDESCR_OFFSETINFOTYPE2_ARRAYMDIM = 11;
    private const int S7COMMP_TAGDESCR_OFFSETINFOTYPE2_STRUCT = 12;
    private const int S7COMMP_TAGDESCR_OFFSETINFOTYPE2_STRUCT1DIM = 13;
    private const int S7COMMP_TAGDESCR_OFFSETINFOTYPE2_STRUCTMDIM = 14;
    private const int S7COMMP_TAGDESCR_OFFSETINFOTYPE2_PROGRAMALARM = 15;

    private const int S7COMMP_TAGDESCR_BITOFFSETINFO_RETAIN = 0x80;
    private const int S7COMMP_TAGDESCR_BITOFFSETINFO_NONOPTBITOFFSET = 0x70;
    private const int S7COMMP_TAGDESCR_BITOFFSETINFO_CLASSIC = 0x08;
    private const int S7COMMP_TAGDESCR_BITOFFSETINFO_OPTBITOFFSET = 0x07;

    public uint LID { get; private set; }
    public uint SymbolCrc { get; private set; }
    public uint Softdatatype { get; private set; }
    public ushort AttributeFlags { get; private set; }

    public int AttributeSection => (AttributeFlags & S7COMMP_TAGDESCR_ATTRIBUTE2_SECTION) >> 4;

    public int AttributeBitoffset => AttributeFlags & S7COMMP_TAGDESCR_ATTRIBUTE2_BITOFFSET;

    public bool AttributeFlagHmiVisible => (AttributeFlags & S7COMMP_TAGDESCR_ATTRIBUTE2_HMIVISIBLE) != 0;

    public bool AttributeFlagHmiAccessible => (AttributeFlags & S7COMMP_TAGDESCR_ATTRIBUTE2_HMIACCESSIBLE) != 0;

    public bool AttributeFlagOptimizedAccess => (AttributeFlags & S7COMMP_TAGDESCR_ATTRIBUTE2_OPTIMIZEDACCESS) != 0;

    public bool AttributeFlagHmiReadonly => (AttributeFlags & S7COMMP_TAGDESCR_ATTRIBUTE2_HMIREADONLY) != 0;

    public byte BitoffsetinfoFlags { get; set; }

    public bool BitoffsetinfoFlagRetain => (BitoffsetinfoFlags & S7COMMP_TAGDESCR_BITOFFSETINFO_RETAIN) != 0;

    public bool BitoffsetinfoFlagClassic => (BitoffsetinfoFlags & S7COMMP_TAGDESCR_BITOFFSETINFO_CLASSIC) != 0;

    public int BitoffsetinfoNonoptimizedBitoffset => (BitoffsetinfoFlags & S7COMMP_TAGDESCR_BITOFFSETINFO_NONOPTBITOFFSET) >> 4;

    public int BitoffsetinfoOptimizedBitoffset => BitoffsetinfoFlags & S7COMMP_TAGDESCR_BITOFFSETINFO_OPTBITOFFSET;

    public POffsetInfoType? OffsetInfoType { get; set; }

    public int Deserialize(Stream buffer)
    {
        var ret = 0;
        int offsetinfotype;

        ret += S7p.DecodeUInt32LE(buffer, out var _lid);
        LID = _lid;
        ret += S7p.DecodeUInt32LE(buffer, out var _symbolCrc);
        SymbolCrc = _symbolCrc;
        ret += S7p.DecodeByte(buffer, out var bval);
        Softdatatype = bval;    // For keepint the type similar
        ret += S7p.DecodeUInt16(buffer, out var _attributeFlags);
        AttributeFlags = _attributeFlags;

        offsetinfotype = (AttributeFlags & S7COMMP_TAGDESCR_ATTRIBUTE2_OFFSETINFOTYPE) >> 12;

        ret += S7p.DecodeByte(buffer, out var _bitoffsetinfoFlags);
        BitoffsetinfoFlags = _bitoffsetinfoFlags;

        OffsetInfoType = POffsetInfoType.Deserialize(buffer, offsetinfotype, out var length);
        ret += length;

        return ret;
    }

    public override string ToString()
    {
        return $"""
            <VartypeListElement>
            <LID>{LID}</LID>
            <SymbolCRC>{SymbolCrc}</SymbolCRC>
            <AttributeFlags>{AttributeFlags}</AttributeFlags>
            <BitoffsetinfoFlags>{BitoffsetinfoFlags}</BitoffsetinfoFlags>
            <OffsetInfoType>{OffsetInfoType}</OffsetInfoType>
            </VartypeListElement>
            """;
    }
}

