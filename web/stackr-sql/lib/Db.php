<?php
declare(strict_types=1);

namespace StackRSql;

use PDO;
use RuntimeException;

/**
 * Accès MySQL : chaque identifiant venant de l'utilisateur est vérifié dans INFORMATION_SCHEMA
 * puis cité entre accents graves ; les valeurs passent par des requêtes préparées.
 */
final class Db
{
    public const SYSTEM_DBS = ['information_schema', 'mysql', 'performance_schema', 'sys'];
    public const PAGE_SIZE = 50;
    private const MAX_RESULT_ROWS = 1000;
    private const MAX_CELL = 2000;

    private function __construct(private readonly PDO $pdo)
    {
    }

    /** Hôtes acceptés : la machine locale uniquement. */
    public static function isLoopback(string $host): bool
    {
        return in_array(strtolower($host), ['127.0.0.1', 'localhost', '::1'], true);
    }

    public static function connect(string $host, int $port, string $user, string $password): self
    {
        if (!self::isLoopback($host)) {
            throw new RuntimeException('Seules les connexions locales (127.0.0.1) sont autorisées.');
        }
        if ($port < 1 || $port > 65535) {
            throw new RuntimeException('Port invalide.');
        }
        $dsn = sprintf('mysql:host=%s;port=%d;charset=utf8mb4', $host === '::1' ? '[::1]' : $host, $port);
        $pdo = new PDO($dsn, $user, $password, [
            PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION,
            PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
            PDO::ATTR_EMULATE_PREPARES => false,
            PDO::ATTR_TIMEOUT => 5,
            PDO::MYSQL_ATTR_MULTI_STATEMENTS => false,
        ]);
        return new self($pdo);
    }

    public static function quote(string $identifier): string
    {
        return '`' . str_replace('`', '``', $identifier) . '`';
    }

    public function serverVersion(): string
    {
        return (string) $this->pdo->getAttribute(PDO::ATTR_SERVER_VERSION);
    }

    /** @return list<array{name:string,collation:string,size:int,tables:int}> */
    public function databases(): array
    {
        $rows = $this->pdo->query(
            'SELECT s.SCHEMA_NAME AS name, s.DEFAULT_COLLATION_NAME AS collation,
                    COALESCE(SUM(t.DATA_LENGTH + t.INDEX_LENGTH), 0) AS size, COUNT(t.TABLE_NAME) AS tables
             FROM INFORMATION_SCHEMA.SCHEMATA s
             LEFT JOIN INFORMATION_SCHEMA.TABLES t ON t.TABLE_SCHEMA = s.SCHEMA_NAME
             GROUP BY s.SCHEMA_NAME, s.DEFAULT_COLLATION_NAME
             ORDER BY s.SCHEMA_NAME'
        )->fetchAll();
        return array_map(static fn (array $r) => [
            'name' => (string) $r['name'],
            'collation' => (string) $r['collation'],
            'size' => (int) $r['size'],
            'tables' => (int) $r['tables'],
            'system' => in_array(strtolower((string) $r['name']), self::SYSTEM_DBS, true),
        ], $rows);
    }

    /** Renvoie le nom exact de la base si elle existe, sinon lève une exception. */
    public function requireDb(mixed $db): string
    {
        if (!is_string($db) || $db === '') {
            throw new RuntimeException('Base de données manquante.');
        }
        $st = $this->pdo->prepare('SELECT SCHEMA_NAME FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = ?');
        $st->execute([$db]);
        $name = $st->fetchColumn();
        if ($name === false) {
            throw new RuntimeException('Base de données introuvable.');
        }
        return (string) $name;
    }

    public function requireTable(string $db, mixed $table): string
    {
        if (!is_string($table) || $table === '') {
            throw new RuntimeException('Table manquante.');
        }
        $st = $this->pdo->prepare('SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = ? AND TABLE_NAME = ?');
        $st->execute([$db, $table]);
        $name = $st->fetchColumn();
        if ($name === false) {
            throw new RuntimeException('Table introuvable.');
        }
        return (string) $name;
    }

    /** @return list<string> */
    public function columnNames(string $db, string $table): array
    {
        $st = $this->pdo->prepare('SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = ? AND TABLE_NAME = ? ORDER BY ORDINAL_POSITION');
        $st->execute([$db, $table]);
        return array_map('strval', $st->fetchAll(PDO::FETCH_COLUMN));
    }

