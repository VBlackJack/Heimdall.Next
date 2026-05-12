# Audit UX — Parcours SSH

**Projet** : Heimdall.Next (.NET 10 + WPF)
**Build de référence** : v2026.042409 (Debug)
**Date** : 2026-04-25
**Auteur** : Julien Bombled (assisté Claude)
**Mode** : audit ciblé UX, profondeur structurée
**Référentiels** : Nielsen 10 heuristiques, méthodologie `project-audit/references/ux.md`
**Périmètre** : dialog de connexion SSH, terminal embarqué (pipe + SSH.NET), Tunnel Manager, host-key prompt, parcours end-to-end (création → host key → auth → terminal → reconnect → déconnexion)
**Hors périmètre** : audit accessibilité WCAG complet, audit code/sécurité, audit performance

---

## Status — All findings closed (19/19)

> Audit closed on 2026-04-28. Every finding below has been implemented,
> tested, and pushed to `master`. The original prose is kept as-is for
> historical context; the table below is the authoritative closure ledger.

| ID  | Title                                              | Commit    |
|-----|----------------------------------------------------|-----------|
| F1  | SshAuthHint state-aware (key/password/agent)       | `0c36b9a` |
| F2  | Trust this session host-key + Copy fingerprint     | `b2379d4` |
| F3  | EmbeddedSshView Connecting overlay & two-phase init| `083c62f`, `6ba0619`, `e16613f`, `fb77200` |
| F4  | LocaleChanged subscription in EmbeddedSshView      | `e4180f3` |
| F5  | Disconnect marker localized EN/FR                  | `7184426` |
| F6  | (covered in tech-debt sweep)                       | `3cbc431` |
| F7  | Dead i18n keys removed                             | `f78008d` |
| F8  | Dead i18n keys removed                             | `f78008d` |
| F9  | Themed paste confirmation dialog with preview      | `ba7307f` |
| F10 | SSH Test Connection probe (button + chip)          | `3fd3cd2` |
| F11 | Manual tunnel creation dialog                      | `42271d8` |
| F12 | StatusTunnelClosed always emitted                  | `001aecd` |
| F13 | Bounded auto-reconnect SSH with countdown          | `1635c3d` |
| F14 | (covered in tech-debt sweep)                       | `3cbc431` |
| F15 | (covered in tech-debt sweep)                       | `3cbc431` |
| F16 | Pageant agent chip                                 | `3cbc431` |
| F17 | Fallback Content XAML for 7 SSH toolbar controls   | `a41667a` |
| F18 | Cancel SSH connect on tab close + tests            | `fb77200` |
| F19 | SshLoadingBar (dead UI) removed                    | `083c62f` |

Build baseline at closure: **4506 passing, 6 skipped** (`dotnet test Heimdall.slnx --no-build`).

## Résumé exécutif

Le parcours SSH est globalement fonctionnel et le travail i18n + a11y de base (AutomationProperties, LiveRegion polite) est sérieux comparé à beaucoup d'apps WPF. Trois zones tirent l'expérience vers le bas : (1) la *visibilité d'état* pendant la phase de connexion est défaillante — la barre de progression de `EmbeddedSshView` est du code mort ; (2) le hint d'authentification du dialog mentionne toujours « SSH password authentication » même quand une clé est configurée, ce qui ment activement à l'utilisateur ; (3) le prompt host-key TOFU n'offre aucune granularité (« trust once » vs « trust permanently »), forçant un choix permanent dans une situation où l'utilisateur peut encore vouloir investiguer.

À l'échelle du parcours, on compte **2 findings critiques**, **12 importants**, **5 mineurs** (19 au total). Aucun ne casse la fonctionnalité ; tous dégradent la lisibilité du système ou la confiance.

---

## Findings par sévérité

### 🔴 Critique (2)

| # | Finding | Heuristique |
|---|---------|-------------|
| F1 | `SshAuthHint` est une constante, ment sur la méthode d'auth utilisée | UX-03 / UX-04 |
| F2 | Host-key prompt sans granularité d'engagement (trust once vs permanent) | UX-02 |

