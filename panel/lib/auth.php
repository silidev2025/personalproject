<?php
declare(strict_types=1);

function panel_session(): void
{
    if (session_status() === PHP_SESSION_ACTIVE) {
        return;
    }

    $conf = panel_conf();
    session_set_cookie_params([
        'lifetime' => 0,
        'path' => '/',
        'httponly' => true,
        'samesite' => 'Lax',
        'secure' => !empty($_SERVER['HTTPS']),
    ]);
    session_name('rosepanel');
    session_start();

    $ttl = (int)($conf['session_ttl'] ?? 43200);
    $now = time();
    if (isset($_SESSION['started']) && $now - (int)$_SESSION['started'] > $ttl) {
        $_SESSION = [];
        session_destroy();
        session_start();
    }

    if (!isset($_SESSION['csrf'])) {
        $_SESSION['csrf'] = bin2hex(random_bytes(24));
    }
}

function is_logged_in(): bool
{
    panel_session();
    return !empty($_SESSION['admin']);
}

function require_login(): void
{
    if (!is_logged_in()) {
        header('Location: login.php');
        exit;
    }
}

function csrf_token(): string
{
    panel_session();
    return (string)$_SESSION['csrf'];
}

function csrf_field(): string
{
    return '<input type="hidden" name="csrf" value="' . htmlspecialchars(csrf_token(), ENT_QUOTES) . '">';
}

function check_csrf(): void
{
    panel_session();
    $sent = $_POST['csrf'] ?? '';
    if (!is_string($sent) || !hash_equals((string)$_SESSION['csrf'], $sent)) {
        http_response_code(400);
        echo 'bad request token';
        exit;
    }
}

function attempt_login(string $user, string $password): bool
{
    $conf = panel_conf();
    $expectUser = (string)($conf['admin_user'] ?? '');
    $expectHash = (string)($conf['admin_hash'] ?? '');

    if ($expectUser === '' || $expectHash === '') {
        return false;
    }

    $userOk = hash_equals($expectUser, $user);
    $passOk = password_verify($password, $expectHash);
    if (!$userOk || !$passOk) {
        usleep(250000);
        return false;
    }

    panel_session();
    session_regenerate_id(true);
    $_SESSION['admin'] = $user;
    $_SESSION['started'] = time();
    return true;
}

function do_logout(): void
{
    panel_session();
    $_SESSION = [];
    if (ini_get('session.use_cookies')) {
        $params = session_get_cookie_params();
        setcookie(session_name(), '', time() - 42000, $params['path']);
    }
    session_destroy();
}
