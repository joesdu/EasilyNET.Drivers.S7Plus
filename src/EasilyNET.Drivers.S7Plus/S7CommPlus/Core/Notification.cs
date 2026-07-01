// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

internal sealed class Notification(byte protocolVersion)
{
    public byte ProtocolVersion { get; set; } = protocolVersion;

    public uint SubscriptionObjectId { get; private set; }
    public ushort Unknown2 { get; private set; }
    public ushort Unknown3 { get; private set; }
    public ushort Unknown4 { get; private set; }

    public byte NotificationCreditTick { get; private set; }
    public uint NotificationSequenceNumber { get; private set; }
    public byte SubscriptionChangeCounter { get; private set; }

    public DateTime Add1Timestamp { get; private set; }
    public byte Add1SubscriptionChangeCounter { get; private set; }

    public Dictionary<uint, PValue> Values { get; private set; } = [];       // Item reference number, Value
    public Dictionary<uint, byte> ReturnValues { get; private set; } = [];   // Item reference number, ReturnValue

    public uint P2SubscriptionObjectId { get; private set; }           // for alarm object
    public ushort P2Unknown1 { get; private set; }                       // for alarm object
    public byte P2ReturnValue { get; private set; }                      // for alarm object

    private List<PObject> _p2Objects = [];
    public List<PObject> P2Objects { get => _p2Objects; private set => _p2Objects = value; }              // for alarm object