### 🟠 Important (12)

| # | Finding | Heuristique |
|---|---------|-------------|
| F3 | État « Connecting » jamais émis dans `EmbeddedSshView` — barre de progression dead code | UX-01 |
| F4 | `LocalizeButtons()` n'est pas branché à `LocaleChanged` — sessions SSH actives ne suivent pas le changement de langue | UX-04 |
| F5 | Marqueur de fin de session injecté dans le flux terminal en anglais hardcodé | UX-04 |
| F6 | Section SSH du `ServerDialog` mélange `{loc:Translate}` et `ApplyLocalization()` legacy — risque de labels vides | UX-04 |
| F7 | Trois doublons de clés i18n pour le warning paste (`PasteWarningMessage` ⇄ `PasteWarningDangerous`, etc.) | UX-04 |
| F8 | Trois clés i18n distinctes pour la colonne « Local Port » du Tunnel Manager | UX-04 |
| F9 | Smart Paste Guard utilise `MessageBox.Show` natif — break le thème Dracula | UX-04 |
| F10 | Aucun bouton « Test Connection » dans la section SSH du dialog | UX-03 |
| F11 | Création manuelle de tunnel impossible — Tunnel Manager est observe-only | UX-06 |
| F12 | Fermeture silencieuse de tunnel sans `error` → aucune notification | UX-01 |
| F13 | Auto-reconnect SSH absent (RDP en a un borné à 20 essais) | UX-06 |
| F14 | Pas d'indicateur visuel « Pageant chargé / N clés disponibles » | UX-05 |

### 🟡 Mineur (5)

| # | Finding | Heuristique |
|---|---------|-------------|
| F15 | `BrowseSshKeyCommand` est un `[RelayCommand]` vide — dead binding | UX-04 (technique) |
| F16 | Endpoint affiché en SSH.NET vs pipe-mode incohérent (`user@host:port` vs `L("SshEndpointViaPlink")`) | UX-04 |
| F17 | Labels du toolbar SSH non définis dans XAML — si `_localizer` est null à l'init, contenu vide | UX-04 |
| F18 | LiveRegion `polite` du status text ne signale jamais « Connecting » au lecteur d'écran | UX-08 / a11y |
| F19 | `SshLoadingBar` reste `IsHitTestVisible="False"` + `IsIndeterminate="True"` en XAML — animation toujours active si jamais elle redevenait visible | cosmétique |

---

## Findings détaillés par catégorie

### Visibilité de l'état système (UX-01)

**Score : 1/3**

#### F3 🟠 — Pas de feedback « connexion en cours » dans le terminal embarqué

**Fichiers** : `src/Heimdall.App/Views/EmbeddedSshView.xaml` (lignes 123–132), `src/Heimdall.App/Views/EmbeddedSshView.xaml.cs` (lignes 277, 309, 1442–1473).

La XAML déclare un `ProgressBar x:Name="SshLoadingBar"` indéterminé. Le code-behind n'expose la visibilité qu'à travers `UpdateStatus("Connecting")` (lignes 1460–1461). Or `UpdateStatus("Connecting")` n'est appelé **nulle part dans le projet** (vérifié par grep : la seule occurrence est l'entrée du `switch` qui mappe la chaîne vers `SshSessionStatusConnecting`). Les deux points d'entrée du contrôle (`InitializeSession`, `InitializeTerminalSession`) appellent directement `UpdateStatus("Connected")` après que le handler a déjà fait le travail réseau.

Conséquence : pendant la phase d'établissement (résolution DNS, TCP, key exchange, auth), l'utilisateur ne voit aucun signal dans la zone terminal — l'overlay « Reconnect » n'est pas affiché, la barre de progression non plus, le titre reste vide. Le seul indicateur global vit dans `MainViewModel.StatusText` (status bar bas de fenêtre), peu visible quand l'utilisateur regarde la zone terminale.

