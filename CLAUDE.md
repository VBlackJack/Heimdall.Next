# CLAUDE.md

This file provides guidance to Claude Code when working with code in this repository.

## Project Overview

**Heimdall.Next** is a ground-up rewrite of Heimdall (PowerShell 5.1 + WPF) as a modern .NET 10 + WPF application. It is a secure Windows RDP/SSH/SFTP connection manager designed to be a MobaXterm alternative with superior security and modern UX.

## Repository Layout

- **Working directory**: `G:\_dev\SnapConnect\Heimdall.Next`
- **Solution file**: `Heimdall.sln`
- **Legacy project**: `G:\_dev\SnapConnect\RDPManager` (PowerShell version, maintained in parallel)

## Architecture

```
Heimdall.Next/
├── src/
│   ├── Heimdall.Core/          # Shared models, security, config, state machine, i18n
│   ├── Heimdall.Ssh/           # SSH engine (SSH.NET), tunnels, TOFU, failure classifier
│   ├── Heimdall.Rdp/           # RDP engine (ActiveX), credential autofill, aspect ratio
│   ├── Heimdall.Sftp/          # SFTP browser (SSH.NET), remote editing, sudo transfers
│   ├── Heimdall.Terminal/      # Terminal engine (ConPTY/Microsoft.Terminal.Control)
│   └── Heimdall.App/           # WPF application (MVVM, views, themes)
├── tests/
│   ├── Heimdall.Core.Tests/    # xUnit tests for core library
│   └── Heimdall.Ssh.Tests/     # xUnit tests for SSH engine
└── config/
    ├── settings.default.json   # Factory defaults
    └── servers.default.json    # Server template
```

## Technology Stack

- **.NET 10** (C# 14) — full async/await, modern language features
- **WPF** — Windows Presentation Foundation for desktop UI
- **CommunityToolkit.Mvvm** — MVVM framework
- **SSH.NET** — Native SSH/SFTP engine (replaces Plink)
- **Microsoft.Terminal.Control** — Native terminal (ConPTY, GPU-rendered)
- **System.Text.Json** — JSON serialization
- **xUnit + Moq + FluentAssertions** — Testing

## Key Design Decisions

### SSH Engine: SSH.NET (not Plink)
- Programmatic auth control (events, callbacks) instead of stderr parsing
- In-process tunnels (no process spawn overhead)
- Native SFTP API (no psftp process)
- Plink kept only as legacy fallback for import/compatibility

### Terminal: Microsoft.Terminal.Control (not WebView2+xterm.js)
- Native ConPTY integration
- GPU-rendered terminal (DirectX)
- No WebView2 dependency
- Fallback to WebView2+xterm.js if Terminal.Control unavailable

### RDP: ActiveX MsTscAx (same as legacy)
- WindowsFormsHost in WPF
- COM event sinks (cleaned up with proper async)
- Credential autofill via EnumWindows + CredUI injection

## Build & Test

```bash
dotnet build
dotnet test
dotnet run --project src/Heimdall.App
```

## Code Standards

- **License header**: Apache 2.0 with author "Julien Bombled" on all new files
- **Language**: All code, comments, and documentation in English
- **No hardcoding**: URLs, paths, magic numbers go in config; strings go in i18n resources
- **Async by default**: Use async/await everywhere, no blocking calls on UI thread
- **MVVM**: Views in XAML, logic in ViewModels, no code-behind except minimal event wiring
- **Nullable reference types**: Enabled project-wide

## Migration from Legacy Heimdall

- Config format compatible (same JSON schema for settings.json / servers.json)
- Credential migration tool reads DPAPI-encrypted fields from legacy config
- Import/export supports same formats (JSON, CSV, MobaXterm, mRemoteNG)
- All 78+ functions from legacy have mapped equivalents in the new architecture
