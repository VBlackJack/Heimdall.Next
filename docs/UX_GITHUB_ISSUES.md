# GitHub Issue Drafts - UX Backlog

Ce document contient des tickets GitHub prets a copier-coller.
Un ticket GitHub = un travail a faire, avec un probleme clair et des criteres de validation.

Si tu debutes, n'ouvre pas les 10 tickets d'un coup.
Commence par les 4 premiers: ce sont les plus utiles et les moins risqués.

Labels conseilles:
- `ux`
- `accessibility`
- `enhancement`
- `priority:p0` ou `priority:p1`

## Ordre recommande

1. `UX-001` Shell de chargement commun
2. `UX-002` Verrouillage + annulation des actions async
3. `UX-003` Support clavier principal
4. `UX-004` Accessibilite du tab Outils
5. `UX-005` Palette plus explicite
6. `UX-006` Contexte cible + ajout d'outil
7. `UX-007` Nettoyage hardcoding/i18n visible
8. `UX-008` Standardisation visuelle des outils
9. `UX-009` Notes power user
10. `UX-010` Checklist UX + cleanup Notes config

---

## UX-001 - Ajouter un shell de chargement/erreur/etat vide commun pour les outils async

**Labels**
`ux`, `enhancement`, `priority:p0`

**Probleme**
Les outils async n'ont pas un comportement visuel coherent pendant le chargement.
Selon l'outil, l'utilisateur voit soit un curseur occupé, soit rien, soit un message d'erreur brut.

**Objectif**
Creer un pattern unique pour les outils async:
- chargement visible
- etat vide explicite
- erreur inline localisee

**Portee**
- Base commune dans `src/Heimdall.App/Themes/CommonControls.xaml`
- Premiere vague sur les outils async les plus visibles dans `src/Heimdall.App/Views/Tools/`

**Criteres d'acceptation**
- Chaque outil async affiche un etat de chargement visible.
- Chaque outil async affiche un etat vide quand rien n'a encore ete lance.
- Chaque outil async affiche une erreur inline comprehensible.
- Aucun ecran ne reste vide sans explication pendant une operation.

**Fichiers utiles**
- `src/Heimdall.App/Themes/CommonControls.xaml`
- `src/Heimdall.App/Views/Tools/`

---

## UX-002 - Verrouiller les inputs pendant les actions async et exposer un vrai bouton Stop/Cancel

**Labels**
`ux`, `enhancement`, `priority:p0`

**Probleme**
Beaucoup d'outils laissent les champs modifiables pendant l'execution.
Cela peut provoquer des doubles lancements, des etats incoherents ou des courses entre actions.

**Objectif**
Pendant une operation async:
- desactiver les champs critiques
- empecher le double clic
- afficher un bouton `Stop` ou `Cancel` si l'outil sait deja s'annuler

**Portee**
- Outils qui utilisent deja `_setBusy` ou `CancellationToken`
- Mise a jour progressive dans `src/Heimdall.App/Views/Tools/`

**Criteres d'acceptation**
- Impossible de lancer deux fois la meme action en parallele depuis l'UI.
- Les champs critiques sont verrouilles pendant l'execution.
- Quand l'annulation existe deja en interne, elle est visible dans l'UI.
- L'outil revient proprement a l'etat initial apres annulation.

**Fichiers utiles**
- `src/Heimdall.App/Views/Tools/`

---

## UX-003 - Rendre les outils "keyboard-first" sur l'action principale

**Labels**
`ux`, `accessibility`, `priority:p0`

**Probleme**
Le support clavier est trop variable d'un outil a l'autre.
Dans beaucoup d'ecrans, `Enter` ne lance pas l'action principale et le focus initial n'est pas pose au bon endroit.

**Objectif**
Mettre un comportement clavier uniforme:
- focus automatique sur l'input principal a l'ouverture
- `Enter` declenche l'action principale

**Portee**
- Outils "form-first" dans `src/Heimdall.App/Views/Tools/`
- Utiliser `PingToolView` comme reference de comportement

