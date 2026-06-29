// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

internal interface IOffsetInfoType_Relation
{
    uint GetRelationId();
}

internal interface IOffsetInfoType_MDim
{
    int GetArrayLowerBounds();
    uint GetArrayElementCount();
    int[] GetMdimArrayLowerBounds();
    uint[] GetMdimArrayElementCount();
}

internal interface IOffsetInfoType_1Dim
{
    int GetArrayLowerBounds();
    uint GetArrayElementCount();
}

internal abstract class POffsetInfoType
{
    // Offsetinfo type for tag description.
    // Values 1..7 are used in old firmware version.
    // TLS supporting firmware version are using 8..15.
    public enum OffsetInfoType
    {
        FbArray = 0,
        StructElemStd = 1,
        StructElemString = 2,
        StructElemArray1Dim = 3,
        StructElemArrayMDim = 4,
        StructElemStruct = 5,
        StructElemStruct1Dim = 6,
        StructElemStructMDim = 7,
        Std = 8,
        String = 9,
        Array1Dim = 10,
        ArrayMDim = 11,
        Struct = 12,
        Struct1Dim = 13,
        StructMDim = 14,
        FbSfb = 15
    }

    public uint OptimizedAddress;
    public uint NonoptimizedAddress;

    public abstract bool HasRelation();
    public abstract bool Is1Dim();
    public abstract bool IsMDim();

    internal static POffsetInfoType? Deserialize(Stream buffer, int offsetinfotype, out int length)
    {
        switch ((OffsetInfoType)offsetinfotype)
        {
            case OffsetInfoType.FbArray:
                return POffsetInfoType_FbArray.Deserialize(buffer, out length);
            case OffsetInfoType.StructElemStd:
            case OffsetInfoType.Std:
                return POffsetInfoType_Std.Deserialize(buffer, out length, offsetinfotype);
            case OffsetInfoType.StructElemString:
            case OffsetInfoType.String:
                return POffsetInfoType_String.Deserialize(buffer, out length);
            case OffsetInfoType.StructElemArray1Dim:
            case OffsetInfoType.Array1Dim:
                return POffsetInfoType_Array1Dim.Deserialize(buffer, out length);
            case OffsetInfoType.StructElemArrayMDim:
            case OffsetInfoType.ArrayMDim:
                return POffsetInfoType_ArrayMDim.Deserialize(buffer, out length);
            case OffsetInfoType.StructElemStruct:
            case OffsetInfoType.Struct:
                return POffsetInfoType_Struct.Deserialize(buffer, out length);
            case OffsetInfoType.StructElemStruct1Dim:
            case OffsetInfoType.Struct1Dim:
                return POffsetInfoType_Struct1Dim.Deserialize(buffer, out length);
            case OffsetInfoType.StructElemStructMDim:
            case OffsetInfoType.StructMDim:
                return POffsetInfoType_StructMDim.Deserialize(buffer, out length);
            case OffsetInfoType.FbSfb:
                return POffsetInfoType_FbSfb.Deserialize(buffer, out length);
        }
        length = 0;
        return null;
    }
}

internal class POffsetInfoType_FbSfb : POffsetInfoType, IOffsetInfoType_Relation
{
    public ushort UnspecifiedOffsetinfo1;
    public ushort UnspecifiedOffsetinfo2;
    public uint RelationId;
    public uint Info4;
    public uint Info5;
    public uint Info6;
    public uint Info7;
    public uint RetainSectionOffset;
    public uint VolatileSectionOffset;

    public override bool HasRelation() { return true; }
    public override bool Is1Dim() { return false; }
    public override bool IsMDim() { return false; }

