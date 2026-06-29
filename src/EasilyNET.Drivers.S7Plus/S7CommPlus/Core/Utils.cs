// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
using System.Text;

namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

internal sealed class Utils
{
    public static string HexDump(byte[] bytes, int bytesPerLine = 16)
    {
        if (bytes == null)
        {
            return "<null>";
        }

        var bytesLength = bytes.Length;

        var HexChars = "0123456789ABCDEF".ToCharArray();

        var firstHexColumn =
              8                             // 8 characters for the address
            + 3;                            // 3 spaces

        var firstCharColumn = firstHexColumn
            + (bytesPerLine * 3)              // - 2 digit for the hexadecimal value and 1 space
            + ((bytesPerLine - 1) / 8)        // - 1 extra space every 8 characters from the 9th
            + 2;                            // 2 spaces 

        var lineLength = firstCharColumn
            + bytesPerLine                  // - characters to show the ascii value
            + Environment.NewLine.Length;   // Carriage return and line feed (should normally be 2)

        var line = (new string(' ', lineLength - Environment.NewLine.Length) + Environment.NewLine).ToCharArray();
        var expectedLines = (bytesLength + bytesPerLine - 1) / bytesPerLine;
        var result = new StringBuilder(expectedLines * lineLength);

        for (var i = 0; i < bytesLength; i += bytesPerLine)
        {
            line[0] = HexChars[(i >> 28) & 0xF];
            line[1] = HexChars[(i >> 24) & 0xF];
            line[2] = HexChars[(i >> 20) & 0xF];
            line[3] = HexChars[(i >> 16) & 0xF];
            line[4] = HexChars[(i >> 12) & 0xF];
            line[5] = HexChars[(i >> 8) & 0xF];
            line[6] = HexChars[(i >> 4) & 0xF];
            line[7] = HexChars[(i >> 0) & 0xF];

            var hexColumn = firstHexColumn;
            var charColumn = firstCharColumn;

            for (var j = 0; j < bytesPerLine; j++)
            {
                if (j > 0 && (j & 7) == 0)
                {
                    hexColumn++;
                }

                if (i + j >= bytesLength)
                {
                    line[hexColumn] = ' ';
                    line[hexColumn + 1] = ' ';
                    line[charColumn] = ' ';
                }
                else
                {
                    var b = bytes[i + j];
                    line[hexColumn] = HexChars[(b >> 4) & 0xF];
                    line[hexColumn + 1] = HexChars[b & 0xF];
                    line[charColumn] = b < 32 ? '·' : (char)b;
                }
                hexColumn += 3;
                charColumn++;
            }
            result.Append(line);
        }
        return result.ToString();
    }

    public static DateTime DtFromValueTimestamp(ulong value)
    {
        // Protocol ValueTimestamp is number of nanoseconds from 1. Jan 1970 (Unit Time).
        // .Net DateTime tick is 100 ns based
        ulong epochTicks = 621355968000000000; // Unix Time (UTC) on 1st January 1970.
        return new DateTime((long)((value / 100) + epochTicks), DateTimeKind.Utc);
    }

    public static byte GetUInt8(byte[] array, uint pos)
    {
        return array[pos];
    }

    public static ushort GetUInt16LE(byte[] array, uint pos)
    {
        return (ushort)((array[pos + 1] * 256) + array[pos]);
    }

    public static ushort GetUInt16(byte[] array, uint pos)
    {
        return (ushort)((array[pos] * 256) + array[pos + 1]);
    }

    public static short GetInt16(byte[] array, uint pos)
    {
        return (short)((array[pos] << 8) | array[pos + 1]);
    }

    public static uint GetUInt32LE(byte[] array, uint pos)
    {
        return ((uint)array[pos + 3] * 16777216) + ((uint)array[pos + 2] * 65536) + ((uint)array[pos + 1] * 256) + array[pos];
    }

    public static uint GetUInt32(byte[] array, uint pos)
    {
        return ((uint)array[pos] * 16777216) + ((uint)array[pos + 1] * 65536) + ((uint)array[pos + 2] * 256) + array[pos + 3];
    }

    public static int GetInt32(byte[] array, uint pos)
    {
        return (array[pos] << 24) | (array[pos + 1] << 16) | (array[pos + 2] << 8) | array[pos + 3];
    }

    public static float GetFloat(byte[] array, uint pos)
    {
        var v = new byte[4];
        v[3] = array[pos];
        v[2] = array[pos + 1];
        v[1] = array[pos + 2];
        v[0] = array[pos + 3];
        return BitConverter.ToSingle(v, 0);
    }

    public static double GetDouble(byte[] array, uint pos)
    {
        var v = new byte[8];
        v[7] = array[pos];
        v[6] = array[pos + 1];
        v[5] = array[pos + 2];
        v[4] = array[pos + 3];
        v[3] = array[pos + 4];
        v[2] = array[pos + 5];
        v[1] = array[pos + 6];
        v[0] = array[pos + 7];
        return BitConverter.ToDouble(v, 0);
    }

    public static string GetUtfString(byte[] array, uint pos, uint len)
    {
        return System.Text.Encoding.UTF8.GetString(array, (int)pos, (int)len);
    }
}
