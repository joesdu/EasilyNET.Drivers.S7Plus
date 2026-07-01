// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
using Microsoft.Extensions.Logging;
using EasilyNET.Drivers.S7Plus.S7CommPlus.Core;
using System.Globalization;
using System.Text;

namespace EasilyNET.Drivers.S7Plus.S7CommPlus.ClientApi;

/* Ideas for improvement:
 * - Optional initial value in constructor
 * - Uniform the different PLC datatypes on the biggest .Net type,
 *   to reduce the amount of types (e.g. on integer types only 64 bit).
 * Many datatypes differ only by the type in the protocol.
 * But there's some special handling needed for type like String, WString, date/time etc.
 */
internal abstract class PlcTag(string name, ItemAddress address, uint softdatatype)
{
    public string Name { get; set; } = name;
    public ItemAddress Address { get; set; } = address;
    public short Quality { get; set; } = PlcTagQC.TAG_QUALITY_WAITING_FOR_INITIAL_DATA;
    public uint Datatype { get; set; } = softdatatype;

    public ulong LastReadError { get; set; }
    public ulong LastWriteError { get; set; }

    public abstract void ProcessReadResult(object obj, ulong err);

    public virtual void ProcessWriteResult(ulong err)
    {
        LastWriteError = err;
    }

    public abstract PValue GetWriteValue();

    protected static int CheckErrorAndType(ulong error, object valueObj, Type checkType)
    {
        ArgumentNullException.ThrowIfNull(valueObj, nameof(valueObj));
        int res;
        if (error != 0)
        {
            S7Log.Instance?.LogDebug("CheckErrorAndType(): error=" + error);
            res = -1;
        }
        else if (valueObj.GetType() != checkType)
        {
            S7Log.Instance?.LogDebug($"CheckErrorAndType(): Type of value is not as excpected. Expected: {checkType} Received: {valueObj.GetType()}.");
            res = -1;
        }
        else
        {
            res = 0;
        }
        return res;
    }

    protected static string ResultString(PlcTag tag, string value)
    {
        ArgumentNullException.ThrowIfNull(tag, nameof(tag));
        return $"{tag.Quality:X02}: {value}";
    }

    protected static int BcdByteToInt(byte value)
    {
        return (10 * (value / 16)) + (value % 16);
    }

    protected static byte IntToBcdByte(int value)
    {
        return (byte)((value / 10 * 16) + (value % 10));
    }

    protected static ushort BcdUshortToUshort(ushort value)
    {
        return (ushort)((value & 0x000f) + (((value & 0x00f0) >> 4) * 10) + (((value & 0x0f00) >> 8) * 100) + (((value & 0xf000) >> 12) * 1000));
    }

    protected static ushort UshortToBcdUshort(ushort value)
    {
        var b = new byte[4];
        b[0] = (byte)(value % 10);
        value /= 10;
        b[1] = (byte)(value % 10);
        value /= 10;
        b[2] = (byte)(value % 10);
        value /= 10;
        b[3] = (byte)(value % 10);
        return (ushort)(b[0] + (b[1] << 4) + (b[2] << 8) + (b[3] << 12));
    }
}

internal sealed class PlcTagBool(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    public bool Value { get; set; }

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueBool)) == 0)
        {
            Value = ((ValueBool)obj).Value;
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueBool(Value);
    }

    public override string ToString()
    {
        return ResultString(this, Value.ToString());
    }
}

internal sealed class PlcTagByte(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    public byte Value { get; set; }

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueByte)) == 0)
        {
            Value = ((ValueByte)obj).Value;
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueByte(Value);
    }

    public override string ToString()
    {
        return ResultString(this, Value.ToString(CultureInfo.InvariantCulture));
    }
}

internal sealed class PlcTagChar(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    private string m_Encoding = "ISO-8859-1";

    public char Value
    {
        get; set;  // TODO: check if fits in ASCII area, include the encoding?
    }

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueUSInt)) == 0)
        {
            var v = new byte[1];
            v[0] = ((ValueUSInt)obj).Value;
            Value = Encoding.GetEncoding(m_Encoding).GetString(v)[0];
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        var c = new char[1];
        c[0] = Value;
        var b = Encoding.GetEncoding(m_Encoding).GetBytes(c);
        var pv = new ValueUSInt(b[0]);
        return pv;
    }

    public void SetCharEncoding(string encoding)
    {
        m_Encoding = encoding;
    }

    public override string ToString()
    {
        return ResultString(this, Value.ToString());
    }
}

internal sealed class PlcTagWord(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    public ushort Value { get; set; }

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueWord)) == 0)
        {
            Value = ((ValueWord)obj).Value;
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueWord(Value);
    }

    public override string ToString()
    {
        return ResultString(this, Value.ToString(CultureInfo.InvariantCulture));
    }
}