    public static POffsetInfoType_FbSfb Deserialize(Stream buffer, out int length)
    {
        var ret = 0;
        var oi = new POffsetInfoType_FbSfb();

        ret += S7p.DecodeUInt16LE(buffer, out oi.UnspecifiedOffsetinfo1);
        ret += S7p.DecodeUInt16LE(buffer, out oi.UnspecifiedOffsetinfo2);

        ret += S7p.DecodeUInt32LE(buffer, out oi.OptimizedAddress);
        ret += S7p.DecodeUInt32LE(buffer, out oi.NonoptimizedAddress);

        ret += S7p.DecodeUInt32LE(buffer, out oi.RelationId);
        ret += S7p.DecodeUInt32LE(buffer, out oi.Info4);
        ret += S7p.DecodeUInt32LE(buffer, out oi.Info5);
        ret += S7p.DecodeUInt32LE(buffer, out oi.Info6);
        ret += S7p.DecodeUInt32LE(buffer, out oi.Info7);
        ret += S7p.DecodeUInt32LE(buffer, out oi.RetainSectionOffset);
        ret += S7p.DecodeUInt32LE(buffer, out oi.VolatileSectionOffset);

        length = ret;
        return oi;
    }

    public override string ToString()
    {
        var s = "";
        s += $"<POffsetInfoType_FbSfb>{Environment.NewLine}";
        s += $"<UnspecifiedOffsetinfo1>{UnspecifiedOffsetinfo1}</UnspecifiedOffsetinfo1>{Environment.NewLine}";
        s += $"<UnspecifiedOffsetinfo2>{UnspecifiedOffsetinfo2}</UnspecifiedOffsetinfo2>{Environment.NewLine}";
        s += $"<OptimizedAddress>{OptimizedAddress}</OptimizedAddress>{Environment.NewLine}";
        s += $"<NonoptimizedAddress>{NonoptimizedAddress}</NonoptimizedAddress>{Environment.NewLine}";
        s += $"<RelationId>{RelationId}</RelationId>{Environment.NewLine}";
        s += $"<Info4>{Info4}</Info4>{Environment.NewLine}";
        s += $"<Info5>{Info5}</Info5>{Environment.NewLine}";
        s += $"<Info6>{Info6}</Info6>{Environment.NewLine}";
        s += $"<Info7>{Info7}</Info7>{Environment.NewLine}";
        s += $"<RetainSectionOffset>{RetainSectionOffset}</RetainSectionOffset>{Environment.NewLine}";
        s += $"<VolatileSectionOffset>{VolatileSectionOffset}</VolatileSectionOffset>{Environment.NewLine}";
        s += $"</POffsetInfoType_FbSfb>{Environment.NewLine}";

        return s;
    }

    public uint GetRelationId()
    {
        return RelationId;
    }
}

internal class POffsetInfoType_StructMDim : POffsetInfoType, IOffsetInfoType_Relation, IOffsetInfoType_MDim
{
    public ushort UnspecifiedOffsetinfo1;
    public ushort UnspecifiedOffsetinfo2;
    public int ArrayLowerBounds;
    public uint ArrayElementCount;
    public int[] MdimArrayLowerBounds = new int[6];
    public uint[] MdimArrayElementCount = new uint[6];
    public uint NonoptimizedStructSize;
    public uint OptimizedStructSize;
    public uint RelationId;
    public uint StructInfo4;
    public uint StructInfo5;
    public uint StructInfo6;
    public uint StructInfo7;

    public override bool HasRelation() { return true; }
    public override bool Is1Dim() { return false; }
    public override bool IsMDim() { return true; }

    public static POffsetInfoType_StructMDim Deserialize(Stream buffer, out int length)
    {
        var ret = 0;
        var oi = new POffsetInfoType_StructMDim();

        ret += S7p.DecodeUInt16LE(buffer, out oi.UnspecifiedOffsetinfo1);
        ret += S7p.DecodeUInt16LE(buffer, out oi.UnspecifiedOffsetinfo2);

        ret += S7p.DecodeUInt32LE(buffer, out oi.OptimizedAddress);
        ret += S7p.DecodeUInt32LE(buffer, out oi.NonoptimizedAddress);

        ret += S7p.DecodeInt32LE(buffer, out oi.ArrayLowerBounds);
        ret += S7p.DecodeUInt32LE(buffer, out oi.ArrayElementCount);

        for (var d = 0; d < 6; d++)
        {
            ret += S7p.DecodeInt32LE(buffer, out oi.MdimArrayLowerBounds[d]);
        }
        for (var d = 0; d < 6; d++)
        {
            ret += S7p.DecodeUInt32LE(buffer, out oi.MdimArrayElementCount[d]);
        }

        ret += S7p.DecodeUInt32LE(buffer, out oi.NonoptimizedStructSize);
        ret += S7p.DecodeUInt32LE(buffer, out oi.OptimizedStructSize);

        ret += S7p.DecodeUInt32LE(buffer, out oi.RelationId);
        ret += S7p.DecodeUInt32LE(buffer, out oi.StructInfo4);
        ret += S7p.DecodeUInt32LE(buffer, out oi.StructInfo5);
        ret += S7p.DecodeUInt32LE(buffer, out oi.StructInfo6);
        ret += S7p.DecodeUInt32LE(buffer, out oi.StructInfo7);

        length = ret;
        return oi;
    }

