# Audit UX — Surface RDP de Heimdall.Next

**Date :** 2026-04-30
**Auteur :** Claude (Cowork architect mode)
**Build audité :** v2026.042409 (Debug)
**Scope :** Quatre axes — Découverte & lancement, Création/édition de profil, Expérience de session live, Erreurs & edge cases — plus une revue d'accessibilité WCAG 2.1 AA sur l'ensemble.
**Méthodologie :** Audit code-only (XAML + C# + locales) selon les heuristiques de Nielsen, les critères WCAG 2.1 AA et les standards `dev-standards` du projet (Apache 2.0, anglais, zéro hardcoding, MVVM).

> ⚠️ **Note d'honnêteté.** Cet audit s'appuie sur des explorations parallèles de sous-agents qui ont parfois cité des numéros de ligne qu'ils ne pouvaient pas vérifier. Tous les findings du présent rapport ont été soit relus directement, soit marqués `[hypothèse]` quand la confirmation finale demande un test runtime. Trois faux positifs identifiés par les sous-agents ont été retirés (voir « Faux positifs écartés » en fin de doc).

## Résumé exécutif

La surface RDP est mature : 60+ clés de localisation dédiées, 24 codes de déconnexion mappés avec recovery hints, parité fr.json (26 clés `RdpDisconnect*` dans chaque locale), un overlay reconnect avec actions contextuelles, un autofill CredUI multi-broker, et un découplage propre Embedded/External. La plomberie est solide.

Les frictions UX restantes sont concentrées sur :

1. **L'accessibilité fine** — l'overlay reconnect n'est pas un dialogue modal du point de vue UI Automation, le focus n'y est pas déplacé à l'apparition, plusieurs indicateurs (HealthDot, RedirectionIndicators, ConnectionPhaseStepper) reposent sur la couleur seule pour signifier un état.
2. **Trois messages d'erreur RDP hardcodés en anglais** dans `RdpHandler.cs` qui contournent l'i18n.
3. **Plusieurs frictions discrètes** dans le ServerDialog côté RDP (effet `RdpUseGlobalDefaults` invisible, manque de tooltips sur les checkboxes Performance, gestion du couple `DOMAIN\user`).
4. **Ergonomie de session** — sortie fullscreen sans hint visible, feedback `SendKeys` muet, "Skip" manquant pendant le compte à rebours de stabilisation 10 s.

Total : **2 critiques, 14 importants, 17 mineurs**. Aucun finding ne bloque la release ; les deux critiques sont l'i18n des erreurs RdpHandler et l'accessibilité de l'overlay reconnect.

## Findings par sévérité

### 🔴 Critiques (2)

| ID | Axe | Fichier:ligne | Résumé |
|----|-----|---------------|--------|
| RDP-ERR-01 | Erreurs | `src/Heimdall.App/Services/Handlers/RdpHandler.cs:96, 110, 207` | Trois messages d'erreur en anglais hardcodés (`"Failed to decrypt RDP password."`, `"Failed to store RDP credentials."`, `"mstsc.exe did not start."`) bypass l'i18n et apparaîtront en anglais dans une UI française. Viole `dev-standards` (zero hardcoding) et la cohérence de localisation. |
| RDP-A11Y-01 | A11y | `src/Heimdall.App/Views/EmbeddedRdpView.xaml:343-435` + `xaml.cs:1763-1837` | L'overlay reconnect est un simple `Border` à `Visibility` togglé. Aucun déplacement programmatique du focus vers `OverlayReconnectButton` n'est effectué dans `ShowReconnectOverlay()` (vérifié — aucune occurrence de `.Focus()` sur le bouton dans le fichier). Conséquence : un utilisateur clavier ou screen reader ne sait pas qu'un dialogue est apparu, et continue de "taper dans le vide" sur la surface RDP désactivée. Viole WCAG 2.4.3 (Focus order) et 4.1.3 (Status messages). |

### 🟠 Importants (14)

| ID | Axe | Fichier:ligne | Résumé |
|----|-----|---------------|--------|
| RDP-LIVE-01 | Session | `EmbeddedRdpView.xaml:26-61` | Trois boutons mutuellement exclusifs (`DisconnectButton`, `CancelReconnectButton`, `CancelConnectButton`) ont tous `TabIndex="0"`. La logique de visibilité fait que UN seul est visible à la fois, donc pas de collision runtime ; mais l'ordre Tab démarre toujours par "le premier visible", ce qui veut dire que selon la phase, le clavier atterrit sur Disconnect, Cancel-reconnect ou Cancel-connect. C'est correct mais sémantiquement fragile et casse l'attente "Tab=action principale" (cf. RDP-LIVE-02 pour l'autre angle). |
| RDP-LIVE-02 | Session | `EmbeddedRdpView.xaml:26-30` | `DisconnectButton` est `TabIndex=0` — la première destination Tab dans la toolbar quand la session est connectée. C'est l'action **destructive**. Un utilisateur qui appuie Enter par réflexe coupe la session sans confirmation. Recommandation : reculer Disconnect en fin d'ordre Tab ou ajouter une confirmation discrète (modificateur `Shift`, ou `Hold to disconnect`). |
| RDP-LIVE-03 | Session | `EmbeddedRdpView.xaml:344-435` (overlay) | L'overlay reconnect ne mappe pas Esc → Close ni Enter → Reconnect. Pattern modal standard absent. À combiner avec RDP-A11Y-01. |
| RDP-LIVE-04 | Session | `EmbeddedRdpView.xaml.cs:159-170` (`SetFullscreen`) | Quand l'utilisateur entre en fullscreen, le `SessionHeaderBar` est masqué, supprimant tous les boutons de toolbar. La sortie repose entièrement sur `RdpFullscreenToggleShortcut` (settings). Aucun toast / banner transitoire ne rappelle le raccourci. Risque d'utilisateurs "perdus" en fullscreen, surtout si le raccourci a été remappé. Recommandation : flash 3 s "Press {shortcut} to exit fullscreen" à l'entrée. |
| RDP-LIVE-05 | Session | `EmbeddedRdpView.xaml.cs:1071-1098` (`UpdateAutofillState`) | L'état `Filled` se masque après 3 s (`AutofillFilledDisplayDuration`). Les états `TimedOut` et `Failed` n'ont pas de timer et persistent jusqu'à reconnexion / disposition. Asymétrie qui peut "polluer" la status bar avec un message d'échec qui n'est plus pertinent une fois que l'utilisateur a tapé son mot de passe à la main. Recommandation : appliquer le même timer 3 s aux 3 états transitoires, ou les masquer dès `OnRdpConnected`. |
| RDP-LIVE-06 | Session | `EmbeddedRdpView.xaml.cs:502-543` (`SendKeysToRemote`) | Aucun feedback UI après envoi de touches via `PostMessage`. Si le PostMessage échoue silencieusement (HWND non trouvé), l'utilisateur n'a aucun indice. Recommandation : flash transitoire 1.5 s "Ctrl+Alt+Del envoyé" dans la status zone. |
| RDP-LIVE-07 | Session | `EmbeddedRdpView.xaml.cs:783, 789-828` (stabilization) | Le compte à rebours 10 s `_initialResizeEnableDelay` bloque les changements de résolution dynamique. Aucun bouton "Skip" ; aucune action utilisateur ne raccourcit l'attente. Sur des sessions rapides (LAN), 10 s est long. Recommandation : "Skip stabilization" comme MenuItem secondaire dans la resolution menu, ou raccourcir à 5 s avec mesure télémétrie. |
| RDP-PROF-01 | Profil | `ServerDialog.xaml:1688` + `ServerDialogViewModel.cs:RdpUseGlobalDefaults` | Quand `RdpUseGlobalDefaults=true`, **toutes les options RDP du formulaire restent visiblement éditables** alors qu'elles sont ignorées au runtime. L'utilisateur édite et "sauvegarde" des valeurs qui n'auront aucun effet. Recommandation : binding `IsEnabled="{Binding !RdpUseGlobalDefaults}"` sur les sections "RDP Options" / "Device redirection" / "Advanced behavior" / "Experience" + bandeau d'avertissement. |
| RDP-PROF-02 | Profil | `ServerDialog.xaml:738-740` | Champ `RdpUsername` unique : aucune séparation `DOMAIN\user` ni hint format. La COM API RDP attend `domain` séparé ; `EmbeddedRdpView.SplitUsername()` parse au connect, mais l'utilisateur n'en a aucun signal lors de la saisie. Recommandation : ajouter le watermark `DOMAIN\user, user@domain ou user` + tooltip. |
| RDP-PROF-03 | Profil | `RdpConnectivityTester.cs` | Le bouton "Test RDP connection" ne teste qu'une accessibilité TCP (`port open`). Le message de succès ne le précise pas. Un utilisateur peut croire que NLA, TLS et credentials sont validés. Recommandation : reformuler le message succès en "TCP {host}:{port} reachable — credentials and RDP handshake not tested". |
| RDP-DISC-01 | Découverte | `CommandPaletteViewModel.Search.cs:138-159` | Quand l'utilisateur tape une bare IP, la palette propose **systématiquement** SSH + RDP en parallèle. Bonne intention, mais si l'IP est un endpoint non-Windows (Linux), proposer RDP est trompeur. Recommandation : pondérer l'ordre de proposition selon l'historique de connexion, ou ajouter un raccourci "Connect as" qui détecte le port ouvert avant de proposer. |
| RDP-ERR-02 | Erreurs | `RdpDisconnectActionPolicy.cs:24-28` | Les codes éligibles à "Edit profile" (2055, 2311, 2825, 3080, 3848, 4360) couvrent les cas credentials/security/certificate. Mais le code 2308 (NLA refused, mappé en `RdpDisconnectSecurityError`) n'est pas listé. Pourtant le message d'erreur invite à "verify NLA is supported... or disable NLA in the server profile" — sans bouton "Edit profile" pour faciliter l'action. Recommandation : ajouter 2308 à la liste, ou élargir la politique à *toute* erreur d'auth/security. |
| RDP-ERR-03 | Erreurs | `EmbeddedRdpView.xaml.cs:830-869` (`OnRdpDisconnected`) | Le flag `_userInitiatedDisconnect` supprime systématiquement l'overlay quand l'utilisateur clique "Disconnect". Aucune confirmation pré-disconnect, aucun moyen de revenir en arrière. Sur un click accidentel (Tab+Enter avec RDP-LIVE-02), session perdue. Recommandation : confirmation discrète "Disconnect from {server}? [Disconnect] [Cancel]" ou option settings "Confirm before disconnecting RDP sessions". |
| RDP-A11Y-02 | A11y | `EmbeddedRdpView.xaml:267-325` (`RedirectionIndicatorsPanel`) | Les 8 icônes utilisent `AutomationProperties.HelpText` mais **pas** `AutomationProperties.Name`. `HelpText` est une description secondaire (équivalent tooltip) — le `Name` est le label primaire annoncé par les screen readers. De plus, l'état activé/désactivé est encodé uniquement par couleur (`AccentBrush` vs `TextDisabledBrush`) — viole WCAG 1.4.1 (Use of color). Recommandation : (1) ajouter `Name` localisé sur chaque icône ; (2) ajouter un glyphe différenciant (✓ ou underline) en plus de la couleur. |

