// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
using Microsoft.Extensions.Logging;
using EasilyNET.Drivers.S7Plus.S7CommPlus.ClientApi;
using EasilyNET.Drivers.S7Plus.S7CommPlus.Core;
using EasilyNET.Drivers.S7Plus.S7CommPlus.Net;

namespace EasilyNET.Drivers.S7Plus;

internal sealed partial class S7CommPlusConnection
{
    // *************************************************
    // *                IMPORTANT!                     *
    // * This is basically a test for subscriptions,   *
    // * how to use them, and how to later integrate   *
    // * this into the complete library!               *
    // *************************************************

    private Dictionary<uint, PlcTag> m_SubscribedTags = []; // ItemRefId
    private readonly byte m_SubcriptionChangeCounter = 1;
    private readonly uint m_SubscriptionRelationId = 0x7fffc001; // TODO! Unknown value!0x7fffc001. Seems to be a startvalue, increases on next CreateObject. Guess: It's stored in the plc under this id.
    private short m_NextCreditLimit;
    private uint m_SubscriptionObjectId;

    /// <summary>
    /// Creates a subscription
    /// </summary>
    /// <param name="plcTags">The list of tags to add to the subscription</param>
    /// <param name="cycleTime">Cycle time for update in milliseconds. Lowest value seems to be 100 ms (if it's not dependant on the CPU).</param>
    /// <param name="ct">cancellation token</param>
    /// <returns></returns>
    public async Task<int> SubscriptionCreateAsync(List<PlcTag> plcTags, ushort cycleTime, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plcTags, nameof(plcTags));
        int res;
        m_SubscribedTags = [];
        var subsobj = new PObject
        {
            ClassId = Ids.ClassSubscription,
            RelationId = m_SubscriptionRelationId
        };
        subsobj.AddAttribute(Ids.ObjectVariableTypeName, new ValueWString($"Subscription_{m_SubscriptionRelationId}"));
        subsobj.AddAttribute(Ids.SubscriptionFunctionClassId, new ValueUSInt(0));
        subsobj.AddAttribute(Ids.SubscriptionMissedSendings, new ValueUInt(0));
        subsobj.AddAttribute(Ids.SubscriptionSubsystemError, new ValueLInt(0));
        subsobj.AddAttribute(Ids.SubscriptionRouteMode, new ValueUSInt(0x14)); // TODO Unknown, mostly seen 0x04, 0x14 or 0x15. Needs to be tested

        // Testresults of some RouteModes (0x04, 0x14, 0x20) some applications are using, together with credit limits:
        // For Alarm Subscription RouteMode 0x02 is used.
        //-----------+-------------+-----------------------------------------------------------------------------------------------------------------------------------------------------------------
        // RouteMode | CreditLimit | Behaviour
        //-----------+-------------+-----------------------------------------------------------------------------------------------------------------------------------------------------------------
        // 0x00      |  0          | No notification at all
        // 0x00      | -1          | All values on create; then values that have changed, empty Notification each cycle; unlimited without retriggering; CreditTick always 0
        // 0x00      | n>0         | All values on create; then values that have changed, empty Notification each cycle; stops after CreditTick reaches difference of n when not set to new value
        // 0x04      | 0           | Identical to 0x00 / 0
        // 0x04      | -1          | Identical to 0x00 / -1
        // 0x04      | n>0         | Identical to 0x00 / n>0
        // 0x14      | 0           | Identical to 0x00 / 0
        // 0x14      | -1          | Identical to 0x00 / -1
        // 0x14      | n>0         | Identical to 0x00 / n>0
        // 0x20      | 0           | Identical to 0x00 / 0
        // 0x20      | -1          | All values on create; then values that have changed, on cycle without change no notification; unlimited without retriggering; CreditTick always 0
        // 0x20      | n>0         | All values on create; then values that have changed, on cycle without change no notification; stops after CreditTick reaches difference of n when not set to new value

