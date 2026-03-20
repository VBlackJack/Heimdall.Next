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

namespace Heimdall.Ssh.Tests;

public class TunnelManagerTests : IDisposable
{
    private readonly TunnelManager _manager = new();

    public void Dispose()
    {
        _manager.Dispose();
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static TunnelInfo MakeInfo(int localPort, string server = "gw.example.com")
    {
        return new TunnelInfo(
            ServerName: server,
            LocalPort: localPort,
            RemoteHost: "target.internal",
            RemotePort: 3389,
            StartedAt: DateTime.UtcNow,
            IsAlive: true);
    }

    private sealed class FakeHandle : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private bool RegisterFake(int localPort, FakeHandle? handle = null, Func<bool>? isAlive = null)
    {
        return _manager.TryRegisterExternalTunnel(
            MakeInfo(localPort),
            handle ?? new FakeHandle(),
            isAlive ?? (() => true));
    }

    // ── Initial state ─────────────────────────────────────────────────

    [Fact]
    public void NewManager_HasNoActiveTunnels()
    {
        Assert.Empty(_manager.GetActiveTunnels());
    }

    [Fact]
    public void HasTunnel_ReturnsFalse_ForUntrackedPort()
    {
        Assert.False(_manager.HasTunnel(12345));
    }

    // ── TryRegisterExternalTunnel ─────────────────────────────────────

    [Fact]
    public void TryRegisterExternalTunnel_Succeeds_ForNewPort()
    {
        var result = RegisterFake(10001);

        Assert.True(result);
        Assert.True(_manager.HasTunnel(10001));
    }

    [Fact]
    public void TryRegisterExternalTunnel_ReturnsFalse_ForDuplicatePort()
    {
        RegisterFake(10001);

        var result = RegisterFake(10001);

        Assert.False(result);
    }

    [Fact]
    public void TryRegisterExternalTunnel_DisposesHandle_OnDuplicatePort()
    {
        RegisterFake(10001);
        var duplicateHandle = new FakeHandle();

        _manager.TryRegisterExternalTunnel(MakeInfo(10001), duplicateHandle, () => true);

        Assert.True(duplicateHandle.Disposed);
    }

    [Fact]
    public void TryRegisterExternalTunnel_ThrowsArgumentNull_ForNullInfo()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _manager.TryRegisterExternalTunnel(null!, new FakeHandle(), () => true));
    }

    [Fact]
    public void TryRegisterExternalTunnel_ThrowsArgumentNull_ForNullHandle()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _manager.TryRegisterExternalTunnel(MakeInfo(10001), null!, () => true));
    }

    [Fact]
    public void TryRegisterExternalTunnel_ThrowsArgumentNull_ForNullIsAlive()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _manager.TryRegisterExternalTunnel(MakeInfo(10001), new FakeHandle(), null!));
    }

    [Fact]
    public void TryRegisterExternalTunnel_ThrowsObjectDisposed_AfterDispose()
    {
        _manager.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            _manager.TryRegisterExternalTunnel(MakeInfo(10001), new FakeHandle(), () => true));
    }

    // ── GetActiveTunnels ──────────────────────────────────────────────

    [Fact]
    public void GetActiveTunnels_ReturnsRegisteredExternalTunnels()
    {
        RegisterFake(10001, isAlive: () => true);
        RegisterFake(10002, isAlive: () => false);

        var tunnels = _manager.GetActiveTunnels();

        Assert.Equal(2, tunnels.Count);
    }

    [Fact]
    public void GetActiveTunnels_ReflectsIsAliveStatus()
    {
        var alive = true;
        RegisterFake(10001, isAlive: () => alive);

        var tunnels = _manager.GetActiveTunnels();
        Assert.True(tunnels[0].IsAlive);

        alive = false;
        tunnels = _manager.GetActiveTunnels();
        Assert.False(tunnels[0].IsAlive);
    }

    [Fact]
    public void GetActiveTunnels_ReturnsEmptyAfterCloseAll()
    {
        RegisterFake(10001);
        RegisterFake(10002);

        _manager.CloseAllTunnels();

        Assert.Empty(_manager.GetActiveTunnels());
    }

    // ── GetTunnel ─────────────────────────────────────────────────────

    [Fact]
    public void GetTunnel_ReturnsNull_ForUntrackedPort()
    {
        Assert.Null(_manager.GetTunnel(99999));
    }

    [Fact]
    public void GetTunnel_ReturnsInfo_ForRegisteredExternalTunnel()
    {
        RegisterFake(10001);

        var info = _manager.GetTunnel(10001);

        Assert.NotNull(info);
        Assert.Equal(10001, info.LocalPort);
        Assert.Equal("target.internal", info.RemoteHost);
    }

    // ── AddReference / ReleaseReference ───────────────────────────────

    [Fact]
    public void AddReference_IncrementsRefCount()
    {
        RegisterFake(10001);

        // Initial registration adds 1 ref. Adding another should keep the tunnel alive.
        _manager.AddReference(10001);

        // Release once: should return false (still 1 ref remaining)
        var shouldClose = _manager.ReleaseReference(10001);

        Assert.False(shouldClose);
        Assert.True(_manager.HasTunnel(10001));
    }

    [Fact]
    public void ReleaseReference_ReturnsTrueAndCloses_WhenLastRefReleased()
    {
        RegisterFake(10001);

        // Registration adds 1 ref. Release it.
        var shouldClose = _manager.ReleaseReference(10001);

        Assert.True(shouldClose);
        Assert.False(_manager.HasTunnel(10001));
    }

    [Fact]
    public void ReleaseReference_OnUntrackedPort_ReturnsTrue()
    {
        // Releasing a port that was never tracked should return true (count = 0)
        var result = _manager.ReleaseReference(55555);

        Assert.True(result);
    }

    [Fact]
    public void AddReference_MultipleIncrements_RequireMatchingReleases()
    {
        RegisterFake(10001);

        // Add 2 more refs (total 3 including initial)
        _manager.AddReference(10001);
        _manager.AddReference(10001);

        // Release 1: still 2 refs
        Assert.False(_manager.ReleaseReference(10001));
        Assert.True(_manager.HasTunnel(10001));

        // Release 2: still 1 ref
        Assert.False(_manager.ReleaseReference(10001));
        Assert.True(_manager.HasTunnel(10001));

        // Release 3: last ref, tunnel closed
        Assert.True(_manager.ReleaseReference(10001));
        Assert.False(_manager.HasTunnel(10001));
    }

    // ── CloseTunnel (with ref count) ──────────────────────────────────

    [Fact]
    public void CloseTunnel_DoesNotClose_WhenRefsRemain()
    {
        RegisterFake(10001);

        // Add an extra ref (total 2)
        _manager.AddReference(10001);

        // CloseTunnel checks ref count; tunnel should survive
        _manager.CloseTunnel(10001);

        Assert.True(_manager.HasTunnel(10001));
    }

    // ── ForceCloseTunnel ──────────────────────────────────────────────

    [Fact]
    public void ForceCloseTunnel_ClosesRegardlessOfRefCount()
    {
        RegisterFake(10001);
        _manager.AddReference(10001);
        _manager.AddReference(10001);

        _manager.ForceCloseTunnel(10001);

        Assert.False(_manager.HasTunnel(10001));
    }

    [Fact]
    public void ForceCloseTunnel_DisposesHandle()
    {
        var handle = new FakeHandle();
        RegisterFake(10001, handle: handle);

        _manager.ForceCloseTunnel(10001);

        Assert.True(handle.Disposed);
    }

    [Fact]
    public void ForceCloseTunnel_NoOp_ForUntrackedPort()
    {
        // Should not throw
        _manager.ForceCloseTunnel(99999);
    }

    // ── CloseAllTunnels ───────────────────────────────────────────────

    [Fact]
    public void CloseAllTunnels_ClosesAllRegisteredTunnels()
    {
        var handle1 = new FakeHandle();
        var handle2 = new FakeHandle();
        RegisterFake(10001, handle: handle1);
        RegisterFake(10002, handle: handle2);

        _manager.CloseAllTunnels();

        Assert.False(_manager.HasTunnel(10001));
        Assert.False(_manager.HasTunnel(10002));
        Assert.True(handle1.Disposed);
        Assert.True(handle2.Disposed);
    }

    [Fact]
    public void CloseAllTunnels_IgnoresRefCounts()
    {
        RegisterFake(10001);
        _manager.AddReference(10001);
        _manager.AddReference(10001);

        _manager.CloseAllTunnels();

        Assert.False(_manager.HasTunnel(10001));
    }

    [Fact]
    public void CloseAllTunnels_OnEmptyManager_DoesNotThrow()
    {
        _manager.CloseAllTunnels();

        Assert.Empty(_manager.GetActiveTunnels());
    }

    // ── Events ────────────────────────────────────────────────────────

    [Fact]
    public void TunnelOpened_FiresOnRegister()
    {
        TunnelInfo? receivedInfo = null;
        _manager.TunnelOpened += info => receivedInfo = info;

        RegisterFake(10001);

        Assert.NotNull(receivedInfo);
        Assert.Equal(10001, receivedInfo.LocalPort);
    }

    [Fact]
    public void TunnelClosed_FiresOnForceClose()
    {
        int? closedPort = null;
        string? closedError = null;
        _manager.TunnelClosed += (port, error) =>
        {
            closedPort = port;
            closedError = error;
        };

        RegisterFake(10001);
        _manager.ForceCloseTunnel(10001);

        Assert.Equal(10001, closedPort);
        Assert.Null(closedError);
    }

    [Fact]
    public void TunnelClosed_FiresOnReleaseReference_WhenLastRef()
    {
        int? closedPort = null;
        _manager.TunnelClosed += (port, _) => closedPort = port;

        RegisterFake(10001);
        _manager.ReleaseReference(10001);

        Assert.Equal(10001, closedPort);
    }

    [Fact]
    public void TunnelClosed_DoesNotFire_WhenRefsRemain()
    {
        bool fired = false;
        _manager.TunnelClosed += (_, _) => fired = true;

        RegisterFake(10001);
        _manager.AddReference(10001);

        _manager.ReleaseReference(10001);

        Assert.False(fired);
    }

    [Fact]
    public void TunnelClosed_FiresForEachTunnel_OnCloseAll()
    {
        var closedPorts = new List<int>();
        _manager.TunnelClosed += (port, _) => closedPorts.Add(port);

        RegisterFake(10001);
        RegisterFake(10002);

        _manager.CloseAllTunnels();

        Assert.Contains(10001, closedPorts);
        Assert.Contains(10002, closedPorts);
    }

    // ── Dispose ───────────────────────────────────────────────────────

    [Fact]
    public void Dispose_ClosesAllTunnels()
    {
        var handle = new FakeHandle();
        RegisterFake(10001, handle: handle);

        _manager.Dispose();

        Assert.True(handle.Disposed);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        RegisterFake(10001);

        _manager.Dispose();
        _manager.Dispose();
    }

    [Fact]
    public void Dispose_FiresTunnelClosedEvents()
    {
        var closedPorts = new List<int>();
        _manager.TunnelClosed += (port, _) => closedPorts.Add(port);

        RegisterFake(10001);
        RegisterFake(10002);

        _manager.Dispose();

        Assert.Contains(10001, closedPorts);
        Assert.Contains(10002, closedPorts);
    }

    // ── AllocatePort ──────────────────────────────────────────────────

    [Fact]
    public void AllocatePort_WithZero_ReturnsEphemeralPort()
    {
        int port = _manager.AllocatePort(0);

        Assert.True(port > 0);
    }

    [Fact]
    public void AllocatePort_TrackedPort_ReturnsAlternative()
    {
        RegisterFake(10001);

        int port = _manager.AllocatePort(10001);

        // Should return a different port since 10001 is tracked
        Assert.NotEqual(10001, port);
        Assert.True(port > 0);
    }

    // ── Multiple tunnels on different ports ────────────────────────────

    [Fact]
    public void MultipleTunnels_TrackedIndependently()
    {
        RegisterFake(10001);
        RegisterFake(10002);

        _manager.ForceCloseTunnel(10001);

        Assert.False(_manager.HasTunnel(10001));
        Assert.True(_manager.HasTunnel(10002));
    }

    [Fact]
    public void RefCounting_IndependentPerPort()
    {
        RegisterFake(10001);
        RegisterFake(10002);

        _manager.AddReference(10001);

        // Release 10002 (1 ref) -> should close
        Assert.True(_manager.ReleaseReference(10002));
        Assert.False(_manager.HasTunnel(10002));

        // 10001 still has 2 refs
        Assert.True(_manager.HasTunnel(10001));
    }
}