    public override string ToString()
    {
        var s = "";
        s += $"<POffsetInfoType_StructMDim>{Environment.NewLine}";
        s += $"<UnspecifiedOffsetinfo1>{UnspecifiedOffsetinfo1}</UnspecifiedOffsetinfo1>{Environment.NewLine}";
        s += $"<UnspecifiedOffsetinfo2>{UnspecifiedOffsetinfo2}</UnspecifiedOffsetinfo2>{Environment.NewLine}";

        s += $"<OptimizedAddress>{OptimizedAddress}</OptimizedAddress>{Environment.NewLine}";
        s += $"<NonoptimizedAddress>{NonoptimizedAddress}</NonoptimizedAddress>{Environment.NewLine}";

        s += $"<ArrayLowerBounds>{ArrayLowerBounds}</ArrayLowerBounds>{Environment.NewLine}";
        s += $"<ArrayElementCount>{ArrayElementCount}</ArrayElementCount>{Environment.NewLine}";

        for (var d = 0; d < 6; d++)
        {
            s += $"<MdimArrayLowerBounds[{d}]>{MdimArrayLowerBounds[d]}</MdimArrayLowerBounds[{d}]>{Environment.NewLine}";
        }
        for (var d = 0; d < 6; d++)
        {
            s += $"<MdimArrayElementCount[{d}]>{MdimArrayElementCount[d]}</MdimArrayElementCount[{d}]>{Environment.NewLine}";
        }

        s += $"<OptimizedStructSize>{OptimizedStructSize}</OptimizedStructSize>{Environment.NewLine}";
        s += $"<NonoptimizedStructSize>{NonoptimizedStructSize}</NonoptimizedStructSize>{Environment.NewLine}";

        s += $"<RelationId>{RelationId}</RelationId>{Environment.NewLine}";
        s += $"<StructInfo4>{StructInfo4}</StructInfo4>{Environment.NewLine}";
        s += $"<StructInfo5>{StructInfo5}</StructInfo5>{Environment.NewLine}";
        s += $"<StructInfo6>{StructInfo6}</StructInfo6>{Environment.NewLine}";
        s += $"<StructInfo7>{StructInfo7}</StructInfo7>{Environment.NewLine}";

        s += $"</POffsetInfoType_StructMDim>{Environment.NewLine}";

        return s;
    }

    public uint GetRelationId()
    {
        return RelationId;
    }

    public int GetArrayLowerBounds()
    {
        return ArrayLowerBounds;
    }

    public uint GetArrayElementCount()
    {
        return ArrayElementCount;
    }

    public int[] GetMdimArrayLowerBounds()
    {
        return MdimArrayLowerBounds;
    }

    public uint[] GetMdimArrayElementCount()
    {
        return MdimArrayElementCount;
    }
}

internal class POffsetInfoType_Struct1Dim : POffsetInfoType, IOffsetInfoType_Relation, IOffsetInfoType_1Dim
{
    public ushort UnspecifiedOffsetinfo1;
    public ushort UnspecifiedOffsetinfo2;
    public int ArrayLowerBounds;
    public uint ArrayElementCount;
    public uint NonoptimizedStructSize;
    public uint OptimizedStructSize;
    public uint RelationId;
    public uint StructInfo4;
    public uint StructInfo5;
    public uint StructInfo6;
    public uint StructInfo7;

    public override bool HasRelation() { return true; }
    public override bool Is1Dim() { return true; }
    public override bool IsMDim() { return false; }

