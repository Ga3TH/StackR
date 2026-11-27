# Changelog

Format basé sur [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/),
versionnage [SemVer](https://semver.org/lang/fr/).

## [0.1.6] - 2026-11-27

### Corrigé
- StackR SQL : le logo en haut à gauche ne s'affichait pas.

## [0.1.5] - 2026-11-27

### Ajouté
- StackR SQL : interface MySQL web soignée sur http://localhost/sql (locale uniquement), bouton « Ouvrir StackR SQL » dans la page Base de données.

### Modifié
- **Interface repensée**, plus agréable et plus détaillée :
  - Tableau de bord : accueil « Bonjour / Bonsoir » avec résumé (services actifs, sites, PHP actif) ; cartes de services avec pastille d'état colorée (pulsation pendant un démarrage ou un arrêt), mémoire, durée de fonctionnement, PID et ports en puces, boutons Démarrer/Arrêter, Redémarrer et Journaux ; « Mes sites » (type de projet, ouverture en un clic) ; outils rapides en tuiles (Terminal, Dossier www, HeidiSQL, Adminer, Mailpit) ; conseil clair quand le port 80 est déjà pris (Laragon resté ouvert…).
  - Sites : recherche, cartes détaillées (Laravel / Symfony / WordPress / PHP / HTML détecté, badge HTTPS, racine web, PHP, Git, Composer, Node, dernière modification), actions Ouvrir, Dossier, VS Code, Terminal ici et « Mettre à la corbeille » (confirmation, Corbeille de Windows, sous-dossier direct de www seulement).
  - Barre d'état en bas de la fenêtre (Apache ● MySQL ● Mailpit ● · PHP · dossier www), raccourcis Ctrl+1…6 (pages) et F5 (actualiser), notifications de réussite (toasts).
  - Animations discrètes : transition entre les pages, apparition en cascade des cartes, élévation au survol ; désactivées si Windows coupe les animations.
- Légendes des actions répétées d'une liste en info-bulle (moins de texte à l'écran).

### Ajouté (cœur)
- Lecture de la mémoire et de l'heure de démarrage des services, détection du type de projet, liste et création de bases MySQL en utf8mb4 (fichier d'options temporaire, jamais de mot de passe en ligne de commande), gravité des lignes de journal — avec tests.
- Outil de développement `tools/StackR.Screens` : captures de chaque page en clair et en sombre avec des données factices.

## [0.1.4] - 2026-11-27

### Ajouté
- **Mises à jour dans l'application** : un bandeau « Mise à jour disponible — StackR x.y.z » en haut de la fenêtre (notes de version dépliables, masquable pour la version concernée) avec le bouton « Mettre à jour ». Copie installée : téléchargement de l'installateur de la release (HTTPS, SHA-256 vérifié, progression), confirmation, installation silencieuse puis relance de StackR, services laissés actifs. Copie portable ou release sans empreinte : ouverture de la page de la release.
- Recherche au lancement puis toutes les 6 h (API publique de GitHub, préversions ignorées), réglable dans Paramètres (« Rechercher les mises à jour automatiquement ») ; bouton « Rechercher maintenant » (« StackR est à jour (x.y.z) »).
- L'installateur relance StackR après une installation silencieuse.

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
