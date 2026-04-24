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

using Heimdall.Ssh.Agents;
using Renci.SshNet;

namespace Heimdall.Ssh.Pageant;

/// <summary>
/// Wraps a Pageant-loaded SSH key as an <see cref="IPrivateKeySource"/> for SSH.NET.
/// Compatibility wrapper kept for code that still constructs Pageant-specific
/// key sources directly. New code should use <see cref="SshAgentKeyWrapper"/>.
/// </summary>
/// <remarks>
/// Signing is delegated through <see cref="PageantAgentKey"/>, so the private
/// key material remains inside Pageant.
/// </remarks>
internal sealed class PageantKeyWrapper : SshAgentKeyWrapper, IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Creates a wrapper for the specified Pageant key.
    /// </summary>
    /// <param name="key">Pageant key identity (blob, comment, type).</param>
    public PageantKeyWrapper(PageantKey key)
        : base(new PageantAgentKey(key))
    {
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}
