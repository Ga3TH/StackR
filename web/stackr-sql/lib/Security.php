<?php
declare(strict_types=1);

namespace StackRSql;

/** En-têtes de sécurité, session durcie, jeton CSRF, échappement. */
final class Security
{
    public static function sendHeaders(): void
    {
        header('X-Frame-Options: DENY');
        header("Content-Security-Policy: default-src 'self'; img-src 'self' data:; form-action 'self'; frame-ancestors 'none'; base-uri 'none'");
        header('X-Content-Type-Options: nosniff');
        header('Referrer-Policy: no-referrer');
        header('Cache-Control: no-store');
    }

    public static function startSession(): void
    {
        session_name('STACKRSQL');
        session_set_cookie_params([
            'lifetime' => 0,
            'path' => self::basePath(),
            'secure' => !empty($_SERVER['HTTPS']) && $_SERVER['HTTPS'] !== 'off',
            'httponly' => true,
            'samesite' => 'Strict',
        ]);
        ini_set('session.use_strict_mode', '1');
        ini_set('session.use_only_cookies', '1');
        session_start();
        // Inactivité de plus de 2 h : on oublie les identifiants.
        $now = time();
        if (isset($_SESSION['seen']) && $now - (int) $_SESSION['seen'] > 7200) {
            $_SESSION = [];
            session_regenerate_id(true);
        }
        $_SESSION['seen'] = $now;
    }

    /** Chemin du dossier de l'application (ex. « /sql/ »). */
    public static function basePath(): string
    {
        $dir = str_replace('\\', '/', dirname($_SERVER['SCRIPT_NAME'] ?? '/'));
        return rtrim($dir, '/') . '/';
    }

    public static function csrfToken(): string
    {
        if (empty($_SESSION['csrf'])) {
            $_SESSION['csrf'] = bin2hex(random_bytes(32));
        }
        return (string) $_SESSION['csrf'];
    }

    public static function checkCsrf(?string $token): bool
    {
        return is_string($token) && !empty($_SESSION['csrf']) && hash_equals((string) $_SESSION['csrf'], $token);
    }

    public static function e(?string $s): string
    {
        return htmlspecialchars((string) $s, ENT_QUOTES | ENT_SUBSTITUTE, 'UTF-8');
    }
}
