// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
using System.Buffers.Binary;

namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

internal static class S7p
{
    public static int EncodeByte(Stream buffer, byte value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        buffer.WriteByte(value);
        return 1;
    }

    public static int EncodeUInt16(Stream buffer, ushort value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        buffer.WriteByte((byte)((value & 0xFF00) >> 08));
        buffer.WriteByte((byte)((value & 0x00FF) >> 00));
        return 2;
    }

    public static int EncodeInt16(Stream buffer, short value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        buffer.WriteByte((byte)((value & 0xFF00) >> 08));
        buffer.WriteByte((byte)((value & 0x00FF) >> 00));
        return 2;
    }

    public static int EncodeUInt32(Stream buffer, uint value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        buffer.WriteByte((byte)((value & 0xFF000000) >> 24));
        buffer.WriteByte((byte)((value & 0x00FF0000) >> 16));
        buffer.WriteByte((byte)((value & 0x0000FF00) >> 08));
        buffer.WriteByte((byte)((value & 0x000000FF) >> 00));
        return 4;
    }

    public static int EncodeInt32(Stream buffer, int value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        buffer.WriteByte((byte)((value & 0xFF000000) >> 24));
        buffer.WriteByte((byte)((value & 0x00FF0000) >> 16));
        buffer.WriteByte((byte)((value & 0x0000FF00) >> 08));
        buffer.WriteByte((byte)((value & 0x000000FF) >> 00));
        return 4;
    }

    public static int EncodeUInt64(Stream buffer, ulong value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        buffer.WriteByte((byte)((value & 0xFF00000000000000) >> 56));
        buffer.WriteByte((byte)((value & 0x00FF000000000000) >> 48));
        buffer.WriteByte((byte)((value & 0x0000FF0000000000) >> 40));
        buffer.WriteByte((byte)((value & 0x000000FF00000000) >> 32));
        buffer.WriteByte((byte)((value & 0x00000000FF000000) >> 24));
        buffer.WriteByte((byte)((value & 0x0000000000FF0000) >> 16));
        buffer.WriteByte((byte)((value & 0x000000000000FF00) >> 08));
        buffer.WriteByte((byte)((value & 0x00000000000000FF) >> 00));
        return 8;
    }

    public static int EncodeInt64(Stream buffer, long value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        buffer.WriteByte((byte)(((ulong)value & 0xFF00000000000000) >> 56));
        buffer.WriteByte((byte)((value & 0x00FF000000000000) >> 48));
        buffer.WriteByte((byte)((value & 0x0000FF0000000000) >> 40));
        buffer.WriteByte((byte)((value & 0x000000FF00000000) >> 32));
        buffer.WriteByte((byte)((value & 0x00000000FF000000) >> 24));
        buffer.WriteByte((byte)((value & 0x0000000000FF0000) >> 16));
        buffer.WriteByte((byte)((value & 0x000000000000FF00) >> 08));
        buffer.WriteByte((byte)((value & 0x00000000000000FF) >> 00));
        return 8;
    }

