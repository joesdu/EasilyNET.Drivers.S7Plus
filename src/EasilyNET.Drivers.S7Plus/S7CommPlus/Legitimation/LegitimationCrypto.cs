// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
using System.Security.Cryptography;

namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Legitimation;

internal static class LegitimationCrypto
{

    /// <summary>
    /// SHA256
    /// </summary>
    /// <param name="data">Data to hash</param>
    /// <returns>Hash</returns>
    public static byte[] Sha256(byte[] data)
    {
        return SHA256.HashData(data);
    }

    /// <summary>
    /// Encrypt AES256CBC
    /// </summary>
    /// <param name="plainBytes">Plain data</param>
    /// <param name="key">Encryption key</param>
    /// <param name="iv">Init vector</param>
    /// <returns>Encrypted data</returns>
    public static byte[] EncryptAesCbc(byte[] plainBytes, byte[] key, byte[] iv)
    {
        byte[]? encryptedBytes = null;

        // Set up the encryption objects
        using (var aes = Aes.Create())
        {
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            // Encrypt the input plaintext using the AES algorithm
            using var encryptor = aes.CreateEncryptor();
            encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        }

        return encryptedBytes;
    }
}
