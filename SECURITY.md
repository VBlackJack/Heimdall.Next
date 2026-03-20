<!--
  Copyright 2026 Julien Bombled

  Licensed under the Apache License, Version 2.0 (the "License");
  you may not use this file except in compliance with the License.
  You may obtain a copy of the License at

      http://www.apache.org/licenses/LICENSE-2.0
-->

# Security Policy

## Supported Versions

| Version | Supported |
|---------|-----------|
| Latest release | Yes |
| Previous release | Security patches only |
| Older | No |

## Reporting a Vulnerability

If you discover a security vulnerability in Heimdall.Next, please report it responsibly:

1. **Do NOT open a public GitHub issue** for security vulnerabilities
2. Email the maintainer directly with details of the vulnerability
3. Include steps to reproduce, affected versions, and potential impact
4. Allow reasonable time for a fix before public disclosure

## Security Architecture

Heimdall.Next implements defense-in-depth:

- **Credential storage**: DPAPI (user-scope) + HMAC-SHA256 integrity via `CredentialProtector`
- **PIN protection**: PBKDF2-SHA256 with 100,000 iterations
- **File protection**: Windows ACLs (current user + Administrators + SYSTEM) on config, logs, and temp files
- **Atomic file creation**: `SecureFileWriter.WriteAndProtect()` eliminates TOCTOU race conditions
- **Input validation**: Compiled regex patterns against command injection (CWE-78)
- **XML hardening**: `DtdProcessing.Prohibit` + `XmlResolver=null` on all XML importers
- **Process isolation**: `UseShellExecute=false` for all untrusted input; structured argument lists
- **WebView2 sandboxing**: CSP (`default-src 'none'`), navigation blocking, message origin validation
- **SSH security**: TOFU host key verification, Pageant IPC process owner validation
- **Plink password files**: Atomic ACL on Windows, mode 0600 on Unix (no fallback)

## Dependencies

| Dependency | Purpose | Update Policy |
|------------|---------|---------------|
| SSH.NET | SSH/SFTP connections | Monitor advisories |
| WebView2 | Terminal rendering | Evergreen auto-update |
| AvalonEdit | Code editor | Pin to stable |
| CommunityToolkit.Mvvm | MVVM framework | Follow releases |