**Recommandation** : appeler `UpdateStatus("Connecting")` depuis `SshHandler.ConnectAsync` avant le `EnsureCoreWebView2Async` (ou exposer une `void NotifyConnecting(string endpoint)` que le handler appelle avant de démarrer le travail). Au pire, remplacer le `Visibility="Visible"` par défaut de `SshLoadingBar` par `Collapsed` et écrire `UpdateStatus("Connecting")` au tout début de `InitializeSession` puis `UpdateStatus("Connected")` au milieu — cela donne au moins un flash visible.

#### F12 🟠 — Tunnel fermé silencieusement sans message d'erreur

**Fichier** : `src/Heimdall.App/ViewModels/Tunnels/TunnelsViewModel.cs` (lignes 252–260).

```csharp
private void OnTunnelClosed(int localPort, string? error)
{
    RefreshList();
    if (!string.IsNullOrEmpty(error))
    {
        _main.StatusText = _localizer.Format("StatusTunnelClosed", localPort) + $" ({error})";
    }
}
```

Quand `error` est null/vide (cas d'une fermeture côté serveur sans raison communiquée), la liste se met à jour silencieusement. La ligne disparaît du DataGrid sans status bar message. Si l'utilisateur n'avait pas le panneau ouvert, il ne sait rien.

**Recommandation** : émettre toujours `StatusTunnelClosed`, et — c'est la clé UX — déclencher un toast léger (ou flash de la badge) dès qu'un tunnel disparaît, surtout quand c'est non-initié par l'utilisateur (pas via `Close`/`CloseAll` commands).

#### F18 🟡 — LiveRegion ne couvre pas la phase « Connecting »

**Fichier** : `src/Heimdall.App/Views/EmbeddedSshView.xaml` (ligne 117).

`StatusTextBlock` a `AutomationProperties.LiveSetting="Polite"` — c'est correct. Mais comme `UpdateStatus("Connecting")` n'est jamais appelé (cf. F3), le lecteur d'écran annonce « Connected » → « Disconnected » et saute la phase intermédiaire. Conséquence directe de F3 : corriger F3 corrige F18 « gratuitement ».

---

### Contrôle utilisateur et liberté (UX-02)

**Score : 2/3**

#### F2 🔴 — Host-key prompt sans granularité d'engagement

**Fichiers** : `src/Heimdall.App/Views/Dialogs/HostKeyPromptDialog.xaml` (lignes 169–188), `src/Heimdall.App/ViewModels/Dialogs/HostKeyPromptDialogViewModel.cs`.

Le dialog présente deux boutons : **Reject** et **Accept**. Quand l'utilisateur clique Accept (qu'on soit en first-use TOFU ou en mismatch), le fingerprint est **définitivement** stocké dans `HostKeyStore`. Il n'existe pas d'option intermédiaire :

- pas de « Trust once » (utile pour tester un endpoint avant de décider) ;
- pas de « Show full key » / « Copy fingerprint » pour comparer hors-bande ;
- pas de lien direct vers la documentation host-key cachée du serveur ;
- pas d'option « Re-display dans 24h » pour les rotations programmées.

C'est particulièrement gênant en cas de **mismatch** : la couleur passe au rouge (bonne pratique visuelle, lignes 43–50), mais la seule façon de continuer à investiguer sans changer l'état persistant est de cliquer Reject — ce qui interrompt le workflow et oblige à réinitier la connexion après vérification manuelle.