### 🟡 Mineurs (17)

| ID | Axe | Fichier:ligne | Résumé |
|----|-----|---------------|--------|
| RDP-LIVE-08 | Session | `EmbeddedRdpView.xaml:74-153` | Cinq boutons toolbar (Fullscreen, SendKeys, Split, Resolution, AntiIdle) glyphes-only. Tooltips et `AutomationProperties.Name` localisés présents (correct). Mais aucune légende / cheat-sheet de raccourcis n'est exposée. Cf. `dev-standards` "Recognition over recall". Optionnel : panneau Help (?) ou `Ctrl+?` |
| RDP-LIVE-09 | Session | `EmbeddedRdpView.xaml.cs:1153-1174` (`ConnectionPhaseStepper`) | Les 4 segments du stepper n'ont pas de `Name` individuel. Le screen reader annonce "phase stepper" mais pas la phase courante. Recommandation : `LiveSetting="Polite"` sur un TextBlock annexe annonçant la phase, ou Name dynamique. |
| RDP-LIVE-10 | Session | `EmbeddedRdpView.xaml:62-73` (`HealthDot`) | Encodage couleur seul (Success/Warning/Error/Disabled). Tooltip présent (correct), mais l'état n'est pas annoncé à un utilisateur clavier (Focusable=False). Recommandation : ajouter un glyphe (✓ ⚠ ✕ ∘) ou agrandir à 14 px et marquer `LiveSetting=Polite`. |
| RDP-LIVE-11 | Session | `EmbeddedRdpView.xaml.cs:1894-1926` (`OnOverlayCopyErrorClick`) | Vérifié : la copie ne contient que `ReconnectMessageText` + `ReconnectSecondaryText` (si visible) + `ReconnectCodeText` (si visible), joints par `Environment.NewLine`. Pas de timestamp, pas de hostname, pas de durée de session, pas de version d'app. Un report de support sera incomplet. Recommandation : enrichir avec un en-tête `Heimdall RDP error report — {ISO8601 UTC} — {server} ({host}:{port}) — session {duration}` puis le payload actuel. |
| RDP-LIVE-12 | Session | `EmbeddedRdpView.xaml:248-266` (`AntiIdleBadge`) | Tooltip "Anti-idle active" — le verbe "click" n'apparaît pas. Le badge est cliquable (handler `OnAntiIdleBadgeClick`) mais l'affordance est invisible. Recommandation : tooltip "Anti-idle active — click to disable for this session". |
| RDP-LIVE-13 | Session | `EmbeddedRdpView.xaml:343-435` | Pendant Reconnect, la surface RDP derrière l'overlay est blanche/grise. L'utilisateur ne peut pas relire le dernier état. Recommandation : laisser la surface en background semi-transparent. |
| RDP-LIVE-14 | Session | `EmbeddedRdpView.xaml.cs:OnResolutionButtonClick` | Le menu Resolution highlight le preset actif via `IsChecked`. Si l'utilisateur a redimensionné la fenêtre en mode "Fit", `_manualResolutionWidth` reste 0 et "Fit" reste coché alors que la résolution effective varie. Inexactitude visuelle mineure. |
| RDP-LIVE-15 | Session | `EmbeddedRdpView.xaml.cs:1417-1420` (`UpdateSessionStatus` Reconnecting) | "Reconnecting attempt {0} of {1}" sans ETA / temps écoulé global. `ReconnectElapsedText` existe mais n'est pas combiné. Recommandation : "Reconnecting attempt 5/20 — 23s elapsed". |
| RDP-PROF-04 | Profil | `ServerDialog.xaml:1689-1693` (Advanced expander) | `RdpBitmapCaching`, `RdpAutoReconnect`, `RdpDisableUdp` n'ont pas de tooltip explicatif. L'utilisateur ne sait pas si "Disable UDP" est une optimisation ou un fix. Recommandation : ajouter `RdpBitmapCachingHint`, `RdpAutoReconnectHint`, `RdpDisableUdpHint` aux locales et binder en tooltip. |
| RDP-PROF-05 | Profil | `ServerDialog.xaml:1554-1559` (RdpMode combo) | Les `ComboBoxItem` ont `Content=""` (string vide) — le contenu est probablement injecté au runtime via `ApplyLocalization()` (pattern legacy). Pour les nouveaux développements `{loc:Translate}` est préférable (live update sur changement de locale). Cohérence avec la migration i18n incrémentale documentée dans CLAUDE.md. |
| RDP-PROF-06 | Profil | `CommandPaletteViewModel.cs:Connect ad-hoc` | Après une connexion ad-hoc RDP réussie, aucune affordance "Save as profile". Recommandation : action "Save this session" dans le menu contextuel du tab. |
| RDP-DISC-02 | Découverte | Sidebar / TreeView | Aucun raccourci clavier exposé pour "Connect to highlighted server" (Enter classique attendu). À vérifier en runtime. [hypothèse] |
| RDP-ERR-04 | Erreurs | `RdpHostDiagnosticFactory.cs:30-32` | Le fallback `RdpDisconnectUnknownCode` est mappé. Vérifier que la locale précise un chemin de récupération ("Check the connection logs in Help > Diagnostics"). À auditer côté locale FR. |
| RDP-ERR-05 | Erreurs | `EmbeddedRdpView.xaml.cs:722-740` (`RetryBeginConnectAsync`) | Boucle de retry pré-connect 10×120 ms avec un seul log warn — pas de feedback UI pendant l'attente. Au pire 1.2 s sans signal. Recommandation : status "Initializing remote desktop surface…" pendant la boucle. |
| RDP-ERR-06 | Erreurs | `CredentialAutofill.cs:106-117` | Le pattern `TitlePattern` couvre 8 langues + "mstsc". Si un broker exotique (Windows Hello / Smartcard / FIDO) ouvre une dialog avec un titre non couvert, l'autofill timeout silencieusement. Recommandation : à mettre dans le `RdpAutofillTimedOut` un hint "If you use Windows Hello / smart card, this is normal — type your password manually". |
| RDP-A11Y-03 | A11y | `EmbeddedRdpView.xaml:367-369` (Severity icon) | Icône Segoe MDL2 U+E783 explicitement masquée (`AutomationProperties.Name=""`) — choix correct. Mais la severity (warning vs error vs terminal) est encodée par `OverlaySeverityStrip` couleur + glyphe. Si la couleur est absente, le glyphe seul est ambigu. Recommandation : préfixer le `ReconnectMessageText` par "(Warning)" / "(Error)" selon `RdpDisconnectSeverity`. |
| RDP-A11Y-04 | A11y | `Themes/CommonControls.xaml` (`ToolbarGhostButtonStyle`) [hypothèse — style non lu] | Padding "6,2" + FontSizeBody ~13-14 px → cible tactile estimée 24x18 px. WCAG 2.5.5 AA recommande 44x44 (relaxation 24x24 si espacement adjacent suffisant). Recommandation : `MinHeight=32` + tester sur tablette tactile. |

