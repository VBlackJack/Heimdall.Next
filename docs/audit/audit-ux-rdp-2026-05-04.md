# Audit UX — Surface RDP de Heimdall.Next (passe pure-UX)

**Date :** 2026-05-04
**Auteur :** Claude (Cowork architect mode)
**Build audité :** v2026.042409 (Debug)
**Scope :** Quatre surfaces — Session embedded, ServerDialog (profil RDP), parcours de lancement, Settings RDP. **Angle pure UX** (flux, friction, hiérarchie, découvrabilité, micro-interactions, cohérence avec mRemoteNG / MobaXterm). Accessibilité et i18n hors scope cette fois.
**Méthodologie :** Audit code-only (XAML + C# + locales), exploration parallèle par sous-agents, vérification directe des numéros de ligne pour chaque finding cité, framework `design:design-critique` (first impression / usability / hiérarchie / cohérence). **Audit différentiel croisé par Codex** (cf. amendement § Findings additionnels Codex 2026-05-04) : 4 findings supplémentaires intégrés après vérification, dont 2 P1 majeurs que j'avais loupés.
**Relation aux audits précédents :** Cet audit s'inscrit dans la continuité de `audit-ux-rdp-2026-04-30.md` (28/33 findings traités, 5 deferred documentés). **Aucun finding n'est répété ici** : tous les angles couverts en avril (i18n, accessibilité, erreurs RDP, watermark `DOMAIN\user`, confirmation disconnect, etc.) sont supposés résolus ou explicitement deferred dans le rapport précédent. Je me concentre sur les angles UX que la passe précédente n'a pas couverts ou pas approfondis.

> ⚠️ **Honnêteté.** Pendant l'exploration initiale, un sous-agent a identifié comme "critique" l'absence totale d'un onglet RDP dans Settings. Vérification directe des lignes 2164-2243 de `MainWindow.xaml` → l'onglet **existe** et expose 18 propriétés. Faux positif écarté avant écriture du rapport. Voir section finale.

## Résumé exécutif

La surface RDP est dans un état de finition rare pour un connection manager : 24 codes de déconnexion mappés, autofill multi-broker, overlay reconnect avec actions contextuelles, Settings RDP dédié avec bouton "Apply to all", confirmation pré-disconnect optionnelle, watermark de format username. Beaucoup des frictions classiques d'un client RDP ont déjà été résolues par la passe d'avril.

Les frictions UX restantes — **angle pure UX** — sont concentrées sur :

1. **Mode Embedded ↔ External** est un choix architectural per-profile, sans override "one-shot" au moment du lancement. Pour basculer d'avis, il faut éditer le profil. Friction notable dans les workflows de support / triage.
2. **Settings RDP : regroupement et exposition incomplets** : 2 propriétés impactant directement l'expérience de session ne sont pas exposées du tout (`RdpResolutionPresets`, `RdpDialogAdvancedDefault`), et 3 autres (`RdpResizeEnableDelayMs`, `RdpArtifactCleanupDelayMs`, `RdpCredentialAutofillTimeoutMs`) sont planquées dans l'onglet **Advanced** alors qu'elles seraient cherchées dans l'onglet RDP.
3. **Hiérarchie de l'onglet Settings RDP** : 18 contrôles empilés en `StackPanel` plat sans subtitle visuel à part "Redirection". Pas de regroupement Performance / Réseau / Périphériques. Pas de tooltip d'aide. Pas de "Reset section".
4. **Resolution menu** : `IsChecked` sur le preset actif est correct mais il n'y a pas d'indication visuelle du **mode** courant (Fit / Fixed / Smart). L'utilisateur ne sait pas, au coup d'œil, comment sa résolution se comporte si la fenêtre est redimensionnée.
5. **Letterbox** (mode Fixed avec résolution < surface) : marges noires sans cadre ni hint, juste du `SurfaceBrush` autour. Pour un nouvel utilisateur c'est un bug visuel, pas une feature.
6. **Densité de la toolbar de session** : 7 boutons + status zone + 8 redirection indicators + AntiIdle badge + Health dot. Cohérent avec MobaXterm mais sans **groupement** ni **séparateurs**, ce qui rend la lecture lente.
7. **ServerDialog** : 4 onglets (Connection / Authentication / Tunneling / Options) + advanced expanders imbriqués + sections conditionnelles. La "courbe de découverte" pour configurer un profil RDP non-trivial (résolution Fixed + multimon + Advanced behavior) reste élevée même avec l'ouverture en advanced mode.

Total après amendement Codex : **2 critiques, 13 importants, 11 mineurs** (26 findings). Les 2 critiques sont des incohérences de comportement / honnêteté UI sur le mode External, signalées par Codex. Aucun blocage release au sens strict, mais les 2 critiques méritent d'être corrigées avant la prochaine release publique.

## Findings par surface

### A. Session embedded RDP

| ID | Sév. | Fichier:ligne | Constat | Recommandation |
|----|------|---------------|---------|----------------|
| RDP-LIVE-16 | 🟠 | `EmbeddedRdpView.xaml:148-174` | **Resolution menu : pas d'indication du mode actif (Fit / Fixed / Smart)**. Le `IsChecked` du preset choisi est correct (suite à RDP-LIVE-14) mais l'utilisateur ne sait pas, en ouvrant le menu, si la résolution suit la fenêtre ou non. Conséquence : confusion entre "j'ai choisi 1920×1080" et "ma fenêtre fait 1366×768 et je vois letterbox". | Ajouter un sous-titre / pill au-dessus de la liste des presets : "Active mode: Fit window" / "Active mode: Fixed (1920×1080)" / "Active mode: Smart sizing". Optionnel : un tag-couleur AccentBrush sur la pill du mode courant. |
| RDP-LIVE-17 | 🟠 | `EmbeddedRdpView.xaml:396-414` (FormsHost) + `LetterboxLayoutCalculator.cs` | **Letterbox sans cadre**. En mode `Fixed + !InitialSmartSizing` avec résolution < surface, les marges autour du `WindowsFormsHost` sont remplies par `SurfaceBrush` sans bordure ni légende. Pour un utilisateur ne connaissant pas le concept, ça lit comme un bug d'affichage, pas une décision intentionnelle. | (1) Border 1 px `BorderBrush` autour du FormsHost en mode Fixed pour matérialiser la zone RDP ; (2) hint discret "Fixed 1920×1080 — resize the window or change resolution to fill" en bas à droite, fade-out au bout de 4 s à la première apparition de letterbox dans la session. |
| RDP-LIVE-18 | 🟠 | `EmbeddedRdpView.xaml:62-391` (toolbar globale) | **Densité visuelle de la toolbar sans groupement**. 7 boutons icon-only + status zone large + 8 redirection indicators + AntiIdle badge + Health dot, alignés sans séparateur ni regroupement logique. La lecture en `O(1)` est impossible — l'utilisateur scanne. Comparaison : MobaXterm groupe ses boutons par famille (session / view / tools) avec un mini-séparateur 1 px. mRemoteNG fait pareil. | Insérer 2 séparateurs verticaux (4×16 px, `BorderBrush` α=40 %) : (a) entre `[Disconnect/Cancel]` et `[Health/Fullscreen/SendKeys]` ; (b) entre `[SendKeys/Split]` et `[Resolution]`. Coût : ~6 lignes XAML, gain de lisibilité significatif sans changement fonctionnel. |
| RDP-LIVE-19 | 🟠 | `EmbeddedRdpView.xaml:324-391` (RedirectionIndicators) | **Indicateurs de redirection toujours affichés (8 icônes)**, même quand toutes sont désactivées. Sur un profil sans redirection, c'est une rangée de 8 icônes barrées qui occupent visuellement la status zone. Bruit visuel disproportionné par rapport à l'information. | Mode "auto-collapse" : par défaut, n'afficher que les redirections **actives** ; un bouton "+ N disabled" expand la liste complète au survol. Setting opt-in `RdpRedirectionIndicatorsAlwaysExpanded` (default false) pour les utilisateurs qui préfèrent l'affichage actuel. |
| RDP-LIVE-20 | 🟡 | `EmbeddedRdpView.xaml.cs` (`SendKeys` menu) | **`SendKeys` menu n'inclut pas Win+L (lock workstation), Win+D (show desktop), Win+E (file explorer)**. Cas d'usage admin courants : verrouiller la session distante en partant, afficher le bureau pour vérifier qu'on est bien sur la bonne machine. Aujourd'hui : seuls Ctrl+Alt+Del, Win, Alt+Tab, Ctrl+Esc, PrtScn, Esc. | Ajouter Win+L / Win+D / Win+E au menu, dans une sous-section "System" séparée des "Common". Ou exposer une "Custom send keys" entry avec free-form input. |
| RDP-LIVE-21 | 🟡 | `EmbeddedRdpView.xaml:148-174` (Resolution button glyph) | **Le bouton Resolution garde le même glyphe quel que soit le mode actif**. En mode Fixed avec letterbox, l'utilisateur n'a aucun indice glanché-en-passant que sa résolution est figée — il faut ouvrir le menu pour le savoir. | Glyphe différent par mode : `` (rectangle plain) pour Fixed, `` (corner-grow) pour Fit, `` + animation pour Smart. Tooltip mis à jour avec le mode courant. |
| RDP-LIVE-22 | 🟡 | `EmbeddedRdpView.xaml:417-513` (overlay Disconnect) | **Le bouton "Edit Profile" sur l'overlay reconnect est masqué quand l'erreur n'est pas listée dans `RdpDisconnectActionPolicy.ShouldOfferEditProfile`**. Pour les codes "auth/security" oui, mais pour les codes réseau (transient ou terminal), l'utilisateur qui veut changer la résolution / désactiver multimon / changer de gateway après un échec doit fermer l'overlay puis rouvrir le ServerDialog manuellement. | Toujours montrer "Edit profile" — il est rarement nuisible et coûteux à manquer. Alternative : conditionner sur `severity != Information` (codes "transient ok" peuvent rester sans). |

### B. ServerDialog — profil RDP

| ID | Sév. | Fichier:ligne | Constat | Recommandation |
|----|------|---------------|---------|----------------|
| RDP-PROF-07 | 🟠 | `ServerDialog.xaml:1533-1779` (TabItem Options) | **L'onglet "Options" RDP empile 4 expanders (Resolution / Audio-color / Redirection / Performance)** sans navigation interne ni table des matières. L'utilisateur qui cherche "Disable UDP" doit deviner qu'il est dans "Performance" (advanced) puis dérouler. Coût cognitif élevé pour les profils non-triviaux. | Ajouter une mini-toc collapsible en haut de l'onglet (4 chips : Display / Audio / Devices / Performance) qui scroll-to-section au clic. Coût : un `WrapPanel` + 4 `ButtonStyle` ghost. |
| RDP-PROF-08 | 🟠 | `ServerDialog.xaml:1658-1685` (Resolution mode combo) | **Le mode Multimon est dans le ComboBox de mode résolution**, et non comme toggle séparé. C'est techniquement vrai (multimon implique fixed+stretch sur N écrans), mais l'utilisateur cherche "multimonitor" comme une **feature de redirection**, pas comme un **mode résolution**. Découvrabilité réduite. | Soit (a) dupliquer "Enable multi-monitor" dans la section Display avec un binding two-way vers `RdpResolutionMode == Multimon`, soit (b) renommer l'option ComboBox en "Multi-monitor (auto-resolution)" pour être plus explicite sur ce que ça implique. |
| RDP-PROF-09 | 🟠 | `ServerDialog.xaml` (advanced mode) | **L'advanced mode est un toggle binaire sticky, pas un toggle par-onglet**. Quand l'utilisateur active advanced pour configurer une seule chose (ex. `RdpDisableUdp`), tous les autres onglets passent en advanced. Au revisit du profil, l'advanced reste actif même si rien d'avancé n'est plus configuré. | Soit (a) reset advanced à false à la prochaine ouverture si aucun champ advanced ≠ default, soit (b) advanced par-onglet (chip "Advanced" cliquable dans le header de chaque onglet). Option (a) plus simple. |
| RDP-PROF-10 | 🟡 | `ServerDialog.xaml.cs` (Step 1 → Step 2) | **Step 2 ne montre pas visuellement quel protocole a été choisi en Step 1**. Une fois en Step 2, le titre du dialog reste "Add server" et le seul indice est le "Back" button. Si l'utilisateur a cliqué par erreur RDP au lieu de SSH, il découvre l'erreur seulement quand il voit les champs RDP-spécifiques. | Ajouter une chip protocole (Geo.Protocol.Rdp + "RDP" label) à droite du titre Step 2, cliquable pour revenir à Step 1. Cohérent avec les patterns wizard modernes (Stripe, Linear). |
| RDP-PROF-11 | 🟡 | `ServerDialog.xaml:1772-1776` (NLA + DynamicResolution + AudioCapture) | **Trois checkboxes alignés sans hiérarchie**. NLA est un setting de **sécurité** (impact auth), DynamicResolution est un setting de **display**, AudioCapture est un setting de **device** — trois familles. Les regrouper visuellement aiderait à la mémorisation. | Soit déplacer NLA dans une section "Authentication" (où il y a déjà username/password), soit ajouter de petits labels-section ("Security:", "Display:", "Audio:") au-dessus de chaque checkbox pertinente. |
| RDP-PROF-12 | 🟡 | `ServerDialogViewModel.cs:RdpFixedWidth/Height` | **Width/Height sont libres entre 200 et 7680 / 4320, sans suggestion de presets**. L'utilisateur tape les chiffres à la main alors que les ratios standards (16:9, 16:10, 4:3) couvrent 95 % des cas. | Ajouter à droite des deux TextBox un petit ComboBox "Common: 1920×1080 / 2560×1440 / 3840×2160 / Custom" qui pré-remplit les deux champs au choix. |

### C. Parcours de lancement

| ID | Sév. | Fichier:ligne | Constat | Recommandation |
|----|------|---------------|---------|----------------|
| RDP-DISC-03 | 🟠 | `RdpHandler.cs:79-86` + `ServerDialogViewModel.RdpMode` | **Mode Embedded ↔ External est per-server only, pas d'override one-shot au lancement**. Cas d'usage : l'utilisateur veut tester un comportement en mstsc (debug RDP) sans modifier le profil. Aujourd'hui : il faut éditer le profil, lancer, re-éditer pour rétablir. Friction notable en triage / support. | Ajouter dans le menu contextuel "Connect with..." (sous-menu Connect) avec deux items : "Connect (embedded)" / "Connect (external mstsc)" — toujours visibles pour les profils RDP, override one-shot non persisté. |
| RDP-DISC-04 | 🟡 | `CommandPaletteViewModel.Search.cs` (ad-hoc) | **La palette propose toujours SSH avant RDP** pour une bare IP/hostname, même quand l'utilisateur a un historique 100 % RDP avec ce host. RDP-DISC-01 du précédent audit était sur la pondération générale (deferred) ; ici c'est plus précis : l'historique **par-host** est ignoré. | Quand `CommandPaletteViewModel` génère les deux options ad-hoc, biaiser l'ordre selon `LastConnectionHistory[host]` si disponible. Coût : un Dictionary<string, ProtocolType> alimenté à chaque connexion réussie. |
| RDP-DISC-05 | 🟡 | (absence de fonctionnalité) | **Pas de "Recent connections"** quick-list dans la sidebar ou le menu. Pour reconnecter à un serveur récent, il faut soit naviguer le TreeView (long si 100+ serveurs), soit utiliser Ctrl+K (palette) — qui demande de retaper le nom. | Ajouter une mini-section "Recents" (top 5) dans la sidebar Sessions tab, ou un sous-menu File > Recent connections (pattern VS Code, JetBrains). Pourrait être alimenté par le même Dictionary que RDP-DISC-04. |

### D. Settings RDP

| ID | Sév. | Fichier:ligne | Constat | Recommandation |
|----|------|---------------|---------|----------------|
| RDP-SET-01a | 🟠 | `MainWindow.xaml:2164-2243` (Mw_SettingsTabRdp) | **2 propriétés `AppSettings` RDP ne sont exposées nulle part dans l'UI** : `RdpResolutionPresets` (le menu de résolutions de la session live, par défaut 10 entrées 1024×768 à 3840×2160 — l'utilisateur ne peut pas en ajouter / retirer sans éditer `settings.json`), et `RdpDialogAdvancedDefault` (toggle qui force advanced mode par défaut dans ServerDialog, persisté implicitement quand l'utilisateur active advanced une fois). | (1) Ajouter dans l'onglet RDP une sous-section "Resolution presets" avec une `ListBox` éditable (entrées format "WIDTHxHEIGHT", boutons add/remove/reset). (2) Ajouter une checkbox "Open Server dialog in advanced mode by default" liée à `RdpDialogAdvancedDefault`. |
| RDP-SET-01b | 🟠 | `MainWindow.xaml:2345-2386` (`Mw_SettingsRdpAdvancedTimeoutsExpander` dans onglet Advanced) | **3 timeouts RDP cruciaux sont planqués dans l'onglet Advanced**, pas dans l'onglet RDP : `RdpResizeEnableDelayMs` (10 s d'attente avant qu'un resize fonctionne — les utilisateurs LAN voudraient le réduire), `RdpArtifactCleanupDelayMs`, `RdpCredentialAutofillTimeoutMs` (90 s, pertinent pour Windows Hello / smartcard). Quand l'utilisateur cherche à régler "le délai de resize RDP", il va naturellement dans l'onglet RDP, pas Advanced — il ne les trouve pas. | Déplacer `Mw_SettingsRdpAdvancedTimeoutsExpander` du tab Advanced vers le bas du tab RDP, en `IsExpanded=False` (préserve l'effet "noyau simple, advanced replié"). L'onglet Advanced peut conserver d'autres réglages cross-protocol mais ces 3 sont strictement RDP. |
| RDP-SET-02 | 🟠 | `MainWindow.xaml:2167-2240` | **L'onglet RDP est un long `StackPanel` de 18 contrôles avec un seul subtitle ("Redirection")**. Pas de groupement Display / Audio / Network / Devices. Coût de scan élevé. Comparaison : l'onglet SSH & SFTP a des sections claires (Plink path / Auth / Anti-idle / Reconnect). | Réorganiser en 4 sections avec subtitles : (a) Defaults for new RDP profiles → mode + resolution + color depth, (b) Display → dynamic resolution + multimon, (c) Audio → audio mode + audio capture, (d) Performance → bitmap caching + compression + auto-reconnect, (e) Devices → 7 redirections. |
| RDP-SET-03 | 🟡 | `MainWindow.xaml:2224-2238` (checkboxes redirections + perf) | **18 checkboxes sans tooltip explicatif**. "RdpDefaultBitmapCaching" — l'utilisateur lambda ne sait pas si c'est pour économiser bande passante ou améliorer perf. RDP-PROF-04 du précédent audit a ajouté des tooltips dans ServerDialog ; **les mêmes tooltips ne sont pas réutilisés ici**. | Réutiliser les clés `Rdp*Hint` déjà localisées (BitmapCaching, AutoReconnect, DisableUdp, etc.) comme `ToolTip` sur les checkboxes correspondantes du Settings tab. Coût quasi-nul. |
| RDP-SET-04 | 🟡 | `MainWindow.xaml:2164-2243` | **Pas de "Reset RDP defaults to factory"**. L'utilisateur qui a bricolé sans suivre n'a aucun moyen de revenir à un état propre sans toucher tous les autres settings via le bouton global. | Ajouter en bas de l'onglet un `LinkButton` discret "Reset RDP defaults" qui restaure les valeurs de `settings.default.json` pour les seules propriétés `RdpDefault*` + `DefaultResolution*`. |
| RDP-SET-05 | 🟡 | `MainWindow.xaml:2177-2184` (Apply to all button) | **Le bouton "Apply to all" est petit (`FontSizeCaption` + Padding 8,2) à côté du ComboBox du mode**. Action **destructive de masse** (overwrite la propriété `RdpMode` de tous les profils RDP existants) — risque de mis-clic. | Soit (a) confirmation modale "This will change the RDP mode of N existing profiles. Continue?" avant d'appliquer ; soit (b) styliser le bouton avec un fond `WarningBrush` α=20 % pour matérialiser le risque ; idéalement les deux. |

## Priorités recommandées (révisées après amendement Codex)

Top 5 par ratio impact / effort, dans l'ordre où je les attaquerais :

1. **RDP-DISC-06** 🔴 (Codex P1#1) — Mode External doit lire le profil de résolution (`server.RdpResolutionMode` / `RdpFixedWidth/Height`), pas `settings.DefaultResolution*`. Bug d'incohérence comportementale : ce que l'utilisateur configure dans le profil n'est pas appliqué quand mstsc est lancé. Coût : refactor du `RdpFileGenerator` call dans `RdpHandler.cs:122-135` pour passer par `RdpProfileResolver` (qui résout déjà `ColorDepth` correctement — étendre le pattern à width/height/multimon).
2. **RDP-LIVE-23** 🔴 (Codex P1#2) — `ServerStatusToColorConverter.cs:53` mappe `"launchedexternalclient" → SuccessBrush` (vert). UI surchage l'état réel ("client lancé" ≠ "session connectée et authentifiée"). Coût : ajouter une couleur dédiée `LaunchedBrush` (orange/jaune) ou réutiliser `WarningBrush` ; revoir la chaîne de status pour ne pas afficher `StatusConnected` côté `CommandPaletteViewModel.cs:414`.
3. **RDP-LIVE-18** 🟠 — Séparateurs dans la toolbar embedded. Coût ~6 lignes XAML, gain immédiat de lisibilité pour tous les utilisateurs à chaque session.
4. **RDP-SET-02** 🟠 — Réorganiser l'onglet Settings RDP en 5 sous-sections. Coût ~30 lignes XAML, transforme un mur de checkboxes en surface scannable.
5. **RDP-DISC-03** 🟠 — "Connect with..." sous-menu pour override Embedded/External. Cohérent avec le findings RDP-DISC-06 (si External applique enfin le profil, l'override one-shot devient un vrai outil de triage utilisable).

À traiter ensuite : RDP-LIVE-17 (letterbox), RDP-SET-01b (timeouts mal placés), RDP-PROF-13 (picker de moniteurs — Codex P2#3), RDP-DISC-07 (unifier les chemins d'import .rdp — Codex P2#4).

## Findings additionnels Codex (2026-05-04, amendement)

Après publication du draft initial, audit différentiel transmis par Codex (mode read-only). Codex a relevé 6 findings ; 2 chevauchent mes propres findings (RDP-DISC-04 ↔ Codex P2#5 sur la pondération palette ; RDP-ERR-04 du précédent audit ↔ Codex P3 sur les codes inconnus déjà deferred). Les 4 autres sont nouveaux et **vérifiés directement** sur les références fichier:ligne avant intégration. Mention explicite de l'origine pour traçabilité.

| ID | Sév. | Fichier:ligne | Constat | Recommandation |
|----|------|---------------|---------|----------------|
| RDP-DISC-06 | 🔴 | `RdpHandler.cs:122-135` (Codex P1#1) | **Le mode External ignore le profil de résolution**. Le `.rdp` généré utilise `settings.DefaultResolutionWidth/Height` (settings global) au lieu de `server.RdpResolutionMode` / `server.RdpFixedWidth/Height` / `server.RdpMultiMonitor`. Résultat : un profil configuré en `Fixed 2560×1440 + Multimon`, lancé en mode External, démarre en `1920×1080` mono-écran. **Inconsistance frappante** car `ColorDepth` est résolu correctement via `RdpProfileResolver.ResolveColorDepth` ligne 130 — la résolution n'a juste pas suivi le même pattern. Bug fonctionnel UX masqué : l'utilisateur croit que le profil est appliqué. | Étendre `RdpProfileResolver` avec `ResolveResolution(server, settings)` qui retourne un tuple `(Width, Height, MultiMonitor, SmartSizing)` selon `RdpResolutionMode`. Brancher sur `RdpFileGenerator.Generate` lignes 128-129. Test unitaire couvrant les 5 modes (Auto / FitWindow / Fixed / SmartSizing / Multimon). |
| RDP-LIVE-23 | 🔴 | `ServerStatusToColorConverter.cs:53` + `CommandPaletteViewModel.cs:414` (Codex P1#2) | **`"launchedexternalclient"` est peint en vert `SuccessBrush`**, identique à `"connected"`. Pourtant `LaunchedExternalClient` signifie "mstsc.exe a été lancé", pas "la session RDP est établie et authentifiée". L'UI ment sur la nature de l'état : un mstsc qui crash auth-failure est peint en vert pendant des secondes. De plus, `CommandPaletteViewModel.cs:414` utilise `StatusConnected` pour des connexions qui sont en fait `LaunchedExternalClient`. Honnêteté UI compromise. | (1) Ajouter un brush `LaunchedBrush` (palette Dracula : Yellow `#F1FA8C` est libre et lisible) ou réutiliser `WarningBrush` ; mapper `"launchedexternalclient"` dessus ligne 53. (2) Auditer les sites qui poussent `StatusConnected` pour s'assurer qu'ils n'écrasent pas `LaunchedExternalClient` quand le client externe est utilisé (`CommandPaletteViewModel.cs:414` cité par Codex). |
| RDP-PROF-13 | 🟠 | `RdpDisplayResolver.cs:70-79` (Codex P2#3) | **Multimon prend tous les écrans, pas de picker**. `CoalesceSize(hostContext.MonitorBoundsPhysicalPx, DefaultSize)` → toutes les bornes du host context. Setups 3 écrans, laptop+dock, écran vertical : l'utilisateur n'a aucun moyen de dire "RDP sur écrans 1+2 mais pas 3 (qui est mon écran de monitoring)". `IMsRdpClientNonScriptable5.SelectedMonitors` (la propriété COM) accepte un tableau d'indices ; aujourd'hui le code passe la liste complète implicitement. | Ajouter à `ServerDialog` (section Display, mode Multimon actif) un mini-picker de moniteurs : checkboxes `Monitor 1 (1920×1080)` / `Monitor 2 (3840×2160)` / etc. listant les écrans détectés via `Screen.AllScreens`. Persister `server.RdpSelectedMonitors` (string[]). Brancher sur `IMsRdpClientNonScriptable5.SelectedMonitors` au connect. |
| RDP-DISC-07 | 🟠 | `SettingsViewModel.cs:790+` (Codex P2#4) | **Deux expériences distinctes pour importer un `.rdp`**. Drag/drop sur la fenêtre principale → flux riche avec preview/conflict detection (workflow soigné). Bouton Import depuis Settings → parsing direct sans preview. Comportements et garanties différents selon le point d'entrée — l'utilisateur ne sait pas lequel utiliser et ne s'attend pas à ce qu'il y ait deux. | Faire pointer `ImportConfigAsync` (Settings) vers le même service que celui utilisé par le drag/drop handler. Détection du type de fichier (.json config vs .rdp) → branchement sur le bon parser. Preview/conflict resolution communs aux deux entrées. |

**Note d'humilité.** Les deux P1 de Codex (RDP-DISC-06, RDP-LIVE-23) sont les findings les plus impactants de tout cet audit — pourtant je les ai loupés en restant trop concentré sur l'embedded et en survolant le mode External. La leçon : un audit UX sur une app qui a deux modes (embedded / external) doit auditer les deux modes en profondeur, sans présumer qu'ils sont équivalents juste parce que la même surface dialog les configure.

## Ce qui marche bien (vérifié)

- **Onglet Settings RDP existe et a une couverture utile** : 18 propriétés exposées dans l'onglet RDP + 3 timeouts dans l'onglet Advanced (cf. RDP-SET-01b pour le repositionnement), dont l'élégant bouton "Apply to all" pour propager le mode à tous les profils. Le `AutomationProperties.LabeledBy` est correctement appliqué partout.
- **Step 1 / Step 2 du ServerDialog** : la transition est fluide (animation 300 ms), et la grille de cartes protocole avec icône + description fonctionne bien comme entrée de funnel.
- **Resolution menu** : l'`IsChecked` sur le preset actif est correct (correction RDP-LIVE-14 du précédent audit), `Skip stabilization` est présent (RDP-LIVE-07), `Fit to Window` est l'option par défaut visible sans scroll.
- **Confirmation pré-disconnect** (RDP-LIVE-02 et RDP-ERR-03 du précédent audit) : `RdpConfirmDisconnect` flag dans Settings, dialog modal avant déconnexion. Réflexe Tab+Enter ne perd plus la session.
- **Overlay reconnect modal** : `IsDialog`, focus programmatique sur "Reconnect", Esc/Enter mappés. Surface RDP transparente derrière (RDP-LIVE-13). Severity prefix "Notice/Warning/Error" (RDP-A11Y-03).
- **Séparation Embedded / External est claire à la lecture du code** : `RdpHandler` route proprement, `EmbeddedSessionManager` gère le tab, mstsc gère son propre cycle de vie. Pas de fuite d'abstraction visible côté utilisateur.
- **Watermark `DOMAIN\user`** (RDP-PROF-02) appliqué et tooltip présent.
- **Test connection** reformulé pour ne pas survendre (RDP-PROF-03).
- **Save as profile** sur sessions ad-hoc (RDP-PROF-06) résout le RDP-DISC-05 partiellement (l'utilisateur peut promouvoir un ad-hoc en profil persistant).

## Ce qui n'a délibérément pas été flaggé

- **Plénitude de l'onglet Options du ServerDialog** : 4 expanders + 18+ contrôles est énorme, mais c'est conforme à ce que MobaXterm / mRemoteNG / Royal TS exposent. Contrainte du domaine, pas de l'app.
- **Modal Window VS Border pour overlay reconnect** : décision déjà documentée dans le précédent audit (un Border ne peut pas exposer Window pattern, focus management programmatique est la bonne approche).
- **WebView2 / xterm.js** : hors scope RDP (concerne SSH).
- **Comparaison directe avec MobaXterm/mRemoteNG sur des screenshots** : sortirait du scope code-only ; à reprendre dans une passe future avec captures d'écran.
- **Onboarding 3-step** : déjà couvert par d'autres audits (`audit-2026-04-22.md`).
- **Glyphes Segoe MDL2 sans label texte sur les boutons toolbar** : convention Windows-native acceptée. `AutomationProperties.Name` localisé suffit.

## Faux positifs des sous-agents écartés

Pendant l'exploration parallèle, un sous-agent a affirmé :

1. ~~« Aucun onglet RDP dans Settings — 0 propriété exposée — 21 propriétés cachées. »~~ — Faux. `Mw_SettingsTabRdp` existe ligne 2164 avec 18 contrôles. Le sous-agent #4 a lu une section partielle de `MainWindow.xaml` qui n'incluait pas l'onglet RDP. **Vérification directe** : `grep "x:Name=.*Settings.*Tab" MainWindow.xaml` retourne 7 onglets dont `Mw_SettingsTabRdp`. Finding `RDPUX-CRITICAL-01` retiré avant écriture du rapport.
2. ~~« Pas de cancel reconnect button. »~~ — Faux. `CancelReconnectButton` est ligne 38-49 de `EmbeddedRdpView.xaml`, visibilité gérée par `IsAutoReconnecting`. Sous-agent #3 ne l'avait pas vu.
3. ~~« 3 timeouts RDP non exposés (RdpResizeEnableDelayMs, RdpArtifactCleanupDelayMs, RdpCredentialAutofillTimeoutMs). »~~ — À moitié faux. Le draft initial du finding RDP-SET-01 listait ces 3 propriétés comme "absentes de l'UI". Vérification directe : elles **sont** exposées dans `Mw_SettingsRdpAdvancedTimeoutsExpander` lignes 2345-2386 de `MainWindow.xaml`, mais dans l'onglet **Advanced**, pas l'onglet RDP. Finding scindé en RDP-SET-01a (vraiment non exposé) et RDP-SET-01b (mal placé) avant publication.

Ces faux positifs n'ont pas été inscrits aux findings. Le coût d'avoir vérifié vaut mieux que le coût d'avoir publié un finding faux.

## Implementation log (cycle 2026-05-04)

Implémentation menée le 2026-05-04 via **8 prompts Codex enchaînés** en mode Pair Architect, avec 2 mini-correctifs (#3-bis tuning du séparateur de toolbar, #6-bis tentative de fix sur le SurfaceBrush du letterbox). Communication architect-Codex en anglais ; screenshots de validation pris à chaque changement UI visible (toolbar séparateurs, Settings RDP réorganisé, menu contextuel "Se connecter via...", suffixe titre forcé, sub-section Moniteurs sélectionnés, dialog reset RDP).

Baseline tests au démarrage : 5,030 passing + 6 skipped. Baseline finale : **5,281 passing + 6 skipped, 0 failing, 0 warning**, parité i18n maintenue (en=fr=5,458 leaf keys finaux).

Légende du statut :

- **Done** — finding traité par un prompt dédié, build + tests verts.
- **Done (groupé)** — traité dans le même prompt qu'un finding voisin (typiquement le prompt #4 qui a regroupé 4 findings Settings).
- **Done (struct only)** — la partie structurelle est livrée, un follow-up est ouvert pour le polish visuel restant.
- **Deferred** — non adressé dans ce cycle, rééligible à un prochain sprint polish.

| # | Finding | Sév. | Prompt | Status | Notes |
|---|---------|------|--------|--------|-------|
| 1 | RDP-DISC-06 | 🔴 | #1 | Done | `RdpProfileResolver.ResolveResolution(server, settings)` retourne `(Width, Height, MultiMonitor, SmartSizing)` ; mode External applique enfin le profil au lieu des settings globaux. +10 tests. |
| 2 | RDP-LIVE-23 | 🔴 | #2 | Done | `launchedexternalclient` mappé sur `WarningBrush` (orange) au lieu de `SuccessBrush` ; 4 sites de transition vers `Connected` corrigés ; status text dédié "External client launched". +2 tests. |
| 3 | RDP-LIVE-18 | 🟠 | #3 | Done | 2 séparateurs verticaux dans la toolbar embedded RDP (group-bound visibility pour SeparatorA-B via DataTrigger) + standardisation des séparateurs SFTP existants. Tuning ultérieur via #3-bis (Width 1.5, Opacity 0.65). |
| 4 | RDP-LIVE-17 | 🟠 | #6 + #6-bis | Done (struct only) | Border 1px autour de `FormsHost` matérialise la zone RDP. Hint badge first-letterbox implémenté avec helpers extraits (`RdpRegionFrameLayout`, `LetterboxHintState`). **Bandes letterbox restent en gris système** au lieu de SurfaceBrush — airspace WindowsFormsHost à creuser. Voir RDP-LIVE-24. +7 tests. |
| 5 | RDP-DISC-03 | 🟠 | #5 | Done | Sous-menu "Se connecter via..." → Embedded/External one-shot. `RdpModeOverride` propagé sur `IProtocolHandler`. Suffixe titre "(forcé intégré/externe)". `IRdpExternalClientLauncher` testable injecté via DI. +9 tests. |
| 6 | RDP-PROF-13 | 🟠 | #7 | Done | Picker de moniteurs (sub-section "Moniteurs sélectionnés") en mode Multimon. `IMonitorEnumerator` test seam. Voie standard documentée `MsRdpClientShell.SetRdpProperty("selectedmonitors", ...)` + fallback `IMsRdpClientNonScriptable5`. Directive `selectedmonitors:s:` côté .rdp External. +11 tests. |
| 7 | RDP-DISC-07 | 🟠 | #8 | Done | `IProfileImportService` extrait de `ServerListViewModel.ImportRdpFilesAsync` ; Settings et drag/drop convergent vers le même flux preview/conflict. Formats historiques (MobaXterm/RDCMan/mRemoteNG) préservés. +6 tests. |
| 8 | RDP-SET-01b | 🟠 | #4 | Done | 3 timeouts RDP relocalisés depuis l'onglet Advanced vers l'onglet RDP (Expander "Timeouts avancés", replié par défaut). |
| 9 | RDP-SET-02 | 🟠 | #4 | Done | Onglet Settings RDP réorganisé en 6 cards : Defaults / Affichage / Audio / Performance / Périphériques / Timeouts avancés. |
| 10 | RDP-SET-03 | 🟡 | #4 | Done (groupé) | Tooltips sur les checkboxes RDP via clés `Rdp*Hint` (3 réutilisées, 11 créées). |
| 11 | RDP-SET-04 | 🟡 | #4 | Done (groupé) | Lien "Réinitialiser les valeurs RDP" en bas de l'onglet RDP, hors card, avec confirmation modale. |
| 12 | RDP-SET-05 | 🟡 | #4 | Done (groupé) | Confirmation modale avant "Apply to all" (action destructive de masse, count des profils RDP affecté affiché). |
| 13 | RDP-LIVE-16 | 🟠 | polish | Done (sprint 2026-05-04 polish) | Indication du mode résolution actif (Fit / Fixed / Smart) dans le menu Resolution. |
| 14 | RDP-LIVE-19 | 🟠 | polish | Done (sprint 2026-05-04 polish) | Auto-collapse des indicateurs de redirection désactivés. |
| 15 | RDP-LIVE-22 | 🟡 | polish | Done (sprint 2026-05-04 polish) | "Edit Profile" toujours visible dans l'overlay reconnect (pas seulement codes auth). |
| 16 | RDP-PROF-07 | 🟠 | polish | Done (sprint 2026-05-04 polish) | Mini-toc des onglets Options (Display / Audio / Devices / Performance). |
| 17 | RDP-PROF-08 | 🟠 | polish | Done (sprint 2026-05-04 polish) | Multimon comme toggle séparé en plus du ComboBox de mode. |
| 18 | RDP-PROF-09 | 🟠 | polish | Done (sprint 2026-05-04 polish) | Reset du toggle Advanced à false si aucun champ avancé personnalisé. |
| 19 | RDP-PROF-10 | 🟡 | polish | Done (sprint 2026-05-04 polish) | Chip de protocole en Step 2 du ServerDialog. |
| 20 | RDP-PROF-11 | 🟡 | polish | Done (sprint 2026-05-04 polish) | Regroupement NLA / DynamicResolution / AudioCapture par famille (Security / Display / Audio). |
| 21 | RDP-PROF-12 | 🟡 | polish | Done (sprint 2026-05-04 polish) | ComboBox "Common: 1920×1080 / 2560×1440 / ..." à côté des champs Width/Height. |
| 22 | RDP-DISC-04 | 🟡 | polish | Done (sprint 2026-05-04 polish) | Pondération palette ad-hoc par historique par-host (RDP en premier si historique RDP). |
| 23 | RDP-DISC-05 | 🟡 | polish | Done (sprint 2026-05-04 polish) | "Recent connections" quick-list dans sidebar ou menu. |
| 24 | RDP-SET-01a | 🟠 | polish | Done (sprint 2026-05-04 polish) | Exposer `RdpResolutionPresets` (UI éditable) et `RdpDialogAdvancedDefault` dans l'onglet RDP. |
| 25 | RDP-LIVE-20 | 🟡 | polish | Done (sprint 2026-05-04 polish) | Win+L / Win+D / Win+E dans le menu SendKeys (sous-section "System"). |
| 26 | RDP-LIVE-21 | 🟡 | polish | Done (sprint 2026-05-04 polish) | Glyphe différent par mode résolution sur le bouton Resolution. |

### Récapitulatif chiffré (après sprint polish 2026-05-04)

| Statut | Compte | Pourcentage |
|--------|-------|-------------|
| Done (cycle initial : direct, groupé, ou struct only) | 12 | 46 % |
| Done (sprint polish 2026-05-04) | 14 | 54 % |
| **Total** | **26** | **100 %** |

**Couverture des findings critiques (🔴)** : 2/2 = 100 %. Les deux findings P1 du fork Codex (mode External ignorant le profil + statut "launched" peint en vert) sont fixés au cycle initial.

**Couverture des findings importants (🟠)** : 13/13 = 100 % après le sprint polish.

**Couverture des findings mineurs (🟡)** : 11/11 = 100 % après le sprint polish.

> Les 2 follow-ups émergés (`RDP-LIVE-24` letterbox SurfaceBrush, `RDP-LIVE-25` tooltip Multi-écran) sont également bouclés dans le sprint polish — voir la section dédiée plus bas.

### Métriques de campagne

- **Prompts Codex enchaînés** : 8 prompts principaux + 2 mini-correctifs (#3-bis tuning, #6-bis SurfaceBrush) = 10 cycles total.
- **Tests** : 5,030 → 5,281 (+251 sur la durée de la campagne, dont au moins 50 directement attribuables à cette campagne ; le reste vient de l'enrichissement parallèle du worktree).
- **Build** : 0 warning, 0 error sur les 10 livraisons. `TreatWarningsAsErrors` actif.
- **Parité i18n** : maintenue à chaque livraison (en=fr=5,458 leaf keys finaux).
- **Faux positifs des sous-agents** écartés en amont du rapport : 3 (cf. section "Faux positifs").
- **Faux positifs Codex** : 0 (toutes les déviations annoncées étaient des améliorations soutenues).
- **Tracking** : commits séparés par prompt côté Codex (pas de squash). Ce rapport est versionné en commit séparé docs-only.

### Patterns d'engineering remarqués pendant le cycle

Trois patterns Codex valent la peine d'être documentés pour future référence :

1. **`SetRdpProperty(...)` + fallback non-scriptable** (prompt #7) — pour les propriétés COM RDP, préférer la voie documentée Microsoft (`MsRdpClientShell.SetRdpProperty`) avec un fallback best-effort sur l'interface non-scriptable. La doc Microsoft ne liste pas toutes les propriétés disponibles via `IMsRdpClient*5`, donc le pattern défensif est nécessaire pour la stabilité long-terme. Mérite peut-être un ajout au CLAUDE.md gotcha section.
2. **Test seams via interface + `_Enumerator` pattern** (prompt #7) — `IMonitorEnumerator` + `WinFormsMonitorEnumerator` sépare l'appel à `Screen.AllScreens` (statique, non mockable) de la logique métier. Reproductible pour toute API statique Windows.
3. **Partial class pour ViewModels qui grossissent** (prompt #7) — `ServerDialogViewModel.PostConnect.cs` extrait le post-connect du dialog principal sans casser l'identité du fichier source. Hygiène à reproduire quand un VM dépasse ~600 lignes.

## Follow-ups (émergents — clos par le sprint polish)

### Nouveaux follow-ups émergés pendant le cycle d'implémentation

| ID | Sév. | Origine | Description | Statut |
|----|------|---------|-------------|--------|
| RDP-LIVE-24 | 🟡 | Validation visuelle prompt #6 | **Letterbox visual polish.** Les bandes letterbox apparaissaient en gris système (~#B0B0B0) au lieu du `SurfaceBrush` Dracula (#1B1C25). Cause probable : règle airspace WindowsFormsHost qui bypasse le `Background` WPF du wrapper. Fix structurel envisagé : dimensionner exactement le `WindowsFormsHost` à la taille letterbox (au lieu de Stretch dans Border) pour que le SurfaceBrush du parent SurfaceContainer soit la couleur visible autour. **Aussi** : vérifier le déclenchement réel du hint badge first-display (non observé sur captures du cycle — possible problème de timing ou de détection). | Done (sprint polish 2026-05-04) — `RdpRegionFrameLayout` pinne `HostWidth/Height` à la taille de la frame quand letterbox actif, le HWND ne s'étend plus au-delà et le `SurfaceBrush` du parent rend correctement les bandes (validation visuelle utilisateur). |
| RDP-LIVE-25 | 🟢 | Découverte capture prompt #7 | **Tooltip "Multi-écran" en Settings RDP devenu partiellement faux** suite à l'arrivée du picker de moniteurs (RDP-PROF-13). Wording actuel : "Utilise tous les écrans locaux pour les sessions RDP externes quand mstsc.exe le prend en charge." | Done (sprint polish 2026-05-04) — `RdpMultiMonitorHint` réécrit en EN/FR pour décrire le rôle du flag global et renvoyer vers le picker per-profile. |

### Deferred du cycle pure-UX (clos par le sprint polish 2026-05-04)

Tous les 14 items deferred du cycle initial ont été bouclés dans le sprint polish 2026-05-04 (cf. section *Sprint polish (2026-05-04)* plus bas). La liste ci-dessous est conservée à titre historique pour suivre l'ordre de reprise effectivement appliqué :

1. **RDP-LIVE-16** (resolution menu mode indicator) — Done, Groupe A.
2. **RDP-LIVE-21** (Resolution button glyph par mode) — Done, Groupe A.
3. **RDP-LIVE-22** (Edit Profile toujours visible overlay) — Done, Groupe B.
4. **RDP-LIVE-25** (tooltip Multi-écran wording) — Done, Groupe B.
5. **RDP-LIVE-20** (Win+L/D/E SendKeys) — Done, Groupe B.
6. **RDP-LIVE-19** (auto-collapse redirection indicators) — Done, Groupe B.
7. **RDP-PROF-11** (regroupement NLA/DynRes/Audio par famille) — Done, Groupe C.
8. **RDP-PROF-12** (presets résolution rapide) — Done, Groupe C.
9. **RDP-PROF-08** (multimon en toggle séparé) — Done, Groupe C.
10. **RDP-PROF-07** (mini-toc onglet Options ServerDialog) — Done, Groupe C.
11. **RDP-PROF-09** (reset Advanced toggle smart) — Done, Groupe D.
12. **RDP-PROF-10** (chip protocole Step 2 ServerDialog) — Done, Groupe D.
13. **RDP-SET-01a** (Resolution presets + AdvancedDefault Settings) — Done, Groupe E.
14. **RDP-DISC-04** + **RDP-DISC-05** (palette par-host bias + Recents) — Done, Groupe F.
15. **RDP-LIVE-24** (letterbox SurfaceBrush) — Done, hors-groupe.

## Sprint polish (2026-05-04)

Sprint pair-architect distinct du cycle initial, mené **dans la même journée** mais avec un mode opératoire différent : pas de Codex en relais, l'agent Cowork (Claude Opus 4.7 1M) implémente lui-même chaque groupe.

### Mode opératoire

- **Découpage en 6 groupes cohérents** (A → F) ré-ordonnés par ratio impact/effort (et non par ordre du rapport), plus un item hors-groupe.
- **Plan de sprint formalisé** dans un fichier de plan (`majestic-juggling-beaver.md`) avant exécution du Groupe A — étape de planning architect séparée du delivery.
- **Validation visuelle screenshots** entre les Groupes A et B, puis bascule en mode "implémente tout" jusqu'au bout du sprint.
- **Livraison incrémentale** : un build + tests à la fin de chaque groupe (sauf Groupes D et E qui ont été enchaînés sans build intermédiaire pour limiter la friction).

### Groupes livrés

| Groupe | Findings | Approche |
|--------|----------|----------|
| **A** | LIVE-16 + LIVE-21 | `RdpResolutionModeIndicator` static helper (testable), `Active mode: <mode>` header dans le `ContextMenu` Resolution toolbar, glyphes Segoe MDL2 par mode (5 codepoints distincts), tooltip enrichi avec mode + dimensions. Header répliqué côté `SessionTabContextMenuFactory` pour le sous-menu clic droit Résolution. |
| **B** | LIVE-22 + LIVE-25 + LIVE-20 + LIVE-19 | `ShouldOfferEditProfile` toujours `true` (decoupling vs `IsProfileRemediationCode` privé qui pilote le primary action), `RdpMultiMonitorHint` réécrit EN/FR, 3 nouvelles entrées SendKeys (Win+L/D/E) + 3 keys i18n + 3 VK constants, `RdpRedirectionVisibilityPolicy` static helper + UI badge `+N` cliquable, opt-in `AppSettings.RdpRedirectionIndicatorsAlwaysExpanded`. |
| **C** | PROF-07 + PROF-08 + PROF-11 + PROF-12 | Mini-toc 4 chips `WrapPanel` avec `Tag` pointant vers les `x:Name` d'ancres existantes + handler `BringIntoView`, `IsMultimonModeSelected` two-way alias, sections labels XAML `Security:` / `Display:` / `Audio:`, ComboBox `Common resolutions` + `RelayCommand ApplyResolutionPreset`. |
| **D** | PROF-09 + PROF-10 | `ServerDialogAdvancedModePolicy.ResolveAdvancedDefault(snapshot)` pour collapse smart, chip protocole cliquable (Path Geo.Protocol.* + label) en remplacement du badge statique + bouton "Back" séparé. |
| **E** | SET-01a | Nouvelle propriété calculée `SettingsViewModel.RdpResolutionPresetsText` (sérialise/désérialise `string[]` en multi-ligne), `RelayCommand ResetRdpResolutionPresets`, observable `RdpDialogAdvancedDefault`, nouvelle carte XAML "Dialogue serveur" en bas de l'onglet Settings RDP. |
| **F** | DISC-04 + DISC-05 | `IRecentConnectionTracker` + impl `RecentConnectionTracker` (in-memory 50 entries, deduped), DI singleton, alimenté depuis `ServerListViewModel.OnConnectionStateChanged` sur `Connected` ou `LaunchedExternalClient`, consommé par `CommandPaletteViewModel.Search.cs` (palette ad-hoc reorder + boost recents quand query vide). |
| Hors-groupe | LIVE-24 | `RdpRegionFrameLayout.HostWidth/Height` pinnés à `FrameWidth/Height` quand `IsLetterboxActive`, `HostHorizontalAlignment/VerticalAlignment` passés à `Left/Top`. Le HWND est alloué exactement à la taille de la zone RDP, le `SurfaceBrush` du `SurfaceContainer` parent rend correctement autour. Validation visuelle utilisateur : ✅ bandes en couleur Dracula sombre comme attendu. |

### Métriques du sprint

| Métrique | Avant | Après |
|----------|-------|-------|
| Tests passing | 5,281 | **5,311** (+30) |
| Tests skipped | 6 | 6 |
| i18n leaf keys (en=fr) | 5,458 | **5,485** (+27) |
| Build warnings | 0 | 0 |
| Build errors | 0 | 0 |

### Nouvelles abstractions livrées par le sprint

- `RdpResolutionModeIndicator` (`Heimdall.App/Views/EmbeddedRdp/`) — helper static stateless pour la résolution du mode effectif live + glyphes/i18n keys + formatage strings.
- `RdpEffectiveResolutionState` (record struct) — `(Mode, Width?, Height?)`.
- `RdpRedirectionVisibilityPolicy` (`Heimdall.App/Views/EmbeddedRdp/`) — helper static pour `+N` chip + per-icon visibility.
- `IRecentConnectionTracker` / `RecentConnectionTracker` (`Heimdall.App/Services/`) — service singleton process-scoped pour l'historique connexions.
- `ServerDialogAdvancedModePolicy.AdvancedRdpSnapshot` + `ResolveAdvancedDefault` + `IsAdvancedRdpCustomized` — extension de la policy existante pour le smart reset PROF-09.
- `AppSettings.RdpRedirectionIndicatorsAlwaysExpanded` — opt-in pour conserver la visibilité legacy des indicators redirection.
- `AppSettings.RdpDialogAdvancedDefault` — désormais exposé en UI (Settings RDP → Dialogue serveur).
- `AppSettings.RdpResolutionPresets` — désormais éditable en UI (Settings RDP → Dialogue serveur → multi-line TextBox).

### Tests ajoutés

- `RdpResolutionModeIndicatorTests` — 12 tests (Resolve cas manual override / profile fallback, GetGlyph distinctness, GetModeLocalizationKey mapping, FormatHeader sans/avec dims, FormatTooltip).
- `RdpRedirectionVisibilityPolicyTests` — 9 tests (visibility matrix `[Theory]`, ShouldShowExpandBadge cas, CountDisabled).
- `RdpDisconnectActionPolicyTests` — refondu : `ShouldOfferEditProfile_ReturnsFalseForOtherCodes` remplacé par `ShouldOfferEditProfile_AlwaysReturnsTrue` couvrant 17 codes + null.
- `RdpRegionFrameLayoutTests` — assertions Host* mises à jour pour le pinning letterbox.

### Patterns d'engineering remarqués pendant le sprint

1. **Decoupling visibility from priority** — pattern appliqué à `RdpDisconnectActionPolicy` : la décision "afficher le bouton" et la décision "quel bouton est primary" sont deux fonctions différentes, idéalement nommées et testées séparément. Ce qui était initialement une seule méthode `ShouldOfferEditProfile` couvrant les deux rôles a été clarifié pour rendre la sémantique évidente : un bouton peut être *visible* sans être *primary*.
2. **Snapshot record pour policy decisions** — `AdvancedRdpSnapshot` capture les états observés, `ResolveAdvancedDefault(persistedDefault, isEditMode, snapshot)` est la décision pure. Pattern reproductible pour toute policy qui dépend d'un état multi-champs : passer un record snapshot plutôt que des paramètres positional.
3. **In-memory tracker derrière une interface** — `IRecentConnectionTracker` est volontairement minimaliste (3 méthodes : `Record`, `GetLastProtocol`, `GetRecents`). Pas de persistance disque dans cette itération ; future work peut ajouter load/save sans changer l'API. Pattern utile pour les features où l'absence de persistance est acceptable au premier jet.

## Note sur la convention de numérotation

Les IDs continuent la numérotation de l'audit 2026-04-30 :

- `RDP-LIVE-NN` : 16 → 25 — 10 findings (LIVE-15 était la dernière dans l'audit du 30 avril ; LIVE-23 ajouté par amendement Codex ; LIVE-24 et LIVE-25 émergés pendant le cycle d'implémentation)
- `RDP-PROF-NN` : 7 → 13 — 7 findings (PROF-06 était la dernière ; PROF-13 ajouté par amendement Codex)
- `RDP-DISC-NN` : 3 → 7 — 5 findings (DISC-02 était la dernière ; DISC-06 et DISC-07 ajoutés par amendement Codex)
- `RDP-SET-NN` : 01a, 01b, 02, 03, 04, 05 — 6 findings (nouveau préfixe, Settings n'avait pas son axe propre dans le précédent audit)

Aucun ID `RDP-A11Y-NN` ni `RDP-ERR-NN` cette fois, conformément au scope pure-UX.
