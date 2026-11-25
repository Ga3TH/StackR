# StackR — Document de conception

Date : 2026-11-25 · Dépôt : https://github.com/Ga3TH/StackR

## 1. Intention

StackR est un environnement de développement web local pour Windows 10 et 11,
qui remplace Laragon (et XAMPP) pour un usage personnel au quotidien. Il doit
être **sécurisé par défaut**, **construit comme un produit pro** (code testé,
architecture claire, README avec tutoriel d'installation) et avoir une
**vraie interface soignée**.

Il ne propose que des **versions stables** des composants, téléchargées depuis
leurs sources officielles et vérifiées par empreinte SHA-256.

Usage actuel à remplacer (relevé dans `C:\laragon`) : Apache 2.4, PHP 8.3,
MySQL 8.4, Mailpit, Node 22, vhosts automatiques `<dossier>.test`, démarrage
avec Windows.

## 2. Objectifs, non-objectifs, anti-objectifs

**Objectifs (v1)**
- Installer / activer / désinstaller des versions stables d'Apache, PHP, MySQL,
  Mailpit, Node.js et Composer.
- Démarrer / arrêter tous les services en un clic, avec état en temps réel.
- Vhosts automatiques : chaque dossier de `www` devient `https://<nom>.test`.
- HTTPS local par autorité de certification propre à la machine.
- Changer de version PHP active en un clic.
- Commandes `php`, `mysql`, `composer`, `node`, `stackr` disponibles dans le
  terminal.
- Icône dans la zone de notification, démarrage avec Windows (optionnel).
- Interface Fluent (Windows 11), thème clair/sombre, aussi propre sous Windows 10.
- README avec tutoriel d'installation pas à pas.

**Non-objectifs (v1)** : Nginx, PostgreSQL, MongoDB, Redis, version PHP par
site, mode « services Windows », macOS/Linux, migration automatique des
bases Laragon (un guide d'export/import suffit).

**Anti-objectifs** (échec même si « ça marche »)
- Un service qui écoute sur autre chose que `127.0.0.1`.
- Un binaire exécuté sans vérification d'empreinte.
- Des droits administrateur exigés pour l'usage courant.
- Une version bêta / RC / dev proposée à l'utilisateur.
- Casser ou modifier l'installation Laragon existante.

## 3. Contraintes

- Windows 10 (22H2) et Windows 11, x64 uniquement.
- Aucun binaire tiers dans le dépôt Git (limite GitHub 100 Mo, fraîcheur des
  versions) : tout est téléchargé à l'installation.
- Apache Lounge et PHP Windows nécessitent le **Visual C++ Redistributable
  2015-2022 x64** : détecté, et installation proposée depuis le lien officiel
  Microsoft.
- Les commits sont faits en local ; l'utilisateur fait lui-même le `git push`.

## 4. Approches envisagées

| Approche | Pour | Contre |
|---|---|---|
| **A. C# / .NET 10 LTS + WPF** *(retenue)* | Natif Windows, ~20 Mo, accès direct process / registre / certificats / UAC, testable | Windows uniquement (c'est le but) |
| B. Electron | UI web facile | 150 Mo+, gourmand, surface d'attaque d'un navigateur embarqué |
| C. Tauri (Rust) | Léger, moderne | Toolchain Rust à ajouter, API Windows plus laborieuses |

A retenue par décision de l'assistant (l'utilisateur a délégué le « comment »),
car elle sert le mieux « sécurisé + pro + belle interface » sur Windows.

## 5. Architecture

```
StackR.sln
├─ src/StackR.Core     bibliothèque : toute la logique, sans UI
├─ src/StackR.App      WPF (WPF-UI Fluent + CommunityToolkit.Mvvm), zone de notif.
├─ src/StackR.Cli      outil `stackr` (System.CommandLine) — aussi l'« élévateur »
├─ tests/StackR.Core.Tests   xUnit
├─ manifest/components.json  catalogue des versions stables + SHA-256
├─ tools/update-manifest.ps1 script mainteneur : récupère les dernières stables + hash
└─ installer/StackR.iss      script Inno Setup (optionnel)
```

### 5.1 Arborescence d'installation (par défaut `C:\StackR`, modifiable)

```
C:\StackR\
  bin\apache\<version>\  bin\php\<version>\  bin\mysql\<version>\
  bin\mailpit\<version>\ bin\nodejs\<version>\ bin\composer\
  bin\shims\            php.cmd, mysql.cmd, composer.cmd, node.cmd…
  etc\                  configs générées (httpd.conf, vhosts, php.ini, my.ini)
  data\mysql\           données MySQL
  data\ssl\             CA + certificats des sites
  data\secrets\         secrets chiffrés DPAPI
  www\                  projets (dossier configurable, ex. C:\laragon\www)
  logs\  tmp\
  stackr.json           configuration utilisateur
```

Hors de `Program Files` volontairement : pas besoin d'admin pour écrire.

### 5.2 Modules de StackR.Core

- **Manifest** : lit `components.json` (embarqué dans l'exe ; mise à jour
  facultative depuis `raw.githubusercontent.com/Ga3TH/StackR/main/manifest/…`
  en HTTPS). Chaque entrée : composant, version, canal `stable`, URL officielle,
  SHA-256, taille, prérequis (ex. VC++ x64, PHP TS pour mod_php).
- **Downloader** : `HttpClient`, reprise impossible = re-téléchargement ;
  progression ; écrit dans `tmp\`, **vérifie le SHA-256 avant extraction**,
  supprime le fichier si mismatch. Protection « zip slip » à l'extraction.
- **ComponentStore** : installé / actif / désinstaller ; une seule version
  active par composant.
- **ConfigRenderer** : génère les fichiers de config depuis des gabarits
  embarqués (aucune édition des fichiers d'origine des archives).
- **ServiceManager** : lance Apache, MySQL, Mailpit comme processus enfants
  cachés, suit leurs PID dans `tmp\state.json`, détecte les ports occupés
  (80, 443, 3306, 8025, 1025) et le processus qui les occupe, arrêt propre
  (MySQL via `mysqladmin shutdown`, Apache/Mailpit par arbre de processus),
  journaux dans `logs\`.
- **SiteScanner + VhostGenerator** : surveille `www\` (FileSystemWatcher),
  détecte la racine web (`public\` si présent, sinon le dossier), génère un
  vhost HTTP + HTTPS par site.
- **HostsFile** : bloc délimité `# >>> StackR` / `# <<< StackR` dans
  `C:\Windows\System32\drivers\etc\hosts`. Écriture uniquement via
  `stackr.exe hosts sync` relancé **élevé (UAC)**, avec noms validés
  (`^[a-z0-9-]+\.test$`). Une seule demande UAC par lot de changements.
- **CertificateAuthority** : CA racine générée avec
  `System.Security.Cryptography` (ECDSA P-256, 10 ans), **contrainte de nom**
  (`NameConstraints` : `.test`, `localhost`) pour qu'elle ne puisse rien
  signer d'autre ; installée dans le magasin *CurrentUser\Root* (confirmation
  Windows, pas d'admin). Certificats de site (825 jours max, SAN). Clé privée
  de la CA chiffrée DPAPI.
- **Secrets** : `ProtectedData` (DPAPI, portée CurrentUser) dans
  `data\secrets\`.
- **MySqlBootstrap** : `mysqld --initialize-insecure` puis immédiatement
  `ALTER USER 'root'@'localhost'` avec mot de passe aléatoire (24 car.) stocké
  dans Secrets ; `bind-address=127.0.0.1`, `mysqlx=OFF`.
- **ShellIntegration** : ajoute `bin\shims` au PATH **utilisateur** (HKCU),
  clé `HKCU\…\Run` pour le démarrage, `WM_SETTINGCHANGE` pour notifier.
- **Prerequisites** : détection VC++ Redist (registre), conflits de ports,
  Laragon en cours d'exécution.

### 5.3 Valeurs de sécurité par défaut

- Apache : `Listen 127.0.0.1:80` / `443`, `ServerTokens Prod`,
  `ServerSignature Off`, `TraceEnable Off`, pas d'index de répertoire hors
  racine `www`, modules minimaux.
- PHP (`php.ini` dev) : `expose_php=Off`, `display_errors=On` (local),
  `allow_url_include=Off`, `session.cookie_httponly=1`, SMTP → Mailpit,
  extensions courantes (pdo_mysql, mbstring, openssl, intl, curl, zip, gd,
  fileinfo, sodium).
- MySQL : localhost seulement, root protégé par mot de passe.
- Mailpit : `--listen 127.0.0.1:8025 --smtp 127.0.0.1:1025`.
- Composer : installé depuis getcomposer.org avec vérification de la
  signature SHA-384 officielle.
- Mises à jour du manifeste : HTTPS uniquement, et les binaires restent
  vérifiés par empreinte quoi qu'il arrive.

### 5.4 Interface (StackR.App)

Fenêtre Fluent (Mica sous Windows 11, fond uni sous Windows 10), navigation à
gauche, thème clair/sombre suivant Windows.

```
┌──────────────┬──────────────────────────────────────────────────┐
│  ◆ StackR    │  Tableau de bord                [▶ Tout démarrer]│
│              │  ┌──────────┐ ┌──────────┐ ┌──────────┐          │
│ ▣ Tableau    │  │ Apache   │ │ MySQL    │ │ Mailpit  │          │
│ ◎ Sites      │  │ ● Actif  │ │ ● Actif  │ │ ○ Arrêté │          │
│ ⬇ Versions   │  │ 2.4.x :80│ │ 8.4 :3306│ │ :8025    │          │
│ ⛁ Base données│  └──────────┘ └──────────┘ └──────────┘          │
│ ≡ Journaux   │  PHP actif : 8.3.x ▾      www : C:\StackR\www    │
│              │  Sites récents : otelo.test ↗  blog.test ↗       │
│ ⚙ Paramètres │                                                  │
└──────────────┴──────────────────────────────────────────────────┘
```

Pages :
1. **Tableau de bord** — cartes de services (état, version, port, bouton),
   tout démarrer/arrêter, PHP actif, raccourcis (terminal, dossier www).
2. **Sites** — liste des `.test`, ouvrir, HTTPS, ouvrir dossier/VS Code,
   « Nouveau site » (vide, ou Laravel via Composer).
3. **Versions** — catalogue stable par composant : installer (barre de
   progression), activer, désinstaller.
4. **Base de données** — hôte/port/utilisateur, mot de passe root masqué
   (afficher/copier), changer le mot de passe, ouvrir le client MySQL.
5. **Journaux** — Apache, PHP, MySQL, Mailpit, suivi en direct.
6. **Paramètres** — dossiers, démarrage avec Windows, démarrage auto des
   services, thème, langue (FR par défaut, EN).

Premier lancement : **assistant** (dossier d'installation → choix des
composants avec versions stables recommandées → téléchargement vérifié →
CA HTTPS → PATH → fin).

Zone de notification : démarrer/arrêter tout, sites, ouvrir, quitter.
Fermer la fenêtre = réduire dans la zone de notification.

### 5.5 CLI

`stackr status | start [svc] | stop [svc] | sites | versions | install <c> <v>
| use php <v> | hosts sync (élevé) | db password`.

## 6. Qualité « pro »

- Tests xUnit sur Core (manifest, vérif SHA-256, zip-slip, rendu de configs,
  validation des noms, bloc hosts, génération de certificats).
- `Directory.Build.props` : `Nullable` activé, warnings = erreurs, analyseurs.
- `dotnet publish` self-contained, single-file, win-x64 : l'utilisateur
  final n'a pas besoin du runtime .NET.
- CI GitHub Actions (`windows-latest`) : build + tests (fichier fourni ; ne
  tourne qu'une fois poussé par l'utilisateur).
- `README.md` (tutoriel d'installation), `SECURITY.md`, `LICENSE` (MIT),
  `CHANGELOG.md`, `.editorconfig`, `.gitignore`.

## 7. Mode de construction

Travail découpé en tâches confiées à des agents, avec un niveau adapté à la
difficulté :
- **Léger (Haiku)** : fichiers de projet, gabarits, docs, README.
- **Standard (Sonnet)** : UI, pages, CLI, services, tests courants.
- **Fort (Opus)** : sécurité (CA, DPAPI, élévation UAC, hosts, téléchargement
  vérifié), gestion des processus, revue finale.

## 8. Laissé à l'appréciation du constructeur

Choix exact des bibliothèques secondaires, détails visuels, icône, messages,
organisation interne des classes — dans le respect des sections ci-dessus.