    public function dbInfo(string $db): array
    {
        foreach ($this->databases() as $d) {
            if ($d['name'] === $db) {
                return $d;
            }
        }
        throw new RuntimeException('Base de données introuvable.');
    }

    public function tables(string $db): array
    {
        $st = $this->pdo->prepare(
            'SELECT TABLE_NAME AS name, TABLE_TYPE AS type, ENGINE AS engine, TABLE_ROWS AS rows_estimate,
                    COALESCE(DATA_LENGTH + INDEX_LENGTH, 0) AS size, TABLE_COLLATION AS collation, TABLE_COMMENT AS comment
             FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = ? ORDER BY TABLE_NAME'
        );
        $st->execute([$db]);
        return array_map(static fn (array $r) => [
            'name' => (string) $r['name'],
            'view' => $r['type'] === 'VIEW',
            'engine' => (string) ($r['engine'] ?? ''),
            'rows' => $r['rows_estimate'] === null ? null : (int) $r['rows_estimate'],
            'size' => (int) $r['size'],
            'collation' => (string) ($r['collation'] ?? ''),
            'comment' => (string) ($r['comment'] ?? ''),
        ], $st->fetchAll());
    }

    public function createDatabase(mixed $name): string
    {
        if (!is_string($name) || !preg_match('/^[A-Za-z0-9_$-]{1,64}$/', $name)) {
            throw new RuntimeException('Nom invalide : lettres, chiffres, « _ », « - » ou « $ » (64 caractères au plus).');
        }
        $this->pdo->exec('CREATE DATABASE ' . self::quote($name) . ' CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci');
        return $name;
    }

    public function dropDatabase(mixed $db, mixed $confirm): void
    {
        $name = $this->requireDb($db);
        if (in_array(strtolower($name), self::SYSTEM_DBS, true)) {
            throw new RuntimeException('Les bases système ne peuvent pas être supprimées.');
        }
        if ($confirm !== $name) {
            throw new RuntimeException('Le nom saisi ne correspond pas : suppression annulée.');
        }
        $this->pdo->exec('DROP DATABASE ' . self::quote($name));
    }

    public function browse(string $db, string $table, int $page, mixed $sort, mixed $dir): array
    {
        $columns = $this->columnNames($db, $table);
        $order = '';
        $sortCol = null;
        if (is_string($sort) && in_array($sort, $columns, true)) {
            $sortCol = $sort;
            $order = ' ORDER BY ' . self::quote($sort) . ($dir === 'desc' ? ' DESC' : ' ASC');
        }
        $from = self::quote($db) . '.' . self::quote($table);
        $total = (int) $this->pdo->query('SELECT COUNT(*) FROM ' . $from)->fetchColumn();
        $pages = max(1, (int) ceil($total / self::PAGE_SIZE));
        $page = min(max(1, $page), $pages);
        $st = $this->pdo->prepare('SELECT * FROM ' . $from . $order . ' LIMIT ? OFFSET ?');
        $st->bindValue(1, self::PAGE_SIZE, PDO::PARAM_INT);
        $st->bindValue(2, ($page - 1) * self::PAGE_SIZE, PDO::PARAM_INT);
        $st->execute();
        return [
            'columns' => $columns,
            'rows' => self::cells($st->fetchAll(PDO::FETCH_NUM)),
            'total' => $total,
            'page' => $page,
            'pages' => $pages,
            'pageSize' => self::PAGE_SIZE,
            'sort' => $sortCol,
            'dir' => $dir === 'desc' ? 'desc' : 'asc',
        ];
    }