## Plan d'action proposé

Ordonné par ROI (impact / effort) et compatible avec un travail Claude Code → Codex en mode prompt-by-prompt :

**Sprint 1 — i18n + a11y modale (prio 1)**

1. RDP-ERR-01 : Extraire les 3 messages hardcodés de `RdpHandler.cs` vers les locales (`RdpErrorDecryptPassword`, `RdpErrorStoreCredentials`, `RdpErrorMstscLaunch`). Ajouter dans en.json + fr.json. Wiring via `LocalizationManager`. Test : la VM/handler reçoit les messages localisés.
2. RDP-A11Y-01 : Dans `ShowReconnectOverlay()`, après `ReconnectOverlay.Visibility = Visible`, ajouter `Dispatcher.BeginInvoke(DispatcherPriority.Input, () => OverlayReconnectButton.Focus())`. Ajouter `KeyboardNavigation.TabNavigation="Cycle"` (déjà présent ligne 393, vérifié) et un Esc handler vers `OnOverlayCloseClick`. Marquer la zone avec `AutomationProperties.IsModal="True"` (à vérifier sur Border).
3. RDP-LIVE-02 + RDP-ERR-03 : Settings flag `RdpConfirmDisconnect` (default=true). Si activé, dialog "Disconnect from {server}?". Bonus : touche Enter ne déclenche pas Disconnect par défaut.