internal sealed class PlcTagInt(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    public short Value { get; set; }

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueInt)) == 0)
        {
            Value = ((ValueInt)obj).Value;
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueInt(Value);
    }

    public override string ToString()
    {
        return ResultString(this, Value.ToString(CultureInfo.InvariantCulture));
    }
}

internal sealed class PlcTagDWord(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    public uint Value { get; set; }

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueDWord)) == 0)
        {
            Value = ((ValueDWord)obj).Value;
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueDWord(Value);
    }

    public override string ToString()
    {
        return ResultString(this, Value.ToString(CultureInfo.InvariantCulture));
    }
}

internal sealed class PlcTagDInt(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    public int Value { get; set; }

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueDInt)) == 0)
        {
            Value = ((ValueDInt)obj).Value;
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueDInt(Value);
    }

    public override string ToString()
    {
        return ResultString(this, Value.ToString(CultureInfo.InvariantCulture));
    }
}

internal sealed class PlcTagReal(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    public float Value { get; set; }

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueReal)) == 0)
        {
            Value = ((ValueReal)obj).Value;
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueReal(Value);
    }

    public override string ToString()
    {
        return ResultString(this, Value.ToString(CultureInfo.InvariantCulture));
    }
}

internal sealed class PlcTagDate(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    // Specifies the number of days from January 1, 1990.
    // .Net has no type with only date or only time
    // TODO: Switch to .Net 6 (for DateOnly) or stay just as UInt?
    public DateTime Value
    {
        get;
        set => field = value >= new DateTime(1990, 1, 1) && value <= new DateTime(2169, 6, 6)
                ? value
                : throw new ArgumentOutOfRangeException("Value", "Date must be >= 1990-01-01 and <= 2169-06-06");
    } = new(1990, 1, 1);

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueUInt)) == 0)
        {
            var v = ((ValueUInt)obj).Value;
            Value = new DateTime(1990, 1, 1).AddDays(v);

            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        var dtbase = new DateTime(1990, 1, 1);
        return new ValueUInt((ushort)(Value - dtbase).Days);
    }

    public override string ToString()
    {
        return ResultString(this, Value.ToShortDateString());
    }
}

internal sealed class PlcTagTimeOfDay(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    // TODO: .Net has no type with only date or only time
    // Specification: 01:02:03 = 3723000 number of milliseconds since 00:00:00
    /// <summary>
    /// Number of milliseconds since 00:00:00, must be below 86400000ms
    /// </summary>
    public uint Value
    {
        get; set => field = value < 86400000 ? value : throw new ArgumentOutOfRangeException("Value", "Number if milliseconds must be < 86400000");
    }

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueUDInt)) == 0)
        {
            Value = ((ValueUDInt)obj).Value;

            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueUDInt(Value);
    }

    public override string ToString()
    {
        uint v, h, m, s, ms;
        string tod;
        v = Value;
        ms = v % 1000;
        v /= 1000;
        s = v % 60;
        v /= 60;
        m = v % 60;
        v /= 60;
        h = v;
        tod = ms > 0 ? $"{h:D02}:{m:D02}:{s:D02}.{ms:D03}" : $"{h:D02}:{m:D02}:{s:D02}";
        return ResultString(this, tod);
    }
}

internal sealed class PlcTagTime(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    /// <summary>
    /// In milliseconds, with sign
    /// </summary>
    public int Value { get; set; }

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueDInt)) == 0)
        {
            Value = ((ValueDInt)obj).Value;
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueDInt(Value);
    }

    public override string ToString()
    {
        int[] divs = [86400000, 3600000, 60000, 1000, 1];
        string[] vfmt = ["{0}d", "{0:D02}h", "{0:D02}m", "{0:D02}s", "{0:D03}ms"];
        var vtime = Value;
        var time_negative = false;
        int val;
        var ts = string.Empty;
        if (vtime == 0)
        {
            ts = "0ms";
        }
        else
        {
            if (vtime < 0)
            {
                ts = "-";
                time_negative = true;
                for (var i = 0; i < 5; i++)
                {
                    divs[i] = -divs[i];
                }
            }

            for (var i = 0; i < 5; i++)
            {
                val = vtime / divs[i];
                vtime -= val * divs[i];
                if (val > 0)
                {
                    ts += string.Format(CultureInfo.InvariantCulture, vfmt[i], val);
                    if ((!time_negative && vtime > 0) || (time_negative && vtime < 0))
                    {
                        ts += "_";
                    }
                }
            }
        }
        return ResultString(this, ts);
    }
}

