// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;

namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

// TODO: Maybe there's room for improvement, as the array classes duplicate many code of the base classes
internal abstract class PValue : IS7pSerialize
{
    protected static byte FLAGS_ARRAY = 0x10;
    protected static byte FLAGS_ADDRESSARRAY = 0x20;
    protected static byte FLAGS_SPARSEARRAY = 0x40;

    protected byte DatatypeFlags;
    public abstract int Serialize(Stream buffer);

    public bool IsArray()
    {
        return (DatatypeFlags & FLAGS_ARRAY) != 0;
    }

    public bool IsAddressArray()
    {
        return (DatatypeFlags & FLAGS_ADDRESSARRAY) != 0;
    }

    public bool IsSparseArray()
    {
        return (DatatypeFlags & FLAGS_SPARSEARRAY) != 0;
    }

    /// <summary>
    /// Deserializes the buffer to the protocol values
    /// </summary>
    /// <param name="buffer">Stream of bytes from the network</param>
    /// <param name="disableVlq">If true, the variable length encoding is disabled for all underlying values (so far only neccessary on SystemEvent)</param>
    /// <returns>The protocol value</returns>
    public static PValue Deserialize(Stream buffer, bool disableVlq = false)
    {
        byte flags;
        byte datatype;

        if (!disableVlq)
        {
            S7p.DecodeByte(buffer, out flags);
            S7p.DecodeByte(buffer, out datatype);
        }
        else
        {
            // If VLQ is disabled, there are two additional bytes we just skip here.
            S7p.DecodeByte(buffer, out _);
            S7p.DecodeByte(buffer, out flags);
            S7p.DecodeByte(buffer, out _);
            S7p.DecodeByte(buffer, out datatype);
        }

        // Sparsearray and Addressarray of Struct are different
        if (flags == FLAGS_ARRAY || flags == FLAGS_ADDRESSARRAY)
        {
            switch (datatype)
            {
                case Datatype.Bool:
                    return ValueBoolArray.Deserialize(buffer, flags, disableVlq);
                case Datatype.USInt:
                    return ValueUSIntArray.Deserialize(buffer, flags, disableVlq);
                case Datatype.UInt:
                    return ValueUIntArray.Deserialize(buffer, flags, disableVlq);
                case Datatype.UDInt:
                    return ValueUDIntArray.Deserialize(buffer, flags, disableVlq);
                case Datatype.ULInt:
                    return ValueULIntArray.Deserialize(buffer, flags, disableVlq);
                case Datatype.SInt:
                    return ValueSIntArray.Deserialize(buffer, flags, disableVlq);
                case Datatype.Int:
                    return ValueIntArray.Deserialize(buffer, flags, disableVlq);
                case Datatype.DInt:
                    return ValueDIntArray.Deserialize(buffer, flags, disableVlq);
                case Datatype.LInt:
                    return ValueLIntArray.Deserialize(buffer, flags, disableVlq);
                case Datatype.Byte:
                    return ValueByteArray.Deserialize(buffer, flags, disableVlq);
                case Datatype.Word:
                    return ValueWordArray.Deserialize(buffer, flags, disableVlq);
                case Datatype.DWord:
                    return ValueDWordArray.Deserialize(buffer, flags, disableVlq);
                case Datatype.LWord:
                    return ValueLWordArray.Deserialize(buffer, flags, disableVlq);
                case Datatype.Real:
                    return ValueRealArray.Deserialize(buffer, flags, disableVlq);
                case Datatype.LReal:
                    return ValueLRealArray.Deserialize(buffer, flags, disableVlq);
                case Datatype.Timestamp:
                    return ValueTimestampArray.Deserialize(buffer, flags, disableVlq);
                case Datatype.Timespan:
                    return ValueTimespanArray.Deserialize(buffer, flags, disableVlq);
                case Datatype.RID:
                    return ValueRIDArray.Deserialize(buffer, flags, disableVlq);
                case Datatype.AID:
                    return ValueAIDArray.Deserialize(buffer, flags, disableVlq);
                case Datatype.Blob:
                    return ValueBlobArray.Deserialize(buffer, flags, disableVlq);
                case Datatype.WString:
                    return ValueWStringArray.Deserialize(buffer, flags, disableVlq);
                default:
                    throw new NotImplementedException();
            }
        }
        else if (flags == FLAGS_SPARSEARRAY)
        {
            switch (datatype)
            {
                case Datatype.DInt:
                    return ValueDIntSparseArray.Deserialize(buffer, flags, disableVlq);
                case Datatype.UDInt:
                    return ValueUDIntSparseArray.Deserialize(buffer, flags, disableVlq);
                case Datatype.Blob:
                    return ValueBlobSparseArray.Deserialize(buffer, flags, disableVlq);
                case Datatype.WString:
                    return ValueWStringSparseArray.Deserialize(buffer, flags, disableVlq);
                default:
                    throw new NotImplementedException();
            }
        }
        else
        {
            switch (datatype)
            {
                case Datatype.Null:
                    return ValueNull.Deserialize(buffer, flags);
                case Datatype.Bool:
                    return ValueBool.Deserialize(buffer, flags);
                case Datatype.USInt:
                    return ValueUSInt.Deserialize(buffer, flags);
                case Datatype.UInt:
                    return ValueUInt.Deserialize(buffer, flags);
                case Datatype.UDInt:
                    return ValueUDInt.Deserialize(buffer, flags, disableVlq);
                case Datatype.ULInt:
                    return ValueULInt.Deserialize(buffer, flags, disableVlq);
                case Datatype.SInt:
                    return ValueSInt.Deserialize(buffer, flags);
                case Datatype.Int:
                    return ValueInt.Deserialize(buffer, flags);
                case Datatype.DInt:
                    return ValueDInt.Deserialize(buffer, flags, disableVlq);
                case Datatype.LInt:
                    return ValueLInt.Deserialize(buffer, flags, disableVlq);
                case Datatype.Byte:
                    return ValueByte.Deserialize(buffer, flags);
                case Datatype.Word:
                    return ValueWord.Deserialize(buffer, flags);
                case Datatype.DWord:
                    return ValueDWord.Deserialize(buffer, flags);
                case Datatype.LWord:
                    return ValueLWord.Deserialize(buffer, flags);
                case Datatype.Real:
                    return ValueReal.Deserialize(buffer, flags);
                case Datatype.LReal:
                    return ValueLReal.Deserialize(buffer, flags);
                case Datatype.Timestamp:
                    return ValueTimestamp.Deserialize(buffer, flags);
                case Datatype.Timespan:
                    return ValueTimespan.Deserialize(buffer, flags, disableVlq);
                case Datatype.RID:
                    return ValueRID.Deserialize(buffer, flags);
                case Datatype.AID:
                    return ValueAID.Deserialize(buffer, flags, disableVlq);
                case Datatype.Blob:
                    return ValueBlob.Deserialize(buffer, flags, disableVlq);
                case Datatype.WString:
                    return ValueWString.Deserialize(buffer, flags, disableVlq);
                case Datatype.Variant:
                    throw new NotImplementedException();
                case Datatype.Struct:
                    return ValueStruct.Deserialize(buffer, flags, disableVlq);
                case Datatype.S7String:
                    throw new NotImplementedException();
            }
        }
        return null;
    }
}

internal sealed class ValueNull : PValue
{
    public ValueNull() : this(0)
    {
    }

    public ValueNull(byte flags)
    {
        DatatypeFlags = flags;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.Null);
        return ret;
    }

    public override string ToString()
    {
        return "<Value type=\"Null\"></Value>";
    }

    public static ValueNull Deserialize(Stream buffer, byte flags)
    {
        return new ValueNull(flags);
    }
}

internal sealed class ValueBool : PValue
{
    bool Value;

    public ValueBool(bool value) : this(value, 0)
    {
    }

    public ValueBool(bool value, byte flags)
    {
        DatatypeFlags = flags;
        Value = value;
    }

    public bool GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.Bool);
        ret += S7p.EncodeByte(buffer, Convert.ToByte(Value));
        return ret;
    }

    public override string ToString()
    {
        return "<Value type=\"Bool\">" + Value.ToString() + "</Value>";
    }

    public static ValueBool Deserialize(Stream buffer, byte flags)
    {
        S7p.DecodeByte(buffer, out var value);
        return new ValueBool(Convert.ToBoolean(value), flags);
    }
}

/// <summary>
/// ValueBoolArray: Important: The length of the array is always a multiple of 8.
/// E.g. reading an Array [0..2] of Bool will be transmitted as 8 elements with actual values at index 0, 1, 2.
/// An Array[0..9] will be transmitted as 16 elements and so on.
/// At this time, serialize doesn't respect the padding elements, must be done on a higher level.
/// </summary>
internal sealed class ValueBoolArray : PValue
{
    bool[] Value;

    public ValueBoolArray(bool[] value) : this(value, FLAGS_ARRAY)
    {
    }

    public ValueBoolArray(bool[] value, byte flags)
    {
        DatatypeFlags = flags;
        if (value != null)
        {
            Value = new bool[value.Length];
            Array.Copy(value, Value, value.Length);
        }
    }

