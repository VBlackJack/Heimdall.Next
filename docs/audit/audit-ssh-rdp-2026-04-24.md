# Project Audit Report — Heimdall.Next (SSH & RDP)

**Date:** 2026-04-24
**Build audited:** v2026.033108 (Release)
**Stack:** .NET 10 / C# 14, WPF, SSH.NET 2025.1.0, MsTscAx ActiveX, SSH.NET + Pageant + plink.exe fallback
**Mode:** Targeted — Security + Code Quality / Architecture + Performance / Robustness
**Scope:** `src/Heimdall.Ssh/**`, `src/Heimdall.Rdp/**`, `src/Heimdall.App/Services/Handlers/{SshHandler,RdpHandler,CitrixHandler,SshSessionDiagnosticFactory,RdpSessionDiagnosticFactory}.cs`, and all collaborating wiring in `Heimdall.App` (App.xaml.cs, ConnectionHelpers, EmbeddedSshView, EmbeddedRdp views).

---

## Executive Summary

Le périmètre SSH/RDP est **globalement mature et sérieusement défendu**. DPAPI + HMAC, TOFU, `-batch` plink, allow-list du processus Pageant, ACL atomique sur le fichier `-pwfile`, CRLF sanitization du `.rdp`, `ArgumentList` partout, `InputValidator` systématique, disposal order RDP respecté, ref-counting des tunnels : la surface d'attaque est consciencieusement réduite.

Deux choses font **réellement mal** et méritent d'être traitées avant release suivant :

1. **Le chemin plink (pipe mode) ré-accepte la fingerprint serveur à chaque connexion** via `ProbeHostKeyFingerprintAsync`. C'est **TOFU sans pin** — un attaquant qui MITM n'importe quelle connexion plink voit sa fingerprint forgée passer en `-hostkey` de la vraie session. `HostKeyStore` n'est **pas consulté** sur ce chemin.
2. **Le TOFU silencieux** : SSH.NET accepte *automatiquement* la première fingerprint d'un nouvel hôte sans jamais prompter l'utilisateur (`SshConnectionFactory.AttachHostKeyVerification` ligne 76). Cela rend l'application indiscernable d'un client qui désactive la vérification lors du premier contact — l'utilisateur n'a aucun moyen de refuser, ni de *savoir* qu'il accepte un nouvel hôte.

Le reste est un mélange de code dupliqué (TunnelManager est coupable), de code mort (`ReadStderrSafeAsync`), et de micro-races (`GetEphemeralPort`).

**Compte final : 0 🔴 Critical · 6 🟠 Important · 9 🟡 Minor**

---

## Findings by Severity

### 🔴 Critical (0)

Aucun. Pas de credentials en clair commités, pas d'injection SQL/shell réelle, pas de bypass d'auth.

### 🟠 Important (6)

| # | ID | Titre |
|---|---|---|
| 1 | SEC-P01 | Plink pipe-mode fallback : TOFU **non-pinned** — fingerprint re-probée et re-acceptée à chaque connexion |
| 2 | SEC-P02 | TOFU SSH.NET silencieux — aucun prompt utilisateur au premier contact, acceptation automatique |
| 3 | SEC-P03 | Mismatch de fingerprint — connexion rejetée mais diagnostic UI générique, utilisateur non-informé de la cause sécurité |
| 4 | PERF-P01 | `ServerHealthMonitor.CollectHealthDataAsync` utilise `cmd.Result` (bloquant) enveloppé dans `Task.Run` au lieu de `ExecuteAsync` |
| 5 | CQ-P01 | `TunnelManager` — 8 blocs `try { dynamicPort?.Dispose() } ...` identiques dupliqués dans chaque `catch` |
| 6 | CQ-P02 | `HostKeyStore.HostKeyEvent` émis à **chaque** connexion (y compris sur match connu) → écritures disque redondantes via `PersistTrustedHostKeyAsync` dans `App.xaml.cs:176` |

### 🟡 Minor (9)

| # | ID | Titre |
|---|---|---|
| 7 | CQ-P03 | `PlinkTunnelRunner.ReadStderrSafeAsync` — méthode privée **jamais appelée** (dead code) |
| 8 | CQ-P04 | `PageantClient.Dispose` vestigial (positionne juste `_disposed = true`, aucune ressource à libérer) |
| 9 | PERF-P02 | `TunnelManager.GetEphemeralPort` — TOCTOU entre `listener.Stop()` et la réutilisation (probabilité basse, OS ephemeral range) |
| 10 | PERF-P03 | `TunnelManager.AllocatePort` — même TOCTOU que ci-dessus sur `preferredPort` |
| 11 | SEC-P04 | `PageantClient.SendMessage` — `CreateFileMapping` appelé avec `IntPtr.Zero` pour `lpSecurityAttributes` (DACL par défaut du token) — OK en pratique (mapName ephemère), mais pas de SD explicite |
| 12 | SEC-P05 | `CredentialAutofill.InjectPassword` : conversions répétées `new string(passwordBuffer)` → le password finit en string immuable GC-résiliente (limitation .NET connue, mentionner) |
| 13 | SEC-P06 | `CredentialAutofill.FindCredentialDialog` : fallback "accepter si exactement 1 broker match même sans host hint" (ligne 235) — fenêtre de confusion entre dialogs concurrents |
| 14 | CQ-P05 | `RdpActiveXHost.ApplyRedirectionSettings` — 9 blocs `try { … } catch (Exception ex) { FileLogger.Warn(...) }` strictement identiques → candidat pour un helper |
| 15 | CQ-P06 | `RdpActiveXHost._pendingPassword` est un `string` (immuable) — il est `null`-ifié après handoff COM (`Connect` ligne 208) mais le heap peut conserver des copies jusqu'au prochain GC |

