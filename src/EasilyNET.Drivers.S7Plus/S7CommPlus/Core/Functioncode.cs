// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

internal static class Functioncode
{
    public const int Error = 0x04b1;
    public const int Explore = 0x04bb;
    public const int CreateObject = 0x04ca;
    public const int DeleteObject = 0x04d4;
    public const int SetVariable = 0x04f2;
    public const int GetVariable = 0x04fc; /* only in old 1200 FW? */
    public const int AddLink = 0x0506;
    public const int RemoveLink = 0x051a;
    public const int GetLink = 0x0524;
    public const int SetMultiVariables = 0x0542;
    public const int GetMultiVariables = 0x054c;
    public const int BeginSequence = 0x0556;
    public const int EndSequence = 0x0560;
    public const int Invoke = 0x056b;
    public const int SetVarSubStreamed = 0x057c;
    public const int GetVarSubStreamed = 0x0586;
    public const int GetVariablesAddress = 0x0590;
    public const int Abort = 0x059a;
    public const int Error2 = 0x05a9;
    public const int InitSsl = 0x05b3;
}
