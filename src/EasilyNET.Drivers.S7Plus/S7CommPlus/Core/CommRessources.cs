// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
using EasilyNET.Drivers.S7Plus.S7CommPlus.ClientApi;

namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

internal sealed class CommRessources
{
    public int TagsPerReadRequestMax { get; private set; } = 20;
    public int TagsPerWriteRequestMax { get; private set; } = 20;
    public int PlcAttributesMax { get; private set; }
    public int PlcAttributesFree { get; private set; }
    public int PlcSubscriptionsMax { get; private set; }
    public int PlcSubscriptionsFree { get; private set; }
    public int SubscriptionMemoryMax { get; private set; }
    public int SubscriptionMemoryFree { get; private set; }

    public async Task<int> ReadMaxAsync(S7CommPlusConnection conn, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conn, nameof(conn));
        var adrTagsPerReadRequestMax = new ItemAddress
        {
            AccessArea = Ids.ObjectRoot,
            AccessSubArea = Ids.SystemLimits
        };
        adrTagsPerReadRequestMax.LID.Add(1000);

        var adrTagsPerWriteRequestMax = new ItemAddress
        {
            AccessArea = Ids.ObjectRoot,
            AccessSubArea = Ids.SystemLimits
        };
        adrTagsPerWriteRequestMax.LID.Add(1001);

        var adrPlcSubscriptionsMax = new ItemAddress
        {
            AccessArea = Ids.ObjectRoot,
            AccessSubArea = Ids.SystemLimits
        };
        adrPlcSubscriptionsMax.LID.Add(0);

        var adrPlcAttributesMax = new ItemAddress
        {
            AccessArea = Ids.ObjectRoot,
            AccessSubArea = Ids.SystemLimits
        };
        adrPlcAttributesMax.LID.Add(1);

        var adrSubscriptionMemoryMax = new ItemAddress
        {
            AccessArea = Ids.ObjectRoot,
            AccessSubArea = Ids.SystemLimits
        };
        adrSubscriptionMemoryMax.LID.Add(2);

        var readlist = new List<ItemAddress>
        {
            adrTagsPerReadRequestMax,
            adrTagsPerWriteRequestMax,
            adrPlcSubscriptionsMax,
            adrPlcAttributesMax,
            adrSubscriptionMemoryMax
        };
        // Read SystemLimits
        // Assumption (so far, because for all CPUs which have be seen both values were the same):
        // 1000 = Number for Reading
        // 1001 = Number for Writing
        var (res, values, errors) = await conn.ReadValuesAsync(readlist, ct).ConfigureAwait(false);
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] != null && errors[i] == 0)
            {
                var v = ((ValueDInt)values[i]!).Value;
                switch (i)
                {
                    case 0:
                        TagsPerReadRequestMax = v;
                        break;
                    case 1:
                        TagsPerWriteRequestMax = v;
                        break;
                    case 2:
                        PlcSubscriptionsMax = v;
                        break;
                    case 3:
                        PlcAttributesMax = v;
                        break;
                    case 4:
                        SubscriptionMemoryMax = v;
                        break;
                    default:
                        break;
                }
            }
        }
        return res;
    }

    public async Task<int> ReadFreeAsync(S7CommPlusConnection conn, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conn, nameof(conn));
        var adrPlcSubscriptionsFree = new ItemAddress
        {
            AccessArea = Ids.ObjectRoot,
            AccessSubArea = Ids.FreeItems
        };
        adrPlcSubscriptionsFree.LID.Add(0);

        var adrPlcAttributesFree = new ItemAddress
        {
            AccessArea = Ids.ObjectRoot,
            AccessSubArea = Ids.FreeItems
        };
        adrPlcAttributesFree.LID.Add(1);

        var adrSubscriptionMemoryFree = new ItemAddress
        {
            AccessArea = Ids.ObjectRoot,
            AccessSubArea = Ids.FreeItems
        };
        adrSubscriptionMemoryFree.LID.Add(2);

        var readlist = new List<ItemAddress>
        {
            adrPlcSubscriptionsFree,
            adrPlcAttributesFree,
            adrSubscriptionMemoryFree
        };

        var (res, values, errors) = await conn.ReadValuesAsync(readlist, ct).ConfigureAwait(false);
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] != null && errors[i] == 0)
            {
                var v = ((ValueDInt)values[i]!).Value;
                switch (i)
                {
                    case 0:
                        PlcSubscriptionsFree = v;
                        break;
                    case 1:
                        PlcAttributesFree = v;
                        break;
                    case 2:
                        SubscriptionMemoryFree = v;
                        break;
                    default:
                        break;
                }
            }
        }
        return res;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<CommRessources>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<TagsPerReadRequestMax>{TagsPerReadRequestMax}</TagsPerReadRequestMax>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<TagsPerWriteRequestMax>{TagsPerWriteRequestMax}</TagsPerWriteRequestMax>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<PlcAttributesMax>{PlcAttributesMax}</PlcAttributesMax>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<PlcAttributesFree>{PlcAttributesFree}</PlcAttributesFree>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<PlcSubscriptionsMax>{PlcSubscriptionsMax}</PlcSubscriptionsMax>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<PlcSubscriptionsFree>{PlcSubscriptionsFree}</PlcSubscriptionsFree>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<SubscriptionMemoryMax>{SubscriptionMemoryMax}</SubscriptionMemoryMax>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<SubscriptionMemoryFree>{SubscriptionMemoryFree}</SubscriptionMemoryFree>");
        sb.AppendLine($"</CommRessources>");
        return sb.ToString();
    }
}