    public static POffsetInfoType_Struct1Dim Deserialize(Stream buffer, out int length)
    {
        var ret = 0;
        var oi = new POffsetInfoType_Struct1Dim();

        ret += S7p.DecodeUInt16LE(buffer, out oi.UnspecifiedOffsetinfo1);
        ret += S7p.DecodeUInt16LE(buffer, out oi.UnspecifiedOffsetinfo2);

        ret += S7p.DecodeUInt32LE(buffer, out oi.OptimizedAddress);
        ret += S7p.DecodeUInt32LE(buffer, out oi.NonoptimizedAddress);

        ret += S7p.DecodeInt32LE(buffer, out oi.ArrayLowerBounds);
        ret += S7p.DecodeUInt32LE(buffer, out oi.ArrayElementCount);

        ret += S7p.DecodeUInt32LE(buffer, out oi.NonoptimizedStructSize);
        ret += S7p.DecodeUInt32LE(buffer, out oi.OptimizedStructSize);

        ret += S7p.DecodeUInt32LE(buffer, out oi.RelationId);
        ret += S7p.DecodeUInt32LE(buffer, out oi.StructInfo4);
        ret += S7p.DecodeUInt32LE(buffer, out oi.StructInfo5);
        ret += S7p.DecodeUInt32LE(buffer, out oi.StructInfo6);
        ret += S7p.DecodeUInt32LE(buffer, out oi.StructInfo7);

        length = ret;
        return oi;
    }

    public override string ToString()
    {
        var s = "";
        s += $"<POffsetInfoType_Struct1Dim>{Environment.NewLine}";

        s += $"<UnspecifiedOffsetinfo1>{UnspecifiedOffsetinfo1}</UnspecifiedOffsetinfo1>{Environment.NewLine}";
        s += $"<UnspecifiedOffsetinfo2>{UnspecifiedOffsetinfo2}</UnspecifiedOffsetinfo2>{Environment.NewLine}";

        s += $"<OptimizedAddress>{OptimizedAddress}</OptimizedAddress>{Environment.NewLine}";
        s += $"<NonoptimizedAddress>{NonoptimizedAddress}</NonoptimizedAddress>{Environment.NewLine}";

        s += $"<ArrayLowerBounds>{ArrayLowerBounds}</ArrayLowerBounds>{Environment.NewLine}";
        s += $"<ArrayElementCount>{ArrayElementCount}</ArrayElementCount>{Environment.NewLine}";

        s += $"<OptimizedStructSize>{OptimizedStructSize}</OptimizedStructSize>{Environment.NewLine}";
        s += $"<NonoptimizedStructSize>{NonoptimizedStructSize}</NonoptimizedStructSize>{Environment.NewLine}";

        s += $"<RelationId>{RelationId}</RelationId>{Environment.NewLine}";
        s += $"<StructInfo4>{StructInfo4}</StructInfo4>{Environment.NewLine}";
        s += $"<StructInfo5>{StructInfo5}</StructInfo5>{Environment.NewLine}";
        s += $"<StructInfo6>{StructInfo6}</StructInfo6>{Environment.NewLine}";
        s += $"<StructInfo7>{StructInfo7}</StructInfo7>{Environment.NewLine}";

        s += $"</POffsetInfoType_Struct1Dim>{Environment.NewLine}";

        return s;
    }

    public uint GetRelationId()
    {
        return RelationId;
    }

    public int GetArrayLowerBounds()
    {
        return ArrayLowerBounds;
    }

    public uint GetArrayElementCount()
    {
        return ArrayElementCount;
    }
}

internal class POffsetInfoType_Struct : POffsetInfoType, IOffsetInfoType_Relation
{
    public ushort UnspecifiedOffsetinfo1;
    public ushort UnspecifiedOffsetinfo2;
    public uint RelationId;
    public uint StructInfo4;
    public uint StructInfo5;
    public uint StructInfo6;
    public uint StructInfo7;

    public override bool HasRelation() { return true; }
    public override bool Is1Dim() { return false; }
    public override bool IsMDim() { return false; }