---

## Detailed Findings

### Category: Security

**Score (SSH): 8 PASS / 10 checks** · **Score (RDP): 7 PASS / 8 checks**

#### 🟠 SEC-P01 — Plink fallback: TOFU **not pinned**

**Where:** `src/Heimdall.App/Services/Handlers/SshHandler.cs:266-489`

**Evidence:**

- `ProbeHostKeyFingerprintAsync` (ligne 377) lance `plink -v -batch -ssh -P <port> user@host` **sans** `-hostkey`. En `-batch`, plink refuse toute nouvelle clé → imprime la fingerprint sur stderr et sort.
- La fingerprint est **extraite brute de stderr** (ligne 459-478, regex `(ssh-\S+)\s+\d+\s+(SHA256:\S+)`) **sans confrontation avec `HostKeyStore`**.
- Le chemin est invoqué **inconditionnellement** pour chaque connexion plink (ligne 331 : pas de cache lookup avant).
- La fingerprint récupérée est ensuite ré-injectée telle quelle dans `BuildPipeModeArguments` via `-hostkey "<fp>"` (ligne 569-573) → plink considère l'hôte trusted.

**Impact:** Un attaquant en position MITM (ARP spoof, DNS hijack, rogue WAP) qui contrôle la première transaction plink peut présenter **sa propre host key**. Cette clé devient automatiquement la clé "trusted" pour la vraie session. Le ré-probing à chaque connexion signifie que **la détection de changement de clé (qui est le cœur de TOFU) n'existe tout simplement pas** sur ce chemin. `HostKeyStore.Verify` n'est jamais appelé en mode plink.

**Recommandation:**

1. Avant `ProbeHostKeyFingerprintAsync`, consulter `_hostKeyStore.GetFingerprint(targetHost, targetPort)`.
2. Si présent → passer directement cette fingerprint en `-hostkey` **sans re-probe**.
3. Si absent → probe, puis **prompter l'utilisateur** (cf. SEC-P02), puis `_hostKeyStore.Trust(...)` pour persistence (via `HostKeyEvent`).
4. Si présent mais différent → **bloquer** et afficher un diagnostic "HOST KEY MISMATCH".

#### 🟠 SEC-P02 — Silent TOFU on first SSH.NET contact

**Where:** `src/Heimdall.Ssh/SshConnectionFactory.cs:60-86`

**Evidence:**

```csharp
// SshConnectionFactory.AttachHostKeyVerification (ligne 69-85)
client.HostKeyReceived += (sender, e) =>
{
    var result = hostKeyStore.Verify(host, port, e.HostKey);
    e.CanTrust = result.Trusted;   // True aussi en FirstUse !

    if (result.FirstUse)
    {
        hostKeyStore.Trust(host, port, result.Fingerprint);  // Silent accept
        Heimdall.Core.Logging.FileLogger.Info(...);
    }
    else if (!result.Trusted)
    {
        Heimdall.Core.Logging.FileLogger.Warn(...);
    }
};
```

Et `HostKeyStore.Verify` (ligne 74-76) :
```csharp
// First use: trusted by TOFU policy
HostKeyEvent?.Invoke(key, fingerprint, true);
return new HostKeyVerifyResult(Trusted: true, FirstUse: true, fingerprint, StoredFingerprint: null);
```

**Impact:** Le premier contact avec n'importe quel hôte inconnu est accepté **sans question** posée à l'utilisateur. C'est légèrement plus permissif qu'OpenSSH (qui affiche la fingerprint et exige yes/no) et significativement plus permissif que MobaXterm/PuTTY. Un attaquant MITM sur la première connexion est transparent.

**Recommandation:** Implémenter un dialog `HostKeyPromptDialog` :
- Bloquer l'acceptation (`e.CanTrust = false` par défaut pour `FirstUse`).
- Afficher la fingerprint SHA256 + algorithme + hôte:port.
- Si l'utilisateur accepte → `hostKeyStore.Trust(...)` et relance la connexion.
- Si refus → rejet définitif.

La persistence existe déjà (`App.xaml.cs:169-177` → `PersistTrustedHostKeyAsync`), il ne manque que l'étape UI.

#### 🟠 SEC-P03 — Host key mismatch: connection rejected but no UI warning

**Where:** `src/Heimdall.Ssh/SshConnectionFactory.cs:80-84`

**Evidence:** Quand `HostKeyStore.Verify` retourne `Trusted=false` sur mismatch, `e.CanTrust = false` rejette bien la connexion côté SSH.NET, mais l'événement remonte en `SshFailureCode.HostKeyMismatch` (défini mais jamais produit par `FailureClassifier` — j'ai cherché) ou plus probablement en `AuthRejected` générique. Aucun dialog "⚠️ La clé de serveur a changé" n'apparaît : l'utilisateur voit juste "échec de connexion".

