# Changelog

Format basé sur [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/),
versionnage [SemVer](https://semver.org/lang/fr/).

## [0.1.3] - 2026-11-27

### Ajouté
- **HeidiSQL** 12.22 (portable 64 bits, téléchargement officiel, SHA-256 vérifié) : composant par défaut, installé par l'assistant et par l'installation automatique. Page Base de données : bouton principal « Ouvrir HeidiSQL », déjà connecté à StackR (session « StackR » ; le mot de passe n'est jamais passé en ligne de commande et est retiré du disque peu après le lancement). Adminer devient un bouton secondaire.
- **Thème Adminer** moderne : pepa-linha (clair) et dracula (sombre) du dépôt officiel, même version qu'Adminer ; Adminer choisit selon le thème de Windows.
- **Installateur hors ligne** : l'installateur et l'archive portable embarquent les composants par défaut (`packages\`, ~400 Mo, SHA-256 vérifié à l'installation, repli sur le téléchargement si un fichier est absent ou altéré). L'assistant indique « Tout est inclus : installation hors ligne ». `build.ps1 -WithPackages` et `tools/fetch-packages.ps1`.

## [0.1.2] - 2026-11-27

### Corrigé
- Les changements de configuration (httpd.conf, vhosts des sites, php.ini, my.ini) atteignent désormais les services déjà lancés : Apache (validé par `httpd -t` avant, l'ancienne instance reste en place si la nouvelle configuration est invalide) et MySQL sont redémarrés automatiquement. Corrige `http://localhost/adminer` en 404 tant qu'Apache n'était pas relancé.
- La molette de la souris fonctionne sur toutes les pages et dans l'assistant (défilement transmis à la page quand une zone imbriquée est en butée).

### Ajouté
- Installation automatique, au lancement, des composants par défaut manquants (Apache, PHP, MySQL, Mailpit, Composer, Adminer ; version recommandée, SHA-256 vérifié, activés puis configurés), avec progression et bilan dans le tableau de bord.
- Une légende française sous chaque bouton et interrupteur de l'application (reprise en info-bulle), en français et en anglais.

## [0.1.1] - 2026-11-27

### Corrigé
- Paramètres : les libellés et icônes des options (démarrage avec Windows, démarrage auto des services, mode sombre, HTTPS…) ne s'affichaient pas.
- Apache : la configuration échouait au premier lancement quand aucun site n'existait (`IncludeOptional`).

## [0.1.0] - 2026-11-27

### Ajouté
- Installation, activation et désinstallation de versions stables d'Apache, PHP, MySQL, Mailpit, Node.js et Composer, avec vérification SHA-256.
- Démarrage/arrêt des services, détection des ports occupés.
- Vhosts automatiques `https://<dossier>.test` et autorité de certification locale à contrainte de nom.
- Synchronisation du fichier hosts via `stackr.exe hosts sync` (UAC).
- Interface WPF Fluent (tableau de bord, sites, versions, base de données, journaux, paramètres) et zone de notification.
- CLI `stackr`, intégration PATH utilisateur, démarrage avec Windows.
- Adminer 6.3.1 (build MySQL) installable, servi sur `http://localhost/adminer` (réservé à la machine locale), et bouton « Ouvrir Adminer » dans la page Base de données.
- Mode sombre : interrupteur en tête des Paramètres (option « Suivre Windows »), appliqué instantanément à toutes les fenêtres et restauré au démarrage.
- Publication self-contained single-file, installeur Inno Setup, CI GitHub Actions.
