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

## État (2026-11-26)

Tâches 1–14 et 17 terminées, relues et corrigées. En cours : tâche 15 (tableau de bord et sites),
tâche 16 (versions, base de données, journaux, paramètres, assistant), test de bout en bout réel.

Fait (branche moteur/docs) :
- `StackREngine` : porte d'opérations asynchrone unique (installer — téléchargement hors porte —,
  activer, désinstaller, démarrer, arrêter, appliquer). Variantes internes `*CoreAsync` déjà sous verrou :
  une opération ne reprend jamais la porte. Nouvelles API : `StartAsync(serviceId, ct)`,
  `StopAsync(serviceId, ct)`, `UninstallAsync(kind, version)` (refuse version active / service en marche),
  `EffectiveSettings`. `StartAllAsync` applique la configuration puis ne démarre que les services
  installés (« tout ou rien » sur les ports) ; `StartReport.Warnings` / `NotInstalled`. Le CLI
  (`start`/`stop`, nouvelle commande `uninstall`) passe par ces façades.
- Paramètres : racine, www et ports gardent leur valeur du lancement jusqu'au redémarrage
  (`EffectiveSettings`, utilisé pour les configs et les services) ; HTTPS s'applique tout de suite.
- Installation : dossier restant sans marqueur remplacé, installation concurrente de la même version
  tolérée, erreurs de téléchargement en français. Review Focus 1–5 vérifiés (tests ajoutés : port occupé
  au niveau moteur, annulation/coupure d'installation).
- README (PowerShell `ExecutionPolicy`, Git + SDK via winget, CA `StackR Local CA (<NOM-DU-PC>)`,
  désinstallation complète, migration Laragon sans `--all-databases`), `AppMutex=StackR.App` dans l'installeur.

Reste ensuite : la revue finale (tâche 18).
