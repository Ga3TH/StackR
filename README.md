# StackR

Environnement de développement web local pour Windows, **sécurisé par défaut** :
Apache, PHP, MySQL, Mailpit, Node.js et Composer, avec sites `.test` en HTTPS
automatiques. Une alternative moderne à Laragon et XAMPP.

[![CI](https://github.com/Ga3TH/StackR/actions/workflows/ci.yml/badge.svg)](https://github.com/Ga3TH/StackR/actions/workflows/ci.yml)
[![Licence MIT](https://img.shields.io/badge/licence-MIT-green.svg)](LICENSE)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![Windows 10/11](https://img.shields.io/badge/Windows-10%20%2F%2011-0078D4)

![Tableau de bord](docs/images/dashboard.png)

| Sites | Versions | Base de données |
|---|---|---|
| ![Sites](docs/images/sites.png) | ![Versions](docs/images/versions.png) | ![Base de données](docs/images/database.png) |

## Fonctionnalités

- **Versions stables uniquement** : Apache 2.4.71, PHP 8.4.28 et 8.3.37, MySQL 8.4.13, Mailpit 1.34.0, Node.js 22 / 24, Composer 2.11.3 (le catalogue évolue via `manifest/components.json`). Téléchargement depuis les sources officielles, **empreinte SHA-256 vérifiée** avant extraction.
- **Démarrage en un clic** de tous les services, avec état en temps réel et détection des ports occupés.
- **Sites automatiques** : chaque dossier de `www` devient `https://<nom>.test` (racine `public\` détectée).
- **HTTPS local** grâce à une autorité de certification propre à la machine, limitée à `.test` et `localhost`.
- **Changement de version PHP** en un clic.
- **Commandes dans le terminal** : `php`, `mysql`, `composer`, `node` et `stackr`.
- **Zone de notification**, démarrage avec Windows (optionnel), thème clair/sombre, interface en français (anglais disponible).
- **Aucun droit administrateur** pour l'usage courant (une seule demande UAC pour le fichier hosts).

## Prérequis

- Windows 10 (22H2) ou Windows 11, 64 bits.
- Visual C++ Redistributable 2015-2022 x64 (requis par Apache et PHP) : StackR le détecte et propose l'installation depuis le lien officiel Microsoft.
- Environ 1 Go d'espace disque et une connexion Internet pour le téléchargement des composants.

## Installation pas à pas

### A. Depuis une Release (recommandé)

1. Ouvrez la page [Releases](https://github.com/Ga3TH/StackR/releases) et téléchargez **`StackR-Setup.exe`**.
2. Double-cliquez sur le fichier. Si Windows affiche **« Windows a protégé votre ordinateur »** (SmartScreen), c'est normal : l'installeur n'est pas signé numériquement. Cliquez sur **Informations complémentaires**, puis **Exécuter quand même**.
3. L'assistant installe StackR pour votre utilisateur dans `%LOCALAPPDATA%\Programs\StackR`, sans droits administrateur. Cochez « Créer un raccourci sur le Bureau » si vous le souhaitez.
4. Terminez l'assistant et lancez StackR.

### B. Depuis les sources

1. Installez le SDK .NET 10 :
   ```powershell
   winget install Microsoft.DotNet.SDK.10
   ```
2. Clonez le dépôt et construisez :
   ```powershell
   git clone https://github.com/Ga3TH/StackR.git
   cd StackR
   .\build.ps1
   ```
   `build.ps1` exécute les tests puis publie `StackR.App.exe` et `stackr.exe` côte à côte dans `publish\StackR\`. Si [Inno Setup 6](https://jrsoftware.org/isinfo.php) est installé, l'installeur `publish\StackR-Setup.exe` est aussi généré.
3. Lancez `publish\StackR\StackR.App.exe`.

> Gardez `StackR.App.exe` et `stackr.exe` dans le même dossier : l'application relance `stackr.exe` en mode élevé pour synchroniser le fichier hosts.

## Premier lancement

L'assistant vous guide :

1. **Dossier d'installation** : `C:\StackR` par défaut (modifiable ; hors de `Program Files`, donc sans admin).
2. **Choix des composants** : versions stables recommandées préselectionnées (Apache, PHP, MySQL, Mailpit, Node.js, Composer).
3. **Téléchargement vérifié** : chaque archive est contrôlée par SHA-256 avant extraction. Le mot de passe root de MySQL est généré aléatoirement.
4. **Autorité de certification HTTPS** : Windows demande de confirmer l'ajout de la CA au magasin de l'utilisateur. Acceptez.
5. **PATH** : `php`, `mysql`, `composer`, `node` et `stackr` sont ajoutés à votre PATH utilisateur (ouvrez un nouveau terminal ensuite).
6. **Fin** : cliquez sur **Tout démarrer** dans le Tableau de bord. Créez un dossier dans `www`, il devient `https://<dossier>.test`.

## Interface

- **Tableau de bord** : cartes des services, tout démarrer/arrêter, PHP actif, raccourcis.
- **Sites** : liste des `.test`, ouverture, dossier / VS Code, « Nouveau site » (vide ou Laravel via Composer).
- **Versions** : catalogue stable, installation avec progression, activation, désinstallation.
- **Base de données** : hôte, port, utilisateur, mot de passe root masqué (afficher/copier/changer), client MySQL.
- **Journaux** : Apache, PHP, MySQL, Mailpit en direct.
- **Paramètres** : dossiers, démarrage avec Windows, démarrage auto des services, thème, langue, confiance envers la CA.

Fermer la fenêtre la réduit dans la zone de notification.

## Migration depuis Laragon

StackR ne touche pas à votre installation Laragon.

1. **Arrêtez Laragon** (quittez-le complètement) pour libérer les ports 80, 443 et 3306.
2. **Exportez vos bases** depuis Laragon, avant de l'arrêter si possible :
   ```powershell
   C:\laragon\bin\mysql\<version>\bin\mysqldump.exe -u root --all-databases --routines --events > sauvegarde.sql
   ```
   (ou une base précise : `mysqldump -u root ma_base > ma_base.sql`).
3. **Pointez `www` vers vos projets** : dans StackR, *Paramètres → Dossier www* → `C:\laragon\www` (vos projets sont réutilisés sur place), ou copiez-les dans `C:\StackR\www`.
4. **Importez** dans StackR (le mot de passe root est dans la page *Base de données*) :
   ```powershell
   mysql -u root -p < sauvegarde.sql
   ```
5. **Mettez à jour vos fichiers `.env`** : `DB_HOST=127.0.0.1`, `DB_PORT=3306`, `DB_USERNAME=root` et `DB_PASSWORD=` avec le **nouveau mot de passe root** affiché dans la page *Base de données* (Laragon utilisait un mot de passe vide ; StackR en impose un). Créez de préférence un utilisateur dédié par projet.
6. Vérifiez la version de PHP dans la page *Versions* (par exemple 8.3 comme sous Laragon).

## Utilisation en ligne de commande

```text
stackr status               état des services
stackr start [service]      démarre tout, ou apache | mysql | mailpit
stackr stop [service]       arrête tout, ou un service
stackr sites                liste les sites .test
stackr versions             versions installées et disponibles
stackr install <composant> <version>    ex. stackr install php 8.4.28
stackr use php <version>    active une version de PHP
stackr hosts sync           synchronise le fichier hosts (relancé élevé, UAC)
stackr db password          affiche le mot de passe root MySQL
```

## Sécurité

Voir aussi [SECURITY.md](SECURITY.md).

- Tous les services n'écoutent que sur `127.0.0.1`.
- Binaires issus des sources officielles, SHA-256 vérifié avant extraction, protection zip slip.
- Fichier hosts : bloc `# >>> StackR` / `# <<< StackR`, noms validés `^[a-z0-9-]+\.test$`, écriture par `stackr.exe hosts sync` élevé uniquement.
- CA locale ECDSA P-256 à contrainte de nom (`.test`, `localhost`), clé privée chiffrée DPAPI.
- Mot de passe root MySQL aléatoire (24 caractères), chiffré DPAPI ; `bind-address=127.0.0.1`, `mysqlx=OFF`.
- Apache : `ServerTokens Prod`, `ServerSignature Off`, `TraceEnable Off` ; PHP : `expose_php=Off`, `allow_url_include=Off`.

## Désinstallation propre

1. Dans StackR, arrêtez les services, puis désactivez le démarrage avec Windows (*Paramètres*).
2. Désinstallez StackR (*Paramètres Windows → Applications*), ou supprimez le dossier de l'application.
3. Retirez le **PATH** : supprimez l'entrée `C:\StackR\bin\shims` de vos variables d'environnement utilisateur.
4. Retirez la **CA** : `certmgr.msc` → *Autorités de certification racines de confiance → Certificats*, supprimez « StackR Local CA ».
5. Retirez le **bloc hosts** : éditez `C:\Windows\System32\drivers\etc\hosts` en administrateur et supprimez les lignes entre `# >>> StackR` et `# <<< StackR`.
6. Supprimez le dossier de données (`C:\StackR`) après avoir sauvegardé vos bases et projets.

## FAQ

**Les ports 80/443/3306 sont occupés.** Laragon (ou XAMPP, IIS, Skype…) tourne probablement encore. Quittez Laragon complètement ; StackR indique le processus qui occupe chaque port.

**SmartScreen bloque l'installeur.** Il n'est pas signé numériquement : *Informations complémentaires → Exécuter quand même*. Vous pouvez vérifier l'empreinte du fichier publiée avec la Release.

**Mon antivirus signale un fichier.** Les exécutables non signés déclenchent parfois des faux positifs. Ajoutez une exception pour le dossier d'installation, ou construisez depuis les sources (installation B).

**Le navigateur affiche un avertissement de certificat.** La CA n'est pas approuvée : ouvrez *Paramètres* dans StackR et cliquez sur **Faire confiance** (confirmez la boîte de dialogue Windows), puis redémarrez le navigateur.

**Un site `.test` ne s'ouvre pas.** Vérifiez que la demande UAC du fichier hosts a été acceptée (`stackr hosts sync`).

## Contribuer

```powershell
dotnet build -c Release
dotnet test -c Release
```

- `tools\update-manifest.ps1` : script mainteneur qui récupère les dernières versions stables et leurs SHA-256 pour `manifest/components.json`.
- Structure : `src/StackR.Core` (logique), `src/StackR.App` (WPF), `src/StackR.Cli` (`stackr`), `tests/StackR.Core.Tests` (xUnit).
- Les avertissements sont traités comme des erreurs. Aucun binaire tiers n'est versionné.

## Licence

[MIT](LICENSE) © 2026 Ga3TH