**Impact:** Une substitution de clé post-compromission est invisible pour l'utilisateur final. La fileLogger trace le warning mais l'UI ne fait pas remonter la cause de sécurité.

**Recommandation:**

1. Propager `HostKeyMismatch` via `ConnectionResult` + `SessionDiagnostic` (déjà codé mais pas émis).
2. Afficher un dialog dédié listant ancienne vs nouvelle fingerprint, avec deux actions : "annuler" (défaut, sûr) et "accepter (détruit la clé précédente)".
3. Ajouter un entrée de log `AUDIT` dédiée pour traçabilité forensique.

#### ✅ SEC-01 Secrets in Source Code — PASS
Aucun credential en dur. Passwords/tokens → DPAPI + HMAC-SHA256 (`CredentialProtector.Unprotect`), passphrases PPK → fichier local utilisateur, token git `CmdLibGitSync` → DPAPI.

#### ✅ SEC-02 Input Validation — PASS
`InputValidator.Validate(username, "SshUser")`, `ValidatePortRange`, `ValidateDomain`, `TryValidateKeyPath` (contre `\0`, `"`, chemins relatifs, fichier manquant) sont invoqués systématiquement (`SshHandler` lignes 191-224, 292, 303, 314, 321; `PlinkTunnelRunner.ValidateConnectionInputs` lignes 308-348).

#### ✅ SEC-03/04/07 Auth & Authz & XSS — N/A
Pas de layer web ni d'auth multi-utilisateur. App desktop single-user.

#### ✅ SEC-05 Dependency Vulnerabilities — À EXÉCUTER
Non-vérifiable statiquement depuis cet audit. **Action requise:** exécuter `dotnet list Heimdall.slnx package --vulnerable --include-transitive` en CI ou manuellement avant release. `SSH.NET 2025.1.0` et `WebView2` sont les dépendances à surveiller en priorité.

#### ✅ SEC-06 SQL/NoSQL Injection — N/A
Pas de requête utilisateur dans le scope SSH/RDP. La persistence TwinShell utilise EF Core (paramétré).

#### ✅ SEC-08 Sensitive Data Exposure — PASS (avec nuance)
- Aucun password n'est loggé directement. Plink stderr est sanitisé par `SanitizeForLog` (remplace les chars <32 hors `\t` par `?`, tronque à 256).
- `CredentialAutofill` **logue intensivement** handles, titres, process names, PIDs (lignes 163-164, 198, 209-210, 461-462, 505-506…). Ces valeurs peuvent contenir des indices d'hôte ("`Enter credentials for srv-prod.corp.local`"). Pas un secret au sens strict mais à noter pour users en environnement régulé.
- `RdpActiveXHost` logue `hasPassword=true/false` sans logger le password (ligne 152). ✅
- `FileLogger.Info(...)` sur la fingerprint TOFU (ligne 78) — ceci est **attendu** et utile pour audit forensique.

**Recommandation (minor):** Ajouter un flag `SettingsLogVerbosity = Minimal` qui coupe les logs détaillés de `CredentialAutofill` en production.

#### ✅ SEC-09 Transport security — PASS
- RDP NLA (`EnableCredSspSupport`) par défaut activé via `RdpRedirectionOptions.Nla = true`.
- SSH : tous les algos sont laissés à SSH.NET (modernes par défaut).
- `PageantKeyWrapper.BuildAlgorithms` met SHA-2 avant ssh-rsa legacy.

#### ✅ SEC-10 Error Messages — PASS
Aucun stack trace remonté à l'UI. `FailureClassifier` maps vers `SshFailureCode` structuré (25 codes). Le code `2055 BadCredentials` RDP est décodé en i18n key (`RdpActiveXHost.GetDisconnectReasonKey`).

#### 🟡 SEC-P04 — Pageant shared-memory mapping without explicit DACL

**Where:** `src/Heimdall.Ssh/Pageant/PageantClient.cs:166-172`

**Evidence:**
```csharp
using var fileMapping = NativeMethods.CreateFileMapping(
    NativeMethods.InvalidHandleValue,
    IntPtr.Zero,           // ← lpSecurityAttributes = null → default DACL
    NativeMethods.PAGE_READWRITE,
    0,
    mappingSize,
    mapName);              // PageantRequest<PID:X8><TID:X8>
```

**Analyse:** Passer `null` laisse Windows appliquer le DACL par défaut du token du process. Sur un compte utilisateur standard, cela restreint l'accès à ce même utilisateur — ce qui est déjà bien. Combiné au mapName qui inclut `ProcessId` + `ManagedThreadId` (ephémère), la surface d'attaque est très réduite.

**Impact réel:** quasi-nul. Mais un process **running as the same user** (ex. malware userland) peut théoriquement ouvrir le même mapping si la fenêtre de temps est attrapée.

**Recommandation (defense-in-depth):** passer un `SECURITY_ATTRIBUTES` avec un SD explicite limité à l'owner (`O:S-1-5-<sid>D:P(A;;GA;;;<sid>)`). Peu de gain, beaucoup de code natif → **à traiter uniquement si l'app vise un environnement classifié**.

