<?php
declare(strict_types=1);

namespace StackRSql;

use PDOException;
use Throwable;

/** Pages HTML (connexion, coquille de l'application) ; tout est échappé. */
final class View
{
    public static function friendlyError(Throwable $e): string
    {
        $msg = $e->getMessage();
        if ($e instanceof PDOException) {
            // « SQLSTATE[42S02]: Base table or view not found: 1146 … » → message MySQL seul.
            $msg = preg_replace('/^SQLSTATE\[[0-9A-Z]+\]:?\s*(?:\[\d+\]\s*)?(?:[A-Za-z ,]+:\s*\d+\s+)?/', '', $msg) ?? $msg;
        }
        return trim($msg) === '' ? 'Erreur inconnue.' : $msg;
    }

    public static function page(string $name, array $vars): void
    {
        header('Content-Type: text/html; charset=utf-8');
        $e = [Security::class, 'e'];
        $csrf = Security::csrfToken();
        $base = Security::basePath();
        ?>
<!doctype html>
<html lang="fr">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="csrf" content="<?= $e($csrf) ?>">
<title>StackR SQL</title>
<link rel="icon" href="<?= $e($base) ?>assets/icon.svg" type="image/svg+xml">
<link rel="stylesheet" href="<?= $e($base) ?>assets/app.css">
<script src="<?= $e($base) ?>assets/theme.js"></script>
</head>
<?php if ($name === 'login') { self::login($vars, $csrf); } else { self::app($vars); } ?>
</html>
        <?php
    }

    private static function login(array $v, string $csrf): void
    {
        $e = [Security::class, 'e'];
        ?>
<body class="login-page">
<main class="login-card fade-in">
  <div class="brand"><span class="logo" aria-hidden="true"></span><div><h1>StackR SQL</h1><p>Tes bases MySQL, sur ta machine uniquement.</p></div></div>
  <?php if (!empty($v['error'])): ?><div class="alert error" role="alert"><?= $e($v['error']) ?></div><?php endif; ?>
  <?php if (!empty($v['db'])): ?><div class="alert info">Après connexion, ouverture de la base <strong><?= $e($v['db']) ?></strong>.</div><?php endif; ?>
  <form method="post" action="?action=login" autocomplete="off">
    <input type="hidden" name="csrf" value="<?= $e($csrf) ?>">
    <?php if (!empty($v['db'])): ?><input type="hidden" name="db" value="<?= $e($v['db']) ?>"><?php endif; ?>
    <div class="row2">
      <label>Hôte<input name="host" value="<?= $e($v['host'] ?? '127.0.0.1') ?>" required></label>
      <label>Port<input name="port" type="number" min="1" max="65535" value="<?= $e((string) ($v['port'] ?? 3306)) ?>" required></label>
    </div>
    <label>Utilisateur<input name="user" value="<?= $e($v['user'] ?? 'root') ?>" required autocomplete="username"></label>
    <label>Mot de passe<input name="password" type="password" autofocus autocomplete="current-password"></label>
    <button class="btn primary wide" type="submit">Se connecter</button>
  </form>
  <p class="hint">Le mot de passe root est affiché dans StackR, page « Base de données ». Il reste dans la session PHP, jamais dans l'adresse.</p>
  <button class="theme-toggle ghost" type="button" data-theme-toggle title="Thème clair / sombre" aria-label="Basculer le thème"></button>
</main>
</body>
        <?php
    }

    private static function app(array $v): void
    {
        $e = [Security::class, 'e'];
        $csrf = Security::csrfToken();
        ?>
<body class="app" data-initial-db="<?= $e($v['db'] ?? '') ?>" data-notice="<?= $e($v['notice'] ?? '') ?>">
<aside class="sidebar">
  <div class="brand small"><span class="logo" aria-hidden="true"></span><div><strong>StackR SQL</strong><span class="muted" id="server"><?= $e($v['user'] . '@' . $v['host'] . ':' . $v['port']) ?></span></div></div>
  <div class="side-actions">
    <input id="filter" type="search" placeholder="Filtrer…" aria-label="Filtrer les bases et tables">
    <button class="btn icon" id="new-db" title="Nouvelle base de données" aria-label="Nouvelle base de données">+</button>
  </div>
  <nav id="tree" class="tree" aria-label="Bases de données"><div class="skeleton-list"><i></i><i></i><i></i><i></i><i></i></div></nav>
  <div class="side-foot">
    <button class="theme-toggle ghost" type="button" data-theme-toggle title="Thème clair / sombre" aria-label="Basculer le thème"></button>
    <form method="post" action="?action=logout"><input type="hidden" name="csrf" value="<?= $e($csrf) ?>"><button class="btn ghost" type="submit">Se déconnecter</button></form>
  </div>
</aside>
<main class="main">
  <header class="topbar">
    <div class="crumbs" id="crumbs"><span class="muted">Choisis une base de données</span></div>
    <div class="tabs" id="tabs" role="tablist">
      <button role="tab" data-tab="overview" class="active">Vue d'ensemble</button>
      <button role="tab" data-tab="data" disabled>Données</button>
      <button role="tab" data-tab="structure" disabled>Structure</button>
      <button role="tab" data-tab="sql">SQL</button>
    </div>
  </header>
  <section class="view" id="view"></section>
</main>
<div id="toasts" class="toasts" aria-live="polite"></div>
<dialog id="dialog"></dialog>
<form id="export-form" method="post" action="?action=export" hidden>
  <input type="hidden" name="csrf" value="<?= $e($csrf) ?>"><input type="hidden" name="db"><input type="hidden" name="table"><input type="hidden" name="format">
</form>
<script src="<?= $e(Security::basePath()) ?>assets/app.js"></script>
</body>
        <?php
    }
}
