<!--
  Copyright 2026 Julien Bombled

  Licensed under the Apache License, Version 2.0 (the "License");
  you may not use this file except in compliance with the License.
  You may obtain a copy of the License at

      http://www.apache.org/licenses/LICENSE-2.0
-->

# Heimdall.Next

Secure Windows connection manager for RDP, SSH, and SFTP. Built with .NET 10 and WPF.

> Ground-up rewrite of the original [Heimdall](../RDPManager) (PowerShell 5.1) as a modern, high-performance desktop application.

<!-- TODO: Add screenshot -->

## Features

### Remote Desktop (RDP)

- Embedded sessions via ActiveX MsTscAx in tabbed interface
- External sessions via mstsc.exe
- Credential autofill for both modes (CredUI + UI Automation, EnumThreadWindows)
- Dynamic resolution resize with stabilization guard
- Aspect ratio management
- Anti-idle (SetThreadExecutionState prevents Windows sleep during sessions)

### SSH Terminal

- Embedded terminal via WebView2 + xterm.js (full VT100/xterm rendering)
- Pipe mode transport (NOT ConPTY) for correct arrow keys, colors, and escape sequences
- Plink backend with `-t` flag for remote PTY allocation
- Pageant agent integration (native IPC via shared memory + WM_COPYDATA)
- SSH keepalive heartbeat (CR every 30s for TMOUT prevention)
- TOFU host key verification with persistent fingerprint store

### SSH Tunneling

- SSH.NET in-process tunnels (no process spawn overhead)
- Plink fallback for Pageant-only authentication
- Multi-gateway chaining with circular dependency detection
- 25 structured failure codes with targeted i18n error messages
- Pre-flight auth checks (Pageant detection, key file validation)

### SFTP Browser

- SSH.NET native SFTP engine (no psftp process)
- Remote file editor with FileSystemWatcher auto-upload
- Path escaping for special characters
- Engine complete, UI panel pending

### Import & Migration

- Migration from legacy Heimdall v1 (DPAPI-encrypted credentials)
- Import from MobaXterm, mRemoteNG, CSV (engine ready)

### User Interface

- Dark and Light themes (1,700+ lines of WPF control styles)
- TreeView hierarchy: Project > Group > Server with merged status dots
- Tabbed session management with drag-and-drop reordering
- Server dialog with 5 tabs: Connection, Tunneling, Authentication, Options, Info
- Context menus per node type (server, group, project)
- Fullscreen mode (F11) with exit icon
- Toggle sidebar (Ctrl+B)
- Keyboard shortcuts: Ctrl+N (new), Ctrl+E (edit), Delete, Ctrl+F (filter), F11
- Windows sleep prevention during active sessions

### Security

- DPAPI encryption + HMAC-SHA256 integrity for stored credentials
- PBKDF2-SHA256 PIN hashing
- File ACL enforcement (current user + Administrators + SYSTEM)
- Input validation against injection patterns
- Secure file writes (UTF-8 without BOM)

### Internationalization

- Bilingual interface: English and French
- 1,412 localization keys
- JSON-based locale files

### Operations

- File logging to `logs/heimdall_YYYYMMDD.log`
- Self-contained portable builds (no installer required)
- 218 unit tests (xUnit)

## Requirements

| Dependency | Minimum Version |
|---|---|
| Windows | 10 / 11 |
| .NET Runtime | 10.0 (bundled in portable build) |
| WebView2 Runtime | Evergreen (for SSH terminal) |
| PuTTY (Plink + Pageant) | 0.81+ |

## Installation

Download the latest portable build and run `Heimdall.Next.exe` from the extracted folder. No installation required.

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

## Architecture

The solution is split into 6 projects with clear dependency boundaries:

```
Heimdall.App          WPF application (MVVM, views, themes, services)
  +-- Heimdall.Core     Models, security (DPAPI, HMAC, PIN), config, state machine, i18n
  +-- Heimdall.Ssh      SSH engine (SSH.NET), tunnels, Pageant IPC, TOFU, failure classifier
  +-- Heimdall.Rdp      RDP engine (ActiveX MsTscAx), credential autofill, aspect ratio
  +-- Heimdall.Sftp     SFTP browser (SSH.NET), remote file editing, path escaping
  +-- Heimdall.Terminal  Terminal sessions (pipe mode, ConPTY), smart paste guard
```

Test projects: `Heimdall.Core.Tests`, `Heimdall.Ssh.Tests` (xUnit + Moq + FluentAssertions).

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for detailed design decisions.

## License

Copyright 2026 Julien Bombled

Licensed under the Apache License, Version 2.0. See [LICENSE](LICENSE) for details.