    public static POffsetInfoType_Struct Deserialize(Stream buffer, out int length)
    {
        var ret = 0;
        var oi = new POffsetInfoType_Struct();

        ret += S7p.DecodeUInt16LE(buffer, out oi.UnspecifiedOffsetinfo1);
        ret += S7p.DecodeUInt16LE(buffer, out oi.UnspecifiedOffsetinfo2);

        ret += S7p.DecodeUInt32LE(buffer, out oi.OptimizedAddress);
        ret += S7p.DecodeUInt32LE(buffer, out oi.NonoptimizedAddress);

        ret += S7p.DecodeUInt32LE(buffer, out oi.RelationId);
        ret += S7p.DecodeUInt32LE(buffer, out oi.StructInfo4);
        ret += S7p.DecodeUInt32LE(buffer, out oi.StructInfo5);
        ret += S7p.DecodeUInt32LE(buffer, out oi.StructInfo6);
        ret += S7p.DecodeUInt32LE(buffer, out oi.StructInfo7);

        length = ret;
        return oi;
    }

    public override string ToString()
    {
        var s = "";
        s += $"<POffsetInfoType_Struct>{Environment.NewLine}";

        s += $"<UnspecifiedOffsetinfo1>{UnspecifiedOffsetinfo1}</UnspecifiedOffsetinfo1>{Environment.NewLine}";
        s += $"<UnspecifiedOffsetinfo2>{UnspecifiedOffsetinfo2}</UnspecifiedOffsetinfo2>{Environment.NewLine}";

        s += $"<OptimizedAddress>{OptimizedAddress}</OptimizedAddress>{Environment.NewLine}";
        s += $"<NonoptimizedAddress>{NonoptimizedAddress}</NonoptimizedAddress>{Environment.NewLine}";

        s += $"<RelationId>{RelationId}</RelationId>{Environment.NewLine}";
        s += $"<StructInfo4>{StructInfo4}</StructInfo4>{Environment.NewLine}";
        s += $"<StructInfo5>{StructInfo5}</StructInfo5>{Environment.NewLine}";
        s += $"<StructInfo6>{StructInfo6}</StructInfo6>{Environment.NewLine}";
        s += $"<StructInfo7>{StructInfo7}</StructInfo7>{Environment.NewLine}";

        s += $"</POffsetInfoType_Struct>{Environment.NewLine}";

        return s;
    }

    public uint GetRelationId()
    {
        return RelationId;
    }
}

internal class POffsetInfoType_ArrayMDim : POffsetInfoType, IOffsetInfoType_MDim
{
    public ushort UnspecifiedOffsetinfo1;
    public ushort UnspecifiedOffsetinfo2;
    public int ArrayLowerBounds;
    public uint ArrayElementCount;
    public int[] MdimArrayLowerBounds = new int[6];
    public uint[] MdimArrayElementCount = new uint[6];

    public override bool HasRelation() { return false; }
    public override bool Is1Dim() { return false; }
    public override bool IsMDim() { return true; }

    public static POffsetInfoType_ArrayMDim Deserialize(Stream buffer, out int length)
    {
        var ret = 0;
        var oi = new POffsetInfoType_ArrayMDim();

        ret += S7p.DecodeUInt16LE(buffer, out oi.UnspecifiedOffsetinfo1);
        ret += S7p.DecodeUInt16LE(buffer, out oi.UnspecifiedOffsetinfo2);

        ret += S7p.DecodeUInt32LE(buffer, out oi.OptimizedAddress);
        ret += S7p.DecodeUInt32LE(buffer, out oi.NonoptimizedAddress);

        ret += S7p.DecodeInt32LE(buffer, out oi.ArrayLowerBounds);
        ret += S7p.DecodeUInt32LE(buffer, out oi.ArrayElementCount);

        for (var d = 0; d < 6; d++)
        {
            ret += S7p.DecodeInt32LE(buffer, out oi.MdimArrayLowerBounds[d]);
        }
        for (var d = 0; d < 6; d++)
        {
            ret += S7p.DecodeUInt32LE(buffer, out oi.MdimArrayElementCount[d]);
        }

        length = ret;
        return oi;
    }

