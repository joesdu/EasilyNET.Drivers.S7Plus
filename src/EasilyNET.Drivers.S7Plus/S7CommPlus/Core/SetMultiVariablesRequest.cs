// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
using EasilyNET.Drivers.S7Plus.S7CommPlus.ClientApi;
using System.Text;

namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

internal sealed class SetMultiVariablesRequest(byte protocolVersion) : IS7pRequest
{
    private byte TransportFlags { get; set; } = 0x34;

    public uint InObjectId { get; set; }
    /* Special
     * Plain variables are accessed with InObjectId = 0. Then the user needs to add
     * the addresses via the addresslist and the valuelist the values which should be written.
     * The field after Itemcount then doesn't contains the number of addresses, but the number
     * of field inside it.
     * Which is in the normal use case:
     * 1. SymbolCRC (maybe zero of not CRC check is needed)
     * 2. Access base-area
     * 3. Number of fields which are now following
     * Depending on the address this if 
     *
     * If values inside objects are to be written, then the addresslist contains only a single value.
     * But counting them is identically.
     *
     * The only misleading thing is, we have two addresslists as members for both use-cases.
     * TODO
     */
    public List<uint> AddressList { get; private set; } = [];
    public List<ItemAddress> AddressListVar { get; private set; } = [];
    public List<PValue> ValueList { get; private set; } = [];

    public uint SessionId { get; set; }
    public byte ProtocolVersion { get; set; } = protocolVersion;
    public ushort FunctionCode => Functioncode.SetMultiVariables;
    public ushort SequenceNumber { get; set; }
    public uint IntegrityId { get; set; }
    public bool WithIntegrityId { get; set; } = true;

    public int Serialize(Stream buffer)
    {
        uint i;
        var ret = 0;
        ret += S7p.EncodeByte(buffer, Opcode.Request);
        ret += S7p.EncodeUInt16(buffer, 0);                               // Reserved
        ret += S7p.EncodeUInt16(buffer, FunctionCode);
        ret += S7p.EncodeUInt16(buffer, 0);                               // Reserved
        ret += S7p.EncodeUInt16(buffer, SequenceNumber);
        ret += S7p.EncodeUInt32(buffer, SessionId);
        ret += S7p.EncodeByte(buffer, TransportFlags);

        // Request set
        ret += S7p.EncodeUInt32(buffer, InObjectId);
        ret += S7p.EncodeUInt32Vlq(buffer, (uint)ValueList.Count);
        if (InObjectId > 0)
        {
            ret += S7p.EncodeUInt32Vlq(buffer, (uint)AddressList.Count);
            foreach (var id in AddressList)
            {
                ret += S7p.EncodeUInt32Vlq(buffer, id);
            }
        }
        else
        {
            uint fieldCount = 0;
            foreach (var adr in AddressListVar)
            {
                fieldCount += adr.NumberOfFields;
            }
            ret += S7p.EncodeUInt32Vlq(buffer, fieldCount);

            foreach (var adr in AddressListVar)
            {
                ret += adr.Serialize(buffer);
            }
        }

        i = 1;
        foreach (var value in ValueList)
        {
            // Item Number + Value
            ret += S7p.EncodeUInt32Vlq(buffer, i);
            ret += value.Serialize(buffer);
            i++;
        }
        // 1 Fill byte
        ret += S7p.EncodeByte(buffer, 0x00);
        ret += S7p.EncodeObjectQualifier(buffer);

        if (WithIntegrityId)
        {
            ret += S7p.EncodeUInt32Vlq(buffer, IntegrityId);
        }

        // Fill?
        ret += S7p.EncodeUInt32(buffer, 0);

        return ret;
    }

    public void SetSessionSetupData(uint sessionId, ValueStruct SessionVersion)
    {
        // Initializes some values for session setup. Depending on the CPU, some more values needs to be set.
        SessionId = sessionId;
        InObjectId = sessionId;
        AddressList.Clear();
        AddressList.Add(Ids.ServerSessionVersion);
        ValueList.Clear();
        ValueList.Add(SessionVersion);
        // As we use her ProtocolVersion 2, without
        WithIntegrityId = false;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<SetMultiVariablesRequest>");
        sb.AppendLine($"<ProtocolVersion>{ProtocolVersion}</ProtocolVersion>");
        sb.AppendLine($"<SequenceNumber>{SequenceNumber}</SequenceNumber>");
        sb.AppendLine($"<SessionId>{SessionId}</SessionId>");
        sb.AppendLine($"<TransportFlags>{TransportFlags}</TransportFlags>");
        sb.AppendLine("<RequestSet>");
        sb.AppendLine($"<InObjectId>{InObjectId}</InObjectId>");
        sb.AppendLine($"<ItemCount>{ValueList.Count}</ItemCount>");
        if (InObjectId > 0)
        {
            sb.AppendLine($"<ItemAddressCount>{AddressList.Count}</ItemAddressCount>");
            sb.AppendLine("<AddressList>");
            foreach (var id in AddressList)
            {
                sb.AppendLine($"<Id>{id}</Id>");
            }
            sb.AppendLine("</AddressList>");
        }
        else
        {
            uint fieldCount = 0;
            foreach (var adr in AddressListVar)
            {
                fieldCount += adr.NumberOfFields;
            }
            sb.AppendLine($"<NumberOfFields>{fieldCount}</NumberOfFields>");
            sb.AppendLine("<AddressList>");
            foreach (var adr in AddressListVar)
            {
                sb.Append(adr.ToString());
            }
            sb.AppendLine("</AddressList>");
        }
        sb.AppendLine("<ValueList>");
        foreach (var val in ValueList)
        {
            sb.AppendLine($"<Value>{val}</Value>");
        }
        sb.AppendLine("</ValueList>");
        sb.AppendLine("</RequestSet>");
        sb.AppendLine("</SetMultiVariablesRequest>");
        return sb.ToString();
    }
}
