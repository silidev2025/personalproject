<?php
declare(strict_types=1);

require __DIR__ . '/lib/db.php';
require __DIR__ . '/lib/auth.php';
require __DIR__ . '/lib/layout.php';

panel_session();

if (is_logged_in()) {
    header('Location: index.php');
    exit;
}

$error = '';

if (($_SERVER['REQUEST_METHOD'] ?? 'GET') === 'POST') {
    check_csrf();
    $user = trim((string)($_POST['user'] ?? ''));
    $password = (string)($_POST['password'] ?? '');

    if (attempt_login($user, $password)) {
        header('Location: index.php');
        exit;
    }
    $error = 'wrong credentials';
}

panel_head('Sign in');
echo '<div class="login">';
echo '<h1>&#9670; Rose panel</h1>';

if ($error !== '') {
    echo '<div class="msg bad">' . h($error) . '</div>';
}

if ((string)panel_conf()['admin_hash'] === '') {
    echo '<div class="msg bad">no admin password set - open setup.php first</div>';
}

echo '<form method="post" action="login.php">';
echo csrf_field();
echo '<label for="user">Username</label>';
echo '<input id="user" type="text" name="user" autocomplete="username" autofocus>';
echo '<label for="password">Password</label>';
echo '<input id="password" type="password" name="password" autocomplete="current-password">';
echo '<button class="btn" type="submit">Sign in</button>';
echo '</form></div>';
echo '</body></html>';
