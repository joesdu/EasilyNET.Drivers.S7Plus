// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

internal static class ElementID
{
    public const byte StartOfObject = 0xa1;
    public const byte TerminatingObject = 0xa2;
    public const byte Attribute = 0xa3;
    public const byte Relation = 0xa4;
    public const byte StartOfTagDescription = 0xa7;
    public const byte TerminatingTagDescription = 0xa8;
    public const byte VartypeList = 0xab;
    public const byte VarnameList = 0xac;
}