    public bool[] GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.Bool);
        // TODO: Should we respect the padding inside this class, or at a higher level?
        ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
        for (var i = 0; i < Value.Length; i++)
        {
            ret += S7p.EncodeByte(buffer, Convert.ToByte(Value[i]));
        }
        return ret;
    }

    public override string ToString()
    {
        var s = new StringBuilder($"<Value type =\"BoolArray\" size=\"{Value.Length}\">");
        for (var i = 0; i < Value.Length; i++)
        {
            s.Append($"<Value>{Value[i]}</Value>");
        }
        s.Append("</Value>");
        return s.ToString();
    }

    public static ValueBoolArray Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        bool[] value;
        uint size;
        if (!disableVlq)
        {
            S7p.DecodeUInt32Vlq(buffer, out size);
        }
        else
        {
            S7p.DecodeUInt32(buffer, out size);
        }
        value = new bool[size];
        for (var i = 0; i < size; i++)
        {
            S7p.DecodeByte(buffer, out var bv);
            value[i] = Convert.ToBoolean(bv);
        }
        return new ValueBoolArray(value, flags);
    }
}

internal sealed class ValueUSInt : PValue
{
    byte Value;

    public ValueUSInt(byte value) : this(value, 0)
    {
    }

    public ValueUSInt(byte value, byte flags)
    {
        DatatypeFlags = flags;
        Value = value;
    }

    public byte GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.USInt);
        ret += S7p.EncodeByte(buffer, Value);
        return ret;
    }

    public override string ToString()
    {
        return $"<Value type=\"USInt\">{Value}</Value>";
    }

    public static ValueUSInt Deserialize(Stream buffer, byte flags)
    {
        S7p.DecodeByte(buffer, out var value);
        return new ValueUSInt(value, flags);
    }
}

internal sealed class ValueUSIntArray : PValue
{
    byte[] Value;

    public ValueUSIntArray(byte[] value) : this(value, FLAGS_ARRAY)
    {
    }

    public ValueUSIntArray(byte[] value, byte flags)
    {
        DatatypeFlags = flags;
        if (value != null)
        {
            Value = new byte[value.Length];
            Array.Copy(value, Value, value.Length);
        }
    }

    public byte[] GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.USInt);
        ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
        for (var i = 0; i < Value.Length; i++)
        {
            ret += S7p.EncodeByte(buffer, Value[i]);
        }
        return ret;
    }

    public override string ToString()
    {
        var s = new StringBuilder($"<Value type =\"USIntArray\" size=\"{Value.Length}\">");
        for (var i = 0; i < Value.Length; i++)
        {
            s.Append($"<Value>{Value[i]}</Value>");
        }
        s.Append("</Value>");
        return s.ToString();
    }

    public static ValueUSIntArray Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        byte[] value;
        uint size;
        if (!disableVlq)
        {
            S7p.DecodeUInt32Vlq(buffer, out size);
        }
        else
        {
            S7p.DecodeUInt32(buffer, out size);
        }
        value = new byte[size];
        for (var i = 0; i < size; i++)
        {
            S7p.DecodeByte(buffer, out value[i]);
        }
        return new ValueUSIntArray(value, flags);
    }
}

internal sealed class ValueUInt : PValue
{
    ushort Value;

    public ValueUInt(ushort value) : this(value, 0)
    {
    }

    public ValueUInt(ushort value, byte flags)
    {
        DatatypeFlags = flags;
        Value = value;
    }

    public ushort GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.UInt);
        ret += S7p.EncodeUInt16(buffer, Value);
        return ret;
    }

    public override string ToString()
    {
        return $"<Value type=\"UInt\">{Value}</Value>";
    }

    public static ValueUInt Deserialize(Stream buffer, byte flags)
    {
        S7p.DecodeUInt16(buffer, out var value);
        return new ValueUInt(value, flags);
    }
}

internal sealed class ValueUIntArray : PValue
{
    ushort[] Value;

    public ValueUIntArray(ushort[] value) : this(value, FLAGS_ARRAY)
    {
    }

    public ValueUIntArray(ushort[] value, byte flags)
    {
        DatatypeFlags = flags;
        if (value != null)
        {
            Value = new ushort[value.Length];
            Array.Copy(value, Value, value.Length);
        }
    }

    public ushort[] GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.UInt);
        ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
        for (var i = 0; i < Value.Length; i++)
        {
            ret += S7p.EncodeUInt16(buffer, Value[i]);
        }
        return ret;
    }

    public override string ToString()
    {
        var s = new StringBuilder($"<Value type =\"UIntArray\" size=\"{Value.Length}\">");
        for (var i = 0; i < Value.Length; i++)
        {
            s.Append($"<Value>{Value[i]}</Value>");
        }
        s.Append("</Value>");
        return s.ToString();
    }

    public static ValueUIntArray Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        ushort[] value;
        uint size;
        if (!disableVlq)
        {
            S7p.DecodeUInt32Vlq(buffer, out size);
        }
        else
        {
            S7p.DecodeUInt32(buffer, out size);
        }
        value = new ushort[size];
        for (var i = 0; i < size; i++)
        {
            S7p.DecodeUInt16(buffer, out value[i]);
        }
        return new ValueUIntArray(value, flags);
    }
}

internal sealed class ValueUDInt : PValue
{
    uint Value;

    public ValueUDInt(uint value) : this(value, 0)
    {
    }

    public ValueUDInt(uint value, byte flags)
    {
        DatatypeFlags = flags;
        Value = value;
    }

    public uint GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.UDInt);
        ret += S7p.EncodeUInt32Vlq(buffer, Value);
        return ret;
    }

    public override string ToString()
    {
        return $"<Value type=\"UDInt\">{Value}</Value>";
    }

    public static ValueUDInt Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        uint value;
        if (!disableVlq)
        {
            S7p.DecodeUInt32Vlq(buffer, out value);
        }
        else
        {
            S7p.DecodeUInt32(buffer, out value);
        }
        return new ValueUDInt(value, flags);
    }
}

internal sealed class ValueUDIntArray : PValue
{
    uint[] Value;

    public ValueUDIntArray(uint[] value) : this(value, FLAGS_ARRAY)
    {
    }

    public ValueUDIntArray(uint[] value, byte flags)
    {
        DatatypeFlags = flags;
        if (value != null)
        {
            Value = new uint[value.Length];
            Array.Copy(value, Value, value.Length);
        }
    }

    public uint[] GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.UDInt);
        ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
        for (var i = 0; i < Value.Length; i++)
        {
            ret += S7p.EncodeUInt32Vlq(buffer, Value[i]);
        }
        return ret;
    }

    public override string ToString()
    {
        var s = new StringBuilder($"<Value type =\"UDIntArray\" size=\"{Value.Length}\">");
        for (var i = 0; i < Value.Length; i++)
        {
            s.Append($"<Value>{Value[i]}</Value>");
        }
        s.Append("</Value>");
        return s.ToString();
    }

    public static ValueUDIntArray Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        uint[] value;
        uint size;
        if (!disableVlq)
        {
            S7p.DecodeUInt32Vlq(buffer, out size);
            value = new uint[size];
            for (var i = 0; i < size; i++)
            {
                S7p.DecodeUInt32Vlq(buffer, out value[i]);
            }
        }
        else
        {
            S7p.DecodeUInt32(buffer, out size);
            value = new uint[size];
            for (var i = 0; i < size; i++)
            {
                S7p.DecodeUInt32(buffer, out value[i]);
            }
        }
        return new ValueUDIntArray(value, flags);
    }
}

// The construction of Sparsearray is almost similar to reading a struct.
// All elements are kind of key,value. And Value is of the selected type.
// The list is terminated by Null.
// E.g.: Reading 1037 (SystemLimits) via GetVarSubStreamed
internal sealed class ValueUDIntSparseArray : PValue
{
    Dictionary<uint, uint> Value;

    public ValueUDIntSparseArray(Dictionary<uint, uint> value) : this(value, FLAGS_SPARSEARRAY)
    {
    }

    public ValueUDIntSparseArray(Dictionary<uint, uint> value, byte flags)
    {
        DatatypeFlags = flags;
        if (value != null)
        {
            Value = [with(value)];
        }
    }

    public Dictionary<uint, uint> GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.UDInt);
        foreach (var v in Value)
        {
            ret += S7p.EncodeUInt32Vlq(buffer, v.Key);
            ret += S7p.EncodeUInt32Vlq(buffer, v.Value);
        }
        ret += S7p.EncodeByte(buffer, 0);
        return ret;
    }

    public override string ToString()
    {
        var s = new StringBuilder("<Value type =\"UDIntSparseArray\">");
        foreach (var v in Value)
        {
            s.Append($"<Value key=\"{v.Key}\">{v.Value}</Value>");
        }
        s.Append("</Value>");
        return s.ToString();
    }

    public static ValueUDIntSparseArray Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        Dictionary<uint, uint> value = [];
        uint k;
        uint v;
        if (!disableVlq)
        {
            S7p.DecodeUInt32Vlq(buffer, out k);
            while (k > 0)
            {
                S7p.DecodeUInt32Vlq(buffer, out v);
                value.Add(k, v);
                S7p.DecodeUInt32Vlq(buffer, out k);
            }
        }
        else
        {
            S7p.DecodeUInt32(buffer, out k);
            while (k > 0)
            {
                S7p.DecodeUInt32(buffer, out v);
                value.Add(k, v);
                S7p.DecodeUInt32(buffer, out k);
            }
        }
        return new ValueUDIntSparseArray(value, flags);
    }
}