        subsobj.AddAttribute(Ids.SubscriptionActive, new ValueBool(true));
        subsobj.AddAttribute(Ids.SubscriptionReferenceList, GetSubscriptionListArray(plcTags));
        subsobj.AddAttribute(Ids.SubscriptionCycleTime, new ValueUDInt(cycleTime));
        subsobj.AddAttribute(Ids.SubscriptionDisabled, new ValueUSInt(0));
        subsobj.AddAttribute(Ids.SubscriptionCount, new ValueUSInt(0));
        m_NextCreditLimit = 10;
        subsobj.AddAttribute(Ids.SubscriptionCreditLimit, new ValueInt(m_NextCreditLimit)); // -1=unlimited, 255 = max
        subsobj.AddAttribute(Ids.SubscriptionTicks, new ValueUInt(65535));
        // 1055 = Unknown -> is working without setting this.
        subsobj.AddAttribute(1055, new ValueUSInt(0));

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
        await WaitForNewS7plusReceivedAsync(m_ReadTimeout, ct);
        if (m_LastError != 0)
        {
            m_client.Disconnect();
            return m_LastError;
        }

        var createObjRes = CreateObjectResponse.DeserializeFromPdu(m_ReceivedPDU);
        if (createObjRes == null)
        {
            log.LogDebug("Subscription - Create: CreateObjectResponse with Error!");
            return S7Consts.errIsoInvalidPDU;
        }