internal sealed class PlcTagS5Time(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    // Specification S5Time:
    // Bits 15, 14: not used
    // Bits 13, 12: time base in binary, 00=10ms, 01=100ms, 10=1s, 11=10s
    // Bits 11..0: time values BCD coded (0 to 999)
    // S5Time_9S_990MS = <Value type="Word">2457</Value>
    // S5Time_2H_46M_30S_0MS = <Value type="Word">14745</Value>

    /// <summary>
    /// TimeValue: between 0..999, factor is TimeBase
    /// </summary>
    public ushort TimeValue
    {
        get;
        set => field = value is >= 0 and <= 999 ? value : throw new ArgumentOutOfRangeException(nameof(value), "TimeValue must be >= 0 and <= 999");
    }
    /// <summary>
    /// TimeBase 0=10ms, 1=100ms, 2=1s, 3=10s
    /// </summary>
    public ushort TimeBase
    {
        get;
        set => field = value is >= 0 and <= 3 ? value : throw new ArgumentOutOfRangeException(nameof(value), "TimeBase must be >= 0 and <= 3");
    }

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueWord)) == 0)
        {
            var v = ((ValueWord)obj).Value;
            TimeValue = BcdUshortToUshort((ushort)(v & 0x0FFF));
            TimeBase = (ushort)((v & 0x3000) >> 12);
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        ushort v;
        v = UshortToBcdUshort(TimeValue);
        v |= (ushort)((TimeBase & 0x3) << 12);
        return new ValueWord(v);
    }

    public override string ToString()
    {
        var s = string.Empty;
        // Scale down to milliseconds
        switch (TimeBase)
        {
            case 0:
                s = $"{TimeValue * 10}ms";
                break;
            case 1:
                s = $"{TimeValue * 100}ms";
                break;
            case 2:
                s = $"{TimeValue * 1000}ms";
                break;
            case 3:
                s = $"{TimeValue * 10000}ms";
                break;
            default:
                break;
        }
        return ResultString(this, s);
    }
}

internal sealed class PlcTagDateAndTime : PlcTag
{
    /* BCD coded:
     * YYMMDDhhmmssuuuQ
     * uuu = milliseconds
     * Q = Weekday 1=Su, 2=Mo, 3=Tu, 4=We, 5=Th, 6=Fr, 7=Sa
     */
    public DateTime Value
    {
        get;
        set => field = value >= new DateTime(1990, 1, 1) && value < new DateTime(2090, 1, 1)
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value), "DateTime must be >= 1990-01-01 and < 2090-01-01");
    }

    public PlcTagDateAndTime(string name, ItemAddress address, uint softdatatype) : base(name, address, softdatatype)
    {
        Value = new DateTime(1990, 1, 1);
    }

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueUSIntArray)) == 0)
        {
            var v = ((ValueUSIntArray)obj).Value;
            var ts = new int[8];
            for (var i = 0; i < 7; i++)
            {
                ts[i] = BcdByteToInt(v[i]);
            }
            // The left nibble of the last byte contains the LSD of milliseconds,
            // the right nibble the weekday (which we don't process here).
            ts[7] = v[7] >> 4;

            var year = ts[0] >= 90 ? 1900 + ts[0] : 2000 + ts[0];
            Value = new DateTime(year, ts[1], ts[2], ts[3], ts[4], ts[5]);
            Value = Value.AddMilliseconds((ts[6] * 10) + ts[7]);

            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        var ts = new int[8];
        var b = new byte[8];
        if (Value.Year < 2000)
        {
            // 90-99 = 1990-1999
            ts[0] = Value.Year - 1900;
        }
        else
        {
            // 00-89 = 2000-2089
            ts[0] = Value.Year - 2000;
        }
        ts[1] = Value.Month;
        ts[2] = Value.Day;
        ts[3] = Value.Hour;
        ts[4] = Value.Minute;
        ts[5] = Value.Second;
        ts[6] = Value.Millisecond / 10;
        ts[7] = (Value.Millisecond % 10) << 4; // Don't set the weekday
        for (var i = 0; i < 7; i++)
        {
            b[i] = IntToBcdByte(ts[i]);
        }
        b[7] = (byte)ts[7];
        return new ValueUSIntArray(b);
    }

    public override string ToString()
    {
        var ts = Value.ToString(CultureInfo.InvariantCulture);
        if (Value.Millisecond > 0)
        {
            ts += $".{Value.Millisecond:D03}";
        }
        return ResultString(this, ts);
    }
}

internal sealed class PlcTagString(string name, ItemAddress address, uint softdatatype, byte maxlength = 254) : PlcTag(name, address, softdatatype)
{
    private readonly byte m_MaxLength = maxlength;
    private string m_Encoding = "ISO-8859-1";

