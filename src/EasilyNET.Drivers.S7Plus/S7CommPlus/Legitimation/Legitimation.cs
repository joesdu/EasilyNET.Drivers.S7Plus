// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
using Microsoft.Extensions.Logging;
using EasilyNET.Drivers.S7Plus.S7CommPlus.Core;
using EasilyNET.Drivers.S7Plus.S7CommPlus.Legitimation;
using EasilyNET.Drivers.S7Plus.S7CommPlus.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace EasilyNET.Drivers.S7Plus;

internal sealed partial class S7CommPlusConnection
{

    private byte[]? omsSecret;

    /// <summary>
    /// Legitimation stage of the connect routine
    /// </summary>
    /// <param name="serverSession">Server sesstion information containing the firmware version</param>
    /// <param name="password">PLC password</param>
    /// <param name="username">PLC username (leave empty for legacy login)</param>
    /// <param name="ct">cancellation token</param>
    /// <returns>error code (0 = ok)</returns>
    private async Task<int> LegitimateAsync(ValueStruct serverSession, string password, string username = "", CancellationToken ct = default)
    {
        // S7-1214C  (6ES7 214-1AG40-0XB0)           1;6ES7 214-1AG40-0XB0 ;V4.5
        // S7-1510SP (6ES7 510-1DJ01-0AB0)           1;6ES7 510-1DJ01-0AB0;V2.9
        // S7-1507SF (6ES7 672-7FC01-0YA0)           1;6ES7 672-7FC01-0YA0;V21.9

        // Parse device and firmware version
        // doc: https://cache.industry.siemens.com/dl/files/068/109769068/att_1329908/v4/109769068_UsingCertificatesWithTIAPortal_DOC_V2_1_en.pdf
        // Certificates in the scope of PG/PC and HMI communication
        //  Starting with TIA Portal V17, PG / PC and HMI communication is secured with TLS, protecting the data exchanged between
        //  Field PGs and HMIs with SIMATIC CPUs. 
        //  The CPU families that support Secure PG / HMI communication are:
        //      • S7 - 1500 controllers as of firmware version V2.9.
        //      • S7 - 1200 controllers as of firmware version V4.5.
        //      • Software controllers as of firmware version V21.9.
        //      • SIMATIC Drive controllers as of firmware version V2.9.
        //      • PLCSim and PLCSim Advanced Version V4.0.
        //  HMI components that support Secure PG/ HMI communication, as of image version V17, are:
        //      • Panels or PCs configured with WinCC Basic, Comfort and Advanced.
        //      • PCs with WinCC RT Professional.
        //      • WinCC Unified PCs and Comfort Panels.
        //  In addition, SINAMICS RT SW, as of version V6.1, and STARTDRIVE, as of version V17, support secure communication
        var sessionVersionPAOMString = ((ValueWString)serverSession.GetStructElement(Ids.LID_SessionVersionSystemPAOMString)).Value;
        var m = IsVersionedRecord.Match(sessionVersionPAOMString);
        if (!m.Success)
        {
            log.LogDebug("S7CommPlusConnection - Legitimate: Could not extract firmware version!");
            return S7Consts.errCliFirmwareNotSupported;
        }
        var deviceVersion = m.Groups[1].Value;   // e.g., "672"
        var firmwareVersion = m.Groups[2].Value; // e.g., "21.9"

        // Compute fwVerNo = major*100 + minor (e.g., "21.9" -> 2109)
        int fwVerNo;
        {
            var parts = firmwareVersion.Split('.');
            if (parts.Length < 2 || !int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor))
            {
                log.LogDebug("S7CommPlusConnection - Legitimate: Invalid firmware format: {FirmwareVersion}", firmwareVersion);
                return S7Consts.errCliFirmwareNotSupported;
            }
            fwVerNo = (major * 100) + minor;
        }

        // Check if we have to use legacy legitimation via the firmware version
        var legacyLegitimation = false;
        if (deviceVersion.StartsWith('5'))  // S7-1500 (5xx)
        {
            if (fwVerNo < 209)
            {
                log.LogDebug("S7CommPlusConnection - Legitimate: Firmware version is not supported!");
                return S7Consts.errCliFirmwareNotSupported;
            }
            if (fwVerNo < 301)
            {
                legacyLegitimation = true;
            }
        }
        else if (sessionVersionPAOMString.Contains("50-0XB0", StringComparison.InvariantCulture) && deviceVersion.StartsWith('2'))   //New S7-1200 G2 (example: "1;6ES7 212-1HG50-0XB0;V1.0")
        {
            legacyLegitimation = false;
        }
        else if (deviceVersion.StartsWith('2')) // S7-1200 (2xx)
        {
            if (fwVerNo < 403)
            {
                log.LogDebug("S7CommPlusConnection - Legitimate: Firmware version is not supported!");
                return S7Consts.errCliFirmwareNotSupported;
            }
            if (fwVerNo < 407)
            {
                legacyLegitimation = true;
            }
        }
        else if (deviceVersion.StartsWith('6')) // S7-1507S (6xx)
        {
            if (fwVerNo < 2109)
            {
                log.LogDebug("S7CommPlusConnection - Legitimate: Firmware version is not supported!");
                return S7Consts.errCliFirmwareNotSupported;
            }
            legacyLegitimation = true;
        }
        else
        {
            log.LogDebug("S7CommPlusConnection - Legitimate: Device version is not supported!");
            return S7Consts.errCliDeviceNotSupported;
        }