    public override string ToString()
    {
        var s = "";
        s += $"<POffsetInfoType_ArrayMDim>{Environment.NewLine}";

        s += $"<UnspecifiedOffsetinfo1>{UnspecifiedOffsetinfo1}</UnspecifiedOffsetinfo1>{Environment.NewLine}";
        s += $"<UnspecifiedOffsetinfo2>{UnspecifiedOffsetinfo2}</UnspecifiedOffsetinfo2>{Environment.NewLine}";

        s += $"<OptimizedAddress>{OptimizedAddress}</OptimizedAddress>{Environment.NewLine}";
        s += $"<NonoptimizedAddress>{NonoptimizedAddress}</NonoptimizedAddress>{Environment.NewLine}";

        s += $"<ArrayLowerBounds>{ArrayLowerBounds}</ArrayLowerBounds>{Environment.NewLine}";
        s += $"<ArrayElementCount>{ArrayElementCount}</ArrayElementCount>{Environment.NewLine}";

        for (var d = 0; d < 6; d++)
        {
            s += $"<MdimArrayLowerBounds[{d}]>{MdimArrayLowerBounds[d]}</MdimArrayLowerBounds[{d}]>{Environment.NewLine}";
        }
        for (var d = 0; d < 6; d++)
        {
            s += $"<MdimArrayElementCount[{d}]>{MdimArrayElementCount[d]}</MdimArrayElementCount[{d}]>{Environment.NewLine}";
        }

        s += $"</POffsetInfoType_ArrayMDim>{Environment.NewLine}";

        return s;
    }

    public int GetArrayLowerBounds()
    {
        return ArrayLowerBounds;
    }

    public uint GetArrayElementCount()
    {
        return ArrayElementCount;
    }

    public int[] GetMdimArrayLowerBounds()
    {
        return MdimArrayLowerBounds;
    }

    public uint[] GetMdimArrayElementCount()
    {
        return MdimArrayElementCount;
    }
}

internal class POffsetInfoType_Array1Dim : POffsetInfoType, IOffsetInfoType_1Dim
{
    public ushort UnspecifiedOffsetinfo1;
    public ushort UnspecifiedOffsetinfo2;
    public int ArrayLowerBounds;
    public uint ArrayElementCount;

    public override bool HasRelation() { return false; }
    public override bool Is1Dim() { return true; }
    public override bool IsMDim() { return false; }

    public static POffsetInfoType_Array1Dim Deserialize(Stream buffer, out int length)
    {
        var ret = 0;
        var oi = new POffsetInfoType_Array1Dim();

        ret += S7p.DecodeUInt16LE(buffer, out oi.UnspecifiedOffsetinfo1);
        ret += S7p.DecodeUInt16LE(buffer, out oi.UnspecifiedOffsetinfo2);

        ret += S7p.DecodeUInt32LE(buffer, out oi.OptimizedAddress);
        ret += S7p.DecodeUInt32LE(buffer, out oi.NonoptimizedAddress);

        ret += S7p.DecodeInt32LE(buffer, out oi.ArrayLowerBounds);
        ret += S7p.DecodeUInt32LE(buffer, out oi.ArrayElementCount);

        length = ret;
        return oi;
    }

    public override string ToString()
    {
        var s = "";
        s += $"<POffsetInfoType_Array1Dim>{Environment.NewLine}";

        s += $"<UnspecifiedOffsetinfo1>{UnspecifiedOffsetinfo1}</UnspecifiedOffsetinfo1>{Environment.NewLine}";
        s += $"<UnspecifiedOffsetinfo2>{UnspecifiedOffsetinfo2}</UnspecifiedOffsetinfo2>{Environment.NewLine}";

        s += $"<OptimizedAddress>{OptimizedAddress}</OptimizedAddress>{Environment.NewLine}";
        s += $"<NonoptimizedAddress>{NonoptimizedAddress}</NonoptimizedAddress>{Environment.NewLine}";

        s += $"<ArrayLowerBounds>{ArrayLowerBounds}</ArrayLowerBounds>{Environment.NewLine}";
        s += $"<ArrayElementCount>{ArrayElementCount}</ArrayElementCount>{Environment.NewLine}";

        s += $"</POffsetInfoType_Array1Dim>{Environment.NewLine}";

        return s;
    }

    public int GetArrayLowerBounds()
    {
        return ArrayLowerBounds;
    }

    public uint GetArrayElementCount()
    {
        return ArrayElementCount;
    }
}