    public string Value
    {
        get;
        set => field = value != null && value.Length <= m_MaxLength
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value), "String is longer than the allowed max. length of " + m_MaxLength);
    } = string.Empty;

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueUSIntArray)) == 0)
        {
            var v = ((ValueUSIntArray)obj).Value;
            int act_len = v[1];
            // IEC 61131-3 states ISO-646 IRV, with optional extensions like "Latin-1 Supplement".
            // Siemens TIA-Portal gives warnings using other than 7 Bit ASCII characters.
            // Let the user define his local encoding via SetStringEncoding().
            Value = Encoding.GetEncoding(m_Encoding).GetString(v, 2, act_len);
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        // Must write the complete array of MaxLength of the string (plus two bytes header).
        var sb = Encoding.GetEncoding(m_Encoding).GetBytes(Value ?? string.Empty);
        var b = new byte[m_MaxLength + 2];
        b[0] = m_MaxLength;
        b[1] = (byte)sb.Length;
        for (var i = 0; i < sb.Length; i++)
        {
            b[i + 2] = sb[i];
        }
        return new ValueUSIntArray(b);
    }

    public void SetStringEncoding(string encoding)
    {
        m_Encoding = encoding;
    }

    public override string ToString()
    {
        return ResultString(this, Value);
    }
}

internal sealed class PlcTagPointer(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    public byte[] Value { get; set; } = new byte[6];

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueUSIntArray)) == 0)
        {
            Value = ((ValueUSIntArray)obj).Value;

            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueUSIntArray(Value);
    }

    public override string ToString()
    {
        var dbnr = (Value[0] * 256) + Value[1];
        int area = Value[2];
        var bitnr = Value[5] & 0x7;
        var bytenr = (Value[5] >> 3) + (Value[4] * 32) + ((Value[3] & 0x7) * 8192);
        return ResultString(this, $"DB={dbnr} Area=0x{area:X02} Byte={bytenr} Bit={bitnr}");
    }
}

internal sealed class PlcTagAny(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    public byte[] Value { get; set; } = new byte[10];

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueUSIntArray)) == 0)
        {
            Value = ((ValueUSIntArray)obj).Value;

            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueUSIntArray(Value);
    }

    public override string ToString()
    {
        int hdr = Value[0];
        int datatype = Value[1];
        var factor = (Value[2] * 256) + Value[3];
        var dbnr = (Value[4] * 256) + Value[5];
        int area = Value[6];
        var bitnr = Value[9] & 0x7;
        var bytenr = (Value[9] >> 3) + (Value[8] * 32) + ((Value[7] & 0x7) * 8192);

        return ResultString(this, $"HDR={hdr:X02} Type={datatype:X02} Factor={factor} DB={dbnr} Area=0x{area:X02} Byte={bytenr} Bit={bitnr}");
    }
}

internal sealed class PlcTagLReal(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    public double Value { get; set; }

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueLReal)) == 0)
        {
            Value = ((ValueLReal)obj).Value;
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueLReal(Value);
    }

    public override string ToString()
    {
        return ResultString(this, Value.ToString(CultureInfo.InvariantCulture));
    }
}

internal sealed class PlcTagULInt(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    public ulong Value { get; set; }

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueULInt)) == 0)
        {
            Value = ((ValueULInt)obj).Value;
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueULInt(Value);
    }

    public override string ToString()
    {
        return ResultString(this, Value.ToString(CultureInfo.InvariantCulture));
    }
}

internal sealed class PlcTagLInt(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    public long Value { get; set; }

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueLInt)) == 0)
        {
            Value = ((ValueLInt)obj).Value;
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueLInt(Value);
    }

    public override string ToString()
    {
        return ResultString(this, Value.ToString(CultureInfo.InvariantCulture));
    }
}

internal sealed class PlcTagLWord(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    public ulong Value { get; set; }

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueLWord)) == 0)
        {
            Value = ((ValueLWord)obj).Value;
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueLWord(Value);
    }

    public override string ToString()
    {
        return ResultString(this, Value.ToString(CultureInfo.InvariantCulture));
    }
}

internal sealed class PlcTagUSInt(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    public byte Value { get; set; }

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueUSInt)) == 0)
        {
            Value = ((ValueUSInt)obj).Value;
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueUSInt(Value);
    }

    public override string ToString()
    {
        return ResultString(this, Value.ToString(CultureInfo.InvariantCulture));
    }
}

internal sealed class PlcTagUInt(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    public ushort Value { get; set; }

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueUInt)) == 0)
        {
            Value = ((ValueUInt)obj).Value;
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueUInt(Value);
    }

    public override string ToString()
    {
        return ResultString(this, Value.ToString(CultureInfo.InvariantCulture));
    }
}

internal sealed class PlcTagUDInt(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    public uint Value { get; set; }

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueUDInt)) == 0)
        {
            Value = ((ValueUDInt)obj).Value;
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueUDInt(Value);
    }

    public override string ToString()
    {
        return ResultString(this, Value.ToString(CultureInfo.InvariantCulture));
    }
}

