// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

internal static class Opcode
{
    public static byte Request = 0x31;
    public static byte Response = 0x32;
    public static byte Notification = 0x33;
    public static byte Response2 = 0x02;
}
