# 🟦 Valider en runtime la migration FTP / FTPS vers FluentFTP

> Cette procédure valide, sur de vrais serveurs, la migration de `FtpBrowser` vers
> FluentFTP (commit `236c5a7`). Le code est sain et la CI est verte ; ça couvre le
> **seul risque restant** : l'intégration réseau réelle FTP/FTPS. Version anglaise
> (officielle) : `ftp-fluentftp-runtime-test.md`.

> ⚠️ Fais une cible à la fois : d'abord **A — FTP clair**, puis **B — FTPS explicite**.
> Si une ligne échoue, arrête-toi, note ce que tu vois et l'extrait de log, puis continue.

## 📑 Sommaire

| Étape | Quoi | Durée estimée |
|---|---|---|
| Prép | Deux serveurs + fichiers de test + app lancée | ~15 min |
| 1 | FTP clair : connexion + avertissement cleartext | ~5 min |
| 2 | FTP clair : list, transfert, noms spéciaux, rename/delete, close | ~15 min |
| 3 | FTPS : connexion TLS + canal de données protégé | ~5 min |
| 4 | FTPS : rejeu du contrat + close | ~10 min |

## 📋 Ce dont tu as besoin avant de commencer

| Élément | Détail |
|---|---|
| 🖥️ App lancée | Build Debug démarrée (`Run.bat` ou `dotnet run --project src/Heimdall.App`) |
| 🌐 Cible A — FTP clair | Sans TLS, avec un vrai compte (`<HOST_A>`, port `21`, `<USER>`, `<PASSWORD>`) |
| 🔒 Cible B — FTPS explicite | AUTH TLS sur le port 21, **certificat reconnu par Windows** (`<HOST_B>`) |
| 📛 Cert de confiance sur B | Un cert auto-signé sera **rejeté par design** (cf. Étape 3) — utilise un cert de confiance ou ajoute-le au magasin Windows |
| 📄 Gros fichier de test | `test-upload.bin`, ~5–10 Mo (assez gros pour voir la barre de progression) |
| 🔑 Son SHA-256 | `Get-FileHash test-upload.bin` — note la valeur |
| 📄 Fichier avec espace | `mon fichier.txt` |
| 📄 Fichier avec dièse | `note#1.txt` |
| 📝 FileLogger du jour | Ouvre-le à côté de l'app pour suivre en direct |

> 💡 Pas de serveur sous la main ? Le plus rapide : Docker `delfer/alpine-ftp-server`
> pour le FTP clair, et une instance FileZilla Server (Windows) ou `vsftpd` configurée
> en FTPS explicite.

## ✅ ÉTAPE 1 — FTP clair : connexion et avertissement cleartext

> Le FTP clair transmet les identifiants sans chiffrement. L'app doit prévenir à la fois
> dans l'UI et dans le log. On vérifie aussi que le log reste credential-clean — host et
> port uniquement, jamais le login ni le mot de passe.

| # | Action |
|---|---|
| ☐ 1 | Crée un profil FTP : `<HOST_A>`, port `21`, `<USER>`, `<PASSWORD>`, **TLS désactivé** |
| ☐ 2 | Connecte-toi → état **connecté**, racine `/` listée |
| ☐ 3 | Regarde le bandeau de session / la barre de statut → l'**avertissement cleartext** est visible (`WarnFtpCleartext`) |
| ☐ 4 | Regarde le FileLogger → une ligne `Warn` affiche `connecting to ftp://host:port without TLS` |
| ☐ 5 | ⚠️ Inspecte cette ligne → elle contient **host + port uniquement**, jamais le login ni le mot de passe |

> 🔴 Arrête-toi si le login ou le mot de passe apparaît dans le log → bug credential-clean, à corriger en priorité.

## ✅ ÉTAPE 2 — FTP clair : contrat complet

> On exerce maintenant chaque opération `IRemoteBrowser` : listing, transferts avec
> contrôle d'intégrité, le correctif des noms spéciaux, rename/delete, et une fermeture
> propre. C'est le gros de la validation.

### 2a — Lister et naviguer

| # | Action |
|---|---|
| ☐ 1 | Liste la racine → fichiers/dossiers affichés, taille et date cohérentes |
| ☐ 2 | Vérifie un **dossier** → taille 0, type dossier correct |
| ☐ 3 | Vérifie un **fichier** → taille réelle, date cohérente |
| ☐ 4 | Entre dans un sous-dossier → navigation OK, chemin courant mis à jour |
| ☐ 5 | Tente d'entrer dans un dossier **inexistant** → erreur propre, pas de crash |

### 2b — Upload / download avec intégrité

| # | Action |
|---|---|
| ☐ 1 | Uploade `test-upload.bin` → la barre de progression bouge, transfert complet |
| ☐ 2 | Vérifie la taille distante après upload → égale à la taille locale |
| ☐ 3 | Re-télécharge-le sous un autre nom local → transfert complet |
| ☐ 4 | Compare le **SHA-256** du fichier re-téléchargé → identique à la valeur notée |

> 🔴 Arrête-toi si les checksums diffèrent → corruption de transfert (mode binaire / canal de données), à investiguer.

### 2c — Noms spéciaux (le bug URI d'origine)