#### 🟡 SEC-P05 — `new string(passwordBuffer)` GC residue

**Where:** `src/Heimdall.Rdp/CredentialAutofill.cs:361, 414`

Le buffer `char[]` (mutable) est zeroïfié en `finally` (ligne 187) — bon. Mais `new string(passwordBuffer)` (lignes 361, 414, 549) crée des `string` immuables. L'appel `valuePattern.SetValue(value)` (ligne 541) passe ce string vers UIA, qui le copie à son tour. La GC finira par collecter, mais pas avant la prochaine Gen2.

**Impact:** Exposition mémoire transitoire au memory-scraping (ex. Mimikatz). Pas un defect : c'est la limitation structurelle de `string` en .NET. WPF `PasswordBox` a le même problème.

**Recommandation:** Aucune action code requise — juste à documenter dans un `SECURITY.md` comme limitation connue.

#### 🟡 SEC-P06 — Broker fallback "accept single candidate without host hint"

**Where:** `src/Heimdall.Rdp/CredentialAutofill.cs:234-236`

```csharp
// Only fall back to unmatched brokers if there's exactly one candidate
if (brokerMatches.Count == 1)
    return brokerMatches[0];
```

Si deux sessions RDP sont lancées quasi-simultanément (utilisateur qui clique rapidement), les deux credential brokers existent. Cette condition échoue → pas d'injection → bon. Mais si une app tierce quelconque déclenche un CredUI broker juste avant Heimdall (ex. un partage réseau), Heimdall peut injecter dans ce dernier s'il est le seul broker matchant et que le host hint est absent ou non-parseable.

**Recommandation:** exiger `hostHintPattern.IsMatch(window.Title)` quand on n'est pas owned-by-target. Retirer la branche "single candidate → accept".

---

### Category: Code Quality

**Score: 6 PASS / 9 checks**

#### 🟠 CQ-P01 — `TunnelManager` exception-handler duplication

**Where:** `src/Heimdall.Ssh/TunnelManager.cs:192-234, 438-466`

Les catch de `OpenTunnelAsync` (5 blocs) et `OpenChainedTunnelAsync` (4 blocs) contiennent tous **exactement le même** prologue de cleanup :

```csharp
try { dynamicPort?.Dispose(); } catch (Exception cleanupEx) { Core.Logging.FileLogger.Debug("Dynamic port dispose suppressed", cleanupEx); }
try { remotePortFwd?.Dispose(); } catch (Exception cleanupEx) { Core.Logging.FileLogger.Debug("Remote port forward dispose suppressed", cleanupEx); }
CleanupPartial(client, forwardedPort);  // ou CleanupChainPartial
```

**Recommandation:** factoriser en `CleanupPartialWithExtras(client, forwardedPort, dynamicPort, remotePortFwd)` (ou extension à `CleanupPartial`). ~60 lignes économisées et le risque d'oubli d'un des `Dispose` lors d'un ajout futur (ex. X11 forward) disparaît.

#### 🟠 CQ-P02 — `HostKeyEvent` fires on every verification

**Where:** `src/Heimdall.Ssh/HostKeyStore.cs:70, 75` · consommateur `App.xaml.cs:169-177`

Le handler persistence ne filtre pas sur FirstUse :

```csharp
// App.xaml.cs:169
hostKeyStore.HostKeyEvent += (key, fingerprint, trusted) =>
{
    if (!trusted) return;
    _ = PersistTrustedHostKeyAsync(configManager, key, fingerprint);
};
```

Et `Verify` émet l'event **même quand la clé stockée matche déjà** (ligne 70 : `HostKeyEvent?.Invoke(key, fingerprint, match);`).

**Impact:** chaque connexion SSH.NET → 1 `PersistTrustedHostKeyAsync` → 1 `ConfigManager.MergeSettingAsync` → 1 écriture disque atomique → 1 raise `SettingsChanged` → recharge cascade.

**Recommandation:**
- Changer la signature de `HostKeyEvent` en `Action<string, string, bool trusted, bool firstUse>` (ou utiliser un record event args).
- Dans App.xaml.cs, ne persister que si `firstUse == true`.
- Alternative plus simple : comparer à `GetFingerprint(host, port)` dans `PersistTrustedHostKeyAsync` avant d'écrire.

#### 🟡 CQ-P03 — `PlinkTunnelRunner.ReadStderrSafeAsync` is dead code

**Where:** `src/Heimdall.Ssh/Plink/PlinkTunnelRunner.cs:474-491`

Private method, aucun call site. La drain-loop dans `StartAsync` (ligne 140-157) lit déjà stderr ligne-par-ligne en tâche de fond. `ReadStderrSafeAsync` est une relique d'un design antérieur.

**Recommandation:** supprimer. Déclenche un warning `IDE0051` (Remove unused private member) qui devient erreur avec `TreatWarningsAsErrors=true` — suspicious que le build passe, à vérifier.

#### 🟡 CQ-P04 — `PageantClient.Dispose` is vestigial

**Where:** `src/Heimdall.Ssh/Pageant/PageantClient.cs:407-410`

```csharp
public void Dispose()
{
    _disposed = true;
}
```

