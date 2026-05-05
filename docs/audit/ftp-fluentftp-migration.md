# FTP / FluentFTP Migration Roadmap

This file tracks the audit follow-up for FTP item M2 / A4.

## Why

`Heimdall.Sftp.FtpBrowser` is built on `System.Net.FtpWebRequest`, which has
been marked obsolete since .NET 6 and requires a local `#pragma warning disable
SYSLIB0014`. The audit flagged three concrete consequences:

- Synchronous I/O is wrapped in `Task.Run`, so cancellation is best-effort
  rather than true async socket cancellation.
- `EnableSsl=true` issues `AUTH TLS`, but the current implementation does not
  explicitly enforce `PROT P` for the data channel.
- LIST output parsing relies on Unix and DOS regexes; non-English server
  locales can silently produce empty or malformed entries.

## Recommended Target

[FluentFTP](https://github.com/robinrodricks/FluentFTP) is the preferred
replacement candidate: MIT licensed, actively maintained, real async APIs,
FTPS support with protected data channels, and robust directory listing
parsing across server variants.

## Scope Sketch

1. Replace `FtpBrowser` internals with FluentFTP's async client.
2. Keep `IRemoteBrowser` as the public surface so the embedded file-browser UI
   does not need a protocol-specific rewrite.
3. Map FluentFTP exceptions to typed Heimdall exceptions where reasonable.
4. Preserve the cleartext FTP warning behaviour and expose TLS state.
5. Migrate `FtpBrowserParsingTests` away from home-grown LIST strings toward
   FluentFTP list item mapping tests.

## Out Of Scope Until Migration Starts

- SCP, FXP, or cross-server transfer support.
- Resumable transfers. FluentFTP supports them, but the current `FtpBrowser`
  does not, so that would be a feature addition rather than a migration.

## Tracking

Open a GitHub issue when this is ready to pick up. Reference audit P2 #12 and
this roadmap file in the issue body.