internal sealed class PlcTagSInt(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    public sbyte Value { get; set; }

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueSInt)) == 0)
        {
            Value = ((ValueSInt)obj).Value;
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueSInt(Value);
    }

    public override string ToString()
    {
        return ResultString(this, Value.ToString(CultureInfo.InvariantCulture));
    }
}

internal sealed class PlcTagWChar(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    public char Value { get; set; }

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueUInt)) == 0)
        {
            Value = (char)((ValueUInt)obj).Value;
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueUInt(Convert.ToUInt16(Value));
    }

    public override string ToString()
    {
        return ResultString(this, Value.ToString());
    }
}

internal sealed class PlcTagWString(string name, ItemAddress address, uint softdatatype, ushort maxlength = 254) : PlcTag(name, address, softdatatype)
{
    public string Value
    {
        get; set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value.Length <= maxlength
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value), $"String is longer than the allowed max. length of {maxlength}");
        }
    } = string.Empty;

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueUIntArray)) == 0)
        {
            var v = ((ValueUIntArray)obj).Value;
            //var max_len = v[0];
            var act_len = v[1];

            var b = new byte[act_len * 2];
            Buffer.BlockCopy(v, 4, b, 0, act_len * 2);
            Value = Encoding.Unicode.GetString(b, 0, act_len * 2);
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        // Must write the complete array of MaxLength of the string (plus two ushort for the header).
        var b = new ushort[Value.Length + 2];
        b[0] = maxlength;
        b[1] = (ushort)Value.Length;
        for (var i = 0; i < Value.Length; i++)
        {
            b[i + 2] = Convert.ToUInt16(Value[i]);
        }
        return new ValueUIntArray(b);
    }

    public override string ToString()
    {
        return ResultString(this, Value);
    }
}

internal sealed class PlcTagLTime(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    public long Value { get; set; }

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueTimespan)) == 0)
        {
            Value = ((ValueTimespan)obj).Value;
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueTimespan(Value);
    }

    private string ValueAsString()
    {
        return ValueTimespan.ToString(Value);
    }

    public override string ToString()
    {
        return ResultString(this, ValueAsString());
    }
}

internal sealed class PlcTagLTOD(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    // TODO: Like the 32 Bit Types Date/TOD, in .Net there's no type for date / time only. Only in .Net 6.
    // Specification: Number of nanoseconds since 00:00:00.
    /// <summary>
    /// Number of nanoseconds since 00:00:00, must be below 86400000000000ns
    /// </summary>
    public ulong Value
    {
        get; set
        {
            field = value < 86400000000000
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value), "Number if nanoseconds must be < 86400000000000");
            field = value;
        }
    }

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueULInt)) == 0)
        {
            Value = ((ValueULInt)obj).Value;
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueULInt(Value);
    }

    public override string ToString()
    {
        ulong v, h, m, s, ns;
        string tod;
        v = Value;
        ns = v % 1000000000;
        v /= 1000000000;
        s = v % 60;
        v /= 60;
        m = v % 60;
        v /= 60;
        h = v;
        tod = ns > 0 ? $"{h:D02}:{m:D02}:{s:D02}.{ns:D09}" : $"{h:D02}:{m:D02}:{s:D02}";
        return ResultString(this, tod);
    }
}

internal sealed class PlcTagLDT(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    public ulong Value { get; set; }

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueTimestamp)) == 0)
        {
            Value = ((ValueTimestamp)obj).Value;
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueTimestamp(Value);
    }

    private string ValueAsString()
    {
        return ValueTimestamp.ToString(Value);
    }

    public override string ToString()
    {
        return ResultString(this, ValueAsString());
    }
}

