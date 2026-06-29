// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

internal interface IS7pResponse
{
    byte ProtocolVersion
    {
        get;
        set;
    }

    ushort FunctionCode
    {
        get;
    }

    ushort SequenceNumber
    {
        get;
        set;
    }

    uint IntegrityId
    {
        get;
        set;
    }

    bool WithIntegrityId
    {
        get;
        set;
    }
}