        if (createObjRes.ReturnValue == 0)
        {
            // Save the ObjectId, to modify the existing subscription if needed
            if (createObjRes.ObjectIds is { Count: > 0 })
            {
                m_SubscriptionObjectId = createObjRes.ObjectIds[0];
            }
        }
        else
        {
            // If creating a subscription fails, the object is still created and should be deleted.
            // At least deleting it, gives no error.
            log.LogDebug($"Subscription - Create: Failed with Returnvalue = 0x{createObjRes.ReturnValue:X8}");
            res = S7Consts.errCliInvalidParams;
        }
        return res;
    }

    private int SubscriptionSetCreditLimit(short limit)
    {
        int res;
        var setVarReq = new SetVariableRequest(ProtocolVersion.V2)
        {
            TransportFlags = 0x74, // Set flag, that we need no response
            InObjectId = m_SubscriptionObjectId,
            Address = Ids.SubscriptionCreditLimit,
            Value = new ValueInt(limit)
        };
        res = SendS7plusFunctionObject(setVarReq);
        return res;
    }

    private ValueUDIntArray GetSubscriptionListArray(List<PlcTag> plcTags)
    {
        var la = new List<uint>
        {
            // 0x8?ssxxxx = 8 = flag CreateNew, ss = 1 byte subscription Change counter, xxxx = unknown
            0x80000000 | ((uint)m_SubcriptionChangeCounter << 16),
            0,                     // Number of items to unsubscribe
            (uint)plcTags.Count   // Number of items to subscribe
        };

        uint tagReferenceId = 1;
        uint head;
        foreach (var tag in plcTags)
        {
            // Save the reference Id in the dictionary. In the notification we get this reference Id back
            // and know to which tag the value belongs to.
            m_SubscribedTags.Add(tagReferenceId, tag);
            // Write the Item address
            head = 0x80040000;
            // It's not known where 0x8004 stands for -> 4 was a guess it's for the number of fields
            // before the LIDs, but that's wrong (coincidentally fits here in this special case).
            // Get the number of IDs in advance, Sub-Area counts as one, and then count each LID.
            // 0x8aaabbbb = aaa = unknown value, bbbb = number of fields in the 2nd part.
            head |= (uint)(1 + tag.Address.LID.Count);
            la.Add(head);
            la.Add(tagReferenceId);
            la.Add(0); // Unknown 1
            la.Add(tag.Address.AccessArea);
            la.Add(tag.Address.SymbolCrc);
            // Count value in head starts from here
            la.Add(tag.Address.AccessSubArea);
            foreach (var li in tag.Address.LID)
            {
                la.Add(li);
            }
            tagReferenceId++;
        }
        // Convert all data to protocol UDInt Array (VLQ encoded)
        return new ValueUDIntArray([.. la], 0x20); // 0x20 -> Adressarray
    }

    public async Task<int> TestWaitForVariableChangeNotificationsAsync(int untilNumberOfNotifications, CancellationToken ct = default)
    {
        var res = 0;
        short creditLimitStep = 5;

        for (var i = 1; i <= untilNumberOfNotifications; i++)
        {
            log.LogDebug($"{Environment.NewLine}WaitForNotifications(): *** Loop #{i} ***");
            m_LastError = 0;
            await WaitForNotificationAsync(5000, ct);
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
                log.LogDebug($"Notification: CreditTick={noti.NotificationCreditTick} SequenceNumber={noti.NotificationSequenceNumber} PLC-Timestamp={noti.Add1Timestamp}.{noti.Add1Timestamp.Millisecond:D03} ValuesCount={noti.Values.Count}");
                foreach (var v in noti.Values)
                {
                    log.LogDebug($"---> key={v.Key} value={v.Value}");
                    // Error value in tags expects a 64 bit value, in subscriptions it's only 1 byte (for it's not known what all values are for -> TODO)
                    // 未知 item-reference id（如告警/额外项）不应抛 KeyNotFoundException 中断整个通知处理
                    if (m_SubscribedTags.TryGetValue(v.Key, out var subTag))
                    {
                        subTag.ProcessReadResult(v.Value, 0);
                    }
                    else
                    {
                        log.LogDebug($"Notification: unknown item reference id {v.Key}, skipped");
                    }
                }

                if (noti.NotificationCreditTick >= m_NextCreditLimit - 1) // Set new limit one tick before it expires, to get a constant flow of data
                {
                    // CreditTick in Notification is only one byte
                    m_NextCreditLimit = (short)((m_NextCreditLimit + creditLimitStep) % 255);
                    // 信用额度为 0 等同“不再发送任何通知”，回绕到 0 会让长跑订阅卡死：跳过 0 保持数据流
                    if (m_NextCreditLimit <= 0)
                    {
                        m_NextCreditLimit = (short)creditLimitStep;
                    }
                    log.LogDebug($"--> Credit limit of {noti.NotificationCreditTick} reached. SetCreditLimit to {m_NextCreditLimit}");
                    SubscriptionSetCreditLimit(m_NextCreditLimit);
                }
            }
        }
        return res;
    }

    public async Task<int> SubscriptionDeleteAsync(CancellationToken ct = default)
    {
        int res;
        m_SubscribedTags.Clear();
        m_SubscriptionObjectId = 0;
        log.LogDebug($"SubscriptionDelete: Calling DeleteObject for SessionId2={SessionId2:X8}");
        res = await DeleteObjectAsync(SessionId2, ct).ConfigureAwait(false);
        return res;
    }

    private bool m_disposed;

    /// <summary>
    /// 异步释放连接持有的资源：底层 <see cref="S7Client" />（内部停止收发泵并关闭 Socket）、
    /// 异步响应信号量与接收缓冲用的 <see cref="MemoryStream" />。
    /// 注意：此处不做优雅的会话删除（DeleteObject 的网络往返）；需要优雅断开时请显式调用 <see cref="DisconnectAsync" />。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (m_disposed)
        {
            return;
        }
        m_disposed = true;
        if (m_client is not null)
        {
            await m_client.DisposeAsync().ConfigureAwait(false);
        }
        m_responseSignal?.Dispose();
        m_notificationSignal?.Dispose();
        m_ReceivedPDU?.Dispose();
        m_ReceivedTempPDU?.Dispose();
        lock (m_pduLock)
        {
            while (m_ReceivedResponses.Count > 0)
            {
                m_ReceivedResponses.Dequeue().Pdu.Dispose();
            }
            while (m_ReceivedNotifications.Count > 0)
            {
                m_ReceivedNotifications.Dequeue().Dispose();
            }
        }
    }
}
