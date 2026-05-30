# FTP / FluentFTP Migration

Status: closed on 2026-05-30. FTP item M2 / A4 has been delivered.

## Why

`Heimdall.Sftp.FtpBrowser` was built on `System.Net.FtpWebRequest`, which has
been marked obsolete since .NET 6 and required a local `#pragma warning disable
SYSLIB0014`. The audit flagged three concrete consequences:

- Synchronous I/O is wrapped in `Task.Run`, so cancellation is best-effort
  rather than true async socket cancellation.
- `EnableSsl=true` issued `AUTH TLS`, but the old implementation did not
  explicitly enforce `PROT P` for the data channel.
- LIST output parsing relies on Unix and DOS regexes; non-English server
  locales can silently produce empty or malformed entries.

## Delivered Target

Heimdall now uses [FluentFTP](https://github.com/robinrodricks/FluentFTP):
MIT licensed, actively maintained, real async APIs, FTPS support with protected
data channels, and robust directory listing parsing across server variants.

## Delivered Scope

1. Replace `FtpBrowser` internals with FluentFTP's async client.
2. Keep `IRemoteBrowser` as the public surface so the embedded file-browser UI
   does not need a protocol-specific rewrite.
3. Preserve the cleartext FTP warning behaviour and expose TLS state.
4. Configure explicit FTPS with FluentFTP `DataConnectionEncryption`.
5. Migrate `FtpBrowserParsingTests` away from home-grown LIST strings toward
   FluentFTP list item mapping tests.

## Left Out Of Scope

- SCP, FXP, or cross-server transfer support.
- Resumable transfers. FluentFTP supports them, but the current `FtpBrowser`
  does not, so that would be a feature addition rather than a migration.