internal sealed class ValueULInt : PValue
{
    ulong Value;

    public ValueULInt(ulong value) : this(value, 0)
    {
    }

    public ValueULInt(ulong value, byte flags)
    {
        DatatypeFlags = flags;
        Value = value;
    }

    public ulong GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.ULInt);
        ret += S7p.EncodeUInt64Vlq(buffer, Value);
        return ret;
    }

    public override string ToString()
    {
        return $"<Value type=\"ULInt\">{Value}</Value>";
    }

    public static ValueULInt Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        ulong value;
        if (!disableVlq)
        {
            S7p.DecodeUInt64Vlq(buffer, out value);
        }
        else
        {
            S7p.DecodeUInt64(buffer, out value);
        }
        return new ValueULInt(value, flags);
    }
}

internal sealed class ValueULIntArray : PValue
{
    ulong[] Value;

    public ValueULIntArray(ulong[] value) : this(value, FLAGS_ARRAY)
    {
    }

    public ValueULIntArray(ulong[] value, byte flags)
    {
        DatatypeFlags = flags;
        if (value != null)
        {
            Value = new ulong[value.Length];
            Array.Copy(value, Value, value.Length);
        }
    }

    public ulong[] GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.ULInt);
        ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
        for (var i = 0; i < Value.Length; i++)
        {
            ret += S7p.EncodeUInt64Vlq(buffer, Value[i]);
        }
        return ret;
    }

    public override string ToString()
    {
        var s = new StringBuilder($"<Value type =\"ULIntArray\" size=\"{Value.Length}\">");
        for (var i = 0; i < Value.Length; i++)
        {
            s.Append($"<Value>{Value[i]}</Value>");
        }
        s.Append("</Value>");
        return s.ToString();
    }

    public static ValueULIntArray Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        ulong[] value;
        uint size;
        if (!disableVlq)
        {
            S7p.DecodeUInt32Vlq(buffer, out size);
            value = new ulong[size];
            for (var i = 0; i < size; i++)
            {
                S7p.DecodeUInt64Vlq(buffer, out value[i]);
            }
        }
        else
        {
            S7p.DecodeUInt32(buffer, out size);
            value = new ulong[size];
            for (var i = 0; i < size; i++)
            {
                S7p.DecodeUInt64(buffer, out value[i]);
            }
        }
        return new ValueULIntArray(value, flags);
    }
}

internal sealed class ValueSInt : PValue
{
    sbyte Value;

    public ValueSInt(sbyte value) : this(value, 0)
    {
    }

    public ValueSInt(sbyte value, byte flags)
    {
        DatatypeFlags = flags;
        Value = value;
    }

    public sbyte GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.SInt);
        ret += S7p.EncodeByte(buffer, (byte)Value);
        return ret;
    }

    public override string ToString()
    {
        return $"<Value type=\"SInt\">{Value}</Value>";
    }

    public static ValueSInt Deserialize(Stream buffer, byte flags)
    {
        byte value;
        S7p.DecodeByte(buffer, out value);
        return new ValueSInt((sbyte)value, flags);
    }
}

internal sealed class ValueSIntArray : PValue
{
    sbyte[] Value;

    public ValueSIntArray(sbyte[] value) : this(value, FLAGS_ARRAY)
    {
    }

    public ValueSIntArray(sbyte[] value, byte flags)
    {
        DatatypeFlags = flags;
        if (value != null)
        {
            Value = new sbyte[value.Length];
            Array.Copy(value, Value, value.Length);
        }
    }

    public sbyte[] GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.SInt);
        ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
        for (var i = 0; i < Value.Length; i++)
        {
            ret += S7p.EncodeByte(buffer, (byte)Value[i]);
        }
        return ret;
    }

    public override string ToString()
    {
        var s = new StringBuilder($"<Value type =\"SIntArray\" size=\"{Value.Length}\">");
        for (var i = 0; i < Value.Length; i++)
        {
            s.Append($"<Value>{Value[i]}</Value>");
        }
        s.Append("</Value>");
        return s.ToString();
    }

    public static ValueSIntArray Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        sbyte[] value;
        uint size;
        if (!disableVlq)
        {
            S7p.DecodeUInt32Vlq(buffer, out size);
        }
        else
        {
            S7p.DecodeUInt32(buffer, out size);
        }
        value = new sbyte[size];
        for (var i = 0; i < size; i++)
        {
            S7p.DecodeByte(buffer, out var b);
            value[i] = (sbyte)b;
        }
        return new ValueSIntArray(value, flags);
    }
}

internal sealed class ValueInt : PValue
{
    short Value;

    public ValueInt(short value) : this(value, 0)
    {
    }

    public ValueInt(short value, byte flags)
    {
        DatatypeFlags = flags;
        Value = value;
    }

    public short GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.Int);
        ret += S7p.EncodeInt16(buffer, Value);
        return ret;
    }

    public override string ToString()
    {
        return $"<Value type=\"Int\">{Value}</Value>";
    }

    public static ValueInt Deserialize(Stream buffer, byte flags)
    {
        S7p.DecodeInt16(buffer, out var value);
        return new ValueInt(value, flags);
    }
}

internal sealed class ValueIntArray : PValue
{
    short[] Value;

    public ValueIntArray(short[] value) : this(value, FLAGS_ARRAY)
    {
    }

    public ValueIntArray(short[] value, byte flags)
    {
        DatatypeFlags = flags;
        if (value != null)
        {
            Value = new short[value.Length];
            Array.Copy(value, Value, value.Length);
        }
    }

    public short[] GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.Int);
        ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
        for (var i = 0; i < Value.Length; i++)
        {
            ret += S7p.EncodeInt16(buffer, Value[i]);
        }
        return ret;
    }

    public override string ToString()
    {
        var s = new StringBuilder($"<Value type =\"IntArray\" size=\"{Value.Length}\">");
        for (var i = 0; i < Value.Length; i++)
        {
            s.Append($"<Value>{Value[i]}</Value>");
        }
        s.Append("</Value>");
        return s.ToString();
    }

    public static ValueIntArray Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        short[] value;
        uint size;
        if (!disableVlq)
        {
            S7p.DecodeUInt32Vlq(buffer, out size);
        }
        else
        {
            S7p.DecodeUInt32(buffer, out size);
        }
        value = new short[size];
        for (var i = 0; i < size; i++)
        {
            S7p.DecodeInt16(buffer, out value[i]);
        }
        return new ValueIntArray(value, flags);
    }
}

internal sealed class ValueDInt : PValue
{
    int Value;

    public ValueDInt(int value) : this(value, 0)
    {
    }

    public ValueDInt(int value, byte flags)
    {
        DatatypeFlags = flags;
        Value = value;
    }

    public int GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.DInt);
        ret += S7p.EncodeInt32Vlq(buffer, Value);
        return ret;
    }

    public override string ToString()
    {
        return $"<Value type=\"DInt\">{Value}</Value>";
    }

    public static ValueDInt Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        int value;
        if (!disableVlq)
        {
            S7p.DecodeInt32Vlq(buffer, out value);
        }
        else
        {
            S7p.DecodeInt32(buffer, out value);
        }
        return new ValueDInt(value, flags);
    }
}

internal sealed class ValueDIntArray : PValue
{
    int[] Value;

    public ValueDIntArray(int[] value) : this(value, FLAGS_ARRAY)
    {
    }

    public ValueDIntArray(int[] value, byte flags)
    {
        DatatypeFlags = flags;
        if (value != null)
        {
            Value = new int[value.Length];
            Array.Copy(value, Value, value.Length);
        }
    }

    public int[] GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.DInt);
        ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
        for (var i = 0; i < Value.Length; i++)
        {
            ret += S7p.EncodeInt32Vlq(buffer, Value[i]);
        }
        return ret;
    }

    public override string ToString()
    {
        var s = new StringBuilder($"<Value type =\"DIntArray\" size=\"{Value.Length}\">");
        for (var i = 0; i < Value.Length; i++)
        {
            s.Append($"<Value>{Value[i]}</Value>");
        }
        s.Append("</Value>");
        return s.ToString();
    }

    public static ValueDIntArray Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        int[] value;
        uint size;
        if (!disableVlq)
        {
            S7p.DecodeUInt32Vlq(buffer, out size);
            value = new int[size];
            for (var i = 0; i < size; i++)
            {
                S7p.DecodeInt32Vlq(buffer, out value[i]);
            }
        }
        else
        {
            S7p.DecodeUInt32(buffer, out size);
            value = new int[size];
            for (var i = 0; i < size; i++)
            {
                S7p.DecodeInt32(buffer, out value[i]);
            }
        }
        return new ValueDIntArray(value, flags);
    }
}