internal class POffsetInfoType_String : POffsetInfoType
{
    public ushort UnspecifiedOffsetinfo1;   // This is the max. length of the string
    public ushort UnspecifiedOffsetinfo2;   // max. lengh plus 2 bytes stringheader

    public override bool HasRelation() { return false; }
    public override bool Is1Dim() { return false; }
    public override bool IsMDim() { return false; }

    public static POffsetInfoType_String Deserialize(Stream buffer, out int length)
    {
        var ret = 0;
        var oi = new POffsetInfoType_String();

        ret += S7p.DecodeUInt16LE(buffer, out oi.UnspecifiedOffsetinfo1);
        ret += S7p.DecodeUInt16LE(buffer, out oi.UnspecifiedOffsetinfo2);

        ret += S7p.DecodeUInt32LE(buffer, out oi.OptimizedAddress);
        ret += S7p.DecodeUInt32LE(buffer, out oi.NonoptimizedAddress);

        length = ret;
        return oi;
    }

    public override string ToString()
    {
        var s = "";
        s += $"<POffsetInfoType_String>{Environment.NewLine}";

        s += $"<UnspecifiedOffsetinfo1>{UnspecifiedOffsetinfo1}</UnspecifiedOffsetinfo1>{Environment.NewLine}";
        s += $"<UnspecifiedOffsetinfo2>{UnspecifiedOffsetinfo2}</UnspecifiedOffsetinfo2>{Environment.NewLine}";

        s += $"<OptimizedAddress>{OptimizedAddress}</OptimizedAddress>{Environment.NewLine}";
        s += $"<NonoptimizedAddress>{NonoptimizedAddress}</NonoptimizedAddress>{Environment.NewLine}";

        s += $"</POffsetInfoType_String>{Environment.NewLine}";

        return s;
    }
}

internal class POffsetInfoType_Std : POffsetInfoType
{
    public override bool HasRelation() { return false; }
    public override bool Is1Dim() { return false; }
    public override bool IsMDim() { return false; }

    public static POffsetInfoType_Std Deserialize(Stream buffer, out int length, int offsetinfotype)
    {
        var ret = 0;
        var oi = new POffsetInfoType_Std();
        // The order of addresses is swapped between old Std (8) and new (1) offsetinfotype.
        ushort v;
        if ((OffsetInfoType)offsetinfotype == OffsetInfoType.Std)
        {
            ret += S7p.DecodeUInt16LE(buffer, out v);
            oi.OptimizedAddress = v;
            ret += S7p.DecodeUInt16LE(buffer, out v);
            oi.NonoptimizedAddress = v;
        }
        else
        {
            ret += S7p.DecodeUInt16LE(buffer, out v);
            oi.NonoptimizedAddress = v;
            ret += S7p.DecodeUInt16LE(buffer, out v);
            oi.OptimizedAddress = v;
        }
        length = ret;
        return oi;
    }

    public override string ToString()
    {
        var s = "";
        s += $"<POffsetInfoType_Std>{Environment.NewLine}";

        s += $"<OptimizedAddress>{OptimizedAddress}</OptimizedAddress>{Environment.NewLine}";
        s += $"<NonoptimizedAddress>{NonoptimizedAddress}</NonoptimizedAddress>{Environment.NewLine}";

        s += $"</POffsetInfoType_Std>{Environment.NewLine}";

        return s;
    }
}

internal class POffsetInfoType_FbArray : POffsetInfoType, IOffsetInfoType_Relation
{
    public ushort UnspecifiedOffsetinfo1;
    public ushort UnspecifiedOffsetinfo2;
    public uint RelationId;
    public uint Info4;
    public uint Info5;
    public uint Info6;
    public uint Info7;
    public uint RetainSectionOffset;
    public uint VolatileSectionOffset;
    public uint ArrayElementCount;
    public uint ClassicSectionSize;
    public uint RetainSectionSize;
    public uint VolatileSectionSize;
    public int[] MdimArrayLowerBounds = new int[6];
    public uint[] MdimArrayElementCount = new uint[6];

    public override bool HasRelation() { return true; }
    public override bool Is1Dim() { return false; } //!!! TODO
    public override bool IsMDim() { return false; } //!!! TODO