**Recommandation** : ajouter un troisième bouton « Trust once (this session) » qui injecte le fingerprint dans une `HashSet<string>` mémoire de `HostKeyStore`, jamais persistée. En mismatch, ce bouton devient prioritaire (il devrait être l'action par défaut, pas Accept). Ajouter aussi un `Hyperlink` « Copy presented fingerprint » à droite du `PresentedFingerprintBox`.

#### F11 🟠 — Tunnel Manager est observe-only

**Fichier** : `src/Heimdall.App/ViewModels/Tunnels/TunnelsViewModel.cs` (commandes `Close`, `CloseAll`, `CopyPort`, `TogglePanel`).

Le panneau et l'onglet Tunnels n'offrent que des actions destructives ou de lecture seule. Aucun bouton « + New Tunnel ». Pour qu'un tunnel apparaisse, il faut qu'une session SSH avec gateway soit déjà initiée. L'utilisateur power-user qui veut juste un port-forward ad-hoc (par ex. pour pointer un navigateur ou un client SQL local) doit créer un faux serveur, le connecter, puis ne pas utiliser la session shell — workflow détourné.

**Recommandation** : ajouter une commande `NewTunnelCommand` ouvrant un mini-dialog (Local Port, Remote Host, Remote Port, Gateway). Cible courte : 5 champs, déjà tous présents en backend (`TunnelService.SetupTunnelIfNeededAsync`). Donne aussi un bénéfice secondaire : le panneau Tunnels devient un vrai « Tunnel Manager » comme le suggère son nom.

---

### Prévention des erreurs et récupération (UX-03)

**Score : 2/4**

#### F1 🔴 — `SshAuthHint` ment sur la méthode d'authentification

**Fichier** : `src/Heimdall.App/ViewModels/Dialogs/ServerDialogViewModel.cs` (ligne 229).

```csharp
public string SshAuthHint => L("ServerDialogSshAuthHintPassword");
```

La propriété est une **constante**. Le hint affiché sous le champ password (`ServerDialog.xaml` ligne 715) est toujours `"SSH password authentication"`, peu importe l'état :

- si l'utilisateur a saisi un `SshKeyPath` → le hint dit toujours « password » alors que la clé prendra le dessus ;
- si Pageant est en cours → idem, hint trompeur ;
- si rien n'est rempli → hint suggère que le password est suffisant alors que `SshHandler` rejettera la connexion.

Cette propriété viole UX-03 (prévention d'erreurs) ET UX-04 (cohérence) : la promesse visuelle du dialog ne correspond pas au comportement réel du handler. C'est le finding le plus actionnable de ce rapport — un computed property correct résout tout.

**Recommandation** :

```csharp
public string SshAuthHint => L(
    !string.IsNullOrWhiteSpace(SshKeyPath) ? "ServerDialogSshAuthHintKey"
    : !string.IsNullOrEmpty(SshPassword)   ? "ServerDialogSshAuthHintPassword"
    : "ServerDialogSshAuthHintAgent");

partial void OnSshKeyPathChanged(string value) =>
    OnPropertyChanged(nameof(SshAuthHint));
partial void OnSshPasswordChanged(string value) =>
    OnPropertyChanged(nameof(SshAuthHint));
```

Et créer les deux clés i18n manquantes (`ServerDialogSshAuthHintKey`, `ServerDialogSshAuthHintAgent`).

#### F10 🟠 — Pas de « Test Connection » dans la section SSH

Recherche : la clé `ErrorSshTestConnectionFailed` existe dans `locales/en.json` (ligne 452), suggérant que la fonctionnalité existe quelque part, mais elle n'est pas exposée dans la zone SSH visible (lignes 670–733 du `ServerDialog.xaml`). L'utilisateur doit donc : sauvegarder → fermer le dialog → tenter une connexion → lire l'erreur dans le terminal/status → rouvrir le dialog → corriger. Cycle long pour une mauvaise passphrase ou un username typo'd.

**Recommandation** : ajouter un bouton « Test » à droite du hint `SshAuthHint`, qui appelle `AuthPreflightChecker` + un seul SSH banner-grab (déjà implémenté dans `SshFingerprinter`). Affiche le résultat sous forme de chip ✓/✗ avec message i18n. Ne pas faire un full-shell — ça suffit pour valider host + auth.

#### F14 🟠 — Pageant invisible pré-connexion

**Fichiers** : `src/Heimdall.Ssh/Pageant/PageantClient.cs`, `src/Heimdall.Ssh/Agents/SshAgentRegistry.cs`.

Le système détecte Pageant + OpenSSH Agent automatiquement. En cas d'échec, le message `ErrorSshPageantNotRunning` (`en.json` ligne 120) est explicite :

> No SSH agent is running. Start Windows OpenSSH Agent or Pageant and load the SSH key before connecting.

Bonne récupération. Mais avant la connexion, le dialog n'affiche **rien** sur l'état des agents : l'utilisateur ne sait pas si Pageant est lancé ni quelles clés y sont chargées. Workflow réel observé : on tente la connexion, on obtient l'erreur, on lance Pageant, on charge la clé, on reteste. Trois aller-retours.

**Recommandation** : sous le hint d'auth, afficher un petit chip statique :
- `✓ SSH agent: Pageant (3 keys loaded)` (vert)
- `⚠ SSH agent: Pageant running, no keys loaded` (orange)
- `○ No SSH agent detected` (gris)

Le chip se rafraîchit à l'ouverture du dialog (et sur clic sur le chip → re-scan). Coût : ~30 lignes de code, gros bénéfice.

---

### Cohérence et standards (UX-04)

**Score : 4/9** — c'est la catégorie la plus dégradée.

#### F4 🟠 — Sessions SSH actives ne suivent pas le changement de langue

**Fichier** : `src/Heimdall.App/Views/EmbeddedSshView.xaml.cs` (lignes 1155–1184, méthode `LocalizeButtons()`).

`LocalizeButtons()` est appelée une seule fois, depuis `InitializeSession` (ligne 274) ou `InitializeTerminalSession` (ligne 306). Aucune souscription à `_localizer.LocaleChanged`. Conséquence : si l'utilisateur a une session SSH ouverte et bascule la langue dans Settings, la barre d'outils (Disconnect, Reconnect, badges, tooltips, AutomationProperties) reste figée dans la langue d'origine.

**Recommandation** : dans `InitializeSession` après `LocalizeButtons()`, ajouter `_localizer!.LocaleChanged += OnLocaleChanged;` avec `private void OnLocaleChanged(string _) => LocalizeButtons();`. Détacher proprement dans `Dispose()`.

#### F5 🟠 — Marqueur fin de session hardcodé en anglais

**Fichier** : `src/Heimdall.App/Views/EmbeddedSshView.xaml.cs` (ligne 886).

```csharp
var disconnectText = $"\r\n\x1b[90m[Session disconnected: {errorMessage}]\x1b[0m\r\n";
```

Cette ligne injecte du texte anglais dans le flux terminal. `errorMessage` peut déjà être localisé selon la source, mais le préfixe `[Session disconnected:` ne l'est pas.

**Recommandation** : utiliser `string.Format(L("SshTerminalDisconnectMarker"), errorMessage)` avec une nouvelle clé i18n (`SshTerminalDisconnectMarker = "[Session terminée : {0}]"` en FR).

#### F6 🟠 — Mélange `{loc:Translate}` + `ApplyLocalization()` legacy dans la section SSH du `ServerDialog`

**Fichier** : `src/Heimdall.App/Views/Dialogs/ServerDialog.xaml` (lignes 686–731).

Plusieurs labels n'ont pas de `Text=` ni de `{loc:Translate}` :

```xml
<TextBlock x:Name="DlgSrv_BasicSshCredentialsTitle" Style="{StaticResource DialogSectionTitleStyle}"/>
<TextBlock x:Name="DlgSrv_BasicSshUsernameLabel" Style="{StaticResource LabelStyle}" .../>
<TextBlock x:Name="DlgSrv_BasicSshKeyLabel" Style="{StaticResource LabelStyle}" .../>
```

Ces labels comptent sur l'`ApplyLocalization()` du code-behind (pattern legacy mentionné dans `CLAUDE.md`). Si l'init de localisation est court-circuitée pour une raison quelconque (DI bug, async race), les labels s'affichent vides. À côté, `DlgSrv_BasicSshPasswordLabel` (ligne 708) utilise `Text="{loc:Translate ServerDialogLabelPassword}"` — pattern moderne. La section est inconsistante avec elle-même.

**Recommandation** : convertir tous les `TextBlock` de cette section au pattern `{loc:Translate}`. Coût : ~10 lignes XAML, élimine un risque de label vide et aligne avec le reste du dialog.

#### F7 🟠 — Doublons d'i18n keys pour le warning paste

**Fichier** : `locales/en.json`.

| Clé legacy | Clé moderne | Contenu |
|---|---|---|
| `PasteWarningDangerous` (ligne 1741) | `PasteWarningMessage` (ligne 2312) | `"The clipboard contains a potentially dangerous command.\n\nPaste anyway?"` |
| `PasteWarningMultiline` (1742) | `PasteMultiLineMessage` (2314) | `"Paste {0} lines into terminal?\n\nMulti-line paste executes commands automatically."` |
| `PasteWarningMultilineTitle` (1743) | `PasteMultiLineTitle` (2313) | `"Multi-line Paste"` |

Le code-behind (`EmbeddedSshView.xaml.cs` lignes 811–826) utilise les noms modernes (`PasteWarningMessage`, `PasteWarningTitle`, `PasteMultiLineMessage`, `PasteMultiLineTitle`). Les noms legacy sont du code mort en JSON. Risque : un développeur peut re-router vers les anciens et créer une divergence FR/EN si seul un côté est mis à jour.

**Recommandation** : supprimer les trois entrées legacy, vérifier qu'aucune référence ne reste (`Grep "PasteWarningDangerous|PasteWarningMultiline\b|PasteWarningMultilineTitle"`).

#### F8 🟠 — Trois clés i18n distinctes pour « Local Port »

**Fichier** : `locales/en.json`.

- `ColLocalPort` (ligne 11) — Tunnel DataGrid (générique)
- `TunnelsColLocalPort` (ligne 1826) — Tunnels TAB
- `TunnelPanelColPort` (ligne 2164) — Tunnels panneau retractable

Trois traductions à maintenir pour la même colonne. Risque qu'un onglet et le panneau dérivent vers des libellés différents pour la même donnée (déjà observé : « Local Port » vs « Local » vs « Port »).

**Recommandation** : consolider sur `TunnelLocalPort` (clé unique, namespace clair). Migrer les trois usages, supprimer les anciennes.

#### F9 🟠 — Smart Paste Guard utilise `MessageBox.Show` natif

**Fichier** : `src/Heimdall.App/Views/EmbeddedSshView.xaml.cs` (lignes 809–814 et 824–829).

```csharp
var proceed = System.Windows.MessageBox.Show(
    owner, L("PasteWarningMessage"), L("PasteWarningTitle"),
    MessageBoxButton.YesNo, MessageBoxImage.Warning);
```

Le `MessageBox` Win32 natif ignore complètement le thème Dracula, le DialogCommonStyles.xaml et les AutomationProperties personnalisées. Sur un thème sombre, on obtient un dialog blanc Windows qui paraît étranger à l'app.

**Recommandation** : créer un `PasteConfirmDialog` calé sur `DialogCommonStyles.xaml` (équivalent de `HostKeyPromptDialog` mais minimal), avec deux boutons. Le fait d'avoir une vraie Window WPF permet aussi d'afficher un *aperçu* du contenu (premières lignes) — gros plus UX pour aider à la décision.

#### F16 🟡 — Endpoint affiché incohérent SSH.NET vs pipe-mode

**Fichier** : `src/Heimdall.App/Views/EmbeddedSshView.xaml.cs` (lignes 276 vs 308).

```csharp
// InitializeSession:
EndpointTextBlock.Text = endpoint;            // "user@host:port"

// InitializeTerminalSession:
EndpointTextBlock.Text = L("SshEndpointViaPlink");  // localized "via Plink"
```

Selon le transport, l'utilisateur voit soit `julien@srv01:22` soit « via Plink ». L'info utile n'est pas la même selon le contexte : « via Plink » ne dit rien sur la cible, et `user@host:port` ne dit rien sur le transport.

**Recommandation** : afficher toujours `user@host:port` puis ajouter un suffix discret indiquant le transport seulement quand c'est non-default :
```
julien@srv01:22  ·  via Plink
```
Garde l'info principale visible, complète quand pertinent.

#### F17 🟡 — Labels du toolbar SSH non définis dans XAML

**Fichier** : `src/Heimdall.App/Views/EmbeddedSshView.xaml` (lignes 25–65).

```xml
<Button x:Name="DisconnectButton" .../>   <!-- Pas de Content= -->
<Button x:Name="ReconnectButton" .../>    <!-- Pas de Content= -->
```

Le `Content` est défini dans `LocalizeButtons()` (1158–1163). Si le `_localizer` n'a pas encore été assigné quand le contrôle s'affiche (race ou bug DI), les boutons s'affichent vides. Mineur en pratique car `EmbeddedSessionManager` set `Localizer` avant `Initialize*Session`, mais c'est un contrat invisible.

**Recommandation** : mettre des `Content="Disconnect"` (anglais par défaut) en XAML — fallback safe si la localisation n'est pas appliquée.

---

### Reconnaissance plutôt que rappel (UX-05)

**Score : 1/2**

#### F14 (déjà listé) — Pageant invisible pré-connexion

Voir UX-03. Même finding, autre angle : l'utilisateur doit *se rappeler* d'avoir lancé Pageant et chargé la clé, plutôt que de le *reconnaître* à un état UI.

---

### Efficacité pour utilisateurs experts (UX-06)

**Score : 2/3**

#### F11 (déjà listé) — Pas de création manuelle de tunnel

Voir UX-02. Frustre les power users.

#### F13 🟠 — Pas d'auto-reconnect SSH (alors que RDP en a un)

**Référentiel** : `CLAUDE.md` mentionne pour RDP « Auto-reconnect: Bounded retry (`MaxReconnectAttempts = 20`) with `CancelAutoReconnect` flag ». Pour SSH, aucun équivalent — `EmbeddedSshView.OnDisconnected` (lignes 875–898) affiche l'overlay et attend un clic utilisateur.

C'est une asymétrie de comportement entre deux protocoles : un utilisateur qui connaît le RDP s'attend à ce que SSH se reconnecte de la même façon. La nature TCP de SSH (vs RDP qui a son propre protocole de session-resume) explique en partie pourquoi c'est moins évident, mais une boucle simple « retry après 2s, 5s, 15s, max 3 tentatives » est faisable et utile sur les liens flaky.

**Recommandation** : implémenter un auto-reconnect borné en option (toggle dans Settings, défaut **OFF** pour ne pas surprendre). Quand actif, l'overlay devient un compte à rebours « Reconnecting in 3s… [Cancel] ». Compatible avec l'auth keyless (Pageant/clé en mémoire) ; ne tente pas si la déconnexion est due à `KeyRejected` ou `TooManyAuthFailures`.

---

### Aide et documentation (UX-08)

**Score : 1/2** — non audité en profondeur, mais une observation utile :

Les messages d'erreur SSH sont d'excellente qualité (`en.json` lignes 32–62). Beaucoup terminent par une **action** :

> `ErrorSshKeyboardInteractiveNoPassword`: "Server requires interactive authentication but no password is configured."
> `ErrorPlinkPassphraseUnsupported`: "Plink fallback cannot unlock a passphrase-protected key file. Load the key in Pageant instead."
> `ErrorSshAgentHasNoIdentities`: "...Load an SSH key before connecting."

C'est conforme à la heuristique UX-03 (les erreurs suggèrent des fixes). À garder comme référence pour le reste du projet.

---

## Plan d'action

Recommandé dans cet ordre, basé sur ratio impact/effort.

### Lot 1 — Quick wins (~ 1 demi-journée)

1. **F1** — Corriger `SshAuthHint` pour qu'il reflète l'état réel de l'auth choisie. Ajouter 2 clés i18n (`ServerDialogSshAuthHintKey`, `ServerDialogSshAuthHintAgent`). Test : taper un keypath, vérifier que le hint passe à « SSH key authentication ».
2. **F5** — Localiser le marqueur de fin de session injecté dans le terminal.
3. **F7** — Supprimer les 3 doublons paste-warning de `en.json` + `fr.json` après vérification qu'aucune référence ne subsiste.
4. **F8** — Consolider les 3 clés « Local Port » sur `TunnelLocalPort`.
5. **F17** — Ajouter `Content="Disconnect"` / `Content="Reconnect"` en XAML comme fallback.

### Lot 2 — Visibilité d'état (~ 1 journée)

6. **F3** — Brancher `UpdateStatus("Connecting")` depuis `SshHandler.ConnectAsync` ou via un nouveau hook `EmbeddedSshView.NotifyConnecting()`. Conséquence directe : F18 fixé.
7. **F4** — Souscrire `EmbeddedSshView` à `LocaleChanged` et re-appeler `LocalizeButtons()` ; détacher dans `Dispose`.
8. **F12** — Toujours émettre `StatusTunnelClosed` dans `OnTunnelClosed`, ajouter un flash visuel sur la badge.

### Lot 3 — Fonctionnalités (~ 2-3 journées)

9. **F2** — Ajouter un bouton « Trust once » dans `HostKeyPromptDialog` + memory-only host-key store. En mismatch, faire de cette action le défaut.
10. **F14** — Chip d'état Pageant/Agent dans la section SSH du dialog.
11. **F10** — Bouton « Test » dans la section SSH (preflight + banner grab).
12. **F11** — Bouton « + New Tunnel » dans le panneau Tunnels + dialog ad-hoc.

### Lot 4 — Plus gros chantiers (~ 1 semaine)

13. **F9** — Remplacer le `MessageBox.Show` du Smart Paste par un dialog WPF thémé avec preview du contenu.
14. **F13** — Auto-reconnect SSH borné, opt-in via Settings, avec compte à rebours dans l'overlay.
15. **F6** — Migration complète de la section SSH du `ServerDialog` vers `{loc:Translate}`.

### Reportés / non bloquants

- **F15** — `BrowseSshKeyCommand` vide : nettoyage technique, pas urgent.
- **F16** — Endpoint cohérent SSH.NET / pipe-mode : nice-to-have.
- **F19** — `SshLoadingBar` dead code : disparaît avec F3.

---

## Ce qui n'a PAS été flaggé (et pourquoi)

Pour éviter les passes UX qui « trouvent toujours plus » et pour respecter `references/ux.md` § DO NOT FLAG :

- **Le choix des couleurs Dracula du terminal** — décision de design produit assumée, pas un sujet UX.
- **L'absence d'onboarding SSH dédié** — feature request, pas un défaut UX. L'app a déjà un onboarding global 3 étapes.
- **Le wording exact de chaque label** — sauf les cas où le label *ment* (F1) ou ne *traduit pas* (F5), les libellés sont cohérents et compréhensibles.
- **La densité visuelle de l'overlay Reconnect** — minimaliste, deux boutons clairs, fait son job.
- **La présence du health panel** — nice feature, pas un défaut s'il est collapsed par défaut.
- **L'absence de raccourci clavier global pour Disconnect/Reconnect** — la palette de commandes (Ctrl+K) couvre déjà ce besoin.
- **Le keep-alive invisible (CR toutes les 240s)** — pratique standard SSH, ne mérite pas un indicateur UI.
- **Les messages d'erreur SSH eux-mêmes** — excellente qualité (cf. UX-08 ci-dessus), à utiliser comme référence pour le reste de l'app.

---

## Annexe : référentiel et méthode

- **Heuristiques** : Nielsen Norman Group, 10 usability heuristics (1994).
- **Source skill** : `C:\Users\User\AppData\Roaming\Claude\local-agent-mode-sessions\skills-plugin\…\skills\project-audit\references\ux.md`.
- **Méthodologie** : un seul pass exhaustif sur 8 catégories Nielsen, lecture directe des sources (XAML + code-behind + ViewModel + locales JSON), zéro spéculation — chaque finding pointe vers un fichier et une ligne vérifiables.
- **Audits SSH précédents dans `docs/`** : `../archive/2026/audits/audit-ux-ssh-2026-04-11.md`, `../archive/2026/audits/audit-gap-ssh-terminal-sftp-2026-04-19.md`. Le présent audit a été produit **sans les lire**, pour rester indépendant. Une comparaison croisée (« qu'avons-nous corrigé depuis 04-11 ? ») est un travail à part, pas couvert ici.
