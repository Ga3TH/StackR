<?php
declare(strict_types=1);

// StackR SQL : gestionnaire MySQL local (servi par StackR sur http://localhost/sql, machine locale uniquement).

namespace StackRSql;

use Throwable;

require __DIR__ . '/lib/Security.php';
require __DIR__ . '/lib/Db.php';
require __DIR__ . '/lib/View.php';

Security::sendHeaders();
Security::startSession();

$method = $_SERVER['REQUEST_METHOD'] ?? 'GET';
$action = isset($_GET['action']) && is_string($_GET['action']) ? $_GET['action'] : '';

/** Connexion depuis les identifiants de la session (jamais dans l'URL ni les journaux). */
function session_db(): ?Db
{
    $c = $_SESSION['conn'] ?? null;
    if (!is_array($c)) {
        return null;
    }
    return Db::connect((string) $c['host'], (int) $c['port'], (string) $c['user'], (string) $c['password']);
}

function json_out(array $data, int $status = 200): never
{
    http_response_code($status);
    header('Content-Type: application/json; charset=utf-8');
    echo json_encode($data, JSON_UNESCAPED_UNICODE | JSON_INVALID_UTF8_SUBSTITUTE | JSON_UNESCAPED_SLASHES);
    exit;
}

function redirect_db(?string $db): never
{
    $url = Security::basePath() . ($db !== null && $db !== '' ? '?db=' . rawurlencode($db) : '');
    header('Location: ' . $url, true, 303);
    exit;
}

$deepDb = isset($_GET['db']) && is_string($_GET['db']) ? $_GET['db'] : null;

// --- Formulaires (POST + CSRF) ---------------------------------------------------------------
if ($method === 'POST' && ($action === 'login' || $action === 'logout' || $action === 'export')) {
    if (!Security::checkCsrf($_POST['csrf'] ?? null)) {
        http_response_code(400);
        View::page('login', ['error' => 'Session expirée : recharge la page et réessaie.', 'db' => $deepDb]);
        exit;
    }
    if ($action === 'logout') {
        $_SESSION = [];
        session_regenerate_id(true);
        redirect_db(null);
    }
    if ($action === 'login') {
        $host = trim((string) ($_POST['host'] ?? '127.0.0.1'));
        $port = (int) ($_POST['port'] ?? 3306);
        $user = (string) ($_POST['user'] ?? 'root');
        $password = (string) ($_POST['password'] ?? '');
        $target = isset($_POST['db']) && is_string($_POST['db']) ? $_POST['db'] : null;
        try {
            Db::connect($host, $port, $user, $password);
        } catch (Throwable $e) {
            http_response_code(401);
            View::page('login', [
                'error' => 'Connexion impossible : ' . View::friendlyError($e),
                'host' => $host, 'port' => $port, 'user' => $user, 'db' => $target,
            ]);
            exit;
        }
        session_regenerate_id(true);
        $_SESSION['conn'] = ['host' => $host, 'port' => $port, 'user' => $user, 'password' => $password];
        $_SESSION['csrf'] = bin2hex(random_bytes(32));
        redirect_db($target);
    }
    // Export : téléchargement d'une table en SQL ou CSV.
    try {
        $db = session_db() ?? throw new \RuntimeException('Non connecté.');
        $name = $db->requireDb($_POST['db'] ?? null);
        $table = $db->requireTable($name, $_POST['table'] ?? null);
        $format = ($_POST['format'] ?? '') === 'csv' ? 'csv' : 'sql';
        $file = preg_replace('/[^A-Za-z0-9_.-]+/', '_', $name . '-' . $table) . '.' . $format;
        header('Content-Type: ' . ($format === 'csv' ? 'text/csv' : 'application/sql') . '; charset=utf-8');
        header('Content-Disposition: attachment; filename="' . $file . '"');
        $db->export($name, $table, $format);
    } catch (Throwable $e) {
        http_response_code(400);
        header('Content-Type: text/plain; charset=utf-8');
        echo 'Export impossible : ' . View::friendlyError($e);
    }
    exit;
}

// --- API JSON (POST uniquement, jeton CSRF dans l'en-tête X-CSRF-Token) ------------------------
if ($action === 'api') {
    if ($method !== 'POST') {
        json_out(['error' => 'Méthode non autorisée.'], 405);
    }
    if (!Security::checkCsrf($_SERVER['HTTP_X_CSRF_TOKEN'] ?? null)) {
        json_out(['error' => 'Jeton de sécurité invalide : recharge la page.'], 403);
    }
    $in = json_decode((string) file_get_contents('php://input'), true);
    $in = is_array($in) ? $in : [];
    try {
        $db = session_db();
        if ($db === null) {
            json_out(['error' => 'Session expirée : reconnecte-toi.', 'login' => true], 401);
        }
        $op = (string) ($in['op'] ?? '');
        switch ($op) {
            case 'databases':
                json_out(['databases' => $db->databases(), 'server' => $db->serverVersion()]);
            case 'tables':
                $name = $db->requireDb($in['db'] ?? null);
                json_out(['db' => $db->dbInfo($name), 'tables' => $db->tables($name)]);
            case 'createDb':
                json_out(['created' => $db->createDatabase($in['name'] ?? null)]);
            case 'dropDb':
                $db->dropDatabase($in['db'] ?? null, $in['confirm'] ?? null);
                json_out(['dropped' => true]);
            case 'browse':
                $name = $db->requireDb($in['db'] ?? null);
                $table = $db->requireTable($name, $in['table'] ?? null);
                json_out($db->browse($name, $table, (int) ($in['page'] ?? 1), $in['sort'] ?? null, $in['dir'] ?? 'asc'));
            case 'structure':
                $name = $db->requireDb($in['db'] ?? null);
                $table = $db->requireTable($name, $in['table'] ?? null);
                json_out($db->structure($name, $table));
            case 'query':
                $sql = trim((string) ($in['sql'] ?? ''));
                if ($sql === '') {
                    json_out(['error' => 'Requête vide.'], 400);
                }
                $name = isset($in['db']) && $in['db'] !== '' && $in['db'] !== null ? $db->requireDb($in['db']) : null;
                json_out($db->run($name, $sql));
            default:
                json_out(['error' => 'Opération inconnue.'], 400);
        }
    } catch (Throwable $e) {
        json_out(['error' => View::friendlyError($e)], 400);
    }
}

// --- Pages -----------------------------------------------------------------------------------
if (!isset($_SESSION['conn'])) {
    View::page('login', ['db' => $deepDb]);
    exit;
}
try {
    $db = session_db();
} catch (Throwable $e) {
    $_SESSION = [];
    session_regenerate_id(true);
    View::page('login', ['error' => 'Connexion perdue : ' . View::friendlyError($e), 'db' => $deepDb]);
    exit;
}
// Lien direct ?db=… : vérifié dans la liste des bases avant d'être transmis à l'interface.
$initialDb = null;
$notice = null;
if ($deepDb !== null) {
    try {
        $initialDb = $db->requireDb($deepDb);
    } catch (Throwable) {
        $notice = 'La base demandée est introuvable.';
    }
}
View::page('app', [
    'db' => $initialDb,
    'notice' => $notice,
    'user' => (string) $_SESSION['conn']['user'],
    'host' => (string) $_SESSION['conn']['host'],
    'port' => (int) $_SESSION['conn']['port'],
]);
