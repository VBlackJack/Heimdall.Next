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

using System.IO;
using Heimdall.Core.Configuration;
using Heimdall.Core.Models;
using Heimdall.Core.Security;

namespace Heimdall.App.Services;

public partial class ConnectionService
{
    /// <summary>
    /// Establishes an RDP connection, optionally through an SSH tunnel.
    /// Returns a result containing the tunnel local port (for embedded RDP)
    /// or null on failure.
    /// </summary>
    public async Task<ConnectionResult> ConnectRdpAsync(
        ServerProfileDto server,
        AppSettings settings,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(settings);

        Core.Logging.FileLogger.Info($"ConnectRdpAsync: {server.DisplayName} ({server.RemoteServer}:{server.RemotePort}) Gateway={server.SshGatewayId ?? "none"} Direct={server.UseDirectConnection}");
        _connectionSm.TryTransition(server.Id, Core.Models.ConnectionState.ValidatingConfig);

        // Resolve tunnel if gateway is configured and not a direct connection
        int tunnelLocalPort = server.LocalPort;
        if (!server.UseDirectConnection && !string.IsNullOrEmpty(server.SshGatewayId))
        {
            var tunnelResult = await EstablishTunnelAsync(
                server.Id, server.SshGatewayId, server.RemoteServer,
                server.RemotePort, server.LocalPort, settings, ct)
                .ConfigureAwait(false);

            if (!tunnelResult.Success)
            {
                return new ConnectionResult(false, tunnelResult.ErrorMessage, null);
            }

            // Use the dynamically allocated port (may differ from server.LocalPort)
            tunnelLocalPort = tunnelResult.Tunnel?.LocalPort ?? server.LocalPort;
        }

        _connectionSm.TryTransition(server.Id, Core.Models.ConnectionState.LaunchingRdp);

        var rdpMode = server.RdpMode ?? "External";
        Core.Logging.FileLogger.Info($"RDP mode: {rdpMode}");

        if (string.Equals(rdpMode, "Embedded", StringComparison.OrdinalIgnoreCase))
        {
            // Embedded RDP will be handled by the View layer (ActiveX in WindowsFormsHost).
            // Return the server DTO so the ViewModel can create an embedded session tab.
            return new ConnectionResult(true, null, new RdpSessionResult(server));
        }

        // External mode: launch mstsc.exe
        string? rdpPassword = null;
        try
        {
            var rdpHost = server.UseDirectConnection ? server.RemoteServer : "127.0.0.1";
            var rdpPort = server.UseDirectConnection ? server.RemotePort : tunnelLocalPort;

            // Store credentials in Windows Credential Manager for mstsc auto-login
            if (!string.IsNullOrEmpty(server.RdpUsername) && !string.IsNullOrEmpty(server.RdpPasswordEncrypted))
            {
                try
                {
                    rdpPassword = CredentialProtector.Unprotect(server.RdpPasswordEncrypted);
                    if (rdpPassword is null)
                        throw new InvalidOperationException("Failed to decrypt RDP password.");
                    var credTarget = $"TERMSRV/{rdpHost}";
                    Heimdall.Rdp.CredentialManagerHelper.WriteDomainCredential(credTarget, server.RdpUsername, rdpPassword, out _);
                    Core.Logging.FileLogger.Info($"RDP credentials stored for {credTarget}");
                }
                catch (Exception credEx)
                {
                    Core.Logging.FileLogger.Warn($"Failed to store RDP credentials: {credEx.Message}");
                }
            }

            // Generate and launch .rdp file with restrictive ACL
            var rdpFile = Path.Combine(Path.GetTempPath(), $"heimdall_{server.Id}_{Guid.NewGuid():N}.rdp");
            var rdpContent = Heimdall.Rdp.RdpFileGenerator.Generate(new Heimdall.Rdp.RdpFileOptions
            {
                Host = rdpHost,
                Port = rdpPort,
                Username = server.RdpUsername,
                FullScreen = false,
                AdminMode = false,
                GatewayHostname = server.RdpGateway,
                Redirections = new Heimdall.Rdp.RdpRedirectionOptions
                {
                    Clipboard = server.RdpRedirectClipboard,
                    Drives = server.RdpRedirectDrives,
                    Printers = server.RdpRedirectPrinters,
                    ComPorts = server.RdpRedirectComPorts,
                    SmartCards = server.RdpRedirectSmartCards,
                    Webcam = server.RdpRedirectWebcam,
                    Usb = server.RdpRedirectUsb,
                    AudioMode = server.RdpAudioMode,
                    AudioCapture = server.RdpAudioCapture,
                    MultiMonitor = server.RdpMultiMonitor,
                    DynamicResolution = server.RdpDynamicResolution,
                    Nla = server.RdpNla,
                    BitmapCaching = server.RdpBitmapCaching,
                    Compression = server.RdpCompression,
                    AutoReconnect = server.RdpAutoReconnect
                }
            });
            await File.WriteAllTextAsync(rdpFile, rdpContent, ct).ConfigureAwait(false);

            // Restrict .rdp file ACL — contains connection metadata (no password,
            // but host/user/redirections are sensitive in enterprise environments)
            try { AclEnforcer.SetFileAcl(rdpFile); }
            catch (Exception aclEx)
            {
                Core.Logging.FileLogger.Warn($"Failed to set ACL on .rdp file: {aclEx.Message}");
            }

            var mstscProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "mstsc.exe",
                Arguments = $"\"{rdpFile}\"",
                UseShellExecute = false
            });