    public int Deserialize(Stream buffer)
    {
        var ret = 0;
        byte item_return_value;
        uint itemref;

        ret += S7p.DecodeUInt32(buffer, out var _subscriptionObjectId);
        SubscriptionObjectId = _subscriptionObjectId;
        ret += S7p.DecodeUInt16(buffer, out var _unknown2);
        Unknown2 = _unknown2;
        ret += S7p.DecodeUInt16(buffer, out var _unknown3);
        Unknown3 = _unknown3;
        ret += S7p.DecodeUInt16(buffer, out var _unknown4);
        Unknown4 = _unknown4;

        ret += S7p.DecodeByte(buffer, out var _notificationCreditTick);
        NotificationCreditTick = _notificationCreditTick;
        ret += S7p.DecodeUInt32Vlq(buffer, out var _notificationSequenceNumber);
        NotificationSequenceNumber = _notificationSequenceNumber;
        ret += S7p.DecodeByte(buffer, out var subscrccnt);
        if (subscrccnt > 0)
        {
            SubscriptionChangeCounter = subscrccnt;
        }
        else
        {
            // Newer versions of 1500 if subscrccnt ==0:
            // If this is zero, then an 8 byte UTC Timestamp on microsecond basis follows,
            // where the first byte should be always zero (in the 'near' future).
            buffer.Position -= 1;// Set position back
            ret += S7p.DecodeUInt64(buffer, out var timestamp);
            ulong epochTicks = 621355968000000000; // Unix Time (UTC) on 1st January 1970.
            // Convert to .Net DateTime
            Add1Timestamp = new DateTime((long)((timestamp * 10) + epochTicks), DateTimeKind.Utc);
            ret += S7p.DecodeByte(buffer, out var add1SubscrChangeCounter);
            Add1SubscriptionChangeCounter = add1SubscrChangeCounter;
        }
        // Return value: If the value != 0 then follows a dataset with the common known structure.
        // If an access error occurs, we have here an error-value, in this case datatype==NULL.
        // TODO: The returncodes follow not any known structure. I've tried to reproduce some errors
        // on different controllers and generations with the following results:
        //  hex       bin       ref-id  value   description
        //  0x03 = 0000 0011 -> ntohl   -       Addressing error (S7-1500 - Plcsim), like 0x13
        //  0x13 = 0001 0011 -> ntohl   -       Addressing error (S7-1200) and 1500-Plcsim
        //  0x81 = 1000 0001 ->         object  Standard object starts with 0xa1 (only in protocol version v1?)
        //  0x83 = 1000 0011 ->         value   Standard value structure, then notification value-list (only in protocol version v1?)
        //  0x92 = 1001 0010 -> ntohl   value   Success (S7-1200)
        //  0x9b = 1001 1011 -> vlq32   value   Seen on 1500 and 1200. Following ID or number, then flag, type, value
        //  0x9c = 1001 1100 -> ntohl   ?       Online with variable status table (S7-1200), structure seems to be completely different
        do
        {
            ret += S7p.DecodeByte(buffer, out item_return_value);
            switch (item_return_value)
            {
                case 0x00:
                    break;
                case 0x92:
                    // Item reference number: Is sent to plc in the subscription-telegram for the addresses.
                    ret += S7p.DecodeUInt32(buffer, out itemref);
                    Values.Add(itemref, PValue.Deserialize(buffer));
                    break;
                case 0x9b:
                    ret += S7p.DecodeUInt32Vlq(buffer, out itemref);
                    Values.Add(itemref, PValue.Deserialize(buffer));
                    break;
                case 0x9c:
                    // Don't do anything with the data (for now)
                    ret += S7p.DecodeUInt32(buffer, out _);
                    break;
                case 0x13:
                case 0x03:
                    ret += S7p.DecodeUInt32(buffer, out itemref);
                    ReturnValues.Add(itemref, item_return_value);
                    break;
                //case 0x81: //Only in protocol version v1, but also used in S7-1500 in part 2 for ProgramAlarm
                case 0x83:
                    // Probably only in protocol version v1
                    throw new NotImplementedException();
                default:
                    // unknown return value
                    throw new NotImplementedException();
            }
        } while (item_return_value != 0);

        // If next byte is not zero, an alarm notification object may follow
        ret += S7p.DecodeByte(buffer, out var PeekByte);
        // Set position back
        buffer.Position -= 1;
        if (PeekByte != 0)
        {
            ret += S7p.DecodeUInt32(buffer, out var p2SubscriptionObjectId);
            P2SubscriptionObjectId = p2SubscriptionObjectId;
            ret += S7p.DecodeUInt16(buffer, out var p2Unknown1);
            P2Unknown1 = p2Unknown1;
            ret += S7p.DecodeByte(buffer, out var p2ReturnValue);
            P2ReturnValue = p2ReturnValue;
            // It's not known if there are more than one object (as List), each object has
            // it's return value, or if there is really only one.
            // I wasn't able to produce a notification with more than one.
            if (P2ReturnValue == 0x81)
            {
                ret += S7p.DecodeObjectList(buffer, ref _p2Objects);
            }
            else
            {
                throw new NotImplementedException();
            }
        }
        return ret;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<Notification>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<ProtocolVersion>{ProtocolVersion}</ProtocolVersion>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<SubscriptionObjectId>{SubscriptionObjectId}</SubscriptionObjectId>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<Unknown2>{Unknown2}</Unknown2>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<Unknown3>{Unknown3}</Unknown3>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<Unknown4>{Unknown4}</Unknown4>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<NotificationCreditTick>{NotificationCreditTick}</NotificationCreditTick>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<NotificationSequenceNumber>{NotificationSequenceNumber}</NotificationSequenceNumber>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<SubscriptionChangeCounter>{SubscriptionChangeCounter}</SubscriptionChangeCounter>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<Add1Timestamp>{$"{Add1Timestamp}.{Add1Timestamp.Millisecond:D03} UTC"}</Add1Timestamp>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<Add1SubscriptionChangeCounter>{Add1SubscriptionChangeCounter}</Add1SubscriptionChangeCounter>");
        sb.AppendLine("<ValueList>");
        foreach (var value in Values)
        {
            sb.AppendLine("<Value>");
            sb.AppendLine(CultureInfo.InvariantCulture, $"<ItemRefId>{value.Key}</ItemRefId>");
            sb.AppendLine(value.Value.ToString());
            sb.AppendLine("</Value>");
        }
        sb.AppendLine("</ValueList>");

        sb.AppendLine("<ReturnValueList>");
        foreach (var errval in ReturnValues)
        {
            sb.AppendLine("<ReturnValue>");
            sb.AppendLine(CultureInfo.InvariantCulture, $"<ItemRefId>{errval.Key}</ItemRefId>");
            sb.AppendLine(CultureInfo.InvariantCulture, $"<ReturnValue>{errval.Value}</ReturnValue>");
            sb.AppendLine("</ReturnValue>");
        }
        sb.AppendLine("</ReturnValueList>");
        // For alarm object(s)
        sb.AppendLine(CultureInfo.InvariantCulture, $"<P2SubscriptionObjectId>{P2SubscriptionObjectId}</P2SubscriptionObjectId>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<P2Unknown1>{P2Unknown1}</P2Unknown1>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<P2ReturnValue>{P2ReturnValue}</P2ReturnValue>");
        sb.AppendLine("<P2Objects>");
        foreach (var p2o in P2Objects)
        {
            sb.Append(p2o.ToString());
        }
        sb.AppendLine("</P2Objects>");
        sb.AppendLine("</Notification>");
        return sb.ToString();
    }

    public static Notification? DeserializeFromPdu(Stream pdu)
    {
        // Special handling of ProtocolVersion, which is written to the stream before
        S7p.DecodeByte(pdu, out var protocolVersion);
        S7p.DecodeByte(pdu, out var opcode);
        if (opcode != Opcode.Notification)
        {
            return null;
        }
        var notif = new Notification(protocolVersion);
        notif.Deserialize(pdu);

        return notif;
    }
}