**Sprint 2 — Profil & feedback session**

4. RDP-PROF-01 : Binder `IsEnabled` des sections RDP options sur `!RdpUseGlobalDefaults` + ajouter un `InfoBar` "Using global defaults" quand activé.
5. RDP-PROF-02 : Watermark + tooltip sur `RdpUsername` ("DOMAIN\\user, user@domain, ou user").
6. RDP-PROF-03 : Reformuler `RdpTestSuccess` (locales) en "TCP reachable — handshake/credentials not tested".
7. RDP-LIVE-04 : Toast 3 s à l'entrée fullscreen avec le raccourci de sortie résolu (`RdpFullscreenToggleShortcut` rendu en notation lisible).
8. RDP-LIVE-05 : Aligner les timeouts des états autofill (3 s pour `Filled`, `TimedOut`, `Failed`) ou masquer tous les états transitoires sur `OnRdpConnected`.
9. RDP-LIVE-06 : Status flash "Keys sent" après `SendKeysToRemote`.

**Sprint 3 — A11y détaillée**

10. RDP-A11Y-02 : `AutomationProperties.Name` sur les 8 RedirectionIndicators icons + glyphe différenciant pour l'état désactivé.
11. RDP-LIVE-09 + RDP-LIVE-10 : Stepper et HealthDot annoncés via `LiveSetting=Polite`.
12. RDP-A11Y-03 : Préfixer `ReconnectMessageText` par "(Warning)" / "(Error)" selon `RdpDisconnectSeverity`.
13. RDP-A11Y-04 : `MinHeight=32` sur `ToolbarGhostButtonStyle` (cible tactile).

