# 🟦 Runtime-Validate the FTP / FTPS Migration to FluentFTP

> This procedure validates, against real servers, the `FtpBrowser` migration to
> FluentFTP (commit `236c5a7`). The code is sound and CI is green; this covers the
> **only remaining risk** — real FTP/FTPS network integration. French version:
> `ftp-fluentftp-runtime-test.fr.md`.

> ⚠️ Do one target at a time: first **A — plain FTP**, then **B — explicit FTPS**.
> If a row fails, stop, note what you saw and the log excerpt, then continue.

## 📑 Overview

| Step | What | Est. time |
|---|---|---|
| Prep | Two servers + test files + app running | ~15 min |
| 1 | Plain FTP: connect + cleartext warning | ~5 min |
| 2 | Plain FTP: list, transfer, special names, rename/delete, close | ~15 min |
| 3 | FTPS: TLS connect + protected data channel | ~5 min |
| 4 | FTPS: contract replay + close | ~10 min |

## 📋 What you need before starting

| Item | Detail |
|---|---|
| 🖥️ App running | Debug build started (`Run.bat` or `dotnet run --project src/Heimdall.App`) |
| 🌐 Target A — plain FTP | No TLS, with a real account (`<HOST_A>`, port `21`, `<USER>`, `<PASSWORD>`) |
| 🔒 Target B — explicit FTPS | AUTH TLS on port 21, **certificate trusted by Windows** (`<HOST_B>`) |
| 📛 Trusted cert on B | Self-signed will be **rejected by design** (see Step 3) — use a trusted cert or add it to the Windows store |
| 📄 Big test file | `test-upload.bin`, ~5–10 MB (large enough to see the progress bar) |
| 🔑 Its SHA-256 | `Get-FileHash test-upload.bin` — write the hash down |
| 📄 File with a space | `mon fichier.txt` |
| 📄 File with a hash | `note#1.txt` |
| 📝 Today's FileLogger | Open it next to the app for live monitoring |

> 💡 No server handy? Quickest path: Docker `delfer/alpine-ftp-server` for plain FTP,
> and a FileZilla Server (Windows) or `vsftpd` instance configured for explicit FTPS.

## ✅ STEP 1 — Plain FTP: connect and verify the cleartext warning

> Plain FTP sends credentials unencrypted. The app must warn the operator both in the
> UI and in the log. We also confirm the log stays credential-clean — host and port
> only, never the username or password.

| # | Action |
|---|---|
| ☐ 1 | Create an FTP profile: `<HOST_A>`, port `21`, `<USER>`, `<PASSWORD>`, **TLS off** |
| ☐ 2 | Connect → state becomes **connected**, root `/` is listed |
| ☐ 3 | Look at the session banner / status bar → the **cleartext warning** is visible (`WarnFtpCleartext`) |
| ☐ 4 | Check the FileLogger → a `Warn` line reads `connecting to ftp://host:port without TLS` |
| ☐ 5 | ⚠️ Inspect that line → it contains **host + port only**, never the username or password |

> 🔴 Stop if the username or password appears in the log → credential-clean bug, fix it first.

## ✅ STEP 2 — Plain FTP: full contract

> Now we exercise every `IRemoteBrowser` operation: listing, transfers with integrity
> check, the special-name fix, rename/delete, and a clean close. This is the bulk of
> the validation.

### 2a — List and navigate

| # | Action |
|---|---|
| ☐ 1 | List the root → files/folders shown, size and date look right |
| ☐ 2 | Check a **folder** → size 0, folder type correct |
| ☐ 3 | Check a **file** → real size, coherent date |
| ☐ 4 | Enter a subfolder → navigation works, current path updates |
| ☐ 5 | Try entering a **non-existent** folder → clean error, no crash |

### 2b — Upload / download with integrity

| # | Action |
|---|---|
| ☐ 1 | Upload `test-upload.bin` → progress bar moves, transfer completes |
| ☐ 2 | Check the remote size after upload → equals the local size |
| ☐ 3 | Download it back under a different local name → transfer completes |
| ☐ 4 | Compare the **SHA-256** of the re-downloaded file → identical to the noted hash |

> 🔴 Stop if the checksums differ → transfer corruption (binary mode / data channel), investigate.

### 2c — Special names (the original URI bug)

