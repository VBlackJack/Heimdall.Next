# UX Audit — Passe 1 : SSH + RDP Configuration complète

**Date:** 2026-04-11
**Stack:** WPF / .NET 10 / C# 14
**Mode:** Targeted — UX (Nielsen's 10 Heuristics)
**Scope:**
- `GatewayDialog.xaml/cs` + `GatewayDialogViewModel.cs`
- `ServerDialog.xaml` — onglets Connection, Tunneling, Gateway Auth, Options
- `MainWindow.xaml.cs` — panneau Settings SSH + RDP
- `SettingsViewModel.cs` — logique Settings SSH/RDP

---

## Executive Summary

La configuration SSH/RDP est globalement bien structurée, mais 4 problèmes importants dégradent l'expérience : le `GatewayDialog` souffre des mêmes problèmes de label que le `ServerDialog` avant nos corrections (label "Password" statique, aucun hint auth), l'onglet "Gateway Auth" dans le `ServerDialog` affiche uniquement des infos mais aucun contrôle éditable (trompeur), et le panneau Settings manque totalement de contexte pour guider l'utilisateur sur les chemins Plink/PuTTY et la signification de "Embedded vs External". Deux problèmes mineurs de cohérence localization complètent le tableau.

---

## Findings par sévérité

### 🟠 Important (4)

- **GW-01** — `GatewayDialog` : label "Password" toujours statique, même quand une clé est configurée
- **GW-02** — `GatewayDialog` : aucun hint d'auth — l'utilisateur ne sait pas si clé ET password sont nécessaires, ou l'un ou l'autre
- **TAB-01** — Onglet "Gateway Auth" dans `ServerDialog` : vide de contrôles, trompeur
- **SET-01** — Settings SSH/RDP : aucun contexte sur Plink vs PuTTY, ni sur ce que "Embedded/External" signifie concrètement

### 🟡 Mineur (3)

- **TAB-02** — Onglet Tunneling grisé sans tooltip d'explication
- **SET-02** — Labels RDP mode utilisent les clés de localisation SSH (`SettingsSshModeEmbedded/External` pour un ComboBox RDP)
- **GW-03** — Champ `HostKeyFingerprint` en lecture seule sans explication sur son cycle de vie

---

## Findings détaillés

### UX-01 — Visibilité du statut système

**Score : 2/3**

| # | Check | Verdict | Sévérité | Détails |
|---|-------|---------|----------|---------|
| 1 | GatewayDialog: feedback de sauvegarde | ✅ PASS | — | `SaveBtn` déclenche validation synchrone + ferme sur succès |
| 2 | Settings: feedback sur "Apply to all" | ✅ PASS | — | `ConfirmApplySshModeMessage` + `ConfirmApplyRdpModeMessage` avant action |
| 3 | HostKeyFingerprint: statut du champ visible | ❌ FAIL | 🟡 | **GW-03** — `TxtHostKeyFingerprint` est `IsReadOnly="True"` mais aucun label ni hint n'explique que ce champ est auto-rempli après la première connexion. Les utilisateurs voient un champ vide et ne savent pas s'ils doivent le remplir manuellement. |

---

### UX-02 — Contrôle et liberté

**Score : 3/3**

| # | Check | Verdict | Sévérité | Détails |
|---|-------|---------|----------|---------|
| 1 | GatewayDialog: annulation avec dirty check | ✅ PASS | — | `OnWindowClosing` détecte `IsDirty` et propose confirmation |
| 2 | Settings: "Apply to all" réversible | ✅ PASS | — | Confirmation avant action bulk. Pas d'undo mais la confirmation est suffisante. |
| 3 | ServerDialog: retour possible depuis chaque onglet | ✅ PASS | — | Navigation libre entre onglets sans perte de données |

---

### UX-03 — Prévention et récupération d'erreurs

**Score : 0/3**

| # | Check | Verdict | Sévérité | Détails |
|---|-------|---------|----------|---------|
| 1 | GatewayDialog: label password contextuel | ❌ FAIL | 🟠 | **GW-01** — `GatewayDialog.xaml.cs:50` : `LblPassword.Text = vm.Localizer["GatewayDialogLabelPassword"]` est statique. Quand `KeyPath` est renseigné, le champ sert de passphrase de clé — pas d'un mot de passe SSH. Même bug que `ServerDialog` avant correction. Fix identique requis : propriété computed `GatewayPasswordLabel` dans `GatewayDialogViewModel`, `OnKeyPathChanged` notifie, binding dans le XAML. |
| 2 | GatewayDialog: hint auth | ❌ FAIL | 🟠 | **GW-02** — `GatewayDialog.xaml` n'a aucun `TextBlock` hint sous le champ password. L'utilisateur ne sait pas : "dois-je mettre une clé ET un mot de passe ? Ou juste l'un des deux ?" MobaXterm affiche clairement "Leave password blank if using key-only auth." Fix : ajouter un `TextBlock` hint dynamique identique à `DlgSrv_BasicSshAuthHint` dans `ServerDialog`. |
| 3 | Settings: chemins Plink/PuTTY explicites | ❌ FAIL | 🟠 | **SET-01** — Le panneau Settings SSH affiche deux champs de chemin (Plink, PuTTY) sans aucune explication contextuelle. L'utilisateur ne sait pas : "Plink est pour quoi ? PuTTY est pour quoi ? Dois-je configurer les deux ?" Fix : ajouter un `TextBlock` hint sous chaque champ. Ex: Plink → *"Required for Pageant authentication and keyboard-interactive servers."* PuTTY → *"Required when SSH mode is set to External."* |

---

### UX-04 — Cohérence et standards

**Score : 2/3**

| # | Check | Verdict | Sévérité | Détails |
|---|-------|---------|----------|---------|
| 1 | GatewayDialog cohérent avec ServerDialog | ❌ FAIL | 🟡 | **SET-02** — `MainWindow.xaml.cs:381-382` : `Mw_RdpModeEmbedded.Content = vm.Localize("SettingsSshModeEmbedded")` et `Mw_RdpModeExternal.Content = vm.Localize("SettingsSshModeExternal")`. Les clés nommées `Ssh` sont utilisées pour les labels RDP. Si ces clés deviennent spécifiques à SSH (ex: "SSH.NET / Plink"), les labels RDP seront faux silencieusement. Fix : créer `SettingsRdpModeEmbedded` / `SettingsRdpModeExternal` dans les locales. |
| 2 | ServerDialog options RDP/SSH cohérentes | ✅ PASS | — | Les ComboBox mode utilisent le même pattern Tag/SelectedValuePath dans les deux dialogues |
| 3 | GatewayDialog patterns identiques à ServerDialog | ✅ PASS | — | Validation inline, dirty check, PasswordBox + code-behind — pattern correct |

---

### UX-05 — Reconnaissance plutôt que mémorisation

**Score : 1/3**

| # | Check | Verdict | Sévérité | Détails |
|---|-------|---------|----------|---------|
| 1 | Settings SSH/RDP: Embedded vs External expliqués | ❌ FAIL | 🟠 | **SET-01 (suite)** — Les ComboBox "SSH Mode" et "RDP Mode" dans Settings n'ont aucun hint expliquant ce que "Embedded" et "External" signifient. Un utilisateur qui ne connaît pas l'application doit deviner. Fix : ajouter un hint contextuel sous chaque ComboBox : *"Embedded: terminal runs inside Heimdall. External: opens PuTTY."* et *"Embedded: RDP client inside Heimdall. External: opens mstsc.exe."* |
| 2 | GatewayDialog: topologie de chaîne visible | ❌ FAIL | 🟠 | **GW-02 (suite)** — La liste déroulante `ParentGateway` permet de chaîner des gateways, mais contrairement au `ServerDialog` qui affiche un diagramme "PC → Gateway → Server", le `GatewayDialog` n'a aucune représentation visuelle de la chaîne. Un utilisateur qui configure "Gateway B via Gateway A" ne peut pas visualiser le résultat. Fix : ajouter un mini-diagramme textuel dynamique sous `CmbParentGateway` (ex: "A → B → Target") |
| 3 | Onglet Tunneling: contexte clair | ✅ PASS | — | `DlgSrv_TunnelingDesc` et `DlgSrv_TunnelingHint` expliquent le concept |

---

### UX-06 — Efficacité pour utilisateurs avancés

**Score : 2/2**

| # | Check | Verdict | Sévérité | Détails |
|---|-------|---------|----------|---------|
| 1 | "Apply to all" SSH/RDP mode en bulk | ✅ PASS | — | `Mw_ApplySshModeAll` + `Mw_ApplyRdpModeAll` — feature power user présente |
| 2 | Tab order GatewayDialog | ✅ PASS | — | 0→1→2→3→4→5→6 — séquentiel, correct |

---

### UX-07 — Flux critiques

**Score : 1/2**

| # | Check | Verdict | Sévérité | Détails |
|---|-------|---------|----------|---------|
| 1 | Flux "configurer une gateway" end-to-end | ✅ PASS | — | Settings → Ajouter gateway → GatewayDialog → Save → disponible dans ServerDialog |
| 2 | Onglet "Gateway Auth" dans ServerDialog | ❌ FAIL | 🟠 | **TAB-01** — `DlgSrv_TabAuthentication` (ligne 1134) s'active quand une gateway est sélectionnée et **laisse croire** qu'on peut y configurer les credentials de la gateway. En réalité, il contient uniquement le nom et l'endpoint de la gateway en lecture seule — aucun contrôle. L'utilisateur clique sur cet onglet pour éditer l'auth de la gateway et se retrouve devant un écran informatif sans action possible. Fix : soit (a) ajouter un bouton "Edit gateway credentials" qui ouvre le GatewayDialog, soit (b) renommer l'onglet en "Connection Path" et y déplacer le diagramme du tab Connection actuel. Option (b) est plus cohérente avec ce que l'onglet affiche déjà. |

---

### UX-08 — Aide et documentation

**Score : 1/2**

| # | Check | Verdict | Sévérité | Détails |
|---|-------|---------|----------|---------|
| 1 | Hints SSH dans ServerDialog | ✅ PASS | — | `SshAuthHint` dynamique (corrigé en Passe 0) |
| 2 | Onglet Tunneling désactivé sans explication | ❌ FAIL | 🟡 | **TAB-02** — `DlgSrv_TabTunneling` a `ToolTipService.ShowOnDisabled="True"` (ligne 985) mais aucune propriété `ToolTip` définie. Les protocoles FTP, VNC, Telnet, Citrix, Local voient l'onglet grisé sans comprendre pourquoi. Fix : ajouter `ToolTip="{Binding TunnelingUnavailableReason}"` ou une clé locale statique : *"Tunneling is not available for this protocol type."* |

---

## Plan d'action

### 🟠 Prompt A — GatewayDialog : label dynamique + hint auth + diagramme chaîne

**Fichiers à modifier :** `GatewayDialogViewModel.cs`, `GatewayDialog.xaml`, `GatewayDialog.xaml.cs`, `locales/en.json`, `locales/fr.json`

Corrections :
1. Ajouter `GatewayPasswordLabel` (computed, dépend de `KeyPath`)
2. Ajouter `GatewayAuthHint` (computed, dépend de `KeyPath`)
3. Ajouter `GatewayChainSummary` (computed : `ParentGateway.Name → CurrentGateway.Name` si parent sélectionné)
4. `OnKeyPathChanged` → notifie les deux computed
5. `OnSelectedParentGatewayIdChanged` → notifie `GatewayChainSummary`
6. XAML : binding `LblPassword` → `GatewayPasswordLabel`, ajouter `TextBlock` hint sous PasswordBox, ajouter mini-diagramme texte sous `CmbParentGateway`
7. `GatewayDialog.xaml.cs` : retirer l'assignment statique de `LblPassword`
8. Locale : `GatewayAuthHintPassword`, `GatewayAuthHintKey`, `GatewayChainLabel`

---

### 🟠 Prompt B — Settings : hints Plink/PuTTY + explication Embedded/External

**Fichiers à modifier :** `MainWindow.xaml` (section Settings SSH/RDP), `MainWindow.xaml.cs`, `locales/en.json`, `locales/fr.json`

Corrections :
1. Ajouter `TextBlock` hint sous le champ Plink path : *"Required for Pageant authentication and keyboard-interactive servers."*
2. Ajouter `TextBlock` hint sous le champ PuTTY path : *"Required when SSH mode is set to External."*
3. Ajouter `TextBlock` hint sous le ComboBox SSH mode : *"Embedded: terminal runs inside Heimdall. External: opens PuTTY."*
4. Ajouter `TextBlock` hint sous le ComboBox RDP mode : *"Embedded: RDP client runs inside Heimdall. External: opens mstsc.exe."*
5. Remplacer `SettingsSshModeEmbedded/External` par `SettingsRdpModeEmbedded/External` pour les labels RDP
6. Locale : 6 nouvelles clés

---

### 🟠 Prompt C — ServerDialog : onglet "Gateway Auth" → bouton d'édition

**Fichiers à modifier :** `ServerDialog.xaml`, `ServerDialog.xaml.cs`, `ServerDialogViewModel.cs`, `locales/en.json`, `locales/fr.json`

Option retenue : ajouter un bouton "Edit gateway credentials…" dans l'onglet `DlgSrv_TabAuthentication` qui ouvre le `GatewayDialog` pour la gateway sélectionnée. Cela évite de tout restructurer tout en corrigeant le vide trompeur.

---

### 🟡 Prompt D — Tooltips onglet Tunneling + GW-03 hint HostKeyFingerprint

**Fichiers à modifier :** `ServerDialog.xaml`, `ServerDialog.xaml.cs`, `GatewayDialog.xaml`, `GatewayDialog.xaml.cs`, `locales/en.json`, `locales/fr.json`

Corrections :
1. `DlgSrv_TabTunneling` : ajouter `ToolTip="{Binding ...}"` ou clé statique
2. `TxtHostKeyFingerprint` : ajouter `TextBlock` hint en dessous : *"Auto-populated after first connection. Used to prevent host key spoofing."*

---

## Ce qui n'a PAS été flaggué (et pourquoi)

- **Diagramme de connexion dans ServerDialog** — excellent pour la compréhension, pas un problème
- **`IsDefault="True"` sur SaveBtn dans GatewayDialog** — Enter submit fonctionne, pas de problème
- **Live validation uniquement après première soumission** — pattern intentionnel, évite de montrer des erreurs à l'utilisateur avant qu'il ait fini de taper
- **Nombre d'onglets dans ServerDialog** — 4 onglets bien organisés, non surchargé
- **`Port` sans `UpdateSourceTrigger=PropertyChanged` dans GatewayDialog** — validation on-submit intentionnelle pour les champs numériques
