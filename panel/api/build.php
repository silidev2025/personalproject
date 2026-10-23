<?php
declare(strict_types=1);
ini_set('display_errors', '0');

/**
 * POST /api/build.php
 *   {"event":"build","hwid":"...","name":"x","file":"x.exe","size":1234,"version":"1.0.0.0"}
 * -> {"ok":true}
 */

require __DIR__ . '/../lib/db.php';

if (($_SERVER['REQUEST_METHOD'] ?? 'GET') !== 'POST') {
    json_out(['ok' => false, 'error' => 'POST required'], 405);
}

$body = read_json_body();
$hwid = substr(trim((string)($body['hwid'] ?? '')), 0, 128);
$name = substr(trim((string)($body['name'] ?? '')), 0, 128);
$file = substr(trim((string)($body['file'] ?? '')), 0, 160);
$version = substr(trim((string)($body['version'] ?? '')), 0, 32);
$size = (int)($body['size'] ?? 0);

if ($hwid === '' || $file === '') {
    json_out(['ok' => false, 'error' => 'missing hwid or file'], 400);
}

$stmt = db()->prepare(
    'INSERT INTO builds (hwid, name, file, size, version, at) VALUES (?, ?, ?, ?, ?, ?)'
);
$stmt->execute([$hwid, $name, $file, max(0, $size), $version, now_iso()]);

json_out(['ok' => true]);