internal sealed class PlcTagDTL : PlcTag
{
    public DateTime Value
    {
        get;
        set => field = value >= new DateTime(1970, 1, 1) && value <= new DateTime(2262, 4, 11, 23, 47, 16)
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value), "DateTime must be >= 1970-01-01 and <= 2262-04-11 23:47:16");
    }

    public uint ValueNanosecond
    {
        get;
        set => field = value <= 999999999 ? value : throw new ArgumentOutOfRangeException(nameof(value), "Nanosecond must be <= 999999999");
    }

    public ulong DTLInterfaceTimestamp { get; set; } = 0x10ff4ad6dfd5774c; // Oct 23, 2008 16:38:30.406829900 UTC. Should be used from first browse method (or read) and set correctly

    public PlcTagDTL(string name, ItemAddress address, uint softdatatype) : base(name, address, softdatatype)
    {
        Value = new DateTime(1970, 1, 1);
    }

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueStruct)) == 0)
        {
            var struct_val = (ValueStruct)obj;
            // That value is the ID which has the type description
            // E.g. DTL 33554499 = TI_LIB.SimpleType.67
            // Then comes a PackedStruct, with Interface timestamp, transportflags, and a ByteArray with 12 bytes.
            // Generate the separate values back from the array:
            // 0, 1: YEAR, UInt
            // 2: MONTH, USInt
            // 3: DAY, USInt
            // 4: WEEKDAY, USInt
            // 5: HOUR, USInt
            // 6: MINUTE, USInt
            // 7: SECOND, USInt
            // 8, 9, 10, 11: NANOSECOND, UDInt

            // Use the default timestamp, or refresh it from browsing the plc, or from reading dtl first
            DTLInterfaceTimestamp = struct_val.PackedStructInterfaceTimestamp;

            if (struct_val.Value == 0x02000043)
            {
                var elem = struct_val.GetStructElement(0x02000043);
                if (elem.GetType() == typeof(ValueByteArray))
                {
                    var barr = ((ValueByteArray)elem).Value;
                    var year = (barr[0] * 256) + barr[1];
                    ValueNanosecond = ((uint)barr[8] * 16777216) + ((uint)barr[9] * 65536) + ((uint)barr[10] * 256) + barr[11];
                    Value = new DateTime(year, barr[2], barr[3], barr[5], barr[6], barr[7]);
                    Quality = PlcTagQC.TAG_QUALITY_GOOD;
                }
                else
                {
                    Quality = PlcTagQC.TAG_QUALITY_BAD;
                }
            }
            else
            {
                Quality = PlcTagQC.TAG_QUALITY_BAD;
            }
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        var struct_val = new ValueStruct(0x02000043)
        {
            PackedStructInterfaceTimestamp = DTLInterfaceTimestamp
        }; // 0x02000043 = TI_LIB.SimpleType.67 -> DTL Systemdatatype
        var barr = new byte[12];
        barr[0] = (byte)(Value.Year >> 8);
        barr[1] = (byte)Value.Year;
        barr[2] = (byte)Value.Month;
        barr[3] = (byte)Value.Day;
        barr[4] = 0; // Weekday, don't set
        barr[5] = (byte)Value.Hour;
        barr[6] = (byte)Value.Minute;
        barr[7] = (byte)Value.Second;
        barr[8] = (byte)(ValueNanosecond >> 24);
        barr[9] = (byte)(ValueNanosecond >> 16);
        barr[10] = (byte)(ValueNanosecond >> 8);
        barr[11] = (byte)ValueNanosecond;
        struct_val.AddStructElement(0x02000043, new ValueByteArray(barr));
        return struct_val;
    }

    public override string ToString()
    {
        string fmt;
        var ns = ValueNanosecond;
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
            return Value.ToString(CultureInfo.InvariantCulture);
        }
        return string.Format(CultureInfo.InvariantCulture, fmt, Value.ToString(CultureInfo.InvariantCulture), ns);
    }
}

#region Arrays

internal sealed class PlcTagBoolArray(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    public bool[] Value { get; set; } = new bool[1];

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueBoolArray)) == 0)
        {
            Value = ((ValueBoolArray)obj).Value;
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueBoolArray(Value);
    }

    public override string ToString()
    {
        var val = new ValueBoolArray(Value);
        return ResultString(this, val.ToString());
    }
}

internal sealed class PlcTagByteArray(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    public byte[] Value { get; set; } = new byte[1];

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueByteArray)) == 0)
        {
            Value = ((ValueByteArray)obj).Value;
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueByteArray(Value);
    }

    public override string ToString()
    {
        var val = new ValueByteArray(Value);
        return ResultString(this, val.ToString());
    }
}

internal sealed class PlcTagWordArray(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    public ushort[] Value { get; set; } = new ushort[1];

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueWordArray)) == 0)
        {
            Value = ((ValueWordArray)obj).Value;
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueWordArray(Value);
    }

    public override string ToString()
    {
        var val = new ValueWordArray(Value);
        return ResultString(this, val.ToString());
    }
}

internal sealed class PlcTagIntArray(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    public short[] Value { get; set; } = new short[1];

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueIntArray)) == 0)
        {
            Value = ((ValueIntArray)obj).Value;
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueIntArray(Value);
    }

    public override string ToString()
    {
        var val = new ValueIntArray(Value);
        return ResultString(this, val.ToString());
    }
}

internal sealed class PlcTagDWordArray(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    public uint[] Value { get; set; } = new uint[1];

    public override void ProcessReadResult(object obj, ulong err)
    {
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueDWordArray)) == 0)
        {
            Value = ((ValueDWordArray)obj).Value;
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueDWordArray(Value);
    }

    public override string ToString()
    {
        var val = new ValueDWordArray(Value);
        return ResultString(this, val.ToString());
    }
}

