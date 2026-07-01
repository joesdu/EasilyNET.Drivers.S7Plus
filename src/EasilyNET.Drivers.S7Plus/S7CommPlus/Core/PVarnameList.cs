// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
using System.Text;

namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

internal sealed class PVarnameList
{
    public List<string> Names { get; private set; } = [];

    public int Deserialize(Stream buffer)
    {
        var ret = 0;
        int maxret;

        Names = [];

        ret += S7p.DecodeUInt16(buffer, out var blocklen);
        maxret = ret + blocklen;
        while (blocklen > 0)
        {
            do
            {
                // Length of a name is max. 128 chars
                ret += S7p.DecodeByte(buffer, out var namelen);
                ret += S7p.DecodeWString(buffer, namelen, out var name);
                Names.Add(name);
                // Additional 1 Byte with 0 at the end. Why Null termination when the length is given? I don't know...
                ret += S7p.DecodeByte(buffer, out _);
            } while (ret < maxret);
            ret += S7p.DecodeUInt16(buffer, out blocklen);
            maxret = ret + blocklen;
        }
        return ret;
    }

    public override string ToString()
    {
        var s = new StringBuilder("");
        var i = 1;
        s.AppendLine("<VarnameList>");
        foreach (var name in Names)
        {
            s.AppendLine($"""<Name index="{i}">{name}</Name>""");
            i++;
        }
        s.AppendLine("</VarnameList>");
        return s.ToString();
    }
}