> Avec l'ancien `FtpWebRequest`, ces noms échouaient ou étaient mal encodés. FluentFTP
> prend les chemins bruts, donc c'est la preuve clé que la migration a corrigé ça.

| # | Action |
|---|---|
| ☐ 1 | Uploade `mon fichier.txt` (espace) → réussi, bon nom dans le listing |
| ☐ 2 | Uploade `note#1.txt` (dièse) → réussi, bon nom |
| ☐ 3 | Re-télécharge chacun → contenu intact, nom correct |

### 2d — Rename / delete

| # | Action |
|---|---|
| ☐ 1 | Renomme `test-upload.bin` en `renamed.bin` → nouveau nom affiché, ancien disparu |
| ☐ 2 | Crée un dossier `tmpdir` → créé |
| ☐ 3 | Uploade un fichier dedans → présent dans `tmpdir` |
| ☐ 4 | ⚠️ Supprime le dossier **non vide** `tmpdir` → suppression récursive (dossier + contenu) |
| ☐ 5 | Supprime un **fichier seul** (`renamed.bin`) → parti |

### 2e — Mode passif / actif et fermeture

| # | Action |
|---|---|
| ☐ 1 | Profil en **passif** (défaut) : refais un list + un download → OK |
| ☐ 2 | *(Si le serveur le permet)* profil en **actif** : list + download → OK (ou note l'échec NAT/pare-feu) |
| ☐ 3 | Ferme l'onglet de session FTP → onglet fermé, pas de blocage UI |
| ☐ 4 | Regarde le FileLogger → pas d'`ObjectDisposedException`, pas de NRE, rien de non géré |
| ☐ 5 | *(Optionnel)* Gestionnaire des tâches → plus de socket FTP-A après fermeture (`AsyncFtpClient` disposé) |

## ✅ ÉTAPE 3 — FTPS : connexion TLS et canal de données protégé

> Le FTPS explicite passe en TLS via AUTH TLS, et le canal de données est protégé par
> PROT P (`DataConnectionEncryption = true` dans la migration). La validation du
> certificat utilise la chaîne par défaut (`PolicyErrors == None`) — **pas d'accept-all**.

| # | Action |
|---|---|
| ☐ 1 | Crée un profil FTP : `<HOST_B>`, port `21`, `<USER>`, `<PASSWORD>`, **TLS activé** |
| ☐ 2 | Connecte-toi → réussite après la négociation TLS |
| ☐ 3 | Regarde le bandeau / statut → **pas** d'avertissement cleartext (TLS actif) |
| ☐ 4 | Regarde le FileLogger → **aucune** ligne `without TLS` |
| ☐ 5 | Liste la racine → listing OK (le list passe par le canal de données → prouve PROT P) |
| ☐ 6 | Uploade `test-upload.bin`, re-télécharge, compare le **SHA-256** → identique |
| ☐ 7 | *(Optionnel)* Wireshark sur le port data → données **chiffrées**, rien en clair |

> ⚠️ Un cert auto-signé sera **rejeté** — c'est le comportement correct, pas un bug.
> Utilise un cert de confiance (ou ajoute-le au magasin Windows) pour tester le cas nominal.

## ✅ ÉTAPE 4 — FTPS : rejeu du contrat et fermeture

> On rejoue les opérations qui comptent en TLS pour confirmer qu'aucune régression
> n'apparaît sur le chemin chiffré, et que le client se ferme proprement.

| # | Action |
|---|---|
| ☐ 1 | Uploade `mon fichier.txt` + `note#1.txt` → noms spéciaux OK en TLS aussi |
| ☐ 2 | Renomme un fichier → OK |
| ☐ 3 | Supprime un dossier non vide + un fichier seul → OK |
| ☐ 4 | Ferme l'onglet → pas d'exception dans le FileLogger, pas de fuite socket |

## 🧾 Bilan final

| Cible | Connexion | List | Transfert + checksum | Noms spéciaux | Rename/Delete | Close propre | Logs clean |
|-------|-----------|------|----------------------|---------------|---------------|--------------|------------|
| **A — FTP clair** | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ |
| **B — FTPS** | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ | ☐ |

- [ ] Avertissement cleartext **uniquement** sur la cible A
- [ ] Aucun identifiant nulle part dans les logs (A et B)
- [ ] Aucune exception non gérée dans le FileLogger
- [ ] Checksums identiques sur tous les allers-retours

## 🆘 Problèmes fréquents

| Symptôme | Solution rapide |
|---|---|
| La connexion FTPS échoue sur un cert auto-signé | Normal — la validation par chaîne par défaut le rejette. Utilise un cert de confiance ou ajoute-le au magasin Windows |
| Le mode actif tombe en timeout | Souvent un NAT/pare-feu qui bloque le port data, pas un bug — refais en passif |
| Le warning `without TLS` apparaît sur la cible FTPS | TLS n'a pas vraiment négocié — vérifie le flag TLS du profil et le support AUTH TLS du serveur |
| Listing vide sans erreur | Mauvais chemin ou droits — revérifie le dossier courant et les droits du compte |
| Checksums différents après transfert | Problème d'intégrité — relève les tailles de fichier + un extrait de log, ouvre un finding |
