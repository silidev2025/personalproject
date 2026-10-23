<?php
declare(strict_types=1);

/**
 * One-time setup: writes config.php with an admin password hash, then refuses
 * to run again. Delete this file once the panel is reachable.
 */

$configPath = __DIR__ . '/config.php';
$current = is_file($configPath) ? (require $configPath) : [];
if (!is_array($current)) {
    $current = [];
}

$alreadySet = !empty($current['admin_hash']);
$force = isset($_GET['force']);
$done = false;
$error = '';

// keep whatever config.php already carries when a field is left blank
$currentWebhook = (string)($current['webhook'] ?? '');
$currentDb = (string)($current['db'] ?? (__DIR__ . '/data/panel.sqlite'));
$currentSalt = (string)($current['api_salt'] ?? '');

$formWebhook = $currentWebhook;

if (($_SERVER['REQUEST_METHOD'] ?? 'GET') === 'POST' && (!$alreadySet || $force)) {
    $user = trim((string)($_POST['user'] ?? ''));
    $pass = (string)($_POST['pass'] ?? '');
    $pass2 = (string)($_POST['pass2'] ?? '');
    $webhook = trim((string)($_POST['webhook'] ?? ''));
    if ($webhook !== '') {
        $formWebhook = $webhook;
    }

    if ($user === '' || strlen($user) > 64) {
        $error = 'username must be 1-64 characters';
    } elseif (strlen($pass) < 10) {
        $error = 'password must be at least 10 characters';
    } elseif ($pass !== $pass2) {
        $error = 'passwords do not match';
    } elseif ($webhook !== '' && strpos($webhook, 'https://') !== 0) {
        $error = 'webhook must start with https://';
    } else {
        if ($webhook === '') {
            $webhook = $currentWebhook;
        }

        $php = "<?php\ndeclare(strict_types=1);\n\nreturn [\n";
        $php .= "    'db' => " . var_export($currentDb, true) . ",\n";
        $php .= "    'admin_user' => " . var_export($user, true) . ",\n";
        $php .= "    'admin_hash' => " . var_export(password_hash($pass, PASSWORD_BCRYPT), true) . ",\n";
        $php .= "    'api_salt' => " . var_export($currentSalt, true) . ",\n";
        $php .= "    'webhook' => " . var_export($webhook, true) . ",\n";
        $php .= "    'max_body' => 4194304,\n";
        $php .= "    'session_ttl' => 43200,\n";
        $php .= "];\n";

        if (@file_put_contents($configPath, $php, LOCK_EX) === false) {
            $error = 'could not write config.php - check folder permissions';
        } else {
            @mkdir(__DIR__ . '/data', 0775, true);
            $done = true;
        }
    }
}

header('Content-Type: text/html; charset=utf-8');
?>
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Rose panel setup</title>
<style>
body{background:#0c0a0e;color:#ece8f1;font:14px/1.45 "Segoe UI",system-ui,sans-serif;margin:0}
.box{max-width:420px;margin:10vh auto;background:#16121b;border:1px solid #282130;border-radius:12px;padding:26px}
h1{font-size:18px;margin:0 0 6px}
p.sub{color:#928a9c;font-size:12px;margin:0 0 14px}
label{display:block;font-size:11px;color:#928a9c;text-transform:uppercase;letter-spacing:.5px;margin:12px 0 6px}
input{width:100%;padding:9px 11px;border-radius:8px;border:1px solid #282130;background:#1c1722;color:#ece8f1;font:inherit;box-sizing:border-box}
button{width:100%;margin-top:18px;padding:10px;border:0;border-radius:8px;background:#e85a96;color:#fff;font-weight:600;font-size:13px;cursor:pointer}
.msg{padding:10px 14px;border-radius:8px;font-size:13px;margin-bottom:12px;background:#1c1722;border:1px solid #282130}
.msg.bad{border-color:#4a1f28;color:#ec4a60}
.msg.ok{border-color:#1f4a33;color:#5ae094}
code{font-family:Consolas,monospace;font-size:12px;color:#e85a96}
</style>
</head>
<body>
<div class="box">
<h1>&#9670; Panel setup</h1>
<p class="sub">Writes config.php. Reload this file once the panel is live - it locks itself.</p>

<?php if ($error !== ''): ?>
  <div class="msg bad"><?php echo htmlspecialchars($error, ENT_QUOTES); ?></div>
<?php endif; ?>

<?php if ($done): ?>
  <div class="msg ok">config.php written. <strong>Delete setup.php now.</strong></div>
  <p class="sub"><a href="login.php" style="color:#e85a96">open the panel</a></p>
<?php elseif ($alreadySet && !$force): ?>
  <div class="msg ok">Already configured. Add <code>?force=1</code> to overwrite the admin password.</div>
<?php else: ?>
<form method="post" action="setup.php<?php echo $alreadySet ? '?force=1' : ''; ?>">
  <label for="user">Admin username</label>
  <input id="user" name="user" value="admin" autocomplete="username">
  <label for="pass">Password (10+ characters)</label>
  <input id="pass" name="pass" type="password" autocomplete="new-password">
  <label for="pass2">Repeat password</label>
  <input id="pass2" name="pass2" type="password" autocomplete="new-password">
  <label for="webhook">Discord webhook (optional)</label>
  <input id="webhook" name="webhook" value="<?php echo htmlspecialchars($formWebhook, ENT_QUOTES); ?>" placeholder="https://discord.com/api/webhooks/...">
  <button type="submit">Save settings</button>
</form>
<?php endif; ?>
</div>
</body>
</html>
