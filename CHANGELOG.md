# Changelog

Format basé sur [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/),
versionnage [SemVer](https://semver.org/lang/fr/).

## [0.1.0] - Non publiée

### Ajouté
- Installation, activation et désinstallation de versions stables d'Apache, PHP, MySQL, Mailpit, Node.js et Composer, avec vérification SHA-256.
- Démarrage/arrêt des services, détection des ports occupés.
- Vhosts automatiques `https://<dossier>.test` et autorité de certification locale à contrainte de nom.
- Synchronisation du fichier hosts via `stackr.exe hosts sync` (UAC).
- Interface WPF Fluent (tableau de bord, sites, versions, base de données, journaux, paramètres) et zone de notification.
- CLI `stackr`, intégration PATH utilisateur, démarrage avec Windows.
- Publication self-contained single-file, installeur Inno Setup, CI GitHub Actions.