internal sealed class ValueDIntSparseArray : PValue
{
    Dictionary<uint, int> Value;

    public ValueDIntSparseArray(Dictionary<uint, int> value) : this(value, FLAGS_SPARSEARRAY)
    {
    }

    public ValueDIntSparseArray(Dictionary<uint, int> value, byte flags)
    {
        DatatypeFlags = flags;
        if (value != null)
        {
            Value = [with(value)];
        }
    }

    public Dictionary<uint, int> GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.DInt);
        foreach (var v in Value)
        {
            ret += S7p.EncodeUInt32Vlq(buffer, v.Key);
            ret += S7p.EncodeInt32Vlq(buffer, v.Value);
        }
        ret += S7p.EncodeByte(buffer, 0);
        return ret;
    }

    public override string ToString()
    {
        var s = new StringBuilder("<Value type =\"DIntSparseArray\">");
        foreach (var v in Value)
        {
            s.Append($"<Value key=\"{v.Key}\">{v.Value}</Value>");
        }
        s.Append("</Value>");
        return s.ToString();
    }

    public static ValueDIntSparseArray Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        Dictionary<uint, int> value = [];
        uint k;
        int v;
        if (!disableVlq)
        {
            S7p.DecodeUInt32Vlq(buffer, out k);
            while (k > 0)
            {
                S7p.DecodeInt32Vlq(buffer, out v);
                value.Add(k, v);
                S7p.DecodeUInt32Vlq(buffer, out k);
            }
        }
        else
        {
            S7p.DecodeUInt32(buffer, out k);
            while (k > 0)
            {
                S7p.DecodeInt32(buffer, out v);
                value.Add(k, v);
                S7p.DecodeUInt32(buffer, out k);
            }
        }
        return new ValueDIntSparseArray(value, flags);
    }
}

internal sealed class ValueLInt : PValue
{
    long Value;

    public ValueLInt(long value) : this(value, 0)
    {
    }

    public ValueLInt(long value, byte flags)
    {
        DatatypeFlags = flags;
        Value = value;
    }

    public long GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.LInt);
        ret += S7p.EncodeInt64Vlq(buffer, Value);
        return ret;
    }

    public override string ToString()
    {
        return $"<Value type=\"LInt\">{Value}</Value>";
    }

    public static ValueLInt Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        long value;
        if (!disableVlq)
        {
            S7p.DecodeInt64Vlq(buffer, out value);
        }
        else
        {
            S7p.DecodeInt64(buffer, out value);
        }
        return new ValueLInt(value, flags);
    }
}

internal sealed class ValueLIntArray : PValue
{
    long[] Value;

    public ValueLIntArray(long[] value) : this(value, FLAGS_ARRAY)
    {
    }

    public ValueLIntArray(long[] value, byte flags)
    {
        DatatypeFlags = flags;
        if (value != null)
        {
            Value = new long[value.Length];
            Array.Copy(value, Value, value.Length);
        }
    }

    public long[] GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.LInt);
        ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
        for (var i = 0; i < Value.Length; i++)
        {
            ret += S7p.EncodeInt64Vlq(buffer, Value[i]);
        }
        return ret;
    }

    public override string ToString()
    {
        var s = new StringBuilder($"<Value type =\"LIntArray\" size=\"{Value.Length}\">");
        for (var i = 0; i < Value.Length; i++)
        {
            s.Append($"<Value>{Value[i]}</Value>");
        }
        s.Append("</Value>");
        return s.ToString();
    }

    public static ValueLIntArray Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        long[] value;
        uint size;
        if (!disableVlq)
        {
            S7p.DecodeUInt32Vlq(buffer, out size);
            value = new long[size];
            for (var i = 0; i < size; i++)
            {
                S7p.DecodeInt64Vlq(buffer, out value[i]);
            }
        }
        else
        {
            S7p.DecodeUInt32(buffer, out size);
            value = new long[size];
            for (var i = 0; i < size; i++)
            {
                S7p.DecodeInt64(buffer, out value[i]);
            }
        }
        return new ValueLIntArray(value, flags);
    }
}

internal sealed class ValueByte : PValue
{
    byte Value;

    public ValueByte(byte value) : this(value, 0)
    {
    }

    public ValueByte(byte value, byte flags)
    {
        DatatypeFlags = flags;
        Value = value;
    }

    public byte GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.Byte);
        ret += S7p.EncodeByte(buffer, Value);
        return ret;
    }

    public override string ToString()
    {
        return $"<Value type=\"Byte\">{Value}</Value>";
    }

    public static ValueByte Deserialize(Stream buffer, byte flags)
    {
        S7p.DecodeByte(buffer, out var value);
        return new ValueByte(value, flags);
    }
}

internal sealed class ValueByteArray : PValue
{
    byte[] Value;

    public ValueByteArray(byte[] value) : this(value, FLAGS_ARRAY)
    {
    }

    public ValueByteArray(byte[] value, byte flags)
    {
        DatatypeFlags = flags;
        if (value != null)
        {
            Value = new byte[value.Length];
            Array.Copy(value, Value, value.Length);
        }
    }

    public byte[] GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.Byte);
        ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
        for (var i = 0; i < Value.Length; i++)
        {
            ret += S7p.EncodeByte(buffer, Value[i]);
        }
        return ret;
    }

    public override string ToString()
    {
        var s = new StringBuilder($"<Value type =\"ByteArray\" size=\"{Value.Length}\">");
        for (var i = 0; i < Value.Length; i++)
        {
            s.Append($"<Value>{Value[i]}</Value>");
        }
        s.Append("</Value>");
        return s.ToString();
    }

    public static ValueByteArray Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        byte[] value;
        uint size;
        if (!disableVlq)
        {
            S7p.DecodeUInt32Vlq(buffer, out size);
        }
        else
        {
            S7p.DecodeUInt32(buffer, out size);
        }
        value = new byte[size];
        for (var i = 0; i < size; i++)
        {
            S7p.DecodeByte(buffer, out value[i]);
        }
        return new ValueByteArray(value, flags);
    }
}

internal sealed class ValueWord : PValue
{
    ushort Value;

    public ValueWord(ushort value) : this(value, 0)
    {
    }

    public ValueWord(ushort value, byte flags)
    {
        DatatypeFlags = flags;
        Value = value;
    }

    public ushort GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.Word);
        ret += S7p.EncodeUInt16(buffer, Value);
        return ret;
    }

    public override string ToString()
    {
        return $"<Value type=\"Word\">{Value}</Value>";
    }

    public static ValueWord Deserialize(Stream buffer, byte flags)
    {
        S7p.DecodeUInt16(buffer, out var value);
        return new ValueWord(value, flags);
    }
}

internal sealed class ValueWordArray : PValue
{
    ushort[] Value;

    public ValueWordArray(ushort[] value) : this(value, FLAGS_ARRAY)
    {
    }

    public ValueWordArray(ushort[] value, byte flags)
    {
        DatatypeFlags = flags;
        if (value != null)
        {
            Value = new ushort[value.Length];
            Array.Copy(value, Value, value.Length);
        }
    }

    public ushort[] GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.Word);
        ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
        for (var i = 0; i < Value.Length; i++)
        {
            ret += S7p.EncodeUInt16(buffer, Value[i]);
        }
        return ret;
    }

    public override string ToString()
    {
        var s = new StringBuilder($"<Value type =\"WordArray\" size=\"{Value.Length}\">");
        for (var i = 0; i < Value.Length; i++)
        {
            s.Append($"<Value>{Value[i]}</Value>");
        }
        s.Append("</Value>");
        return s.ToString();
    }

    public static ValueWordArray Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        ushort[] value;
        uint size;
        if (!disableVlq)
        {
            S7p.DecodeUInt32Vlq(buffer, out size);
        }
        else
        {
            S7p.DecodeUInt32(buffer, out size);
        }
        value = new ushort[size];
        for (var i = 0; i < size; i++)
        {
            S7p.DecodeUInt16(buffer, out value[i]);
        }
        return new ValueWordArray(value, flags);
    }
}

internal sealed class ValueDWord : PValue
{
    uint Value;

    public ValueDWord(uint value) : this(value, 0)
    {
    }

    public ValueDWord(uint value, byte flags)
    {
        DatatypeFlags = flags;
        Value = value;
    }

    public uint GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.DWord);
        ret += S7p.EncodeUInt32(buffer, Value);
        return ret;
    }

    public override string ToString()
    {
        return $"<Value type=\"DWord\">{Value}</Value>";
    }

    public static ValueDWord Deserialize(Stream buffer, byte flags)
    {
        S7p.DecodeUInt32(buffer, out var value);
        return new ValueDWord(value, flags);
    }
}

internal sealed class ValueDWordArray : PValue
{
    uint[] Value;

    public ValueDWordArray(uint[] value) : this(value, FLAGS_ARRAY)
    {
    }

    public ValueDWordArray(uint[] value, byte flags)
    {
        DatatypeFlags = flags;
        if (value != null)
        {
            Value = new uint[value.Length];
            Array.Copy(value, Value, value.Length);
        }
    }