**Criteres d'acceptation**
- A l'ouverture, le curseur est sur l'input principal.
- Appuyer sur `Enter` lance l'action principale.
- L'ordre de tabulation reste logique.

**Fichiers utiles**
- `src/Heimdall.App/Views/Tools/PingToolView.xaml.cs`
- `src/Heimdall.App/Views/Tools/`

---

## UX-004 - Rendre le tab Outils accessible au clavier et ajouter un etat "aucun resultat"

**Labels**
`ux`, `accessibility`, `priority:p1`

**Probleme**
Le tab Outils fonctionne surtout a la souris.
Quand une recherche ne retourne rien, la surface peut paraitre vide sans message explicite.

**Objectif**
- remplacer les cartes souris-only par de vrais controles focusables
- permettre ouverture et pinning au clavier
- afficher un message `aucun resultat`

**Portee**
- `src/Heimdall.App/MainWindow.xaml`
- `src/Heimdall.App/MainWindow.xaml.cs`

**Criteres d'acceptation**
- Un utilisateur peut parcourir les outils sans souris.
- `Enter` ouvre un outil depuis le tab Outils.
- L'etat `aucun resultat` est visible quand la recherche est vide.
- Le comportement est coherent avec le panneau rapide.

**Fichiers utiles**
- `src/Heimdall.App/MainWindow.xaml`
- `src/Heimdall.App/MainWindow.xaml.cs`

---

## UX-005 - Rendre la palette Ctrl+K explicite pour les outils

**Labels**
`ux`, `enhancement`, `priority:p1`

**Probleme**
La palette sait deja ouvrir les outils, mais ce comportement n'est pas assez visible.
Le placeholder et les aides n'expliquent pas clairement qu'on peut taper `tools`, `ping`, `json`, etc.

**Objectif**
- afficher un vrai placeholder oriente outils
- enrichir les hints de la palette
- rendre les libelles parametres plus explicites

**Portee**
- `src/Heimdall.App/ViewModels/MainViewModel.cs`
- `src/Heimdall.App/MainWindow.xaml`
- `src/Heimdall.App/Services/ToolRegistry.cs`
- `locales/fr.json`
- `locales/en.json`

**Criteres d'acceptation**
- La palette explique clairement comment ouvrir un outil.
- La commande `tools` est decouvrable sans documentation externe.
- Les outils ouverts avec un argument affichent un libelle contextualise quand c'est pertinent.

**Fichiers utiles**
- `src/Heimdall.App/ViewModels/MainViewModel.cs`
- `src/Heimdall.App/MainWindow.xaml`
- `src/Heimdall.App/Services/ToolRegistry.cs`

---

## UX-006 - Rendre explicite le contexte cible des outils reseau et remplacer "Ajouter un outil" par un picker recherchable

**Labels**
`ux`, `enhancement`, `priority:p1`

**Probleme**
Un outil reseau peut heriter silencieusement du serveur selectionne.
En plus, le flux `Ajouter un outil` repose sur un long menu puis une ou deux modales successives.

**Objectif**
- montrer clairement la cible utilisee avant ouverture
- remplacer le menu long par un picker recherchable avec categorie et description

**Portee**
- `src/Heimdall.App/MainWindow.xaml.cs`

**Criteres d'acceptation**
- L'utilisateur voit `Cible: <host>` ou `Aucune cible`.
- L'ajout d'un outil se fait dans une seule surface de selection recherchable.
- Le nombre d'etapes pour ajouter un outil est reduit.

**Fichiers utiles**
- `src/Heimdall.App/MainWindow.xaml.cs`

---

## UX-007 - Supprimer les hardcodings visibles et les valeurs de demonstration

**Labels**
`ux`, `i18n`, `enhancement`, `priority:p1`

**Probleme**
Certains outils affichent encore des valeurs ou textes hardcodes visibles par l'utilisateur.
Cela donne une impression "demo" et fragilise la coherence i18n.

**Objectif**
- retirer les valeurs par defaut visibles non necessaires
- supprimer les restes de texte XAML non localises
- utiliser les ressources i18n ou des valeurs neutres

