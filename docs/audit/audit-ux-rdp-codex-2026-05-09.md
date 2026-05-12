# Audit UX RDP - Heimdall.Next

**Date :** 2026-05-09  
**Auteur :** Codex  
**Scope :** surface RDP actuelle : ServerDialog, session RDP embedded, lancement mstsc externe, Settings RDP, resolution/multimonitor/reconnect.  
**Méthode :** audit code-only sur l'état courant du repo. Je n'ai pas ouvert de vraie session RDP distante ni validé les comportements ActiveX avec un serveur réel.  
**Relation aux audits précédents :** passe differentielle apres `docs/audit/audit-ux-rdp-2026-05-04.md`. Les points deja fermes dans ce rapport ne sont pas repetes sauf si l'etat courant laisse une friction residuelle.

## Resume executif

La surface RDP a nettement progresse depuis les audits d'avril : toolbar mieux groupee, reconnect overlay plus explicite, autofill embedded visible et retryable, mode embedded/external forcable au lancement, presets de resolution configurables, multi-monitor avec selection d'ecrans, redirections auto-collapsee, Settings RDP structure en sections.

Les frictions restantes sont surtout des ecarts entre ce que le modele sait faire et ce que l'utilisateur peut comprendre ou piloter depuis l'UI :

1. **RD Gateway n'est toujours pas editable dans le ServerDialog**, alors que `RdpGateway` est stocke, importe et consomme par le handler externe.
2. **Le mode externe mstsc garde l'autofill comme evenement de log**, pas comme etat visible/actionnable pour l'utilisateur.
3. **Le mode de resolution `Auto` est le default, mais il n'existe pas dans la ComboBox de mode** ; l'utilisateur tombe sur un controle vide dans les options avancees et doit utiliser un lien separe pour y revenir.
4. **Les knobs de resilience RDP importants restent hard-codes** : 20 tentatives de reconnexion, keep-alive 60 s.
5. **Un changement DPI pendant la stabilisation post-connect est explicitement ignore**, ce qui peut laisser une session mal dimensionnee si l'utilisateur deplace la fenetre sur un autre ecran au mauvais moment.
6. **Le bouton "Reset RDP defaults" a un scope plus etroit que la page qui le porte** : il ne remet pas a zero certains reglages RDP visibles juste au-dessus.
7. **L'editeur de presets de resolution est un champ texte brut qui supprime silencieusement les lignes invalides**.

## Findings

