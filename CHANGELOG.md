<!--
  Copyright 2026 Julien Bombled

  Licensed under the Apache License, Version 2.0 (the "License");
  you may not use this file except in compliance with the License.
  You may obtain a copy of the License at

      http://www.apache.org/licenses/LICENSE-2.0
-->

# Changelog

All notable changes to Heimdall.Next are documented in this file.

## [v2026.032002] - 2026-03-20

### Security
- Remove password file TOCTOU fallback (fail hard if SecureFileWriter fails)
- Add Unix file mode 0600 on Plink password files
- Add XXE protection (DtdProcessing.Prohibit) on all XML importers
- Validate CitrixLaunchCommandLine against shell metacharacters
- Wrap async void event handlers with try-catch

### Performance
- Reduce Task.Wait() timeouts from 2-3s to 500ms (4-5x faster session close)
- Parallelize health monitor SSH commands via Task.WhenAll (3x faster)
- Increase health poll interval from 5s to 15s (66% less SSH traffic)
- Cache FolderViewModel.ServerCount with auto-invalidation

### Architecture
- Split ApplyLocalization() into 7 sub-methods
- Extract ImportConfigAsync() into 6 format-specific helpers
- Eliminate CloseAllSessions() code duplication
- Extract CredentialTarget record for credential resolution
- Replace all Debug.WriteLine with FileLogger (77 occurrences)
- Consolidate duplicate DefaultPorts constants
- Extract WebView2 message protocol constants
- Convert async void OpenFile() to async Task

### Tests
- Add 508 tests across 20 new test files (505 to 1013 total)
- Cover: CredentialProtector, DpapiProvider, SecureFileWriter, AclEnforcer
- Cover: RdcManImporter, MRemoteNgImporter, RdpFileImporter, SchemaValidator
- Cover: TunnelManager, RdpFileGenerator, AspectRatioManager
- Cover: LocalizationManager, FileLogger, ConnectionHistory, CommandCredentialProvider

## [v2026.032001] - 2026-03-20

### UX
- 117 fixes across 5 audit passes (cross-validated with Gemini)
- Add 47 i18n keys (2086 EN/FR in perfect parity)
- Add AutomationProperties.Name on all interactive controls (20+)
- Add keyboard focus indicators on PrimaryButtonStyle and SecondaryButtonStyle
- Add TextTrimming on all dynamic TextBlocks
- Add HorizontalScrollBarVisibility="Disabled" on form dialogs
- Localize MessageDialog, SSH status strings, filter placeholders
- Replace all Debug.WriteLine with FileLogger in App layer (31 occurrences)
- Add IsBusy on ImportConfigAsync
- Add CanExecute guards on SettingsViewModel commands
- WebView2 DefaultBackgroundColor now theme-aware

## [v2026.031917] - 2026-03-19

### Initial Release
- 8 protocol support: RDP, SSH, SFTP, VNC, Telnet, FTP, Citrix, Local Shell
- Embedded sessions via ActiveX (RDP), WebView2+xterm.js (SSH/Telnet), noVNC (VNC)
- DPAPI+HMAC credential encryption with external vault integration
- Pageant SSH agent via native Win32 IPC
- Multi-gateway SSH tunnel chaining with ref-counting
- SFTP browser with sudo elevation fallback
- Quick Connect (Ctrl+K), Network Scanner, Macro Recorder
- Dark/Light themes, bilingual EN/FR interface
- Import from MobaXterm, mRemoteNG, RDCMan, .rdp files
- Tab detach to floating windows, split pane sessions
- 505 xUnit tests
