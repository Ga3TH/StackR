# StackR — instructions pour Claude

StackR remplace Laragon/XAMPP pour son auteur : Apache, PHP, MySQL, Mailpit, Node, Composer
sous Windows 10/11, versions **stables** uniquement, sécurisé par défaut, interface Fluent soignée.

## Règles de travail (décidées par l'auteur)

- Travailler en **autonomie**, sans poser de questions ; trancher et noter la décision.
- Utiliser des agents dont le niveau (haiku / sonnet / opus) suit la difficulté de la tâche.
- **Une seule branche : `main`.** Aucune autre branche poussée sur GitHub.
- **Pousser `main` après chaque tâche terminée et testée** (`git push origin main`).
- Réponses, commits, commentaires et textes d'interface en **français** ; identifiants en anglais.
- Ne jamais toucher à `C:\laragon` ni à ses processus.
- L'interface doit être belle et professionnelle (WPF-UI, thème clair/sombre, Mica).

## Où est quoi

- Conception : `docs/superpowers/specs/2026-11-25-stackr-design.md` (fait foi).
- Plan d'implémentation (tâches 1–18) : `docs/superpowers/plans/2026-11-25-stackr.md`.
- `src/StackR.Core` : toute la logique (manifeste, téléchargement vérifié, configs, sites, hosts,
  CA locale, services, PATH). `src/StackR.Cli` : `stackr.exe`. `src/StackR.App` : WPF (`StackR.App.exe`).
- `manifest/components.json` : versions stables + SHA-256 officiels (régénérer avec
  `tools/update-manifest.ps1`, URLs d'archives permanentes).

## Construire et tester

```powershell
dotnet build -c Release          # 0 avertissement exigé (TreatWarningsAsErrors)
dotnet test -c Release --filter "Category!=Integration"
$env:STACKR_E2E = "1"; dotnet test -c Release --filter "Category=Integration"   # vrais composants, ~400 Mo
.\build.ps1                      # publie publish\StackR\ (+ installateur si Inno Setup présent)
```

## Conventions de code

- Espaces de noms `StackR.Core.<Dossier>` (dossier Manifest → `StackR.Core.Manifests`).
- Tests : `tests/StackR.Core.Tests/<Dossier>/<Classe>Tests.cs`, noms `Méthode_Scénario_Attendu`.
- Les tests ne touchent jamais l'état réel du système (hosts, HKCU Path/Run, magasins de certificats,
  `C:\StackR`) : dossiers temporaires et surcharges injectées.
- Écritures de fichiers atomiques via `StackR.Core.IO.AtomicFile`.
- Sécurité non négociable : écoute sur `127.0.0.1` uniquement, SHA-256 vérifié avant extraction,
  HTTPS seulement, secrets DPAPI, CA limitée à `.test`/`localhost`/127.0.0.0/8, droits admin
  seulement pour `stackr hosts sync`.
- Commits : `type(portée): sujet` en français, terminés par la ligne `Co-Authored-By` du modèle utilisé.

## État (2026-11-26, soir)

**Toutes les tâches 1–17 sont fusionnées dans `main`.** Build Release : 0 avertissement ; 495 tests
unitaires verts ; test de bout en bout réel vert (`STACKR_E2E=1`, voir plus bas).

Vérifié en réel (test `tests/StackR.Core.Tests/Integration/EndToEndTests.cs`, ~350 Mo, ~1 min) :
installation vérifiée d'Apache, PHP, MySQL, Mailpit dans une racine « Dossier é » ; démarrage ;
page PHP en HTTP et HTTPS (certificat émis par la CA locale) ; PDO MySQL avec le mot de passe root
généré ; `mail()` → Mailpit ; shim `php` avec ses extensions ; écoute sur 127.0.0.1 uniquement ; arrêt.

Bugs trouvés par ce test et corrigés :
- MySQL lit `my.ini` et ses arguments en **page de code ANSI** (pas UTF-8) → `Config/AnsiPath.cs` :
  `my.ini` écrit en ANSI, chemins hors ANSI remplacés par leur nom court 8.3.
- PHP charge ses extensions avec des API ANSI → chemins de `php.ini` et `PHPIniDir` en ASCII (nom court).
- Shims `php`/`composer` et création Laravel : `PHPRC` vers `etc\` (sinon PHP sans aucune extension).
- `php.ini` : `sendmail_from` (sinon `mail()` échoue sous Windows).

Fait par les agents (worktrees `C:\.fichier G\StackR-wt\*`, branches `wt/*` locales, jamais poussées) :
- Moteur : porte d'opérations unique (`*CoreAsync` sous verrou), `StartAsync`/`StopAsync`/`UninstallAsync`,
  `EffectiveSettings`, `UpdateSettings` (racine/www/ports au redémarrage, HTTPS tout de suite), CLI `uninstall`.
- App : tableau de bord + Sites + `EngineBridge` (pont zone de notification ↔ moteur) ; Versions + Base de
  données (`MySqlPasswordChanger`, `MySqlTerminal`) ; Journaux (`IO/LogTail`) + Paramètres (`PortRules`,
  `ThemeApplier`) ; assistant de premier lancement en 7 étapes (`FirstRunWindow`, `InstallLocationValidator`,
  `FirstRunInstall`) ; ordre de démarrage : moteur créé seulement après le choix de la racine, relance
  (`AppShell.RaiseRestart`, `--restarted`), `crash.log`.
- Textes : une table `.resx` par zone (`Resources/<Zone>Strings*.resx` + `Strings.<Zone>.cs`).
- `STACKR_SETTINGS=<chemin absolu>` : essayer l'application sans toucher à l'installation réelle.

Reste :
- Vérification visuelle de l'application lancée (avec `STACKR_SETTINGS` et une racine temporaire) :
  assistant complet, chaque page, clair/sombre.
- Revue finale (tâche 18).

Fait le 2026-11-27 : `SystemThemeWatcher.Watch` seulement si `App.FollowsSystemTheme` ; lancement
de l'application vérifié (8 s sans plantage, paramètres temporaires via `STACKR_SETTINGS`).