> With the old `FtpWebRequest` these names failed or were mis-encoded. FluentFTP takes
> raw paths, so this is the key proof the migration fixed it.

| # | Action |
|---|---|
| ☐ 1 | Upload `mon fichier.txt` (space) → succeeds, correct name in the listing |
| ☐ 2 | Upload `note#1.txt` (hash) → succeeds, correct name |
| ☐ 3 | Download each one back → content intact, correct name |

### 2d — Rename / delete

| # | Action |
|---|---|
| ☐ 1 | Rename `test-upload.bin` to `renamed.bin` → new name appears, old gone |
| ☐ 2 | Create a folder `tmpdir` → created |
| ☐ 3 | Upload a file into it → present inside `tmpdir` |
| ☐ 4 | ⚠️ Delete the **non-empty** folder `tmpdir` → recursive delete removes folder + contents |
| ☐ 5 | Delete a **single file** (`renamed.bin`) → gone |

### 2e — Passive / active mode and close

| # | Action |
|---|---|
| ☐ 1 | Profile in **passive** (default): redo a list + a download → OK |
| ☐ 2 | *(If the server allows it)* profile in **active**: list + download → OK (or note the NAT/firewall failure) |
| ☐ 3 | Close the FTP session tab → tab closes, no UI freeze |
| ☐ 4 | Check the FileLogger → no `ObjectDisposedException`, no NRE, nothing unhandled |
| ☐ 5 | *(Optional)* Task Manager → no FTP-A socket left after closing (`AsyncFtpClient` disposed) |

## ✅ STEP 3 — FTPS: TLS connect and protected data channel

> Explicit FTPS upgrades to TLS via AUTH TLS, and the data channel is protected with
> PROT P (`DataConnectionEncryption = true` in the migration). Certificate validation
> uses the default chain (`PolicyErrors == None`) — **no blind accept**.

| # | Action |
|---|---|
| ☐ 1 | Create an FTP profile: `<HOST_B>`, port `21`, `<USER>`, `<PASSWORD>`, **TLS on** |
| ☐ 2 | Connect → succeeds after the TLS negotiation |
| ☐ 3 | Look at the banner / status → **no** cleartext warning (TLS active) |
| ☐ 4 | Check the FileLogger → **no** `without TLS` line |
| ☐ 5 | List the root → listing works (the list rides the data channel → proves PROT P) |
| ☐ 6 | Upload `test-upload.bin`, download it back, compare **SHA-256** → identical |
| ☐ 7 | *(Optional)* Wireshark on the data port → payload is **encrypted**, no cleartext |

> ⚠️ A self-signed cert will be **rejected** — that is correct behaviour, not a bug.
> Use a trusted cert (or add it to the Windows store) to test the happy path.

## ✅ STEP 4 — FTPS: contract replay and close

> Re-run the operations that matter over TLS to confirm nothing regresses on the
> encrypted path, and that the client closes cleanly.

| # | Action |
|---|---|
| ☐ 1 | Upload `mon fichier.txt` + `note#1.txt` → special names OK over TLS too |
| ☐ 2 | Rename a file → OK |
| ☐ 3 | Delete a non-empty folder + a single file → OK |
| ☐ 4 | Close the tab → no exception in the FileLogger, no socket leak |

## 🧾 Final scorecard

| Target | Connect | List | Transfer + checksum | Special names | Rename/Delete | Clean close | Logs clean |
|--------|---------|------|---------------------|---------------|---------------|-------------|------------|
| **A — plain FTP** | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ |
| **B — FTPS** | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ |

- [ ] Cleartext warning fires **only** on target A
- [ ] No credentials anywhere in the logs (A and B)
- [ ] No unhandled exception in the FileLogger
- [ ] Checksums identical on every round trip

## 🆘 Common issues

| Symptom | Quick fix |
|---|---|
| FTPS connection fails on a self-signed cert | Expected — default chain validation rejects it. Use a trusted cert or add it to the Windows store |
| Active mode times out | Usually NAT/firewall blocking the data port, not a bug — retest in passive |
| `without TLS` warning appears on the FTPS target | TLS did not actually negotiate — check the profile's TLS flag and the server's AUTH TLS support |
| Listing empty but no error | Wrong path or permissions — re-check the current directory and the account's rights |
| Checksums differ after transfer | Transfer integrity problem — capture the file sizes and a log excerpt, raise it as a finding |