    public uint[] GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.DWord);
        ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
        for (var i = 0; i < Value.Length; i++)
        {
            ret += S7p.EncodeUInt32(buffer, Value[i]);
        }
        return ret;
    }

    public override string ToString()
    {
        var s = new StringBuilder($"<Value type =\"DWordArray\" size=\"{Value.Length}\">");
        for (var i = 0; i < Value.Length; i++)
        {
            s.Append($"<Value>{Value[i]}</Value>");
        }
        s.Append("</Value>");
        return s.ToString();
    }

    public static ValueDWordArray Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        uint[] value;
        uint size;
        if (!disableVlq)
        {
            S7p.DecodeUInt32Vlq(buffer, out size);
        }
        else
        {
            S7p.DecodeUInt32(buffer, out size);
        }
        value = new uint[size];
        for (var i = 0; i < size; i++)
        {
            S7p.DecodeUInt32(buffer, out value[i]);
        }
        return new ValueDWordArray(value, flags);
    }
}

internal sealed class ValueLWord : PValue
{
    ulong Value;

    public ValueLWord(ulong value) : this(value, 0)
    {
    }

    public ValueLWord(ulong value, byte flags)
    {
        DatatypeFlags = flags;
        Value = value;
    }

    public ulong GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.LWord);
        ret += S7p.EncodeUInt64(buffer, Value);
        return ret;
    }

    public override string ToString()
    {
        return $"<Value type=\"LWord\">{Value}</Value>";
    }

    public static ValueLWord Deserialize(Stream buffer, byte flags)
    {
        S7p.DecodeUInt64(buffer, out var value);
        return new ValueLWord(value, flags);
    }
}

internal sealed class ValueLWordArray : PValue
{
    ulong[] Value;

    public ValueLWordArray(ulong[] value) : this(value, FLAGS_ARRAY)
    {
    }

    public ValueLWordArray(ulong[] value, byte flags)
    {
        DatatypeFlags = flags;
        if (value != null)
        {
            Value = new ulong[value.Length];
            Array.Copy(value, Value, value.Length);
        }
    }

    public ulong[] GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.LWord);
        ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
        for (var i = 0; i < Value.Length; i++)
        {
            ret += S7p.EncodeUInt64(buffer, Value[i]);
        }
        return ret;
    }

    public override string ToString()
    {
        var s = new StringBuilder($"<Value type =\"LWordArray\" size=\"{Value.Length}\">");
        for (var i = 0; i < Value.Length; i++)
        {
            s.Append($"<Value>{Value[i]}</Value>");
        }
        s.Append("</Value>");
        return s.ToString();
    }

    public static ValueLWordArray Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        ulong[] value;
        uint size;
        if (!disableVlq)
        {
            S7p.DecodeUInt32Vlq(buffer, out size);
        }
        else
        {
            S7p.DecodeUInt32(buffer, out size);
        }
        value = new ulong[size];
        for (var i = 0; i < size; i++)
        {
            S7p.DecodeUInt64(buffer, out value[i]);
        }
        return new ValueLWordArray(value, flags);
    }
}

internal sealed class ValueReal : PValue
{
    float Value;

    public ValueReal(float value) : this(value, 0)
    {
    }

    public ValueReal(float value, byte flags)
    {
        DatatypeFlags = flags;
        Value = value;
    }

    public float GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.Real);
        ret += S7p.EncodeFloat(buffer, Value);
        return ret;
    }

    public override string ToString()
    {
        return $"<Value type=\"Real\">{Value}</Value>";
    }

    public static ValueReal Deserialize(Stream buffer, byte flags)
    {
        S7p.DecodeFloat(buffer, out var value);
        return new ValueReal(value, flags);
    }
}

internal sealed class ValueRealArray : PValue
{
    float[] Value;

    public ValueRealArray(float[] value) : this(value, FLAGS_ARRAY)
    {
    }

    public ValueRealArray(float[] value, byte flags)
    {
        DatatypeFlags = flags;
        if (value != null)
        {
            Value = new float[value.Length];
            Array.Copy(value, Value, value.Length);
        }
    }

    public float[] GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.Real);
        ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
        for (var i = 0; i < Value.Length; i++)
        {
            ret += S7p.EncodeFloat(buffer, Value[i]);
        }
        return ret;
    }

    public override string ToString()
    {
        var s = new StringBuilder($"<Value type =\"RealArray\" size=\"{Value.Length}\">");
        for (var i = 0; i < Value.Length; i++)
        {
            s.Append($"<Value>{Value[i]}</Value>");
        }
        s.Append("</Value>");
        return s.ToString();
    }

    public static ValueRealArray Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        float[] value;
        uint size;
        if (!disableVlq)
        {
            S7p.DecodeUInt32Vlq(buffer, out size);
        }
        else
        {
            S7p.DecodeUInt32(buffer, out size);
        }
        value = new float[size];
        for (var i = 0; i < size; i++)
        {
            S7p.DecodeFloat(buffer, out value[i]);
        }
        return new ValueRealArray(value, flags);
    }
}

internal sealed class ValueLReal : PValue
{
    double Value;

    public ValueLReal(double value) : this(value, 0)
    {
    }

    public ValueLReal(double value, byte flags)
    {
        DatatypeFlags = flags;
        Value = value;
    }

    public double GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.LReal);
        ret += S7p.EncodeDouble(buffer, Value);
        return ret;
    }

    public override string ToString()
    {
        return $"<Value type=\"LReal\">{Value}</Value>";
    }

    public static ValueLReal Deserialize(Stream buffer, byte flags)
    {
        S7p.DecodeDouble(buffer, out var value);
        return new ValueLReal(value, flags);
    }
}

internal sealed class ValueLRealArray : PValue
{
    double[] Value;

    public ValueLRealArray(double[] value) : this(value, FLAGS_ARRAY)
    {
    }

    public ValueLRealArray(double[] value, byte flags)
    {
        DatatypeFlags = flags;
        if (value != null)
        {
            Value = new double[value.Length];
            Array.Copy(value, Value, value.Length);
        }
    }

    public double[] GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.LReal);
        ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
        for (var i = 0; i < Value.Length; i++)
        {
            ret += S7p.EncodeDouble(buffer, Value[i]);
        }
        return ret;
    }

    public override string ToString()
    {
        var s = new StringBuilder($"<Value type =\"LRealArray\" size=\"{Value.Length}\">");
        for (var i = 0; i < Value.Length; i++)
        {
            s.Append($"<Value>{Value[i]}</Value>");
        }
        s.Append("</Value>");
        return s.ToString();
    }

    public static ValueLRealArray Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        double[] value;
        uint size;
        if (!disableVlq)
        {
            S7p.DecodeUInt32Vlq(buffer, out size);
        }
        else
        {
            S7p.DecodeUInt32(buffer, out size);
        }
        value = new double[size];
        for (var i = 0; i < size; i++)
        {
            S7p.DecodeDouble(buffer, out value[i]);
        }
        return new ValueLRealArray(value, flags);
    }
}

internal sealed class ValueTimestamp : PValue
{
    ulong Value;

    public ValueTimestamp(ulong value) : this(value, 0)
    {
    }

    public ValueTimestamp(ulong value, byte flags)
    {
        DatatypeFlags = flags;
        Value = value;
    }

    public ulong GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.Timestamp);
        ret += S7p.EncodeUInt64(buffer, Value);
        return ret;
    }

    public static string ToString(ulong Value)
    {
        var dt = new DateTime(1970, 1, 1);
        ulong v, ns;
        string fmt;
        v = Value;
        ns = v % 1000000000;
        v /= 1000000000;

        dt = dt.AddSeconds(v);

        if ((ns % 1000) > 0)
        {
            fmt = "{0}.{1:D09}";
        }
        else if ((ns % 1000000) > 0)
        {
            fmt = "{0}.{1:D06}";
            ns /= 1000;
        }
        else if ((ns % 1000000000) > 0)
        {
            fmt = "{0}.{1:D03}";
            ns /= 1000000;
        }
        else
        {
            return dt.ToString(CultureInfo.InvariantCulture);
        }
        return string.Format(CultureInfo.InvariantCulture, fmt, dt.ToString(CultureInfo.InvariantCulture), ns);
    }

    public override string ToString()
    {
        var str = ToString(Value);
        return $"<Value type=\"Timestamp\">{str}</Value>";
    }

    public static ValueTimestamp Deserialize(Stream buffer, byte flags)
    {
        S7p.DecodeUInt64(buffer, out var value);
        return new ValueTimestamp(value, flags);
    }
}

internal sealed class ValueTimestampArray : PValue
{
    ulong[] Value;

    public ValueTimestampArray(ulong[] value) : this(value, 0)
    {
    }

    public ValueTimestampArray(ulong[] value, byte flags)
    {
        DatatypeFlags = flags;
        Value = value;
    }