**Sprint 4 — Polish**

14. RDP-LIVE-07 : "Skip stabilization" dans le menu resolution.
15. RDP-LIVE-15 : ETA dans le message Reconnecting.
16. RDP-LIVE-13 : Surface RDP semi-transparente derrière l'overlay.
17. RDP-PROF-04 : Tooltips sur `RdpBitmapCaching`, `RdpAutoReconnect`, `RdpDisableUdp`.
18. RDP-PROF-06 : "Save as profile" sur sessions ad-hoc.
19. RDP-ERR-02 : Élargir `ShouldOfferEditProfile` aux erreurs d'auth/security (incl. 2308).
20. RDP-ERR-05 : Status pendant la boucle `RetryBeginConnectAsync`.
21. RDP-ERR-06 : Hint Windows Hello / smartcard dans `RdpAutofillTimedOut`.
22. RDP-LIVE-08 : Cheat-sheet de raccourcis (Help panel / `Ctrl+?`).

## What is OK (vérifié, pas flaggé)

- **Parité i18n RdpDisconnect*** : 26 clés en EN / 26 clés en FR (mesuré via Grep). Les messages de récupération sont actionnables ("verify hostname", "disable NLA", "import certificate's CA").
- **`RdpLoadingBar` géré correctement** (lignes 1359, 1437-1450) : `Visibility` togglé sur `IsProgress`, `IsIndeterminate` basculé pour la phase Reconnecting avec progression numérique. Le finding de l'agent était une fausse alerte.
- **`RemotePort` validé** (`ServerDialogViewModel.cs:184`) avec `[Range(1, 65535)]` ; `GetEndpointPortError()` est appelé pour `IsRdpConnection` (ligne 1657). Le finding "pas de validation port RDP" était un faux positif.
- **Default port 3389 sur ad-hoc RDP** (`CommandPaletteViewModel.cs:379`) : `dto.RemotePort = DefaultPorts.Rdp;` — confirmé. Le finding de l'agent était erroné.
- **Webcam redirection en Embedded mode** : checkbox désactivée + badge `[External only]` + tooltip explicatif. Bon pattern d'« error prevention ».
- **Disconnect codes** : 24 codes mappés dans `RdpActiveXHost.GetDisconnectReasonKey()`, severity classification (`Information` / `RecoverableError` / `TerminalError`) utilisée pour piloter le strip de l'overlay.
- **Autofill multi-broker** : `CredentialAutofill` détecte `CredentialUIBroker`, `LogonUI`, `consent`, plus class names `Credential Dialog Xaml Host` / `Windows Security`. Couvre 8 langues.
- **Cleanup mstsc.exe** : fichier `.rdp` ACL-protected (`SecureFileWriter`), credential `TERMSRV/host` supprimé après délai configurable (`RdpArtifactCleanupDelayMs`).
- **Apache 2.0 headers** : présents sur tous les fichiers RDP relus, conformes `dev-standards`.
- **MVVM conformité** : `EmbeddedRdpView.xaml.cs` reste un view-controller (pas de logique métier), MVVM Toolkit `ObservableValidator` côté `ServerDialogViewModel`.
- **`DisplayName` / `Endpoint` LiveSetting absent** sur `SessionTitleText` et `EndpointTextBlock` — correct, ce sont des éléments statiques (à ne pas confondre avec status).
- **Severity icon AutomationProperties.Name=""** intentionnel et correct (icône décorative).
- **Hooks clavier RDP** (`RdpKeyboardEscapeHook`) : hook thread-local correctement uninstall sur dispose, lock sur `SyncRoot`, vérifie le focus avant de fire.