    public function structure(string $db, string $table): array
    {
        $st = $this->pdo->prepare(
            'SELECT COLUMN_NAME AS name, COLUMN_TYPE AS type, IS_NULLABLE AS nullable, COLUMN_DEFAULT AS def,
                    COLUMN_KEY AS col_key, EXTRA AS extra, COLLATION_NAME AS collation, COLUMN_COMMENT AS comment
             FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = ? AND TABLE_NAME = ? ORDER BY ORDINAL_POSITION'
        );
        $st->execute([$db, $table]);
        $columns = $st->fetchAll();
        $st = $this->pdo->prepare(
            'SELECT INDEX_NAME AS name, NON_UNIQUE AS non_unique, INDEX_TYPE AS type,
                    GROUP_CONCAT(COLUMN_NAME ORDER BY SEQ_IN_INDEX SEPARATOR \', \') AS cols
             FROM INFORMATION_SCHEMA.STATISTICS WHERE TABLE_SCHEMA = ? AND TABLE_NAME = ?
             GROUP BY INDEX_NAME, NON_UNIQUE, INDEX_TYPE ORDER BY INDEX_NAME = \'PRIMARY\' DESC, INDEX_NAME'
        );
        $st->execute([$db, $table]);
        $indexes = array_map(static fn (array $r) => [
            'name' => (string) $r['name'],
            'unique' => (int) $r['non_unique'] === 0,
            'type' => (string) $r['type'],
            'columns' => (string) $r['cols'],
        ], $st->fetchAll());
        return ['columns' => $columns, 'indexes' => $indexes];
    }

    /** Exécute une requête de la console (une instruction à la fois). */
    public function run(?string $db, string $sql): array
    {
        if ($db !== null) {
            $this->pdo->exec('USE ' . self::quote($db));
        }
        $start = hrtime(true);
        $st = $this->pdo->query($sql);
        $ms = (hrtime(true) - $start) / 1e6;
        $count = $st->columnCount();
        if ($count === 0) {
            return ['kind' => 'affected', 'affected' => $st->rowCount(), 'ms' => round($ms, 2)];
        }
        $columns = [];
        for ($i = 0; $i < $count; $i++) {
            $meta = $st->getColumnMeta($i);
            $columns[] = (string) ($meta['name'] ?? ('col' . $i));
        }
        $rows = [];
        $truncated = false;
        while (($row = $st->fetch(PDO::FETCH_NUM)) !== false) {
            if (count($rows) >= self::MAX_RESULT_ROWS) {
                $truncated = true;
                break;
            }
            $rows[] = $row;
        }
        $st->closeCursor();
        $ms = (hrtime(true) - $start) / 1e6;
        return ['kind' => 'rows', 'columns' => $columns, 'rows' => self::cells($rows), 'truncated' => $truncated,
            'limit' => self::MAX_RESULT_ROWS, 'ms' => round($ms, 2)];
    }

    /** Écrit la table au format SQL (INSERT) ou CSV sur la sortie. */
    public function export(string $db, string $table, string $format): void
    {
        $from = self::quote($db) . '.' . self::quote($table);
        $columns = $this->columnNames($db, $table);
        $st = $this->pdo->query('SELECT * FROM ' . $from);
        $out = fopen('php://output', 'wb');
        if ($format === 'csv') {
            fwrite($out, "\xEF\xBB\xBF");
            fputcsv($out, $columns, ';', '"', '');
            while (($row = $st->fetch(PDO::FETCH_NUM)) !== false) {
                fputcsv($out, array_map(static fn ($v) => $v === null ? '' : (string) $v, $row), ';', '"', '');
            }
        } else {
            $create = $this->pdo->query('SHOW CREATE TABLE ' . $from)->fetch(PDO::FETCH_NUM);
            fwrite($out, '-- Export StackR SQL : ' . $db . '.' . $table . ' (' . date('Y-m-d H:i:s') . ")\n");
            fwrite($out, "SET NAMES utf8mb4;\n\n");
            if (is_array($create) && isset($create[1])) {
                fwrite($out, 'DROP TABLE IF EXISTS ' . self::quote($table) . ";\n" . $create[1] . ";\n\n");
            }
            $cols = implode(', ', array_map([self::class, 'quote'], $columns));
            while (($row = $st->fetch(PDO::FETCH_NUM)) !== false) {
                $values = array_map(fn ($v) => $v === null ? 'NULL' : $this->pdo->quote((string) $v), $row);
                fwrite($out, 'INSERT INTO ' . self::quote($table) . " ($cols) VALUES (" . implode(', ', $values) . ");\n");
            }
        }
        fclose($out);
    }

    /** Cellules affichables : chaînes ou null, binaire signalé, longueur bornée. */
    private static function cells(array $rows): array
    {
        foreach ($rows as &$row) {
            foreach ($row as &$v) {
                if ($v === null) {
                    continue;
                }
                $v = (string) $v;
                if (!mb_check_encoding($v, 'UTF-8')) {
                    $v = '[binaire, ' . strlen($v) . ' octets]';
                } elseif (mb_strlen($v) > self::MAX_CELL) {
                    $v = mb_substr($v, 0, self::MAX_CELL) . '…';
                }
            }
        }
        return $rows;
    }
}