internal sealed class PlcTagDIntArray(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    public int[] Value { get; private set; } = new int[1];

    public override void ProcessReadResult(object obj, ulong err)
    {
        ArgumentNullException.ThrowIfNull(obj, nameof(obj));
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueDIntArray)) == 0)
        {
            Value = ((ValueDIntArray)obj).Value;
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueDIntArray(Value);
    }

    public override string ToString()
    {
        var val = new ValueDIntArray(Value);
        return ResultString(this, val.ToString());
    }
}

internal sealed class PlcTagRealArray(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    public float[] Value { get; private set; } = new float[1];

    public override void ProcessReadResult(object obj, ulong err)
    {
        ArgumentNullException.ThrowIfNull(obj, nameof(obj));
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueRealArray)) == 0)
        {
            Value = ((ValueRealArray)obj).Value;
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueRealArray(Value);
    }

    public override string ToString()
    {
        var val = new ValueRealArray(Value);
        return ResultString(this, val.ToString());
    }
}

internal sealed class PlcTagUSIntArray(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    public byte[] Value { get; private set; } = new byte[1];

    public override void ProcessReadResult(object obj, ulong err)
    {
        ArgumentNullException.ThrowIfNull(obj, nameof(obj));
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueUSIntArray)) == 0)
        {
            Value = ((ValueUSIntArray)obj).Value;
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueUSIntArray(Value);
    }

    public override string ToString()
    {
        var val = new ValueUSIntArray(Value);
        return ResultString(this, val.ToString());
    }
}

internal sealed class PlcTagUIntArray(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    public ushort[] Value { get; private set; } = new ushort[1];

    public override void ProcessReadResult(object obj, ulong err)
    {
        ArgumentNullException.ThrowIfNull(obj, nameof(obj));
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueUIntArray)) == 0)
        {
            Value = ((ValueUIntArray)obj).Value;
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueUIntArray(Value);
    }

    public override string ToString()
    {
        var val = new ValueUIntArray(Value);
        return ResultString(this, val.ToString());
    }
}

internal sealed class PlcTagUDIntArray(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    public uint[] Value { get; private set; } = new uint[1];

    public override void ProcessReadResult(object obj, ulong err)
    {
        ArgumentNullException.ThrowIfNull(obj, nameof(obj));
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueUDIntArray)) == 0)
        {
            Value = ((ValueUDIntArray)obj).Value;
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueUDIntArray(Value);
    }

    public override string ToString()
    {
        var val = new ValueUDIntArray(Value);
        return ResultString(this, val.ToString());
    }
}

internal sealed class PlcTagSIntArray(string name, ItemAddress address, uint softdatatype) : PlcTag(name, address, softdatatype)
{
    public sbyte[] Value { get; private set; } = new sbyte[1];

    public override void ProcessReadResult(object obj, ulong err)
    {
        ArgumentNullException.ThrowIfNull(obj, nameof(obj));
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueSIntArray)) == 0)
        {
            Value = ((ValueSIntArray)obj).Value;
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        return new ValueSIntArray(Value);
    }

    public override string ToString()
    {
        var val = new ValueSIntArray(Value);
        return ResultString(this, val.ToString());
    }
}

internal sealed class PlcTagDateAndTimeArray : PlcTag
{
    /* BCD coded:
        * YYMMDDhhmmssuuuQ
        * uuu = milliseconds
        * Q = Weekday 1=Su, 2=Mo, 3=Tu, 4=We, 5=Th, 6=Fr, 7=Sa
        */
    public DateTime[] Value
    {
        get;
        private set
        {
            ArgumentNullException.ThrowIfNull(value, nameof(value));
            var dataOk = true;
            foreach (var item in value)
            {
                if (item < new DateTime(1990, 1, 1) || item >= new DateTime(2090, 1, 1))
                {
                    dataOk = false;
                    break;
                }
            }
            field = dataOk ? value : throw new ArgumentOutOfRangeException("Value", "DateTime must be >= 1990-01-01 and < 2090-01-01");
        }
    }

    public PlcTagDateAndTimeArray(string name, ItemAddress address, uint softdatatype) : base(name, address, softdatatype)
    {
        Value = [];
    }