    public static int DecodeByte(Stream buffer, out byte value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Length - buffer.Position < 1)
        {
            value = 0;
            return 0;
        }
        value = (byte)buffer.ReadByte();
        return 1;
    }

    public static int DecodeUInt16(Stream buffer, out ushort value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Length - buffer.Position < 2)
        {
            value = 0;
            return 0;
        }
        value = (ushort)((buffer.ReadByte() << 8) | buffer.ReadByte());
        return 2;
    }

    // Little Endian
    public static int DecodeUInt16LE(Stream buffer, out ushort value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Length - buffer.Position < 2)
        {
            value = 0;
            return 0;
        }
        value = (ushort)(buffer.ReadByte() | (buffer.ReadByte() << 8));
        return 2;
    }

    public static int DecodeInt16(Stream buffer, out short value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Length - buffer.Position < 2)
        {
            value = 0;
            return 0;
        }
        value = (short)((buffer.ReadByte() << 8) | buffer.ReadByte());
        return 2;
    }

    public static int DecodeUInt32(Stream buffer, out uint value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Length - buffer.Position < 4)
        {
            value = 0;
            return 0;
        }
        value = (uint)((buffer.ReadByte() << 24) | (buffer.ReadByte() << 16) | (buffer.ReadByte() << 8) | buffer.ReadByte());
        return 4;
    }

    // Little Endian
    public static int DecodeUInt32LE(Stream buffer, out uint value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Length - buffer.Position < 4)
        {
            value = 0;
            return 0;
        }
        value = (uint)(buffer.ReadByte() | (buffer.ReadByte() << 8) | (buffer.ReadByte() << 16) | (buffer.ReadByte() << 24));
        return 4;
    }

    // Little Endian
    public static int DecodeInt32LE(Stream buffer, out int value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Length - buffer.Position < 4)
        {
            value = 0;
            return 0;
        }
        value = buffer.ReadByte() | (buffer.ReadByte() << 8) | (buffer.ReadByte() << 16) | (buffer.ReadByte() << 24);
        return 4;
    }

    public static int DecodeInt32(Stream buffer, out int value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Length - buffer.Position < 4)
        {
            value = 0;
            return 0;
        }
        value = (buffer.ReadByte() << 24) | (buffer.ReadByte() << 16) | (buffer.ReadByte() << 8) | buffer.ReadByte();
        return 4;
    }

    public static int DecodeUInt64(Stream buffer, out ulong value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Length - buffer.Position < 8)
        {
            value = 0;
            return 0;
        }
        Span<byte> b = stackalloc byte[8];
        buffer.ReadExactly(b);
        value = BinaryPrimitives.ReadUInt64BigEndian(b);
        return 8;
    }

    public static int DecodeInt64(Stream buffer, out long value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Length - buffer.Position < 8)
        {
            value = 0;
            return 0;
        }
        Span<byte> b = stackalloc byte[8];
        buffer.ReadExactly(b);
        value = BinaryPrimitives.ReadInt64BigEndian(b);
        return 8;
    }

    public static int EncodeUInt32Vlq(Stream buffer, uint value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        Span<byte> bytes = stackalloc byte[5]; // 栈上临时缓冲，避免每次 VLQ 编码堆分配（高频采集热点）
        int i, j;
        for (i = 4; i > 0; i--)
        {
            if ((value & (0x7f << (i * 7))) > 0)
            {
                break;
            }
        }
        for (j = 0; j <= i; j++)
        {
            bytes[j] = (byte)(((value >> ((i - j) * 7)) & 0x7f) | 0x80);
        }
        bytes[i] ^= 0x80;
        buffer.Write(bytes[..(i + 1)]);
        return i + 1;
    }

    public static int DecodeUInt32Vlq(Stream buffer, out uint value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Length - buffer.Position < 1) { value = 0; return 0; } // 流尾保护，避免把 -1 当作 0xFF 续位
        int counter;
        uint val = 0;
        byte octet;
        var length = 0;
        for (counter = 1; counter <= 5; counter++)
        {
            octet = (byte)buffer.ReadByte();
            length++;
            val <<= 7;
            var cont = (byte)(octet & 0x80);
            octet &= 0x7f;
            val += octet;
            if (cont == 0)
            {
                break;
            }
        }
        value = val;
        return length;
    }

    public static int DecodeInt32Vlq(Stream buffer, out int value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Length - buffer.Position < 1) { value = 0; return 0; } // 流尾保护，避免把 -1 当作 0xFF 续位
        int counter;
        var val = 0;
        byte octet;
        var length = 0;
        for (counter = 1; counter <= 5; counter++)
        {
            octet = (byte)buffer.ReadByte();
            length++;
            if ((counter == 1) && ((octet & 0x40) != 0))
            {     // check sign 
                octet &= 0xbf;
                val = -64; // pre-load with one complement, excluding first 6 bits
            }
            else
            {
                val <<= 7;
            }
            var cont = (byte)(octet & 0x80);
            octet &= 0x7f;
            val += octet;
            if (cont == 0)
            {
                break;
            }
        }
        value = val;
        return length;
    }

    public static int EncodeInt32Vlq(Stream buffer, int value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        // You can write negative values like
        // -1234567
        // with full one complement bytes as:
        // 8f ff b4 d2 79
        // The read back value (from plc) is encoded as:
        // ff b4 d2 79
        //   or:
        // -255
        // Write with full one complement bytes as:
        // 8f ff ff fe 01
        // The read back value (from plc) is encoded as:
        // fe 01
        //
        // The actual method writes the values in the 2nd compact variant.
        // The decode algorithms can handle both variants.

        Span<byte> b = stackalloc byte[5]; // 栈上临时缓冲，避免每次 VLQ 编码堆分配
        var abs_v = value == int.MinValue ? 2147483648 : (uint)Math.Abs(value);
        b[0] = (byte)(value & 0x7f);
        var length = 1;
        for (var i = 1; i < 5; i++)
        {
            if (abs_v >= 0x40)
            {
                length++;
                abs_v >>= 7;
                value >>= 7;
                b[i] = (byte)((value & 0x7f) + 0x80);
            }
            else
            {
                break;
            }
        }

        // Reverse order of bytes
        for (var i = length - 1; i >= 0; i--)
        {
            buffer.WriteByte(b[i]);
        }
        return length;
    }

    public static int EncodeUInt64Vlq(Stream buffer, ulong value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        // Special handling in the 64 bit VLQ variants:
        // The special handling on the 64 bit variants is neccessary, because without this we would need
        // max. 10 bytes than the max. 9 now.
        // Every byte looses 1 bit for the "continue" flag. For 8 bytes loosing 1 bit, we need 1 more byte.
        // Which in the normal variant would have only 7 bit of space, because 1 bit is for the "continue" flag.
        // The special handling allows to use all 8 bits in the additional 9th byte.
        Span<byte> b = stackalloc byte[9]; // 栈上临时缓冲，避免每次 VLQ 编码堆分配

        var special = value > 0x00ffffffffffffff;
        b[0] = special ? (byte)(value & 0xff) : (byte)(value & 0x7f);

        var length = 1;
        for (var i = 1; i < 9; i++)
        {
            if (value >= 0x80)
            {
                length++;
                if (i == 1 && special)
                {
                    value >>= 8;
                }
                else
                {
                    value >>= 7;
                }
                b[i] = (byte)((value & 0x7f) + 0x80);
            }
            else
            {
                break;
            }
        }

        if (special && length == 8)
        {
            // If the guess from above is, that we need 9 bytes but the value encoding would still fit into 8 bytes, then we need
            // to write an empty 0x80 value for continue flag.
            // Example: 123456789012345678 would be "ed d3 b4 dd 98 e1 f3 4e" and fit into 8 bytes.
            // But writing would fail.
            // Testcases where the special handling is needed are values:
            // - 0x00FFFFFFFFFFFFFF      -> standard
            // - 0x00FFFFFFFFFFFFFF + 1  -> additional 0x80 needed
            // The decode algorithm can handle both variants, but the plc accepts it only with the additional bytes (seems Siemens
            // uses a different algorithm than we are using)
            length++;
            b[8] = 0x80;
        }

        // Reverse order of bytes
        for (var i = length - 1; i >= 0; i--)
        {
            buffer.WriteByte(b[i]);
        }
        return length;
    }

    public static int DecodeUInt64Vlq(Stream buffer, out ulong value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Length - buffer.Position < 1) { value = 0; return 0; } // 流尾保护，避免把 -1 当作 0xFF 续位
        int counter;
        ulong val = 0;
        byte octet;
        byte cont = 0;
        var length = 0;
        for (counter = 1; counter <= 8; counter++)
        {
            octet = (byte)buffer.ReadByte();
            length++;
            val <<= 7;
            cont = (byte)(octet & 0x80);
            octet &= 0x7f;
            val += octet;
            if (cont == 0)
            {
                break;
            }
        }
        if (cont > 0)         /* 8*7 bit + 8 bit = 64 bit -> Special case in last octet! */
        {
            octet = (byte)buffer.ReadByte();
            length++;
            val <<= 8;
            val += octet;
        }
        value = val;
        return length;
    }

    public static int DecodeInt64Vlq(Stream buffer, out long value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Length - buffer.Position < 1) { value = 0; return 0; } // 流尾保护，避免把 -1 当作 0xFF 续位
        long val = 0;
        byte octet;
        byte cont = 0;
        var length = 0;
        int counter;
        for (counter = 1; counter <= 8; counter++)
        {
            octet = (byte)buffer.ReadByte();
            length++;
            if ((counter == 1) && ((octet & 0x40) != 0))
            {     // check sign 
                octet &= 0xbf;
                val = -64; // pre-load with one complement, excluding first 6 bits
            }
            else
            {
                val <<= 7;
            }
            cont = (byte)(octet & 0x80);
            octet &= 0x7f;
            val += octet;
            if (cont == 0)
            {
                break;
            }
        }
        if (cont > 0)
        {
            // 8*7 bit + 8 bit = 64 bit -> Special case in last octet!
            octet = (byte)buffer.ReadByte();
            length++;
            val <<= 8;
            val += octet;
        }
        value = val;
        return length;
    }

    public static int EncodeInt64Vlq(Stream buffer, long value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        Span<byte> b = stackalloc byte[9]; // 栈上临时缓冲，避免每次 VLQ 编码堆分配
        var abs_v = value == long.MinValue ? 9223372036854775808 : (ulong)Math.Abs(value);
        var special = abs_v > 0x007fffffffffffff;
        b[0] = special ? (byte)(value & 0xff) : (byte)(value & 0x7f);

        var length = 1;
        for (var i = 1; i < 9; i++)
        {
            if (abs_v >= 0x40)
            {
                length++;
                if (i == 1 && special)
                {
                    abs_v >>= 8;
                    value >>= 8;
                }
                else
                {
                    abs_v >>= 7;
                    value >>= 7;
                }
                b[i] = (byte)((value & 0x7f) + 0x80);
            }
            else
            {
                break;
            }
        }

        if (special && length == 8)
        {
            // See comment at EncodeUInt64Vlq.
            // Because of the sign bit, the special handling starts here at > 0x007fffffffffffff
            // And we need a different value for negative numbers.
            length++;
            b[8] = value >= 0 ? (byte)0x80 : (byte)0xff;
        }

        // Reverse order of bytes
        for (var i = length - 1; i >= 0; i--)
        {
            buffer.WriteByte(b[i]);
        }
        return length;
    }

    public static int EncodeFloat(Stream buffer, float value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        Span<byte> v = stackalloc byte[4];
        BinaryPrimitives.WriteSingleBigEndian(v, value);
        buffer.Write(v);
        return 4;
    }

    public static int DecodeFloat(Stream buffer, out float value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Length - buffer.Position < 4) { value = 0; return 0; }
        Span<byte> v = stackalloc byte[4];
        buffer.ReadExactly(v);
        value = BinaryPrimitives.ReadSingleBigEndian(v);
        return 4;
    }

    public static int EncodeDouble(Stream buffer, double value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        Span<byte> v = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(v, value);
        buffer.Write(v);
        return 8;
    }

    public static int DecodeDouble(Stream buffer, out double value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Length - buffer.Position < 8) { value = 0; return 0; }
        Span<byte> v = stackalloc byte[8];
        buffer.ReadExactly(v);
        value = BinaryPrimitives.ReadDoubleBigEndian(v);
        return 8;
    }

    public static int EncodeWString(Stream buffer, string value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var bytes = Encoding.UTF8.GetBytes(value);
        buffer.Write(bytes, 0, bytes.Length);
        return bytes.Length;
    }

    public static int DecodeWString(Stream buffer, int length, out string value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Length - buffer.Position < length)
        {
            value = string.Empty;
            return 0;
        }
        var tmp = new byte[length];
        buffer.ReadExactly(tmp, 0, length);
        value = Encoding.UTF8.GetString(tmp);
        return tmp.Length;
    }

    public static int EncodeOctets(Stream buffer, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (value == null || value.Length == 0)
        {
            return 0;
        }

        buffer.Write(value, 0, value.Length);
        return value.Length;
    }

    public static int DecodeOctets(Stream buffer, int length, out byte[]? value)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (length <= 0 || buffer.Length - buffer.Position < length)
        {
            value = null;
            return 0;
        }
        value = new byte[length];
        buffer.ReadExactly(value, 0, length);
        return value.Length;
    }

    ///////////////////////////////////////////////////////////////////////////////////////
    ///////////////////////////////////////////////////////////////////////////////////////
    // High Level Decode/Encode methods

    public static int DecodeObjectList(Stream buffer, ref List<PObject> objList)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var ret = 0;
        objList = [];
        // Peek one byte and set buffer back
        DecodeByte(buffer, out var tagId);
        buffer.Position -= 1;
        while (tagId == ElementID.StartOfObject)
        {
            PObject? obj = null;
            ret += DecodeObject(buffer, ref obj, AsList: true);
            if (obj != null)
            {
                objList.Add(obj);
            }
            DecodeByte(buffer, out tagId);
            buffer.Position -= 1;
        }
        return 0;
    }

    public static int DecodeObject(Stream buffer, ref PObject? obj, bool AsList = false)
    {
        var terminate = false;
        var ret = 0;
        do
        {
            ret += DecodeByte(buffer, out var tagId);
            switch (tagId)
            {
                case ElementID.StartOfObject:
                    if (obj is null)
                    {
                        ret += DecodeUInt32(buffer, out var _relationId);
                        ret += DecodeUInt32Vlq(buffer, out var _classId);
                        ret += DecodeUInt32Vlq(buffer, out var _classFlags);
                        ret += DecodeUInt32Vlq(buffer, out var _attributeId);
                        obj = new PObject
                        {
                            RelationId = _relationId,
                            ClassId = _classId,
                            ClassFlags = _classFlags,
                            AttributeId = _attributeId
                        };
                        // If a List is expected, don't add the objects coming next to the parent object
                        // TODO: May be it's better to always expect a list? Adding the following objects to the first as children must always be wrong.
                        if (!AsList)
                        {
                            ret += DecodeObject(buffer, ref obj);
                        }
                    }
                    else
                    {
                        ret += DecodeUInt32(buffer, out var _relationId);
                        ret += DecodeUInt32Vlq(buffer, out var _classId);
                        ret += DecodeUInt32Vlq(buffer, out var _classFlags);
                        ret += DecodeUInt32Vlq(buffer, out var _attributeId);
                        var newobj = new PObject
                        {
                            RelationId = _relationId,
                            ClassId = _classId,
                            ClassFlags = _classFlags,
                            AttributeId = _attributeId
                        };
                        ret += DecodeObject(buffer, ref newobj);
                        obj.AddObject(newobj);
                    }
                    break;
                case ElementID.TerminatingObject:
                    terminate = true;
                    break;
                case ElementID.Attribute:
                    ret += DecodeUInt32Vlq(buffer, out var id);
                    obj?.AddAttribute(id, PValue.Deserialize(buffer));
                    break;
                case ElementID.StartOfTagDescription:
                    // Skip, only 1200 FW2 and maybe older, which definitively don't support TLS
                    break;
                case ElementID.VartypeList:
                    var typelist = new PVartypeList();
                    ret += typelist.Deserialize(buffer);
                    obj?.SetVartypeList(typelist);
                    break;
                case ElementID.VarnameList:
                    var namelist = new PVarnameList();
                    ret += namelist.Deserialize(buffer);
                    obj?.SetVarnameList(namelist);
                    break;
                default:
                    terminate = true;
                    break;
            }
        } while (!terminate);
        return ret;
    }

    public static int DecodeHeader(Stream buffer, out byte version, out ushort length)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Length - buffer.Position < 4)
        {
            version = 0;
            length = 0;
            return 0;
        }

        buffer.ReadByte(); // Skip one byte (purpose unclear)
        version = (byte)buffer.ReadByte();
        DecodeUInt16(buffer, out length);
        return 4;
    }

    public static int EncodeHeader(Stream buffer, byte version, ushort length)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        buffer.WriteByte(0x72);
        buffer.WriteByte(version);
        EncodeUInt16(buffer, length);
        return 4;
    }

    public static int EncodeObjectQualifier(Stream buffer)
    {
        var ret = 0;

        ret += EncodeUInt32(buffer, Ids.ObjectQualifier);

        var parentRID = new ValueRID(0);
        var compositionAID = new ValueAID(0);
        var keyQualifier = new ValueUDInt(0);

        ret += EncodeUInt32Vlq(buffer, Ids.ParentRID);
        ret += parentRID.Serialize(buffer);

        ret += EncodeUInt32Vlq(buffer, Ids.CompositionAID);
        ret += compositionAID.Serialize(buffer);

        ret += EncodeUInt32Vlq(buffer, Ids.KeyQualifier);
        ret += keyQualifier.Serialize(buffer);

        ret += EncodeByte(buffer, 0);

        return ret;
    }
}
