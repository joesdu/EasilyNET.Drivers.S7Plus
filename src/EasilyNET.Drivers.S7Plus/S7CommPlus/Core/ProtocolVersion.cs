// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

internal static class ProtocolVersion
{
    public const byte V1 = 0x01;
    public const byte V2 = 0x02;
    public const byte V3 = 0x03;
    public const byte SystemEvent = 0xfe;
}
