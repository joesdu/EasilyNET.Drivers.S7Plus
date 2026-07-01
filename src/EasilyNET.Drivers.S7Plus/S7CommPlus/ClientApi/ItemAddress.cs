// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
using EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

namespace EasilyNET.Drivers.S7Plus.S7CommPlus.ClientApi;

internal sealed class ItemAddress : IS7pSerialize
{
    public uint SymbolCrc { get; set; }
    public uint AccessArea { get; set; }
    public uint AccessSubArea { get; set; }
    public List<uint> LID { get; private set; } = [];

    public ItemAddress() : this(0, Ids.DB_ValueActual)
    {
    }

    public ItemAddress(uint area, uint subArea)
    {
        SymbolCrc = 0;
        AccessArea = area;
        AccessSubArea = subArea;
    }

    public ItemAddress(string variableAccessString)
    {
        ArgumentNullException.ThrowIfNull(variableAccessString, nameof(variableAccessString));
        // Uses a complete access string consisting of hexadecimal strings separated by a dot (".").
        // Returns a list of the extracted IDs, e.g. 8A0E0001.A or 52.A
        List<uint> ids = [];
        foreach (var p in variableAccessString.Split('.'))
        {
            ids.Add(uint.Parse(p, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }
        // TODO: Check for an error, number of fields should be at least 2
        SymbolCrc = 0;
        AccessArea = ids[0];
        // Set access area
        if (AccessArea >= 0x8A0E0000)   // 0x8A0A.... = datablocks
        {
            AccessSubArea = Ids.DB_ValueActual;
        }
        else if (AccessArea is Ids.NativeObjects_theS7Timers_Rid or
                   Ids.NativeObjects_theS7Counters_Rid or
                   Ids.NativeObjects_theIArea_Rid or
                   Ids.NativeObjects_theQArea_Rid or
                   Ids.NativeObjects_theMArea_Rid)
        {
            AccessSubArea = Ids.ControllerArea_ValueActual;
        }
        foreach (var i in ids.Skip(1))
        {
            LID.Add(i);
        }
    }

    public string AccessString
    {
        get
        {
            // Generate from the given address an Access-String.
            // Useful if the user has set the address not via access string, but by the single elements.
            var s = $"{AccessArea:X}";
            foreach (var i in LID)
            {
                s += $".{i:X}";
            }
            return s;
        }
    }

    public uint NumberOfFields => (uint)(4 + LID.Count);

    public void SetAccessAreaToDatablock(uint number)
    {
        AccessArea = (ushort)number + 0x8a0e0000;
    }

    public int Serialize(Stream buffer)
    {
        var ret = 0;
        ret += S7p.EncodeUInt32Vlq(buffer, SymbolCrc);
        ret += S7p.EncodeUInt32Vlq(buffer, AccessArea);
        ret += S7p.EncodeUInt32Vlq(buffer, (uint)LID.Count + 1);
        ret += S7p.EncodeUInt32Vlq(buffer, AccessSubArea);
        foreach (var id in LID)
        {
            ret += S7p.EncodeUInt32Vlq(buffer, id);
        }
        return ret;
    }

    public override string ToString()
    {
        var s = "";
        s += $"<ItemAddress>{Environment.NewLine}";
        s += $"<SymbolCrc>{SymbolCrc}</SymbolCrc>{Environment.NewLine}";
        s += $"<AccessArea>{AccessArea}</AccessArea>{Environment.NewLine}";
        s += $"<NumberOfIDs>{LID.Count + 1}</NumberOfIDs>{Environment.NewLine}";
        s += $"<AccessSubArea>{AccessSubArea}</AccessSubArea>{Environment.NewLine}";
        foreach (var id in LID)
        {
            s += $"<LIDvalue>{id}</LIDvalue>{Environment.NewLine}";
        }
        s += $"</ItemAddress>{Environment.NewLine}";
        return s;
    }
}