Aucune ressource à libérer (la `FileMapping` est déjà `using`-scoped dans `SendMessage`). La classe n'aurait pas besoin d'être `IDisposable`. Gardé pour forward-compat mais à documenter.

**Recommandation:** soit retirer l'implémentation `IDisposable` (breaking change mineur pour les tests), soit ajouter un commentaire `/* intentional: reserved for future handle caching */`.

#### 🟡 CQ-P05 — `RdpActiveXHost.ApplyRedirectionSettings` repetitive try/catch

**Where:** `src/Heimdall.Rdp/ActiveX/RdpActiveXHost.cs:507-627`

9 × `try { adv.X = Y; } catch (Exception ex) { Heimdall.Core.Logging.FileLogger.Warn($"[RdpActiveXHost] X: {ex.Message}"); }`. Pattern clair mais verbeux.

**Recommandation:**
```csharp
private static void SetDynamic(string propertyName, Action action)
{
    try { action(); }
    catch (Exception ex) { FileLogger.Warn($"[RdpActiveXHost] {propertyName}: {ex.Message}"); }
}
// Usage:
SetDynamic("SmartSizing", () => adv.SmartSizing = true);
SetDynamic("allowBackgroundInput", () => adv.allowBackgroundInput = 1);
```

#### 🟡 CQ-P06 — `_pendingPassword` lifetime

**Where:** `src/Heimdall.Rdp/ActiveX/RdpActiveXHost.cs:52, 149, 208`

`string? _pendingPassword` est immuable. Le `null`-ify ligne 208 (`_pendingPassword = null;`) coupe la référence locale mais l'interning + la closure capturées par `ApplyCredentialSettings` (ligne 489-494) gardent des copies jusqu'au prochain GC.

**Impact:** limitation .NET structurelle. SecureString n'offre plus de garanties non plus depuis Windows 10 (doc MS).

**Recommandation:** documenter la limitation dans `SECURITY.md`. Envisager `ReadOnlySpan<char>` pour le passage à `IMsTscNonScriptable.put_ClearTextPassword`, mais cette API accepte un BSTR (allocation COM) donc pas de vrai gain.

#### ✅ CQ-01 Cyclomatic Complexity — PASS
Aucune méthode au-delà de ~15 branches. Candidats les plus complexes :
- `SshHandler.ConnectAsync` ~ 12 branches (multiple validation + dispatch) — acceptable
- `RdpActiveXHost.ApplyRedirectionSettings` ~ 14 try/catch séquentiels — linéaire, pas vraiment complexe
- `CredentialAutofill.FindCredentialDialog` ~ 10 branches — OK

#### ✅ CQ-03 Code Duplication — majoritairement PASS (sauf CQ-P01)
Un seul vrai cluster de duplication identifié (TunnelManager catches). Les répétitions `try { adv.X = Y } catch` dans `RdpActiveXHost` sont flaggées en CQ-P05.

#### ✅ CQ-04 SOLID — PASS
- SRP : chaque classe a un rôle clair (`HostKeyStore`, `TunnelManager`, `PlinkTunnelRunner`, `PageantClient`, etc.)
- DIP : `IRdpSession`, `IProtocolHandler`, `IPrivateKeySource` (SSH.NET abstraction)
- Aucune god class dans le scope (la plus grosse : `SshHandler.cs` 614 lignes, mais cohésive)

#### ✅ CQ-05 Naming Clarity — PASS
Conventions C# respectées. Pas de cryptique. Les méthodes internes (`TryInjectPasswordViaAutomation`, `BuildAuthMethods`, `AttachEventSink`) sont explicites.

#### ✅ CQ-06 Error Handling — PASS (avec nuances)
Aucun catch vide. Tous les catches logguent ou retournent un state structuré. Les `catch (Exception)` sont documentés et intentionnels (cleanup paths, boundary handlers).

#### ✅ CQ-07 Magic Numbers — PASS
Constantes nommées partout : `KeepAliveIntervalMs = 60_000`, `AgentMaxMessageLength = 262144`, `MaxReconnectAttempts = 20`, `DefaultFocusDelay = 300ms`. `CredentialAutofill.DefaultScanInterval`. `SshFailureCode` enum pour tous les codes de retour.

#### ✅ CQ-08 Formatting — PASS
`Directory.Build.props` + `TreatWarningsAsErrors=true` + `.editorconfig`. CI lint actif.