## Faux positifs écartés (transparence)

Les sous-agents ont identifié 3 findings que la vérification directe a invalidés :

1. ~~RDP-DISC-01 (sous-agent #1) « ad-hoc RDP n'initialise pas le port »~~ — `dto.RemotePort = DefaultPorts.Rdp` est bien présent ligne 379.
2. ~~RDP-PROF-03 (sous-agent #1) « pas de [Range] sur RemotePort »~~ — `[Range(1, 65535)]` ligne 184 du ServerDialogViewModel, `GetEndpointPortError()` invoqué pour RDP.
3. ~~RDP-LIVE-08 (sous-agent #2) « RdpLoadingBar reste visible une fois connecté »~~ — Visibilité gérée lignes 1359 et 1437.

## Ce qui n'a délibérément pas été flaggé

- **Densité visuelle de la toolbar** : 7 boutons + status zone + redirection indicators. Cohérent avec un connection manager pro (cf. MobaXterm, mRemoteNG) — pas une UX issue.
- **Glyphes Segoe MDL2 sans labels textuels** : convention Windows-native acceptée. Tooltips et `AutomationProperties.Name` sont là.
- **Compte à rebours stabilization 10 s par défaut** : noté dans le CLAUDE.md comme un "RDP gotcha" (prévention disconnect 4360) — pas un défaut, juste une opportunité de polish (RDP-LIVE-07).
- **Mode Embedded vs External** : architecture délibérément à deux paths. Pas un défaut UX.
- **Protocol cards UniformGrid Columns="4"** : layout responsive non vérifié à toutes les tailles de fenêtre — sortirait du scope code-only.
- **WPF UI Automation modal pattern** sur `Border` : techniquement, un Border ne peut pas exposer le `Window` pattern. La correction RDP-A11Y-01 par focus management programmatique est la bonne approche, pas une refactorisation en `Window`.

## Implementation log

Implémentation menée du 2026-04-30 au 2026-04-30 via 12 prompts Codex enchaînés en mode Pair Architect. Baseline tests au démarrage : 4341 passing + 6 skipped (CLAUDE.md). Baseline finale : 4938 passing + 6 skipped, 0 warning, 0 error.

Légende du statut :
- **Done** — finding traité par un prompt Codex et vérifié par build + tests.
- **Done (indirect)** — résolu en cascade par un prompt qui ciblait un finding voisin ; aucune action dédiée nécessaire.
- **Verified already OK** — vérification après-coup montre que le code/locale couvrait déjà le besoin ; finding écarté du backlog.
- **Deferred** — non implémenté dans cette campagne ; raison documentée. Rééligible à un prochain sprint.

| # | Finding | Sév. | Prompt | Status | Validation | Notes |
|---|---------|------|--------|--------|------------|-------|
| 1 | RDP-ERR-01 | 🔴 | 1 | Done | build+test, 4850 pass | i18n des 3 messages hardcodés de `RdpHandler.cs` (clés `RdpErrorDecryptPassword`, `RdpErrorStoreCredentials`, `RdpErrorMstscLaunch`) |
| 2 | RDP-A11Y-01 | 🔴 | 2 | Done | build+test, 4850 pass | `ReconnectOverlay` exposé en `AutomationProperties.IsDialog`, focus programmatique sur `OverlayReconnectButton`, mapping `Esc`/`Enter`, renumérotation TabIndex |
| 3 | RDP-LIVE-01 | 🟠 | 3 | Done (indirect) | — | TabIndex collision sémantique des 3 boutons disconnect/cancel-* atténuée par la confirmation introduite via RDP-LIVE-02 ; pas de refactor TabIndex requis |
| 4 | RDP-LIVE-02 | 🟠 | 3 | Done | build+test, 4857 pass | Confirmation `IDialogService.ShowConfirmAsync` avant disconnect, gated par `AppSettings.RdpConfirmDisconnect` (default true) |
| 5 | RDP-LIVE-03 | 🟠 | 2 | Done (indirect) | — | Le mapping `Esc`/`Enter` de l'overlay a été inclus dans le prompt RDP-A11Y-01 (`OnReconnectOverlayPreviewKeyDown`) |
| 6 | RDP-LIVE-04 | 🟠 | 5 | Done | build+test, 4871 pass | Toast transitoire 3 s à l'entrée fullscreen, render du raccourci configuré via `RdpShortcutParser` |
| 7 | RDP-LIVE-05 | 🟠 | 5 | Done | build+test, 4871 pass | Timer 3 s appliqué aussi aux états `TimedOut`/`Failed` de l'autofill, plus seulement `Filled` |
| 8 | RDP-LIVE-06 | 🟠 | 5 | Done | build+test, 4871 pass | Toast "{0} sent to remote" après chaque `SendKeysToRemote` réussi ; toast d'échec sur catch |
| 9 | RDP-LIVE-07 | 🟠 | 10 | Done | build+test, 4915 pass | MenuItem "Skip stabilization" dans `ResolutionMenu` + `_stabilizationCts` cancellable dans `EnableResolutionUpdatesAsync` |
| 10 | RDP-PROF-01 | 🟠 | 4 | Done | build+test, 4863 pass | `IsEnabled="False"` sur les 4 sections RDP via `DataTrigger Binding RdpUseGlobalDefaults`, plus banner explicatif |
| 11 | RDP-PROF-02 | 🟠 | 4 | Done | build+test, 4863 pass | `WatermarkTextBoxStyle` + tooltip sur `DlgSrv_BasicRdpUsernameBox` (clés `ServerDialogRdpUsernameWatermark` / `…Hint`) |
| 12 | RDP-PROF-03 | 🟠 | 4 | Done | build+test, 4863 pass | Reformulation de `ServerDialogRdpTestSuccess` ("TCP {0} reachable… handshake/credentials not tested") |
| 13 | RDP-DISC-01 | 🟠 | — | Deferred | — | Heuristique pondérée (port-scan, historique) hors scope code-only ; à reprendre quand on aura un signal d'usage côté télémétrie |
| 14 | RDP-ERR-02 | 🟠 | 8 | Done | build+test, 4891 pass | Code 2308 ajouté à `RdpDisconnectActionPolicy.ShouldOfferEditProfile` ; couverture unitaire dans `RdpDisconnectActionPolicyTests` |
| 15 | RDP-ERR-03 | 🟠 | 3 | Done | build+test, 4857 pass | Couvert par la même confirmation que RDP-LIVE-02 ; `_userInitiatedDisconnect` reste mais l'utilisateur peut désormais annuler avant qu'il soit set |
| 16 | RDP-A11Y-02 | 🟠 | 6 | Done | build+test, 4877 pass | `AutomationProperties.Name` localisé sur les 8 RedirectionIndicators ; `TextDecorations="Strikethrough"` en disabled (signal non-couleur) ; `LiveSetting="Polite"` |
| 17 | RDP-LIVE-08 | 🟡 | 11 | Done | build+test, 4935 pass | MenuItem "Keyboard shortcuts…" en bas du `SendKeysMenu` ; `MessageBox.Show` listant raccourcis toolbar + send-keys |
| 18 | RDP-LIVE-09 | 🟡 | 6 | Done | build+test, 4877 pass | `LiveSetting="Polite"` sur `ConnectionPhaseStepper` + `Name` dynamique formaté `"{phase} ({lit} of {total})"` mis à jour dans `TransitionPhase` |
| 19 | RDP-LIVE-10 | 🟡 | 6 | Done | build+test, 4877 pass | Glyphe Segoe MDL2 (✓/⚠/✕/∘) ajouté à côté du HealthDot ; `LiveSetting="Polite"` + `Name` localisé via `UpdateHealthDot` |
| 20 | RDP-LIVE-11 | 🟡 | 9 | Done | build+test, 4911 pass | Clipboard enrichi : header + Time/Server/Tunnel/Session/App + corps original ; toast confirmation |
| 21 | RDP-LIVE-12 | 🟡 | — | Verified already OK | Grep `TooltipAntiIdleActive` | Locale dit déjà "Anti-idle is keeping this session alive. Click to disable for the current session." — finding obsolète |
| 22 | RDP-LIVE-13 | 🟡 | 9 | Done | build+test, 4911 pass | `OverlayBackground` passé à α=0xB3 (≈70 %) sur les 7 thèmes Dracula |
| 23 | RDP-LIVE-14 | 🟡 | — | Deferred | — | "Fit" reste coché si window resized en mode Fit ; nécessiterait de tracker la résolution effective vs presets — coût/valeur faible |
| 24 | RDP-LIVE-15 | 🟡 | — | Verified already OK | Grep `StartReconnectElapsedTracking` | `ReconnectElapsedText` est déjà visible pendant la reconnexion (`5/20 • 23s elapsed`) ; ETA réelle = scope plus large, finding atténué |
| 25 | RDP-PROF-04 | 🟡 | 8 | Done | build+test, 4891 pass | Tooltips ajoutés sur `RdpBitmapCaching`, `RdpAutoReconnect`, `RdpDisableUdp` (clés `Rdp*Hint`) |
| 26 | RDP-PROF-05 | 🟡 | — | Deferred | — | Migration `ApplyLocalization` → `{loc:Translate}` est documentée comme incrémentale dans CLAUDE.md ; pattern legacy intentionnel |
| 27 | RDP-PROF-06 | 🟡 | 12 | Done | build+test, 4938 pass | `SessionTabViewModel.MarkAsAdHoc(dto)` + MenuItem "Save as profile…" + `ServerListViewModel.SaveAdHocAsProfileCommand` |
| 28 | RDP-DISC-02 | 🟡 | — | Deferred | — | Hypothèse non vérifiée en runtime ; à confirmer (ou infirmer) lors d'un prochain audit ; pas un blocage |
| 29 | RDP-ERR-04 | 🟡 | — | Deferred | — | `RdpDisconnectUnknownCode` reste générique ; impact réel faible (24 codes connus mappés explicitement) |
| 30 | RDP-ERR-05 | 🟡 | 9 | Done | build+test, 4911 pass | Status transitoire `RdpStatusInitializingSurface` poussé pendant la boucle `RetryBeginConnectAsync` |
| 31 | RDP-ERR-06 | 🟡 | 7 | Done | build+test, 4885 pass | `RdpAutofillTimedOut` re-formulé pour mentionner Windows Hello / smart card / FIDO |
| 32 | RDP-A11Y-03 | 🟡 | 7 | Done | build+test, 4885 pass | `ReconnectMessageText` préfixé "Notice:" / "Warning:" / "Error:" selon `RdpDisconnectSeverity` (signal non-couleur) |
| 33 | RDP-A11Y-04 | 🟡 | 8 | Done | build+test, 4891 pass | `MinHeight=32` + `MinWidth=32` sur `ToolbarGhostButtonStyle` (cible tactile WCAG 2.5.5) |

### Récapitulatif chiffré

| Statut | Compte | Pourcentage |
|--------|-------|-------------|
| Done (direct ou indirect) | 26 | 79 % |
| Verified already OK | 2 | 6 % |
| Deferred | 5 | 15 % |
| **Total** | **33** | **100 %** |

**Couverture effective : 28/33 (85 %)** en comptant les findings traités + ceux déjà OK. Les 5 deferred sont documentés ci-dessus avec une raison non-bloquante.

### Métriques de campagne

- **Prompts Codex enchaînés** : 12 (Sprint 1 = 3 prompts, Sprint 2 = 2, Sprint 3 = 2, Sprint 4 = 5).
- **Faux positifs des sous-agents** écartés en amont (avant exécution Codex) : 3 (cf. section « Faux positifs écartés » plus haut).
- **Build status** sur les 12 livraisons : 0 warning, 0 error, 0 régression. `TreatWarningsAsErrors` actif.
- **Tests** : 4341 → 4938 (+597 nouveaux tests dans le repo, dont une fraction directement attribuable aux parity tests / unit tests ajoutés pour cet audit). Aucune skip nouvellement introduit (compteur stable à 6 = ThemeServiceTests qui requièrent une `Application` WPF live).
- **Tracking** des changements applicatifs : commits séparés par prompt (pas de squash) côté Codex. Ce rapport de doc fait l'objet d'un **commit séparé docs-only** : `docs(audit): add RDP implementation log`.

### Findings restants à reprendre

Pour mémoire — les 5 deferred, classés par priorité de reprise estimée :

1. **RDP-DISC-02** (raccourci clavier "Connect to highlighted server") — vérification rapide en runtime avant tout autre travail. Si déjà OK, fermer le finding.
2. **RDP-PROF-05** (migration ComboBoxItem `Content=""` vers `{loc:Translate}`) — éligible à un sprint i18n dédié couvrant les vues legacy.
3. **RDP-DISC-01** (pondération RDP/SSH ad-hoc) — attendre signaux télémétrie ou retours utilisateur avant d'implémenter une heuristique.
4. **RDP-ERR-04** (message `RdpDisconnectUnknownCode` plus actionable) — coût minime ; à embarquer dans un prochain pass de polishing locales.
5. **RDP-LIVE-14** (`Resolution menu` IsChecked stale en mode Fit) — coût/valeur faible ; reprendre uniquement si remontée utilisateur.
