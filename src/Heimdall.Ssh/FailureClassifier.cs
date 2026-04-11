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

using System.Net.Sockets;
using Renci.SshNet.Common;

namespace Heimdall.Ssh;

/// <summary>
/// Structured information about an SSH failure.
/// </summary>
/// <param name="Code">Classified failure code.</param>
/// <param name="Message">Human-readable description.</param>
/// <param name="IsFatal">Whether the connection should be aborted.</param>
/// <param name="OriginalException">The original exception, if any.</param>
public record SshFailureInfo(
    SshFailureCode Code,
    string Message,
    bool IsFatal,
    Exception? OriginalException = null);

/// <summary>
/// Classifies SSH.NET exceptions into structured <see cref="SshFailureInfo"/> records.
/// Replaces the legacy stderr regex parsing approach with typed exception analysis.
/// </summary>
public static class FailureClassifier
{
    /// <summary>
    /// Classify an exception into a structured failure code.
    /// SSH.NET provides typed exceptions for auth, connection, timeout, and proxy errors.
    /// </summary>
    /// <param name="ex">The exception to classify.</param>
    /// <param name="connectionParams">Optional connection params for context-aware classification.</param>
    /// <returns>Structured failure information.</returns>
    public static SshFailureInfo Classify(Exception ex, SshConnectionParams? connectionParams = null)
    {
        ArgumentNullException.ThrowIfNull(ex);

        return ex switch
        {
            SshAuthenticationException authEx =>
                ClassifyAuthException(authEx, connectionParams),

            SshConnectionException connEx =>
                ClassifyConnectionException(connEx),

            SshOperationTimeoutException =>
                new SshFailureInfo(SshFailureCode.NetworkTimedOut, "Connection timed out.", true, ex),

            ProxyException proxyEx =>
                new SshFailureInfo(SshFailureCode.ForwardingFailed, $"Proxy error: {proxyEx.Message}", true, ex),

            SocketException socketEx =>
                ClassifySocketException(socketEx),

            IOException ioEx when ioEx.InnerException is SocketException inner =>
                ClassifySocketException(inner),

            OperationCanceledException =>
                new SshFailureInfo(SshFailureCode.AuthTimeout, "Connection cancelled.", false, ex),

            _ => new SshFailureInfo(SshFailureCode.Unknown, ex.Message, true, ex)
        };
    }

    /// <summary>
    /// Format a failure info into a user-facing message, using a localizer function.
    /// Falls back to the raw message if no localized string is found.
    /// </summary>
    /// <param name="info">The failure information to format.</param>
    /// <param name="localizer">Function that maps i18n keys to localized strings (returns null if not found).</param>
    /// <param name="gatewayName">Optional gateway name prefix for multi-hop context.</param>
    /// <returns>Formatted message string.</returns>
    public static string FormatMessage(SshFailureInfo info, Func<string, string?> localizer, string? gatewayName = null)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(localizer);

        var prefix = gatewayName is not null ? $"{gatewayName}: " : "";
        var key = $"ErrorSsh{info.Code}";
        var localized = localizer(key);
        return prefix + (localized ?? info.Message);
    }

    private static SshFailureInfo ClassifyAuthException(
        SshAuthenticationException ex,
        SshConnectionParams? connectionParams)
    {
        var msg = ex.Message ?? "";

        if (msg.Contains("key", StringComparison.OrdinalIgnoreCase))
            return new SshFailureInfo(SshFailureCode.KeyRejected, "Server rejected the SSH key.", true, ex);

        if (msg.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("denied", StringComparison.OrdinalIgnoreCase))
        {
            if (connectionParams?.Password is not null)
                return new SshFailureInfo(SshFailureCode.PasswordRejected, "SSH password was rejected.", true, ex);

            return new SshFailureInfo(SshFailureCode.AuthRejected, "Authentication was rejected.", true, ex);
        }

        if (msg.Contains("too many", StringComparison.OrdinalIgnoreCase))
            return new SshFailureInfo(SshFailureCode.TooManyAuthFailures, "Too many auth failures.", true, ex);

        // Server requires keyboard-interactive but no password was supplied.
        // This happens when PasswordAuthentication is disabled server-side and the
        // KeyboardInteractiveAuthenticationMethod has no response for the prompt.
        if (msg.Contains("keyboard-interactive", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(connectionParams?.Password))
        {
            return new SshFailureInfo(
                SshFailureCode.KeyboardInteractiveNoPassword,
                "Server requires keyboard-interactive authentication, but no password was provided.",
                true,
                ex);
        }

        return new SshFailureInfo(SshFailureCode.NoSupportedAuth, "No supported authentication method.", true, ex);
    }

    private static SshFailureInfo ClassifyConnectionException(
        SshConnectionException ex)
    {
        var msg = ex.Message ?? "";

        if (msg.Contains("refused", StringComparison.OrdinalIgnoreCase))
            return new SshFailureInfo(SshFailureCode.NetworkRefused, "Connection refused.", true, ex);

        if (msg.Contains("reset", StringComparison.OrdinalIgnoreCase))
            return new SshFailureInfo(SshFailureCode.NetworkReset, "Connection reset.", true, ex);

        if (msg.Contains("protocol", StringComparison.OrdinalIgnoreCase))
            return new SshFailureInfo(SshFailureCode.ProtocolError, "SSH protocol error.", true, ex);

        return new SshFailureInfo(SshFailureCode.Unknown, msg, true, ex);
    }

    private static SshFailureInfo ClassifySocketException(SocketException ex)
    {
        return ex.SocketErrorCode switch
        {
            SocketError.ConnectionRefused =>
                new SshFailureInfo(SshFailureCode.NetworkRefused, "Connection refused.", true, ex),

            SocketError.TimedOut =>
                new SshFailureInfo(SshFailureCode.NetworkTimedOut, "Connection timed out.", true, ex),

            SocketError.ConnectionReset =>
                new SshFailureInfo(SshFailureCode.NetworkReset, "Connection reset.", true, ex),

            SocketError.HostUnreachable or SocketError.NetworkUnreachable =>
                new SshFailureInfo(SshFailureCode.NetworkUnreachable, "Host unreachable.", true, ex),

            _ => new SshFailureInfo(SshFailureCode.Unknown, ex.Message, true, ex)
        };
    }
}