        // Get current protection level
        var getVarSubstreamedReq = new GetVarSubstreamedRequest(ProtocolVersion.V2)
        {
            InObjectId = m_SessionId,
            SessionId = m_SessionId,
            Address = Ids.EffectiveProtectionLevel
        };
        var res = SendS7plusFunctionObject(getVarSubstreamedReq);
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

        var getVarSubstreamedRes = GetVarSubstreamedResponse.DeserializeFromPdu(m_ReceivedPDU);
        if (getVarSubstreamedRes == null)
        {
            log.LogDebug("S7CommPlusConnection - Legitimate: GetVarSubstreamedResponse with Error!");
            m_client.Disconnect();
            return S7Consts.errIsoInvalidPDU;
        }

        // Check access level
        var accessLevel = (getVarSubstreamedRes.Value as ValueUDInt)!.Value;
        if (accessLevel > AccessLevel.FullAccess && !string.IsNullOrEmpty(password))
        {
            // Legitimate
            return legacyLegitimation ? await LegitimateLegacyAsync(password, ct).ConfigureAwait(false) : await LegitimateNewAsync(password, username, ct).ConfigureAwait(false);
        }
        else if (accessLevel > AccessLevel.FullAccess)
        {
            log.LogDebug("S7CommPlusConnection - Legitimate: Warning: Access level is not fullaccess but no password set!");
        }

