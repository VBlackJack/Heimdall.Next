<!--
  Copyright 2026 Julien Bombled

  Licensed under the Apache License, Version 2.0 (the "License");
  you may not use this file except in compliance with the License.
  You may obtain a copy of the License at

      http://www.apache.org/licenses/LICENSE-2.0
-->

# Heimdall.Next

**The secure, all-in-one Windows connection manager for RDP, SSH, SFTP, Citrix, and local terminals.**

Built with .NET 10 and WPF. Designed as a modern, high-performance alternative to MobaXterm with enterprise-grade security.

<!-- Screenshot placeholder: ![Heimdall.Next](docs/screenshot.png) -->

---

## Why Heimdall.Next?

- **5 protocols, one interface** --- RDP, SSH, SFTP, Citrix, and local shell sessions in a single tabbed window
- **Zero-trust credential storage** --- DPAPI encryption + HMAC-SHA256 integrity, PBKDF2 PIN protection, Windows ACL enforcement
- **Pageant-native** --- Direct IPC with PuTTY Pageant via shared memory (no agent forwarding hacks)
- **Portable** --- Self-contained build with no installer required

---

## Features

### Remote Desktop (RDP)
- Embedded sessions via ActiveX MsTscAx in a tabbed interface
- External sessions via mstsc.exe with credential autofill
- Dynamic resolution resize with stabilization guard
- Aspect ratio management and anti-idle prevention
- Credential autofill for CredUI dialogs (EnumThreadWindows + UI Automation)

### SSH Terminal
- Embedded terminal via WebView2 + xterm.js (full VT100/xterm rendering)
- Pipe mode transport for correct arrow keys, colors, and escape sequences
- Pageant agent integration (native Win32 IPC via shared memory)
- SSH keepalive heartbeat (prevents TMOUT disconnects)
- TOFU host key verification with persistent fingerprint store
- Multi-gateway tunnel chaining with circular dependency detection
- 25 structured failure codes with localized error messages

### SFTP Browser
- Embedded file browser panel with directory tree and file list
- Remote file editor with AvalonEdit (syntax highlighting, auto-upload on save)
- Sudo edit: transparent fallback to `sudo cat`/`sudo tee` on permission denied
- Drag-and-drop upload and download
- Chmod dialog, path bookmarks, filename filter

### Citrix
- StoreBrowse integration for published applications and desktops
- StoreFront authentication and ICA file generation
- Embedded session tabs with the same UX as RDP

### Multi-Exec Broadcast
- Send keystrokes simultaneously to multiple active SSH sessions
- Per-session opt-in/opt-out broadcast control

### Quick Connect (Ctrl+K)
- Command palette for ad-hoc connections without saving a server profile
- Supports `user@host:port` format with optional protocol prefix
- Recent connection history for quick re-use

### Tunnel Panel
- Retractable side panel showing all active SSH tunnels
- Real-time status, local port, remote target, and gateway chain display
- Manual tunnel teardown without disconnecting the parent session

### User Interface
- Runtime Dark and Light theme switching (1,700+ lines of WPF control styles)
- TreeView hierarchy: Project > Group > Server with merged status dots
- Tabbed session management with drag-and-drop reordering
- Fullscreen mode (F11), toggle sidebar (Ctrl+B), filter (Ctrl+F)
- Bilingual interface: English and French (~1,730 i18n keys)

### Security
- DPAPI encryption + HMAC-SHA256 integrity for stored credentials
- PBKDF2-SHA256 PIN hashing (100,000 iterations)
- Windows ACL enforcement on config and log files
- Input validation against injection patterns (CWE-78)
- Secure file writes (UTF-8 without BOM)

### Import and Migration
- Migration from Heimdall v1 (DPAPI-encrypted credentials preserved)
- Import from MobaXterm, mRemoteNG, CSV

---

## Requirements

| Dependency | Minimum Version |
|---|---|
| Windows | 10 / 11 |
| .NET Runtime | 10.0 (bundled in portable build) |
| WebView2 Runtime | Evergreen (for SSH terminal) |
| PuTTY (Plink + Pageant) | 0.81+ |
| Citrix Workspace App | Latest (for Citrix connections only) |

---

## Quick Start

Download the latest portable build from the [Releases](../../releases) page, extract, and run `Heimdall.Next.exe`. No installation required.

---

## Build from Source

```bash
# Restore, build, and test
dotnet build
dotnet test

# Run in development
dotnet run --project src/Heimdall.App

# Portable build (Debug, auto-increments build number)
powershell -File Build.ps1

# Release build (portable + zip archive)
powershell -File Build.ps1 -Mode Release

# Skip tests
powershell -File Build.ps1 -SkipTests
```

Build output goes to `Dist/debug/` or `Dist/release/` with versioned folder names (`Heimdall.Next_build.YYYY.MMDDxx`).

---

## Technology Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10 (C# 14) |
| UI Framework | WPF (MVVM via CommunityToolkit.Mvvm) |
| Dependency Injection | Microsoft.Extensions.DependencyInjection |
| SSH/SFTP | SSH.NET 2025.1.0 |
| Terminal Rendering | WebView2 + xterm.js |
| Code Editor | AvalonEdit |
| RDP | ActiveX MsTscAx (WindowsFormsHost) |
| Citrix | StoreBrowse CLI integration |
| Crypto | System.Security.Cryptography.ProtectedData (DPAPI) |
| Testing | xUnit + Moq + FluentAssertions (385 tests) |
| Serialization | System.Text.Json |

---

## Architecture

The solution is split into 6 projects with clear dependency boundaries:

```
Heimdall.App          WPF application (MVVM, views, themes, services)
  +-- Heimdall.Core     Models, security (DPAPI, HMAC, PIN), config, state machine, i18n
  +-- Heimdall.Ssh      SSH engine (SSH.NET), tunnels, Pageant IPC, TOFU, failure classifier
  +-- Heimdall.Rdp      RDP + Citrix engine (ActiveX MsTscAx), credential autofill, StoreBrowse
  +-- Heimdall.Sftp     SFTP browser (SSH.NET), remote file editing, sudo fallback
  +-- Heimdall.Terminal  Terminal sessions (pipe mode, ConPTY), smart paste guard
```

Test projects: `Heimdall.Core.Tests`, `Heimdall.Ssh.Tests`.

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for detailed design decisions and data flow diagrams.

---

## License

Copyright 2026 Julien Bombled

Licensed under the Apache License, Version 2.0. See [LICENSE](LICENSE) for details.
