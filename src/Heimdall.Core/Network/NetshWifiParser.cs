/*
 * Copyright 2026 Julien Bombled
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.Globalization;

namespace Heimdall.Core.Network;

/// <summary>
/// Parses <c>netsh wlan show networks mode=bssid</c> output into Wi-Fi entries.
/// </summary>
public static class NetshWifiParser
{
    public static IReadOnlyList<WifiEntry> Parse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var results = new List<WifiEntry>();

        var currentSsid = string.Empty;
        var currentAuth = string.Empty;
        var currentEncryption = string.Empty;

        var currentBssid = string.Empty;
        var currentSignal = string.Empty;
        var currentSignalValue = 0;
        var currentRadioType = string.Empty;
        var currentChannel = string.Empty;
        var inBssid = false;

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase))
            {
                FlushCurrent(results, currentSsid, currentBssid, currentSignal, currentSignalValue, currentChannel, currentAuth, currentEncryption, currentRadioType, inBssid);

                currentSsid = ExtractValue(line);
                currentAuth = string.Empty;
                currentEncryption = string.Empty;
                currentBssid = string.Empty;
                currentSignal = string.Empty;
                currentSignalValue = 0;
                currentRadioType = string.Empty;
                currentChannel = string.Empty;
                inBssid = false;
                continue;
            }

            if (StartsWithAny(line, "Network type", "Type de r"))
            {
                continue;
            }

            if (StartsWithAny(line, "Authentication", "Authentification"))
            {
                currentAuth = ExtractValue(line);
                continue;
            }

            if (StartsWithAny(line, "Encryption", "Chiffrement"))
            {
                currentEncryption = ExtractValue(line);
                continue;
            }

            if (line.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase))
            {
                FlushCurrent(results, currentSsid, currentBssid, currentSignal, currentSignalValue, currentChannel, currentAuth, currentEncryption, currentRadioType, inBssid);

                currentBssid = ExtractValue(line);
                currentSignal = string.Empty;
                currentSignalValue = 0;
                currentRadioType = string.Empty;
                currentChannel = string.Empty;
                inBssid = true;
                continue;
            }

            if (!inBssid)
            {
                continue;
            }

            if (StartsWithAny(line, "Signal"))
            {
                currentSignal = ExtractValue(line);
                var numericPart = currentSignal.Replace("%", string.Empty, StringComparison.Ordinal).Trim();
                int.TryParse(numericPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out currentSignalValue);
                continue;
            }

            if (StartsWithAny(line, "Radio type", "Type de radio"))
            {
                currentRadioType = ExtractValue(line);
                continue;
            }

            if (StartsWithAny(line, "Channel", "Canal"))
            {
                currentChannel = ExtractValue(line);
            }
        }

        FlushCurrent(results, currentSsid, currentBssid, currentSignal, currentSignalValue, currentChannel, currentAuth, currentEncryption, currentRadioType, inBssid);
        return results;
    }

    private static void FlushCurrent(
        List<WifiEntry> results,
        string currentSsid,
        string currentBssid,
        string currentSignal,
        int currentSignalValue,
        string currentChannel,
        string currentAuth,
        string currentEncryption,
        string currentRadioType,
        bool inBssid)
    {
        if (!inBssid || string.IsNullOrWhiteSpace(currentBssid))
        {
            return;
        }

        results.Add(new WifiEntry(
            currentSsid,
            currentBssid,
            currentSignal,
            currentSignalValue,
            currentChannel,
            currentAuth,
            currentEncryption,
            currentRadioType));
    }

    private static string ExtractValue(string line)
    {
        var colonIndex = line.IndexOf(':');
        return colonIndex >= 0 ? line[(colonIndex + 1)..].Trim() : string.Empty;
    }

    private static bool StartsWithAny(string line, params string[] fieldNames)
    {
        foreach (var name in fieldNames)
        {
            if (line.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