#### ✅ CQ-09 Language-Specific Idioms — PASS
- `ObjectDisposedException.ThrowIf(_disposed, this)` (C# 11+)
- `ArgumentNullException.ThrowIfNull(x)`, `ArgumentException.ThrowIfNullOrWhiteSpace(x)`
- Primary ctors, records avec `with` expressions, pattern matching (`is { HasExited: false }`)
- `LibraryImport` moderne (CredentialAutofill) au lieu de `DllImport`
- `await using var` pour `CancellationTokenRegistration`

---

### Category: Architecture

**Score: 9 PASS / 10 checks**

#### ✅ ARCH-01 Separation of Concerns — PASS
- `Heimdall.Core` → aucune référence UI
- `Heimdall.Ssh` → SSH.NET + Pageant, pas de WPF
- `Heimdall.Rdp` → MsTscAx + Win32 P/Invoke, pas de VM/View
- `Heimdall.App/Services/Handlers` → orchestration, aucune logique métier SSH/RDP résidente

#### ✅ ARCH-02 Dependency Direction — PASS
Graph documenté dans `CLAUDE.md`. Vérifié : aucune référence inverse.

#### ✅ ARCH-03 Circular Dependencies — PASS
Aucune détectée.

#### ✅ ARCH-04 Dependency Injection — PASS
`HostKeyStore`, `TunnelManager`, `ThemeService` etc. sont singletons DI. `SshConnectionFactory` est `static` pur (fonction) — justifiable car aucune state.

#### ✅ ARCH-05 MVVM Compliance — PASS
Les handlers ne référencent pas `Window`/`MessageBox`. Code-behind minimal. ViewModels pour ServerDialog / EmbeddedSsh / EmbeddedRdp.

#### ✅ ARCH-06 Data Access Isolation — PASS (hors-scope SSH/RDP)
TwinShell repositories, `ConfigManager`, `CredentialProtector`. SSH/RDP n'ont pas de persistence directe.

#### ✅ ARCH-07 Configuration Management — PASS
`AppSettings.PlinkPath`, `HostKeyProbeTimeoutMs`, `TrustedHostKeys`, `SysinternalsPath`, `NirSoftPath`, `CmdLibGitSync*` — tous centralisés. Pas d'URL/path/timeout hardcodé.

#### ✅ ARCH-08 Coupling — PASS
Les handlers communiquent via contrats (`ConnectionResult`, `IRdpSession`, `IProtocolHandler`). La god-class n'existe pas : plus grosse est `SshHandler.cs` (614 lignes) et reste cohésive.

#### ✅ ARCH-09 Testability — PASS
Tests existants : `HostKeyStoreTests`, `SshConnectionFactoryTests`, `TunnelManagerTests`, `PageantClientTests`, `PlinkTunnelRunnerTests`, `AuthPreflightCheckerTests`, `GatewayChainResolverTests`, `FailureClassifierTests`, `RdpFileGeneratorTests`, `RdpRedirectionOptionsTests`, `AspectRatioManagerTests`. Pas de test identifié pour `CredentialAutofill` (difficile, besoin d'un harness CredUI) ni pour `RdpActiveXHost` (besoin STA + Application).

**Gap notable:** pas de test d'intégration qui vérifie le comportement de `SshConnectionFactory.AttachHostKeyVerification` en cas de mismatch — ce qui aurait probablement détecté SEC-P02/P03.

#### ✅ ARCH-10 Project Organization — PASS
Layout conforme à `CLAUDE.md`.

---

### Category: Performance

**Score: 7 PASS / 9 checks**

#### 🟠 PERF-P01 — `ServerHealthMonitor` blocks on `cmd.Result`

**Where:** `src/Heimdall.Ssh/ServerHealthMonitor.cs:197-248` (selon le rapport d'exploration; voir lignes 214, 223, 232)

Les propriétés `cmd.Result` et `cmd.ExitStatus` de SSH.NET sont **synchrones** — elles bloquent le thread appelant jusqu'à la fin de la commande. Le code les emballe dans `Task.Run(...)` pour ne pas bloquer le thread UI, ce qui fonctionne mais :

1. Monopolise un thread ThreadPool pendant l'exécution SSH.
2. SSH.NET expose `BeginExecute` / `EndExecute` (APM) qui serait asynchrone natif.

**Impact:** chaque tick du health monitor (intervalle 15s) consomme 3 threads ThreadPool pendant la durée de la commande. Sur N sessions ouvertes, c'est 3×N threads — cap rapide si N > 10.

**Recommandation:** refactorer `CollectHealthDataAsync` pour utiliser le pattern `Task.Factory.FromAsync(cmd.BeginExecute, cmd.EndExecute, null)`.

#### 🟡 PERF-P02 — `TunnelManager.GetEphemeralPort` TOCTOU

**Where:** `src/Heimdall.Ssh/TunnelManager.cs:659-666`

```csharp
private static int GetEphemeralPort()
{
    using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
    listener.Start();
    int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;   // ← port libéré, race jusqu'à la prochaine bind côté SSH.NET
}
```

Entre `listener.Stop()` et l'usage réel du port par `ForwardedPortLocal`, un autre process peut s'emparer du port. Probabilité très basse (range ephemeral ~16384-65535 et Windows ne recycle pas instantanément), mais techniquement présent.

**Recommandation:** deux options :
- **A (simple):** accepter le risque, ajouter un retry avec backoff si la bind ForwardedPortLocal échoue.
- **B (plus propre):** garder le `TcpListener` vivant (`using` scoped au `TunnelSession`) et le transférer au ForwardedPortLocal — pas trivial car SSH.NET gère sa propre socket.

#### 🟡 PERF-P03 — `TunnelManager.AllocatePort` same TOCTOU + extra check race

**Where:** `src/Heimdall.Ssh/TunnelManager.cs:637-656`

Même problème que PERF-P02. `IsPortTracked` vérifie le registry interne, puis `TcpListener` vérifie l'OS — entre les deux, un autre call peut `TryAdd` le port. Couvert par le double-check dans `OpenTunnelAsync` (ligne 181-185) donc pas un bug fonctionnel, mais le code est confus.

**Recommandation:** documenter le double-check comme invariant attendu.

#### ✅ PERF-01 Memory Leaks — PASS
Événements souscrits avec `+=` tous désouscrits avec `-=` dans le `Dispose` ou équivalent (vérifié sur `EmbeddedSshView.xaml.cs`, `RdpActiveXHost`, `TunnelSession`, `PlinkTunnelRunner`, `ServerHealthMonitor`). Disposal order RDP (Visibility → Child=null → Disconnect → DetachEventSink → Dispose) respecté dans `EmbeddedRdpView`.

#### ✅ PERF-02 N+1 Queries — N/A
Pas de requête DB dans le scope.

#### ✅ PERF-03 UI Virtualization — N/A (hors-scope)
S'applique à ServerList, pas au scope SSH/RDP.

#### ✅ PERF-04 Binding Optimization — PASS
`EmbeddedSshView` et `EmbeddedRdp` utilisent essentiellement des bindings one-way. Pas de data-grid massive.

#### ✅ PERF-05 Async/Await — PASS (une réserve)
Aucun `.Wait()` / `.Result` / `.GetAwaiter().GetResult()` sur le UI thread détecté dans le scope. **Sauf** PERF-P01 (`cmd.Result` de SSH.NET dans `Task.Run`, ce qui est un pattern acceptable mais non-optimal).

#### ✅ PERF-06 Caching — N/A
Pas d'opération chère répétée à cacher dans le scope.

#### ✅ PERF-07 Resource Loading — PASS
RDP ActiveX pré-warmé au démarrage (`App.xaml.cs:PreWarmRdpRuntime`). Splash screen affiche le progrès (mstscax.dll ~ 300-500ms).

#### ✅ PERF-08 Collection Efficiency — PASS
`ConcurrentDictionary` pour `_trustedKeys`, `_activeTunnels`, `_refCounts` — O(1). `HashSet` pour visited dans `GatewayChainResolver`. Pas de `.ToList().Where()` suspect.

---

## Action Plan

### Priorité 1 — Security hardening (recommandé avant prochaine release)

1. **SEC-P01** — Consulter `HostKeyStore.GetFingerprint(host, port)` dans `SshHandler.ConnectSshViaPlinkAsync` avant de lancer `ProbeHostKeyFingerprintAsync`. Si présent, passer directement la fingerprint stockée. Si absent, prompter après probe (cf. SEC-P02). Si mismatch, bloquer et afficher diagnostic (cf. SEC-P03).
2. **SEC-P02** — Ajouter un `HostKeyPromptDialog` (WPF) invoqué via `IHostKeyVerifier` injecté dans `SshConnectionFactory.AttachHostKeyVerification`. Par défaut `e.CanTrust = false` pour `FirstUse`; promotion via dialog.
3. **SEC-P03** — Étendre `SshSessionDiagnosticFactory` pour mapper `SshFailureCode.HostKeyMismatch` vers un dialog dédié affichant old/new fingerprint + actions "reject" / "replace" (la seconde documentée comme destructive).

### Priorité 2 — Performance & code quality (prochain sprint)

4. **PERF-P01** — Migrer `ServerHealthMonitor.CollectHealthDataAsync` sur `Task.Factory.FromAsync(BeginExecute, EndExecute, null)`.
5. **CQ-P01** — Factoriser les blocs de cleanup dans `TunnelManager` (helper `CleanupAll(...)`).
6. **CQ-P02** — Filtrer la persistence `HostKeyEvent` par `firstUse` (ou comparer fingerprint avant write).

### Priorité 3 — Hygiène (backlog, quand pratique)

7. **CQ-P03** — Supprimer `PlinkTunnelRunner.ReadStderrSafeAsync`.
8. **CQ-P04** — Nettoyer `PageantClient.Dispose` (commentaire ou retrait de `IDisposable`).
9. **CQ-P05** — Extraire helper `SetDynamic(propertyName, action)` dans `RdpActiveXHost`.
10. **SEC-P06** — Retirer la branche "single broker match → accept" dans `CredentialAutofill`.
11. **PERF-P02/P03** — Documenter le double-check `TunnelManager.OpenTunnelAsync:181-185` comme invariant.

### Non-actionable / Documentation only

12. **SEC-P04** — Pageant DACL explicite : uniquement si environnement classifié.
13. **SEC-P05, CQ-P06** — Ajouter section `SECURITY.md` "Known .NET string lifetime limitations for credentials".
14. **SEC-05** — Intégrer `dotnet list package --vulnerable` dans la CI (job dédié).

---

## What Was NOT Flagged (and why)

- **`dynamic` usage dans `RdpActiveXHost`** — requis pour COM IDispatch, pas un code smell ici.
- **Reflection dans `SshShellSession`** (window-change via champ privé SSH.NET) — bien guardée par try-catch et commentée, fallback gracieux.
- **`Task.Run(() => client.Connect())`** dans TunnelManager — SSH.NET `Connect()` est synchrone, le wrap est correct ; pas de pattern async-over-sync inapproprié côté appelant.
- **Pageant `Dispose` vide** — classé Minor, pas un bug.
- **Process.Start("mstsc.exe", quoted .rdp path)** dans `RdpHandler` — l'ACL du .rdp file est appliquée via `SecureFileWriter.WriteAndProtect`, pas d'injection possible (le contenu est sanitisé par `RdpFileGenerator.SanitizeValue`).
- **`SshConnectionFactory` static class** — utilitaire pur, DI inutile. Non-flaggé comme violation ARCH-04.
- **Absence de rate-limiting sur reconnect auto** — borné à 20 tentatives (`MaxReconnectAttempts = 20`), `CancelAutoReconnect` flag respecté. Suffisant.
- **`catch (Exception cleanupEx)` dans `TunnelManager`** — contrat IDisposable ("Dispose must not throw") ; les Debug logs sont corrects ici.
- **`FileLogger.Info("Pageant: X key(s) loaded ...")`** — le comment du key est loggé (identité utilisateur souvent dedans). Acceptable pour troubleshooting, mentionné en SEC-08.
- **`ForwardedPortDynamic/Remote` bind à `127.0.0.1`** — correct, pas de CWE-200.
- **Pas de certificate pinning pour `WebSocketVncProxy`** — hors-scope de cet audit (VNC, pas RDP/SSH).
- **Plink `-pwfile` plutôt que `-pw`** — correct, évite l'exposition dans la command line. ACL atomique via `SecureFileWriter`.
- **`InputValidator.EscapeForDoubleQuotedString` sur `keyPath`** — utilisation cohérente, et `TryValidateKeyPath` refuse `"` et `\0` en amont. Défense en profondeur.

---

## Annex — Files Read During Audit

**SSH layer (read in full):**
- `src/Heimdall.Ssh/SshConnectionFactory.cs` (224 L)
- `src/Heimdall.Ssh/HostKeyStore.cs` (139 L)
- `src/Heimdall.Ssh/TunnelManager.cs` (780 L)
- `src/Heimdall.Ssh/Pageant/PageantClient.cs` (412 L)
- `src/Heimdall.Ssh/Plink/PlinkTunnelRunner.cs` (512 L)
- `src/Heimdall.Ssh/SshShellSession.cs` (334 L)
- `src/Heimdall.Ssh/TunnelSession.cs` (141 L)
- `src/Heimdall.Ssh/SshConnectionParams.cs` (56 L)
- `src/Heimdall.Ssh/FailureClassifier.cs` (172 L)
- `src/Heimdall.Ssh/AuthPreflightChecker.cs` (134 L)
- `src/Heimdall.Ssh/GatewayChainResolver.cs` (126 L)
- `src/Heimdall.Ssh/ServerHealthMonitor.cs` (307 L)
- `src/Heimdall.Ssh/Pageant/PageantHostAlgorithm.cs` (105 L)
- `src/Heimdall.Ssh/Pageant/PageantKeyWrapper.cs` (89 L)
- `src/Heimdall.Ssh/Pageant/PageantKey.cs` (26 L)

**RDP layer (read in full):**
- `src/Heimdall.Rdp/ActiveX/RdpActiveXHost.cs` (658 L)
- `src/Heimdall.Rdp/CredentialAutofill.cs` (758 L)
- `src/Heimdall.Rdp/CredentialManagerHelper.cs` (210 L)
- `src/Heimdall.Rdp/RdpFileGenerator.cs` (281 L)
- `src/Heimdall.Rdp/AspectRatioManager.cs` (114 L)
- `src/Heimdall.Rdp/IRdpSession.cs` (76 L)
- `src/Heimdall.Rdp/RdpRedirectionOptions.cs` (83 L)
- `src/Heimdall.Rdp/ActiveX/ComInterfaces.cs` (97 L)

**App wiring (read in full):**
- `src/Heimdall.App/Services/Handlers/SshHandler.cs` (614 L)
- `src/Heimdall.App/Services/Handlers/RdpHandler.cs` (335 L)
- `src/Heimdall.App/Services/Handlers/CitrixHandler.cs` (272 L)
- `src/Heimdall.App/Services/Handlers/SshSessionDiagnosticFactory.cs` (141 L)
- `src/Heimdall.App/Services/Handlers/RdpSessionDiagnosticFactory.cs` (90 L)
- `src/Heimdall.App/Services/ConnectionHelpers.cs` (extraits pertinents)
- `src/Heimdall.App/App.xaml.cs` (lignes 140-215 — wiring HostKeyStore)
- `src/Heimdall.App/Views/EmbeddedSshView.xaml.cs` (1470 L, audité via subagent)
- `src/Heimdall.App/Views/EmbeddedRdp/RdpHostDiagnosticFactory.cs` (50 L)

**Tests présents (non ré-audités mais recensés):**
- `tests/Heimdall.Ssh.Tests/{HostKeyStoreTests, SshConnectionFactoryTests, TunnelManagerTests, PageantClientTests, PlinkTunnelRunnerTests, AuthPreflightCheckerTests, GatewayChainResolverTests, FailureClassifierTests, RdpFileGeneratorTests, RdpRedirectionOptionsTests, AspectRatioManagerTests}.cs`

---

*Audit realized with the `project-audit` skill. Total findings: 0 🔴 / 6 🟠 / 9 🟡. Reproducible: un deuxième passage sur la même base sans modification doit produire exactement le même rapport.*