        return 0;
    }

    /// <summary>
    /// Legitimate using the new login method (firmware &gt;= 3.1)
    /// </summary>
    /// <param name="password">PLC password</param>
    /// <param name="username">PLC username (leave empy for legacy login)</param>
    /// <param name="ct">cancellation token</param>
    /// <returns>error code (0 = ok)</returns>
    private async Task<int> LegitimateNewAsync(string password, string username = "", CancellationToken ct = default)
    {
        // Get challenge
        var getVarSubstreamedReq_challange = new GetVarSubstreamedRequest(ProtocolVersion.V2)
        {
            InObjectId = m_SessionId,
            SessionId = m_SessionId,
            Address = Ids.ServerSessionRequest
        };
        var res = SendS7plusFunctionObject(getVarSubstreamedReq_challange);
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

        var getVarSubstreamedRes_challenge = GetVarSubstreamedResponse.DeserializeFromPdu(m_ReceivedPDU);
        if (getVarSubstreamedRes_challenge == null)
        {
            log.LogDebug("S7CommPlusConnection - Legitimate: getVarSubstreamedRes_challenge with Error!");
            m_client.Disconnect();
            return S7Consts.errIsoInvalidPDU;
        }

        var challenge = (getVarSubstreamedRes_challenge.Value as ValueUSIntArray)!.Value;

        // Encrypt challengeResponse
        byte[] challengeResponse;
        if (omsSecret == null || omsSecret.Length != 32)
        {
            // Create oms exporter secret
            omsSecret = m_client.GetOMSExporterSecret();
        }
        if (omsSecret == null)
        {
            log.LogDebug("S7CommPlusConnection - Legitimate: OMS exporter secret unavailable!");
            m_client.Disconnect();
            return S7Consts.errIsoInvalidPDU;
        }
        // Roll key
        var key = LegitimationCrypto.Sha256(omsSecret);
        omsSecret = key;

        if (challenge.Length < 16)
        {
            log.LogDebug("S7CommPlusConnection - Legitimate: challenge too short for IV (< 16 bytes)!");
            m_client.Disconnect();
            return S7Consts.errIsoInvalidPDU;
        }
        // Use the first 16 bytes of the challenge as iv
        var iv = new ArraySegment<byte>(challenge, 0, 16).ToArray();
        // Encrypt
        challengeResponse = LegitimationCrypto.EncryptAesCbc(BuildLegitimationPayload(password, username), key, iv);

        // Send challengeResponse
        var setVariableReq = new SetVariableRequest(ProtocolVersion.V2)
        {
            InObjectId = m_SessionId,
            SessionId = m_SessionId,
            Address = Ids.Legitimate,
            Value = new ValueBlob(0, challengeResponse)
        };
        res = SendS7plusFunctionObject(setVariableReq);
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

        var setVariableResponse = SetVariableResponse.DeserializeFromPdu(m_ReceivedPDU);
        if (setVariableResponse == null)
        {
            log.LogDebug("S7CommPlusConnection - Legitimate: setVariableResponse with Error!");
            m_client.Disconnect();
            return S7Consts.errIsoInvalidPDU;
        }
        // Check if the legitimation attempt was successful
        var errorCode = (short)setVariableResponse.ReturnValue;
        if (errorCode < 0)
        {
            log.LogDebug("S7CommPlusConnection - Legitimate: access denied");
            m_client.Disconnect();
            return S7Consts.errCliAccessDenied;
        }

        return 0;
    }

    /// <summary>
    /// Builds the legitimation payload from given username and password.
    /// If username is empty the payload for a legacy login will be build.
    /// If username is not empty the payload for the new login is build.
    /// </summary>
    /// <param name="password">PLC password</param>
    /// <param name="username">PLC username (optional)</param>
    /// <returns>Build payload</returns>
    private static byte[] BuildLegitimationPayload(string password, string username = "")
    {
        var payload = new ValueStruct(Ids.LID_LegitimationPayloadStruct);
        if (!string.IsNullOrEmpty(username))
        {
            // Login with username and password = new login
            payload.AddStructElement(Ids.LID_LegitimationPayloadType, new ValueUDInt(LegitimationType.New));
            payload.AddStructElement(Ids.LID_LegitimationPayloadUsername, new ValueBlob(0, Encoding.UTF8.GetBytes(username)));
            payload.AddStructElement(Ids.LID_LegitimationPayloadPassword, new ValueBlob(0, Encoding.UTF8.GetBytes(password)));

        }
        else
        {
            // Login with only password = legacy login
            // Hash password
            byte[] hashedPw;
            hashedPw = SHA1.HashData(Encoding.UTF8.GetBytes(password));

            payload.AddStructElement(Ids.LID_LegitimationPayloadType, new ValueUDInt(LegitimationType.Legacy));
            payload.AddStructElement(Ids.LID_LegitimationPayloadUsername, new ValueBlob(0, Encoding.UTF8.GetBytes(username)));
            payload.AddStructElement(Ids.LID_LegitimationPayloadPassword, new ValueBlob(0, hashedPw));
        }
        using var memStr = new MemoryStream();
        payload.Serialize(memStr);
        return memStr.ToArray();
    }

    /// <summary>
    /// Legitimate using the old legacy login (firmware version &lt; 3.1)
    /// </summary>
    /// <param name="password">PLC password</param>
    /// <param name="ct">cancellation token</param>
    /// <returns>error code (0 = OK)</returns>
    private async Task<int> LegitimateLegacyAsync(string password, CancellationToken ct = default)
    {

        // Get challenge
        var getVarSubstreamedReq_challange = new GetVarSubstreamedRequest(ProtocolVersion.V2)
        {
            InObjectId = m_SessionId,
            SessionId = m_SessionId,
            Address = Ids.ServerSessionRequest
        };
        var res = SendS7plusFunctionObject(getVarSubstreamedReq_challange);
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

        var getVarSubstreamedRes_challenge = GetVarSubstreamedResponse.DeserializeFromPdu(m_ReceivedPDU);
        if (getVarSubstreamedRes_challenge == null)
        {
            log.LogDebug("S7CommPlusConnection - Legitimate: getVarSubstreamedRes_challenge with Error!");
            m_client.Disconnect();
            return S7Consts.errIsoInvalidPDU;
        }

        var challenge = (getVarSubstreamedRes_challenge.Value as ValueUSIntArray)!.Value;

        // Calculate challengeResponse [sha1(password) xor challenge]
        byte[] challengeResponse;
        challengeResponse = SHA1.HashData(Encoding.UTF8.GetBytes(password));
        if (challengeResponse.Length != challenge.Length)
        {
            log.LogDebug("S7CommPlusConnection - Legitimate: challengeResponse.Length != challenge.Length");
            m_client.Disconnect();
            return S7Consts.errIsoInvalidPDU;
        }
        for (var i = 0; i < challengeResponse.Length; ++i)
        {
            challengeResponse[i] = (byte)(challengeResponse[i] ^ challenge[i]);
        }

        // Send challengeResponse
        var setVariableReq = new SetVariableRequest(ProtocolVersion.V2)
        {
            InObjectId = m_SessionId,
            SessionId = m_SessionId,
            Address = Ids.ServerSessionResponse,
            Value = new ValueUSIntArray(challengeResponse)
        };
        res = SendS7plusFunctionObject(setVariableReq);
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

        var setVariableResponse = SetVariableResponse.DeserializeFromPdu(m_ReceivedPDU);
        if (setVariableResponse == null)
        {
            log.LogDebug("S7CommPlusConnection - Legitimate: setVariableResponse with Error!");
            m_client.Disconnect();
            return S7Consts.errIsoInvalidPDU;
        }
        // Check if the legitimation attempt was successful
        var errorCode = (short)setVariableResponse.ReturnValue;
        if (errorCode < 0)
        {
            log.LogDebug("S7CommPlusConnection - Legitimate: access denied");
            m_client.Disconnect();
            return S7Consts.errCliAccessDenied;
        }

        return 0;
    }

    [GeneratedRegex(@"^[^;]*;[^;]*[17]\s?(\d{3}).*;[VS](\d{1,2}\.\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex IsVersionedRecord { get; }
}