    public override void ProcessReadResult(object obj, ulong err)
    {
        ArgumentNullException.ThrowIfNull(obj, nameof(obj));
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueUSIntArray)) == 0)
        {
            List<DateTime> dateTimes = [];
            var v = ((ValueUSIntArray)obj).Value;
            var pos = 0;
            do
            {
                var ts = new int[8];
                for (var i = 0; i < 7; i++)
                {
                    ts[i] = BcdByteToInt(v[pos + i]);
                }
                // The left nibble of the last byte contains the LSD of milliseconds,
                // the right nibble the weekday (which we don't process here).
                ts[7] = v[pos + 7] >> 4; // 必须按当前元素偏移取该字节，原实现误用固定 v[7] 导致数组中后续元素毫秒位错误

                var year = ts[0] >= 90 ? 1900 + ts[0] : 2000 + ts[0];
                var value = new DateTime(year, ts[1], ts[2], ts[3], ts[4], ts[5]);
                value = value.AddMilliseconds((ts[6] * 10) + ts[7]);
                dateTimes.Add(value);
                pos += 8;
            } while (pos < v.Length);
            Value = [.. dateTimes];
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        var byteStrings = new List<byte>();
        foreach (var item in Value)
        {
            var ts = new int[8];
            var b = new byte[8];
            if (item.Year < 2000)
            {
                // 90-99 = 1990-1999
                ts[0] = item.Year - 1900;
            }
            else
            {
                // 00-89 = 2000-2089
                ts[0] = item.Year - 2000;
            }
            ts[1] = item.Month;
            ts[2] = item.Day;
            ts[3] = item.Hour;
            ts[4] = item.Minute;
            ts[5] = item.Second;
            ts[6] = item.Millisecond / 10;
            ts[7] = (item.Millisecond % 10) << 4; // Don't set the weekday
            for (var i = 0; i < 7; i++)
            {
                b[i] = IntToBcdByte(ts[i]);
            }
            b[7] = (byte)ts[7];
            byteStrings.AddRange(b);
        }
        return new ValueUSIntArray([.. byteStrings]);
    }

    public override string ToString()
    {
        var s = $"""<Value type ="DateAndTimeArray" size="{Value.Length}">""";
        for (var i = 0; i < Value.Length; i++)
        {
            var ts = Value[i].ToString(CultureInfo.InvariantCulture);
            if (Value[i].Millisecond > 0)
            {
                ts += $".{Value[i].Millisecond:D03}";
            }
            s += $"<Value>{ts}</Value>";
        }
        s += "</Value>";
        return ResultString(this, s);
    }
}

internal sealed class PlcTagStringArray(string name, ItemAddress address, uint softdatatype, byte maxlength = 254) : PlcTag(name, address, softdatatype)
{
    private string m_Encoding = "ISO-8859-1";

    public string[] Value
    {
        get;
        set
        {
            var lengthOk = true;
            foreach (var _ in from item in value
                              where item.Length > maxlength
                              select new { })
            {
                lengthOk = false;
                break;
            }

            field = lengthOk
                ? value
                : throw new ArgumentOutOfRangeException("Value", "String is longer than the allowed max. length of " + maxlength);
        }
    } = [];

    public override void ProcessReadResult(object obj, ulong err)
    {
        ArgumentNullException.ThrowIfNull(obj, nameof(obj));
        LastReadError = err;
        if (CheckErrorAndType(err, obj, typeof(ValueUSIntArray)) == 0)
        {
            List<string> strings = [];
            var v = ((ValueUSIntArray)obj).Value;
            var pos = 0;
            do
            {
                int max_len = v[pos];
                int act_len = v[pos + 1];
                // IEC 61131-3 states ISO-646 IRV, with optional extensions like "Latin-1 Supplement".
                // Siemens TIA-Portal gives warnings using other than 7 Bit ASCII characters.
                // Let the user define his local encoding via SetStringEncoding().
                var str = Encoding.GetEncoding(m_Encoding).GetString(v, pos + 2, act_len);
                strings.Add(str);
                pos += max_len + 2;
            } while (pos < v.Length);
            Value = [.. strings];
            Quality = PlcTagQC.TAG_QUALITY_GOOD;
        }
        else
        {
            Quality = PlcTagQC.TAG_QUALITY_BAD;
        }
    }

    public override PValue GetWriteValue()
    {
        var byteStrings = new List<byte>();
        foreach (var item in Value)
        {
            // Must write the complete array of MaxLength of the string (plus two bytes header).
            var sb = Encoding.GetEncoding(m_Encoding).GetBytes(item);
            var b = new byte[maxlength + 2];
            b[0] = maxlength;
            b[1] = (byte)sb.Length;
            for (var i = 0; i < sb.Length; i++)
            {
                b[i + 2] = sb[i];
            }
            byteStrings.AddRange(b);
        }
        return new ValueUSIntArray([.. byteStrings]);
    }

    public void SetStringEncoding(string encoding)
    {
        m_Encoding = encoding;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append($"""<Value type ="StringArray" size="{Value.Length}">""");
        for (var i = 0; i < Value.Length; i++)
        {
            sb.Append($"<Value>{Value[i]}</Value>");
        }
        sb.Append("</Value>");
        return ResultString(this, sb.ToString());
    }
}
#endregion