| ID | Severite | Surface | Constat | Recommandation |
|---|---:|---|---|---|
| RDP-UX-01 | P1 | ServerDialog / Gateway | Le champ **RD Gateway** est absent de l'UI. `RdpHandler` transmet `server.RdpGateway` au `.rdp` externe (`src/Heimdall.App/Services/Handlers/RdpHandler.cs:141`) et le ViewModel le sauvegarde/charge (`src/Heimdall.App/ViewModels/Dialogs/ServerDialogViewModel.cs:1622`, `:1735`), mais `ServerDialog.xaml` ne contient aucun binding `RdpGateway`. La seule section gateway visible est une passerelle **SSH** (`ServerDialog.xaml:687-710`, texte `ServerDialogGatewayRoutingDesc` = "SSH gateway"). | Ajouter une section "RD Gateway" dans Options RDP ou Connection, avec hostname, aide distinguant clairement RD Gateway vs SSH gateway, validation hostname, et indication de compatibilite embedded/external. |
| RDP-UX-02 | P1 | Lancement externe mstsc | L'autofill des credentials en mode embedded a maintenant un statut, retry et dismiss (`EmbeddedRdpView.xaml:247-269`, `EmbeddedRdpView.xaml.cs:1785-2001`). En mode externe, le handler lance `CredentialAutofill.WaitAndFillAsync` en background et ne remonte que des logs timeout/failure (`RdpHandler.cs:270-299`). L'utilisateur voit seulement "External client launched", sans savoir si Heimdall attend le prompt Windows, a injecte le mot de passe ou a abandonne. | Donner au lancement externe une petite surface de suivi : "waiting for Windows credential prompt", "filled", "timed out", "retry autofill", "dismiss". Le statut peut vivre sur la ligne/session lancee, meme sans controle direct de mstsc. |
| RDP-UX-03 | P2 | ServerDialog / Resolution | `RdpResolutionMode.Auto` est la valeur par defaut (`ServerDialogViewModel.cs:786`) et dispose d'un callout (`ServerDialog.xaml:1691-1708`), mais la ComboBox avancee ne propose que FitWindow, Fixed, SmartSizing et Multimon (`ServerDialog.xaml:1720-1737`). Quand le profil est en Auto et que l'utilisateur ouvre l'expander, le controle de mode n'a pas d'item correspondant. Pour revenir a Auto apres un changement, il faut trouver un hyperlink separe (`ServerDialog.xaml:1880-1887`). | Ajouter `Auto` comme premier item de la ComboBox, marquer "Recommended", et conserver le lien "back to Auto" seulement comme raccourci optionnel. |
| RDP-UX-04 | P2 | Reconnect / resilience | Les deux parametres qui conditionnent la tolerance reseau sont fixes dans le code : `MaxAutoReconnectAttempts = 20` et `KeepAliveIntervalMs = 60_000` (`src/Heimdall.Rdp/ActiveX/RdpActiveXHost.cs:54-57`, appliques `:1335-1361`). L'UI ne donne qu'un bool `RdpAutoReconnect` (`ServerDialog.xaml:1998`, `MainWindow.xaml:2248`). | Ajouter en Settings RDP avance des defaults "Max reconnect attempts" et "Keep-alive interval", avec override profil si necessaire. Exposer les valeurs dans le libelle de l'overlay reconnect. |
| RDP-UX-05 | P2 | DPI / stabilisation post-connect | Pendant la fenetre de stabilisation, `OnWindowDpiChanged` met `_dpiChangeDroppedDuringLockout = true` puis retourne (`EmbeddedRdpView.xaml.cs:604-608`). A la fin de la stabilisation, ce flag provoque un retour sans recalcul (`EmbeddedRdpView.xaml.cs:1585-1589`), alors que les dimensions physiques et facteurs DPI ont change. Deplacer l'app entre deux ecrans 100 % / 150 % pendant les 10 s post-connect peut donc laisser une session au mauvais scale jusqu'a une autre action utilisateur. | Ne pas jeter l'evenement DPI : enregistrer un pending display refresh et appeler `ApplyCurrentResolutionAsync("post-stabilization-dpi", force: true)` apres le delai. |
| RDP-UX-06 | P3 | Settings RDP | Le bouton "Reset RDP defaults" est place au bas de toute la page RDP (`MainWindow.xaml:2352-2362`), mais `ApplyRdpDefaults` ne restaure que les defaults de profil et resolution globale (`SettingsViewModel.cs:830-851`). Les reglages RDP visibles sur la meme page mais exclus du reset incluent `RdpDialogAdvancedDefault`, `RdpResolutionPresets`, `RdpResizeEnableDelayMs`, `RdpArtifactCleanupDelayMs` et `RdpCredentialAutofillTimeoutMs` (`SettingsViewModel.cs:565-583`, `:708-726`). | Soit renommer le bouton "Reset new-profile RDP defaults", soit l'etendre a tous les reglages RDP affiches dans l'onglet, avec une confirmation qui liste le scope. |
| RDP-UX-07 | P3 | Settings RDP / presets | Les presets de resolution sont edites dans un `TextBox` multi-ligne brut (`MainWindow.xaml:2322-2339`). Le setter parse les lignes et supprime silencieusement celles qui ne matchent pas `WIDTHxHEIGHT` (`SettingsViewModel.cs:249-277`) ; l'aide dit seulement que les lignes invalides sont ignorees (`locales/en.json:2101`). | Remplacer par une liste editable avec colonnes Width/Height, add/remove/reset, validation inline par ligne, et preview du libelle qui apparaitra dans le menu de session. |

## Points deja solides a preserver

- Le reconnect overlay utilise maintenant le diagnostic detaille et le code RDP quand disponible (`EmbeddedRdpView.xaml.cs:2906-2988`).
- La toolbar RDP a des groupes visuels et des indicateurs plus lisibles (`EmbeddedRdpView.xaml:52-218`).
- Les redirections inactives peuvent etre collapsees derriere un badge `+N` (`EmbeddedRdpView.xaml:344-438`).
- Le multi-monitor dispose d'un picker de moniteurs (`ServerDialog.xaml:1830-1866`) et d'une validation cote ActiveX/generator.
- Le status autofill embedded a des etats visibles et un retry (`EmbeddedRdpView.xaml.cs:1872-2001`).

## Priorites recommandees

1. **RDP-UX-01 RD Gateway UI** : c'est le plus gros ecart fonctionnel pour des environnements Windows enterprise.
2. **RDP-UX-02 autofill externe visible** : reduit fortement la confusion du mode mstsc, qui reste un flux important.
3. **RDP-UX-03 Auto dans la ComboBox** : faible effort, corrige une incoherence tres visible dans le dialogue.
4. **RDP-UX-05 DPI pending refresh** : bug UX discret mais penible sur setups multi-DPI.
5. **RDP-UX-04 resilience knobs** : utile pour support/WAN, peut etre livre en Settings avance sans toucher le flux simple.

## Validation

Audit documentaire/code uniquement. Je n'ai pas lance de vraie connexion RDP, ni effectue de validation visuelle via capture active de l'application.
