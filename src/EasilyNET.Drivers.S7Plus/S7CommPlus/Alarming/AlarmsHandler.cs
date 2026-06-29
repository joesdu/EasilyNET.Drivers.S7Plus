// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
using Microsoft.Extensions.Logging;
using EasilyNET.Drivers.S7Plus.S7CommPlus.Alarming;
using EasilyNET.Drivers.S7Plus.S7CommPlus.Core;
using EasilyNET.Drivers.S7Plus.S7CommPlus.Net;

namespace EasilyNET.Drivers.S7Plus;

internal sealed partial class S7CommPlusConnection
{
    // *************************************************
    // *                IMPORTANT!                     *
    // * This is basically a test for Alarming,        *
    // * how to use it, and how to later integrate     *
    // * this into the complete library!               *
    // *************************************************
    //
    // Example code for testing:
    // CultureInfo ci = new CultureInfo("en-US");
    // conn.AlarmSubscriptionCreate();
    // conn.TestWaitForAlarmNotifications(20000, 3, ci.LCID);
    // conn.AlarmSubscriptionDelete();

    private readonly uint m_AlarmSubscriptionRelationId = 0x7fffc001; // TODO! Unknown value! See also Subscription.cs
    private readonly uint m_AlarmSubscriptionRefRelationId = 0x51010001; // TODO! Unknown value!
    private short m_AlarmNextCreditLimit;

    public int AlarmSubscriptionCreate()
    {
        int res;
        var subsobj = new PObject
        {
            ClassId = Ids.ClassSubscription,
            RelationId = m_AlarmSubscriptionRelationId
        };
        subsobj.AddAttribute(Ids.ObjectVariableTypeName, new ValueWString($"Subscription_{m_AlarmSubscriptionRelationId}"));
        subsobj.AddAttribute(Ids.SubscriptionFunctionClassId, new ValueUSInt(2));
        subsobj.AddAttribute(Ids.SubscriptionMissedSendings, new ValueUInt(0));
        subsobj.AddAttribute(Ids.SubscriptionSubsystemError, new ValueLInt(0));
        subsobj.AddAttribute(Ids.SubscriptionRouteMode, new ValueUSInt(2)); // TODO Unknown
        subsobj.AddAttribute(Ids.SubscriptionActive, new ValueBool(true));
        subsobj.AddAttribute(Ids.SubscriptionReferenceList, new ValueUDIntArray([0x80010000, 0, 0], 0x20)); // 0x20 = Adressarray
        subsobj.AddAttribute(Ids.SubscriptionCycleTime, new ValueUDInt(0));
        subsobj.AddAttribute(Ids.SubscriptionDelayTime, new ValueUDInt(0));
        subsobj.AddAttribute(Ids.SubscriptionDisabled, new ValueUSInt(0));
        subsobj.AddAttribute(Ids.SubscriptionCount, new ValueUSInt(0));
        m_AlarmNextCreditLimit = 10;
        subsobj.AddAttribute(Ids.SubscriptionCreditLimit, new ValueInt(m_AlarmNextCreditLimit)); // -1=unlimited, 255 = max
        subsobj.AddAttribute(Ids.SubscriptionTicks, new ValueUInt(65535)); // 65535
        // 1055 = Unknown -> is working without setting this. Maybe default attribute is zero.
        //subsobj.AddAttribute(1055, new ValueUSInt(0));
        var asrefsobj = new PObject
        {
            ClassId = Ids.AlarmSubscriptionRef_Class_Rid,
            RelationId = m_AlarmSubscriptionRefRelationId
        };
        asrefsobj.AddAttribute(Ids.ObjectVariableTypeName, new ValueWString("S7pDriver_Alarming"));
        asrefsobj.AddAttribute(Ids.SubscriptionReferenceTriggerAndTransmitMode, new ValueUSInt(3));
        asrefsobj.AddAttribute(Ids.AlarmSubscriptionRef_AlarmDomain, new ValueUIntArray([0, 0, 0, 0, 0, 0, 0, 0, 0, 0], 0x10));
        // Also variant to set explicit the alarm domain as filter, for example:
        // {1, 256, 257, 258, 259, 260, 261, 262, 263, 264, 265, 266, 267, 268, 269, 270, 271, 272}
        // Possibly 65535 is "all".
        asrefsobj.AddAttribute(Ids.AlarmSubscriptionRef_AlarmDomain2, new ValueUIntArray([65535], 0x20)); // 0x20 = Adressarray
        // OPTION: 
        // Send text informations with the message, we don't need to browse them in advance.
        asrefsobj.AddAttribute(Ids.AlarmSubscriptionRef_AlarmTextLanguages_Rid, new ValueUDIntArray([], 0x20)); // Empty for all languanges? Otherwise e.g. 1031 for de-de or what you need.
        asrefsobj.AddAttribute(Ids.AlarmSubscriptionRef_SendAlarmTexts_Rid, new ValueBool(true));

        asrefsobj.AddRelation(Ids.AlarmSubscriptionRef_itsAlarmSubsystem, 0x00000008);
        subsobj.AddObject(asrefsobj);
        // Build the request object
        var createObjReq = new CreateObjectRequest(ProtocolVersion.V2, 0, true)
        {
            TransportFlags = 0x34,
            RequestId = SessionId2,
            RequestValue = new ValueUDInt(0)
        };
        createObjReq.SetRequestObject(subsobj);

        // Send it
        res = SendS7plusFunctionObject(createObjReq);
        if (res != 0)
        {
            m_client.Disconnect();
            return res;
        }
        m_LastError = 0;
        WaitForNewS7plusReceived(m_ReadTimeout);
        if (m_LastError != 0)
        {
            m_client.Disconnect();
            return m_LastError;
        }

        var createObjRes = CreateObjectResponse.DeserializeFromPdu(m_ReceivedPDU);
        if (createObjRes == null)
        {
            log.LogDebug("AlarmSubscription - Create: CreateObjectResponse with Error!");
            return S7Consts.errIsoInvalidPDU;
        }

        if (createObjRes.ReturnValue == 0)
        {
            // Save the ObjectId, to modify the existing subscription
            if (createObjRes.ObjectIds == null || createObjRes.ObjectIds.Count <= 0)
            {
                log.LogDebug("AlarmSubscription - Create: No ObjectIds returned!");
                res = S7Consts.errCliInvalidParams;
            }
        }
        else
        {
            // If creating a subscription fails, the object is still created and should be deleted.
            // At least deleting it, gives no error.
            log.LogDebug($"AlarmSubscription - Create: Failed with Returnvalue = 0x{createObjRes.ReturnValue:X8}");
            res = S7Consts.errCliInvalidParams;
        }

        return res;
    }

