// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Legitimation;

internal static class AccessLevel
{
    public const uint FullAccess = 1;
    public const uint ReadAccess = 2;
    public const uint HMIAccess = 3;
    public const uint NoAccess = 4;
}