            var mstscPid = mstscProcess?.Id ?? 0;
            Core.Logging.FileLogger.Info($"Launched mstsc.exe PID={mstscPid} for {server.DisplayName} ({rdpHost}:{rdpPort})");
            _connectionSm.TryTransition(server.Id, Core.Models.ConnectionState.Connected);

            // Start CredUI autofill watcher for the external mstsc process
            if (!string.IsNullOrEmpty(rdpPassword) && mstscPid > 0)
            {
                var autofillPassword = rdpPassword;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var filled = await Heimdall.Rdp.CredentialAutofill.WaitAndFillAsync(
                            mstscPid, rdpHost, autofillPassword,
                            TimeSpan.FromSeconds(90), ct).ConfigureAwait(false);
                        if (!filled)
                            Core.Logging.FileLogger.Warn($"External RDP CredUI autofill timed out for {server.DisplayName}");
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when connection is cancelled during autofill wait
                    }
                    catch (Exception ex)
                    {
                        Core.Logging.FileLogger.Warn($"External RDP CredUI autofill failed: {ex.Message}");
                    }
                }, ct);
            }

            // Clean up .rdp file and CredMan entry after delay
            var credCleanupTarget = !string.IsNullOrEmpty(server.RdpUsername) && !string.IsNullOrEmpty(server.RdpPasswordEncrypted)
                ? $"TERMSRV/{rdpHost}" : null;
            _ = CleanupRdpArtifactsAsync(rdpFile, credCleanupTarget, ct);

            return new ConnectionResult(true, null, null);
        }
        catch (Exception ex)
        {
            Core.Logging.FileLogger.Error("RDP launch failed", ex);
            return new ConnectionResult(false, ex.Message, null);
        }
        finally
        {
            // Clear plaintext password from managed memory deterministically
            rdpPassword = null;
        }
    }

    /// <summary>
    /// Cleans up the temporary .rdp file and CredMan entry after a delay.
    /// </summary>
    private static async Task CleanupRdpArtifactsAsync(
        string rdpFile, string? credCleanupTarget, CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Proceed with cleanup even if cancelled
        }

        try { File.Delete(rdpFile); }
        catch (IOException) { /* Best-effort cleanup */ }

        if (credCleanupTarget is not null)
        {
            Heimdall.Rdp.CredentialManagerHelper.DeleteCredential(credCleanupTarget, out _);
            Core.Logging.FileLogger.Info($"RDP CredMan entry cleaned: {credCleanupTarget}");
        }
    }
}
