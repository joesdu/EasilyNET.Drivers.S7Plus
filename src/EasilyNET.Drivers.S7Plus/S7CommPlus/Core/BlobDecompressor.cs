// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
using System.IO.Compression;

namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Core;

internal static class BlobDecompressor
{
    /// <summary>
    /// 解压 zlib 压缩的 blob（主要用于 XML 类型压缩数据）。
    /// </summary>
    public static string Decompress(byte[] compressed_blob, int startoffset)
    {
        ArgumentNullException.ThrowIfNull(compressed_blob);
        try
        {
            using var input = new MemoryStream(compressed_blob, startoffset, compressed_blob.Length - startoffset);
            using var zstream = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zstream.CopyTo(output);
            return Encoding.UTF8.GetString(output.ToArray());
        }
        catch
        {
            return string.Empty;
        }
    }
}