    public ulong[] GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.Timestamp);
        ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
        for (var i = 0; i < Value.Length; i++)
        {
            ret += S7p.EncodeUInt64(buffer, Value[i]);
        }
        return ret;
    }

    public override string ToString()
    {
        var s = new StringBuilder($"<Value type =\"TimestampArray\" size=\"{Value.Length}\">");
        for (var i = 0; i < Value.Length; i++)
        {
            s.Append($"<Value>{ValueTimestamp.ToString(Value[i])}</Value>");
        }
        s.Append("</Value>");
        return s.ToString();
    }

    public static ValueTimestampArray Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        ulong[] value;
        uint size;
        if (!disableVlq)
        {
            S7p.DecodeUInt32Vlq(buffer, out size);
        }
        else
        {
            S7p.DecodeUInt32(buffer, out size);
        }
        value = new ulong[size];
        for (var i = 0; i < size; i++)
        {
            S7p.DecodeUInt64(buffer, out value[i]);
        }
        return new ValueTimestampArray(value, flags);
    }
}

internal sealed class ValueTimespan : PValue
{
    long Value;

    public ValueTimespan(long value) : this(value, 0)
    {
    }

    public ValueTimespan(long value, byte flags)
    {
        DatatypeFlags = flags;
        Value = value;
    }

    public long GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.Timespan);
        ret += S7p.EncodeInt64Vlq(buffer, Value);
        return ret;
    }

    public static string ToString(long Value)
    {
        string str;
        long[] divs = [86400000000000, 3600000000000, 60000000000, 1000000000, 1000000, 1000, 1];
        string[] vfmt = ["{0}d", "{0:00}h", "{0:00}m", "{0:00}s", "{0:000}ms", "{0:000}us", "{0:000}ns"];
        long val;
        var timespan = Value;
        var time_negative = false;
        if (timespan == 0)
        {
            str = "LT#000ns";
        }
        else
        {
            if (timespan < 0)
            {
                str = "LT#-";
                time_negative = true;
                for (var i = 0; i < 7; i++)
                {
                    divs[i] = -divs[i];
                }
            }
            else
            {
                str = "LT#";
            }

            for (var i = 0; i < 7; i++)
            {
                val = timespan / divs[i];
                timespan -= val * divs[i];
                if (val > 0)
                {
                    str += string.Format(CultureInfo.InvariantCulture, vfmt[i], (int)val);
                    if ((!time_negative && timespan > 0) || (time_negative && timespan < 0))
                    {
                        str += "_";
                    }
                }
            }
        }
        return str;
    }

    public override string ToString()
    {
        var str = ToString(Value);
        return $"<Value type=\"Timespan\">{str}</Value>";
    }

    public static ValueTimespan Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        long value;
        if (!disableVlq)
        {
            S7p.DecodeInt64Vlq(buffer, out value);
        }
        else
        {
            S7p.DecodeInt64(buffer, out value);
        }
        return new ValueTimespan(value, flags);
    }
}

internal sealed class ValueTimespanArray : PValue
{
    long[] Value;

    public ValueTimespanArray(long[] value) : this(value, FLAGS_ARRAY)
    {
    }

    public ValueTimespanArray(long[] value, byte flags)
    {
        DatatypeFlags = flags;
        if (value != null)
        {
            Value = new long[value.Length];
            Array.Copy(value, Value, value.Length);
        }
    }

    public long[] GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.LReal);
        ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
        for (var i = 0; i < Value.Length; i++)
        {
            ret += S7p.EncodeInt64Vlq(buffer, Value[i]);
        }
        return ret;
    }

    public override string ToString()
    {
        var s = new StringBuilder($"<Value type =\"ValueTimespanArray\" size=\"{Value.Length}\">");
        for (var i = 0; i < Value.Length; i++)
        {
            s.Append($"<Value>{ValueTimespan.ToString(Value[i])}</Value>");
        }
        s.Append("</Value>");
        return s.ToString();
    }

    public static ValueTimespanArray Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        long[] value;
        uint size;
        if (!disableVlq)
        {
            S7p.DecodeUInt32Vlq(buffer, out size);
        }
        else
        {
            S7p.DecodeUInt32(buffer, out size);
        }
        value = new long[size];
        for (var i = 0; i < size; i++)
        {
            if (!disableVlq)
            {
                S7p.DecodeInt64Vlq(buffer, out value[i]);
            }
            else
            {
                S7p.DecodeInt64(buffer, out value[i]);
            }
        }
        return new ValueTimespanArray(value, flags);
    }
}

internal sealed class ValueRID : PValue
{
    uint Value;

    public ValueRID(uint rid) : this(rid, 0)
    {
    }

    public ValueRID(uint rid, byte flags)
    {
        DatatypeFlags = flags;
        Value = rid;
    }

    public uint GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.RID);
        ret += S7p.EncodeUInt32(buffer, Value);
        return ret;
    }

    public override string ToString()
    {
        return $"<Value type=\"RID\">{Value}</Value>";
    }

    public static ValueRID Deserialize(Stream buffer, byte flags)
    {
        S7p.DecodeUInt32(buffer, out var value);
        return new ValueRID(value, flags);
    }
}

internal sealed class ValueRIDArray : PValue
{
    uint[] Value;

    public ValueRIDArray(uint[] value) : this(value, FLAGS_ARRAY)
    {
    }

    public ValueRIDArray(uint[] value, byte flags)
    {
        DatatypeFlags = flags;
        if (value != null)
        {
            Value = new uint[value.Length];
            Array.Copy(value, Value, value.Length);
        }
    }

    public uint[] GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.RID);
        ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
        for (var i = 0; i < Value.Length; i++)
        {
            ret += S7p.EncodeUInt32(buffer, Value[i]);
        }
        return ret;
    }

    public override string ToString()
    {
        var s = new StringBuilder($"<Value type =\"RIDArray\" size=\"{Value.Length}\">");
        for (var i = 0; i < Value.Length; i++)
        {
            s.Append($"<Value>{Value[i]}</Value>");
        }
        s.Append("</Value>");
        return s.ToString();
    }

    public static ValueRIDArray Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        uint[] value;
        uint size;
        if (!disableVlq)
        {
            S7p.DecodeUInt32Vlq(buffer, out size);
        }
        else
        {
            S7p.DecodeUInt32(buffer, out size);
        }
        value = new uint[size];
        for (var i = 0; i < size; i++)
        {
            S7p.DecodeUInt32(buffer, out value[i]);
        }
        return new ValueRIDArray(value, flags);
    }
}

internal sealed class ValueAID : PValue
{
    uint Value;

    public ValueAID(uint value) : this(value, 0)
    {
    }

    public ValueAID(uint value, byte flags)
    {
        DatatypeFlags = flags;
        Value = value;
    }

    public uint GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.AID);
        ret += S7p.EncodeUInt32Vlq(buffer, Value);
        return ret;
    }

    public override string ToString()
    {
        return $"<Value type=\"AID\">{Value}</Value>";
    }

    public static ValueAID Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        uint value;
        if (!disableVlq)
        {
            S7p.DecodeUInt32Vlq(buffer, out value);
        }
        else
        {
            S7p.DecodeUInt32(buffer, out value);
        }
        return new ValueAID(value, flags);
    }
}

internal sealed class ValueAIDArray : PValue
{
    uint[] Value;

    public ValueAIDArray(uint[] value) : this(value, FLAGS_ARRAY)
    {
    }

    public ValueAIDArray(uint[] value, byte flags)
    {
        DatatypeFlags = flags;
        if (value != null)
        {
            Value = new uint[value.Length];
            Array.Copy(value, Value, value.Length);
        }
    }

    public uint[] GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.AID);
        ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
        for (var i = 0; i < Value.Length; i++)
        {
            ret += S7p.EncodeUInt32Vlq(buffer, Value[i]);
        }
        return ret;
    }

    public override string ToString()
    {
        var s = new StringBuilder($"<Value type =\"AIDArray\" size=\"{Value.Length}\">");
        for (var i = 0; i < Value.Length; i++)
        {
            s.Append($"<Value>{Value[i]}</Value>");
        }
        s.Append("</Value>");
        return s.ToString();
    }

    public static ValueAIDArray Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        uint[] value;
        uint size;
        if (!disableVlq)
        {
            S7p.DecodeUInt32Vlq(buffer, out size);
            value = new uint[size];
            for (var i = 0; i < size; i++)
            {
                S7p.DecodeUInt32Vlq(buffer, out value[i]);
            }
        }
        else
        {
            S7p.DecodeUInt32(buffer, out size);
            value = new uint[size];
            for (var i = 0; i < size; i++)
            {
                S7p.DecodeUInt32(buffer, out value[i]);
            }
        }
        return new ValueAIDArray(value, flags);
    }
}

internal sealed class ValueBlob : PValue
{
    public uint BlobRootId;
    byte[] Value;

    public bool HasBlobType; // Special
    public byte BlobType;    // Special

    public ValueBlob(uint blobRootId, byte[] value) : this(blobRootId, value, 0)
    {
    }

    public ValueBlob(uint blobRootId, byte[] value, byte flags)
    {
        BlobRootId = blobRootId;
        DatatypeFlags = flags;
        // A blob with size zero is allowed and no error.
        if (value != null)
        {
            Value = new byte[value.Length];
            Array.Copy(value, Value, value.Length);
        }
    }

