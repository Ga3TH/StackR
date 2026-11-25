# Politique de sécurité

## Signaler une vulnérabilité

Merci de **ne pas** ouvrir d'issue publique. Utilisez le signalement privé de
GitHub : onglet **Security** du dépôt
<https://github.com/Ga3TH/StackR>, puis **Report a vulnerability**
(*private vulnerability reporting*). Indiquez la version, les étapes de
reproduction et l'impact.

## Modèle de sécurité (résumé)

- **Écoute locale uniquement** : Apache (80/443), MySQL (3306) et Mailpit
  (8025/1025) n'écoutent que sur `127.0.0.1`.
- **Pas d'administrateur au quotidien** : seule l'écriture du fichier hosts
  passe par `stackr.exe hosts sync` relancé élevé (UAC), avec des noms validés
  (`^[a-z0-9-]+\.test$`) et un bloc délimité `# >>> StackR` / `# <<< StackR`.
- **Binaires vérifiés** : versions stables uniquement, URL officielles,
  SHA-256 vérifié avant extraction, protection contre le zip slip. Composer est
  vérifié par sa signature SHA-384 officielle.
- **HTTPS local** : autorité de certification propre à la machine (ECDSA P-256),
  limitée par `NameConstraints` à `.test` et `localhost`, installée dans
  *CurrentUser\Root* ; clé privée chiffrée par DPAPI.
- **Secrets** : mot de passe root MySQL aléatoire (24 caractères) chiffré par
  DPAPI (portée utilisateur Windows).
- **Durcissement** : `ServerTokens Prod`, `TraceEnable Off`, `expose_php=Off`,
  `allow_url_include=Off`, `mysqlx=OFF`.
- **Manifeste** : mises à jour en HTTPS uniquement ; les empreintes des binaires
  restent contrôlées quoi qu'il arrive.