**Portee**
- `src/Heimdall.App/Views/Tools/Base64ToolView.xaml.cs`
- `src/Heimdall.App/Views/Tools/PingToolView.xaml.cs`
- `src/Heimdall.App/Views/Tools/PingToolView.xaml`

**Criteres d'acceptation**
- Plus de `Hello, World!` visible par defaut.
- Plus de `8.8.8.8` force par defaut.
- Plus de texte XAML residuel non localise sur Ping.

**Fichiers utiles**
- `src/Heimdall.App/Views/Tools/Base64ToolView.xaml.cs`
- `src/Heimdall.App/Views/Tools/PingToolView.xaml.cs`
- `src/Heimdall.App/Views/Tools/PingToolView.xaml`

---

## UX-008 - Standardiser le shell visuel des outils et mieux supporter le split

**Labels**
`ux`, `enhancement`, `priority:p2`

**Probleme**
Les outils n'ont pas tous le meme rythme visuel.
Certains utilisent bien les tokens communs, d'autres non.
Le mode split n'est pas homogene sur les outils denses.

**Objectif**
- harmoniser footer, placeholders, marges et etats
- definir un shell commun pour les outils
- corriger en priorite les outils les plus denses en split

**Portee**
- `src/Heimdall.App/Themes/CommonControls.xaml`
- `src/Heimdall.App/Views/Tools/PortScannerView.xaml`
- `src/Heimdall.App/Views/Tools/PingToolView.xaml`
- `src/Heimdall.App/Views/Tools/NotesToolView.xaml`
- `src/Heimdall.App/Views/Tools/NetworkCartographyView.xaml.cs`

**Criteres d'acceptation**
- Les outils utilisent les tokens communs prevus.
- Les outils prioritaires restent lisibles en largeur reduite.
- Les actions principales ne sortent plus de l'ecran en split.

**Fichiers utiles**
- `src/Heimdall.App/Themes/CommonControls.xaml`
- `src/Heimdall.App/Views/Tools/`

---

## UX-009 - Renforcer Notes pour les power users

**Labels**
`ux`, `accessibility`, `priority:p2`

**Probleme**
Le TreeView Notes propose des actions au clic droit, mais peu de raccourcis clavier evidents.
Les snippets Markdown inseres depuis le menu contextuel sont hardcodes dans le code-behind.

**Objectif**
- ajouter des raccourcis clavier utiles
- centraliser les snippets Markdown

**Portee**
- `src/Heimdall.App/Views/Tools/NotesToolView.xaml.cs`

**Criteres d'acceptation**
- `F2` renomme une note.
- `Delete` supprime une note avec confirmation appropriee.
- Les snippets Markdown ne sont plus ecrits en dur directement dans les handlers UI.

**Fichiers utiles**
- `src/Heimdall.App/Views/Tools/NotesToolView.xaml.cs`

---

## UX-010 - Ajouter une checklist UX/accessibilite et nettoyer la config locale de Notes

**Labels**
`ux`, `accessibility`, `priority:p3`

**Probleme**
Les regressions UX sont faciles a reintroduire.
En plus, `Notes` lit encore une configuration locale de maniere ad hoc.

**Objectif**
- ajouter une checklist simple pour chaque nouvel outil
- faire passer Notes sur une config commune au lieu d'un chemin local special

**Portee**
- `src/Heimdall.App/Views/Tools/NotesToolView.xaml.cs`
- `docs/`

**Criteres d'acceptation**
- Une checklist courte existe pour la revue PR.
- Chaque nouvel outil valide `focus`, `Enter`, `loading`, `error`, `empty`, `tooltip`, `i18n`, `split`.
- Notes n'utilise plus un acces direct ad hoc a `config/settings.json`.

**Fichiers utiles**
- `src/Heimdall.App/Views/Tools/NotesToolView.xaml.cs`
- `docs/`

---

## Comment utiliser ce fichier

1. Cree un ticket GitHub par bloc `UX-00X`.
2. Copie le titre comme titre du ticket.
3. Copie le reste dans la description.
4. Ajoute les labels si tu veux mieux t'organiser.
5. Commence par `UX-001` a `UX-004`.