    public byte[] GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.Blob);
        ret += S7p.EncodeUInt32Vlq(buffer, BlobRootId);
        ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
        ret += S7p.EncodeOctets(buffer, Value);
        return ret;
    }

    public override string ToString()
    {
        var s = new StringBuilder(!HasBlobType
            ? $"<Value type=\"Blob\" BlobRootId=\"{BlobRootId}\">"
            : $"<Value type=\"Blob\" BlobRootId=\"{BlobRootId}\" BlobType=\"{BlobType}\">");
        if (Value != null)
        {
            s.Append(BitConverter.ToString(Value));
        }
        s.Append("</Value>");
        return s.ToString();
    }

    public static ValueBlob Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        uint blobRootId;
        if (!disableVlq)
        {
            S7p.DecodeUInt32Vlq(buffer, out blobRootId);
        }
        else
        {
            S7p.DecodeUInt32(buffer, out blobRootId);
        }
        byte blobType = 0;
        var hasBlobType = false;
        // Special handling:
        // If first value > 1 then special format with 8 additional bytes + 1 type-id + value.
        // On HMI project transfer this occurs with ID=1 (as SubStream) but without the extra bytes.
        // Used for example in Alarm Notifications for the AssociatedValues.
        if (blobRootId > 1)
        {
            hasBlobType = true;
            S7p.DecodeUInt64(buffer, out _); // Don't use it for now. All bytes were zero so far.
            S7p.DecodeByte(buffer, out blobType);
            // - If BlobType value == 0x02 or 0x03, then follows a length specification and the number of bytes.
            //   This is used in alarms and the associated values inside the blob-array.
            // - If BlobType value == 0x00, then follows an ID/value list.
            //   This is used in program transfer.
            switch (blobType)
            {
                case 0x02:
                case 0x03:
                    // handling below is the same from here
                    break;
                default:
                    // can't handle this for now, this is completely different...
                    throw new NotImplementedException();
            }
        }

        uint blobSize;
        if (!disableVlq)
        {
            S7p.DecodeUInt32Vlq(buffer, out blobSize);
        }
        else
        {
            S7p.DecodeUInt32(buffer, out blobSize);
        }
        S7p.DecodeOctets(buffer, (int)blobSize, out var value);
        var blob = new ValueBlob(blobRootId, value, flags)
        {
            HasBlobType = hasBlobType,
            BlobType = blobType
        };
        return blob;
    }
}

internal sealed class ValueBlobArray : PValue
{
    ValueBlob[] Value;

    public ValueBlobArray(ValueBlob[] value) : this(value, FLAGS_ADDRESSARRAY)
    {
    }

    public ValueBlobArray(ValueBlob[] value, byte flags)
    {
        DatatypeFlags = flags;
        if (value != null)
        {
            Value = new ValueBlob[value.Length];
            Array.Copy(value, Value, value.Length);
        }
    }

    public ValueBlob[] GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.Blob);
        ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
        for (var i = 0; i < Value.Length; i++)
        {
            ret += Value[i].Serialize(buffer);
        }
        return ret;
    }

    public override string ToString()
    {
        var s = new StringBuilder($"<Value type =\"ValueBlobArray\" size=\"{Value.Length}\">");
        for (var i = 0; i < Value.Length; i++)
        {
            s.Append($"<Value>{Value[i]}</Value>");
        }
        s.Append("</Value>");
        return s.ToString();
    }

    public static ValueBlobArray Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        uint size;
        if (!disableVlq)
        {
            S7p.DecodeUInt32Vlq(buffer, out size);
        }
        else
        {
            S7p.DecodeUInt32(buffer, out size);
        }
        var value = new ValueBlob[size];
        for (var i = 0; i < size; i++)
        {
            value[i] = ValueBlob.Deserialize(buffer, flags, disableVlq);
        }
        return new ValueBlobArray(value, flags);
    }
}

internal sealed class ValueBlobSparseArray : PValue
{
    public struct BlobEntry
    {
        public uint blobRootId;
        public byte[] value;
    }

    public Dictionary<uint, BlobEntry> Value;

    public ValueBlobSparseArray(Dictionary<uint, BlobEntry> value) : this(value, FLAGS_SPARSEARRAY)
    {
    }

    public ValueBlobSparseArray(Dictionary<uint, BlobEntry> value, byte flags)
    {
        DatatypeFlags = flags;
        if (value != null)
        {
            Value = [with(value)];
        }
    }

    public Dictionary<uint, BlobEntry> GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.Blob);
        foreach (var v in Value)
        {
            ret += S7p.EncodeUInt32Vlq(buffer, v.Key);
            ret += S7p.EncodeUInt32Vlq(buffer, v.Value.blobRootId);
            ret += S7p.EncodeUInt32Vlq(buffer, (uint)v.Value.value.Length);
            ret += S7p.EncodeOctets(buffer, v.Value.value);
        }
        ret += S7p.EncodeByte(buffer, 0);
        return ret;
    }

    public override string ToString()
    {
        var s = new StringBuilder("<Value type=\"BlobSparseArray\">");
        foreach (var v in Value)
        {
            s.Append($"<Value key=\"{v.Key}\" BlobRootId=\"{v.Value.blobRootId}\">");
            if (Value != null && v.Value.value != null)
            {
                s.Append(BitConverter.ToString(v.Value.value));
            }
            s.Append("</Value>");
        }
        s.Append("</Value>");
        return s.ToString();
    }

    public static ValueBlobSparseArray Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        Dictionary<uint, BlobEntry> value = [];
        uint k;
        var v = new BlobEntry();
        uint blobSize;
        if (!disableVlq)
        {
            S7p.DecodeUInt32Vlq(buffer, out k);
            while (k > 0)
            {
                S7p.DecodeUInt32Vlq(buffer, out v.blobRootId);
                S7p.DecodeUInt32Vlq(buffer, out blobSize);
                v.value = new byte[blobSize];
                S7p.DecodeOctets(buffer, (int)blobSize, out v.value);
                value.Add(k, v);

                S7p.DecodeUInt32Vlq(buffer, out k);
            }
        }
        else
        {
            S7p.DecodeUInt32(buffer, out k);
            while (k > 0)
            {
                S7p.DecodeUInt32(buffer, out v.blobRootId);
                S7p.DecodeUInt32(buffer, out blobSize);
                v.value = new byte[blobSize];
                S7p.DecodeOctets(buffer, (int)blobSize, out v.value);
                value.Add(k, v);

                S7p.DecodeUInt32(buffer, out k);
            }
        }
        return new ValueBlobSparseArray(value, flags);
    }
}

internal sealed class ValueWString : PValue
{
    string Value;

    public ValueWString(string value) : this(value, 0)
    {
    }

    public ValueWString(string value, byte flags)
    {
        DatatypeFlags = flags;
        Value = value;
    }

    public string GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.WString);
        ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
        ret += S7p.EncodeWString(buffer, Value);
        return ret;
    }

    public override string ToString()
    {
        return $"<Value type=\"WString\">{Value}</Value>";
    }

    public static ValueWString Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        uint stringlen;
        if (!disableVlq)
        {
            S7p.DecodeUInt32Vlq(buffer, out stringlen);
        }
        else
        {
            S7p.DecodeUInt32(buffer, out stringlen);
        }
        S7p.DecodeWString(buffer, (int)stringlen, out var value);
        return new ValueWString(value, flags);
    }
}

internal sealed class ValueWStringArray : PValue
{
    string[] Value;

    public ValueWStringArray(string[] value) : this(value, FLAGS_ADDRESSARRAY)
    {
    }

    public ValueWStringArray(string[] value, byte flags)
    {
        DatatypeFlags = flags;
        Value = value;
    }

    public string[] GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.WString);
        ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
        for (var i = 0; i < Value.Length; i++)
        {
            ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value[i].Length);
            ret += S7p.EncodeWString(buffer, Value[i]);
        }
        return ret;
    }

    public override string ToString()
    {
        var s = new StringBuilder($"<Value type =\"WStringArray\" size=\"{Value.Length}\">");
        for (var i = 0; i < Value.Length; i++)
        {
            s.Append($"<Value>{Value[i]}</Value>");
        }
        s.Append("</Value>");
        return s.ToString();
    }

    public static ValueWStringArray Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        string[] value;
        uint stringlen;
        uint arraySize;
        if (!disableVlq)
        {
            S7p.DecodeUInt32Vlq(buffer, out arraySize);
            value = new string[arraySize];
            for (var i = 0; i < arraySize; i++)
            {
                S7p.DecodeUInt32Vlq(buffer, out stringlen);
                S7p.DecodeWString(buffer, (int)stringlen, out value[i]);
            }
        }
        else
        {
            S7p.DecodeUInt32(buffer, out arraySize);
            value = new string[arraySize];
            for (var i = 0; i < arraySize; i++)
            {
                S7p.DecodeUInt32(buffer, out stringlen);
                S7p.DecodeWString(buffer, (int)stringlen, out value[i]);
            }
        }
        return new ValueWStringArray(value, flags);
    }
}