    public static POffsetInfoType_FbArray Deserialize(Stream buffer, out int length)
    {
        var ret = 0;
        var oi = new POffsetInfoType_FbArray();

        ret += S7p.DecodeUInt16LE(buffer, out oi.UnspecifiedOffsetinfo1);
        ret += S7p.DecodeUInt16LE(buffer, out oi.UnspecifiedOffsetinfo2);

        ret += S7p.DecodeUInt32LE(buffer, out oi.OptimizedAddress);
        ret += S7p.DecodeUInt32LE(buffer, out oi.NonoptimizedAddress);

        ret += S7p.DecodeUInt32LE(buffer, out oi.RelationId);
        ret += S7p.DecodeUInt32LE(buffer, out oi.Info4);
        ret += S7p.DecodeUInt32LE(buffer, out oi.Info5);
        ret += S7p.DecodeUInt32LE(buffer, out oi.Info6);
        ret += S7p.DecodeUInt32LE(buffer, out oi.Info7);
        ret += S7p.DecodeUInt32LE(buffer, out oi.RetainSectionOffset);
        ret += S7p.DecodeUInt32LE(buffer, out oi.VolatileSectionOffset);
        ret += S7p.DecodeUInt32LE(buffer, out oi.ArrayElementCount);
        ret += S7p.DecodeUInt32LE(buffer, out oi.ClassicSectionSize);
        ret += S7p.DecodeUInt32LE(buffer, out oi.RetainSectionSize);
        ret += S7p.DecodeUInt32LE(buffer, out oi.VolatileSectionSize);

        for (var d = 0; d < 6; d++)
        {
            ret += S7p.DecodeInt32LE(buffer, out oi.MdimArrayLowerBounds[d]);
        }
        for (var d = 0; d < 6; d++)
        {
            ret += S7p.DecodeUInt32LE(buffer, out oi.MdimArrayElementCount[d]);
        }

        length = ret;
        return oi;
    }

    public override string ToString()
    {
        var s = "";
        s += $"<POffsetInfoType_FbArray>{Environment.NewLine}";

        s += $"<UnspecifiedOffsetinfo1>{UnspecifiedOffsetinfo1}</UnspecifiedOffsetinfo1>{Environment.NewLine}";
        s += $"<UnspecifiedOffsetinfo2>{UnspecifiedOffsetinfo2}</UnspecifiedOffsetinfo2>{Environment.NewLine}";

        s += $"<OptimizedAddress>{OptimizedAddress}</OptimizedAddress>{Environment.NewLine}";
        s += $"<NonoptimizedAddress>{NonoptimizedAddress}</NonoptimizedAddress>{Environment.NewLine}";

        s += $"<RelationId>{RelationId}</RelationId>{Environment.NewLine}";
        s += $"<Info4>{Info4}</Info4>{Environment.NewLine}";
        s += $"<Info5>{Info5}</Info5>{Environment.NewLine}";
        s += $"<Info6>{Info6}</Info6>{Environment.NewLine}";
        s += $"<Info7>{Info7}</Info7>{Environment.NewLine}";

        s += $"<RetainSectionOffset>{RetainSectionOffset}</RetainSectionOffset>{Environment.NewLine}";
        s += $"<VolatileSectionOffset>{VolatileSectionOffset}</VolatileSectionOffset>{Environment.NewLine}";
        s += $"<ClassicSectionSize>{ClassicSectionSize}</ClassicSectionSize>{Environment.NewLine}";
        s += $"<RetainSectionSize>{RetainSectionSize}</RetainSectionSize>{Environment.NewLine}";
        s += $"<VolatileSectionSize>{VolatileSectionSize}</VolatileSectionSize>{Environment.NewLine}";

        for (var d = 0; d < 6; d++)
        {
            s += $"<MdimArrayLowerBounds[{d}]>{MdimArrayLowerBounds[d]}</MdimArrayLowerBounds[{d}]>{Environment.NewLine}";
        }
        for (var d = 0; d < 6; d++)
        {
            s += $"<MdimArrayElementCount[{d}]>{MdimArrayElementCount[d]}</MdimArrayElementCount[{d}]>{Environment.NewLine}";
        }

        s += $"</POffsetInfoType_FbArray>{Environment.NewLine}";

        return s;
    }

    public uint GetRelationId()
    {
        return RelationId;
    }
}