    public int TestWaitForAlarmNotifications(int waitTimeout, int untilNumberOfAlarms, int alarmTextsLanguageId)
    {
        var res = 0;
        short creditLimitStep = 5;

        for (var i = 1; i <= untilNumberOfAlarms; i++)
        {
            log.LogDebug($"{Environment.NewLine}WaitForAlarmNotifications(): *** Loop #{i} ***");
            m_LastError = 0;
            WaitForNewS7plusReceived(waitTimeout);
            if (m_LastError != 0)
            {
                return m_LastError;
            }

            var noti = Notification.DeserializeFromPdu(m_ReceivedPDU);
            if (noti == null)
            {
                log.LogDebug("Notification == null!");
                return S7Consts.errIsoInvalidPDU;
            }
            else
            {
                log.LogDebug($"Notification: CreditTick={noti.NotificationCreditTick} SequenceNumber={noti.NotificationSequenceNumber} PLC-Timestamp={noti.Add1Timestamp}.{noti.Add1Timestamp.Millisecond:D03}");

                var dai = AlarmsDai.FromNotificationObject(noti.P2Objects[0], alarmTextsLanguageId);
                if (dai != null)
                {
                    log.LogDebug(dai.ToString());
                }
                if (noti.NotificationCreditTick >= m_AlarmNextCreditLimit - 1) // Set new limit one tick before it expires, to get a constant flow of data
                {
                    // CreditTick in Notification is only one byte
                    m_AlarmNextCreditLimit = (short)((m_AlarmNextCreditLimit + creditLimitStep) % 255);
                    log.LogDebug($"--> Credit limit of {noti.NotificationCreditTick} reached. SetCreditLimit to {m_AlarmNextCreditLimit}");
                    SubscriptionSetCreditLimit(m_AlarmNextCreditLimit);
                }
            }
        }
        return res;
    }

    public int AlarmSubscriptionDelete()
    {
        int res;
        log.LogDebug($"SubscriptionDelete: Calling DeleteObject for SessionId2={SessionId2:X8}");
        res = DeleteObject(SessionId2);
        return res;
    }
}