internal sealed class ValueWStringSparseArray : PValue
{
    Dictionary<uint, string> Value;

    public ValueWStringSparseArray(Dictionary<uint, string> value) : this(value, FLAGS_SPARSEARRAY)
    {
    }

    public ValueWStringSparseArray(Dictionary<uint, string> value, byte flags)
    {
        DatatypeFlags = flags;
        if (value != null)
        {
            Value = [with(value)];
        }
    }

    public Dictionary<uint, string> GetValue()
    {
        return Value;
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.WString);
        foreach (var v in Value)
        {
            ret += S7p.EncodeUInt32Vlq(buffer, v.Key);
            ret += S7p.EncodeUInt32Vlq(buffer, (uint)v.Value.Length);
            ret += S7p.EncodeWString(buffer, v.Value);
        }
        ret += S7p.EncodeByte(buffer, 0);
        return ret;
    }

    public override string ToString()
    {
        var s = new StringBuilder("<Value type =\"WStringSparseArray\">");
        foreach (var v in Value)
        {
            s.Append($"<Value key=\"{v.Key}\">{v.Value}</Value>");
        }
        s.Append("</Value>");
        return s.ToString();
    }

    public static ValueWStringSparseArray Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        Dictionary<uint, string> value = [];
        uint k;
        uint stringlen;
        string v;
        if (!disableVlq)
        {
            S7p.DecodeUInt32Vlq(buffer, out k);
            while (k > 0)
            {
                S7p.DecodeUInt32Vlq(buffer, out stringlen);
                S7p.DecodeWString(buffer, (int)stringlen, out v);
                value.Add(k, v);
                S7p.DecodeUInt32Vlq(buffer, out k);
            }
        }
        else
        {
            S7p.DecodeUInt32(buffer, out k);
            while (k > 0)
            {
                S7p.DecodeUInt32(buffer, out stringlen);
                S7p.DecodeWString(buffer, (int)stringlen, out v);
                value.Add(k, v);
                S7p.DecodeUInt32(buffer, out k);
            }
        }
        return new ValueWStringSparseArray(value, flags);
    }
}

internal sealed class ValueStruct : PValue
{
    uint Value;
    private Dictionary<uint, PValue> Elements = [];
    /// <summary>
    /// InterfaceTimestamp: Only relevant if Value is transmitted as Packed Struct.
    /// Used on transmitting Systemdatatypes in a compact way (e.g. DTL).
    /// </summary>
    public ulong PackedStructInterfaceTimestamp;
    public uint PackedStructTransportFlags = (uint)PackedStructTransportFlagBits.AlwaysSet; // Use 2 as standard value (probably a bitfield)

    [Flags]
    public enum PackedStructTransportFlagBits
    {
        None = 0,
        ClassicNonoptimizedOffsets = 1 << 0,    // Is set when a struct is read from non-optimized datablock
        AlwaysSet = 1 << 1,                     // Is (so far) always set
        Count2Present = 1 << 10                 // If this bit is set, then there's a 2nd counter present. Which if for a rare case you can read an array of struct, if the complete size, the 1st for one element.
    }

    public ValueStruct(uint value) : this(value, 0)
    {
    }

    public ValueStruct(uint value, byte flags)
    {
        DatatypeFlags = flags;
        Value = value;
        Elements = [];
    }

    public uint GetValue()
    {
        return Value;
    }

    public void AddStructElement(uint id, PValue elem)
    {
        Elements.Add(id, elem);
    }

    public PValue GetStructElement(uint id)
    {
        return Elements[id];
    }

    public override int Serialize(Stream buffer)
    {
        var ret = 0;

        ret += S7p.EncodeByte(buffer, DatatypeFlags);
        ret += S7p.EncodeByte(buffer, Datatype.Struct);
        ret += S7p.EncodeUInt32(buffer, Value);
        // TODO: EXPERIMENTAL!
        // Packed Struct, see comment in Deserialize
        if (Value is (> 0x90000000 and < 0x9fffffff) or (> 0x02000000 and < 0x02ffffff))
        {
            // There should be only one Element? The key from the dictionary element is not used.
            // It's somewhat all hacked into the Struct variant...
            foreach (var elem in Elements)
            {
                // The timestamp must be exactly the same as from browsing the Plc, otherwise we
                // get an Error "InvalidTimestampInTypeSafeBlob"
                ret += S7p.EncodeUInt64(buffer, PackedStructInterfaceTimestamp);

                ret += S7p.EncodeUInt32Vlq(buffer, PackedStructTransportFlags);

                if (elem.Value.GetType() == typeof(ValueByteArray))
                {
                    var barr = ((ValueByteArray)elem.Value).GetValue();
                    var elementcount = (uint)barr.Length;
                    ret += S7p.EncodeUInt32Vlq(buffer, elementcount);
                    // Don't use the Serialize method of ValueByteArray, because there is an additional header we don't want here.
                    for (var i = 0; i < barr.Length; i++)
                    {
                        ret += S7p.EncodeByte(buffer, barr[i]);
                    }
                }
                else
                {
                    S7Log.Instance?.LogDebug("ValueStruct.Serialize(): Elements[0] is not of type ValueByteArray");
                }
            }
        }
        else
        {
            foreach (var elem in Elements)
            {
                ret += S7p.EncodeUInt32Vlq(buffer, elem.Key);
                ret += elem.Value.Serialize(buffer);
            }
            ret += S7p.EncodeByte(buffer, 0); // List Terminator
        }
        return ret;
    }

    public override string ToString()
    {
        var s = new StringBuilder("");
        s.AppendLine("<Value type =\"Struct\">");
        s.AppendLine($"<ID>{Value}</ID>");
        if (Value is (> 0x90000000 and < 0x9fffffff) or (> 0x02000000 and < 0x02ffffff))
        {
            s.AppendLine($"<PackedStructInterfaceTimestamp>{PackedStructInterfaceTimestamp}</PackedStructInterfaceTimestamp>");
            s.AppendLine($"<PackedStructTransportFlags>{PackedStructTransportFlags}</PackedStructTransportFlags>");
        }
        foreach (var elem in Elements)
        {
            s.AppendLine("<Element>");
            s.AppendLine($"<ID>{elem.Key}</ID>");
            s.AppendLine(elem.Value.ToString());
            s.AppendLine("</Element>");
        }
        s.AppendLine("</Value>");
        return s.ToString();
    }

    public static ValueStruct Deserialize(Stream buffer, byte flags, bool disableVlq)
    {
        S7p.DecodeUInt32(buffer, out var value);
        ValueStruct stru;
        // Special handling of datatype struct and some specific ID ranges:
        // Some struct elements aren't transmitted as single elements. Instead they are packed (e.g. DTL-Struct).
        // The ID number range where this is used is only guessed (Type Info).
        if (value is (> 0x90000000 and < 0x9fffffff) or (> 0x02000000 and < 0x02ffffff))
        {
            // Packed Struct
            // These are system datatypes. Either the information about them must be read out of the CPU before,
            // or must be known before. As the data are transmitted as Bytearrays, return them in this type. Interpretation must be done later.
            stru = new ValueStruct(value, flags);

            S7p.DecodeUInt64(buffer, out stru.PackedStructInterfaceTimestamp);
            uint transp_flags;
            uint elementcount;
            if (!disableVlq)
            {
                S7p.DecodeUInt32Vlq(buffer, out transp_flags);
                S7p.DecodeUInt32Vlq(buffer, out elementcount);
                if ((transp_flags & (uint)PackedStructTransportFlagBits.Count2Present) != 0)
                {
                    // Here's an additional counter value, for whatever reason...
                    S7p.DecodeUInt32Vlq(buffer, out elementcount);
                }
            }
            else
            {
                S7p.DecodeUInt32(buffer, out transp_flags);
                S7p.DecodeUInt32(buffer, out elementcount);
                if ((transp_flags & (uint)PackedStructTransportFlagBits.Count2Present) != 0)
                {
                    // Here's an additional counter value, for whatever reason...
                    S7p.DecodeUInt32(buffer, out elementcount);
                }
            }
            stru.PackedStructTransportFlags = transp_flags;
            var barr = new byte[elementcount];
            for (var i = 0; i < elementcount; i++)
            {
                S7p.DecodeByte(buffer, out barr[i]);
            }
            var elem = new ValueByteArray(barr);
            stru.AddStructElement(value, elem);
        }
        else
        {
            PValue elem;
            stru = new ValueStruct(value, flags);
            if (!disableVlq)
            {
                S7p.DecodeUInt32Vlq(buffer, out value);
                while (value > 0)
                {
                    elem = PValue.Deserialize(buffer, disableVlq);
                    stru.AddStructElement(value, elem);
                    S7p.DecodeUInt32Vlq(buffer, out value);
                }
            }
            else
            {
                S7p.DecodeUInt32(buffer, out value);
                while (value > 0)
                {
                    elem = PValue.Deserialize(buffer, disableVlq);
                    stru.AddStructElement(value, elem);
                    S7p.DecodeUInt32(buffer, out value);
                }
            }
        }
        return stru;
    }
}
