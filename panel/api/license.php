<?php
declare(strict_types=1);
ini_set('display_errors', '0');

/**
 * POST /api/license.php
 *   {"key":"ROSE-....","hwid":"A1B2C3D4E5F60718"}
 * -> {"active":true,"plan":"standard","expires":"2027-01-01","message":"active"}
 *
 * A key with no hwid binds to the first device that presents it.
 */

require __DIR__ . '/../lib/db.php';

if (($_SERVER['REQUEST_METHOD'] ?? 'GET') !== 'POST') {
    json_out(['active' => false, 'message' => 'POST required'], 405);
}

$body = read_json_body();
$key = trim((string)($body['key'] ?? ''));
$hwid = trim((string)($body['hwid'] ?? ''));

if ($key === '' || $hwid === '') {
    json_out(['active' => false, 'message' => 'missing key or hwid'], 400);
}

if (strlen($hwid) > 128 || strlen($key) > 128) {
    json_out(['active' => false, 'message' => 'field too long'], 400);
}

$stmt = db()->prepare('SELECT * FROM licenses WHERE lic_key = ?');
$stmt->execute([$key]);
$row = $stmt->fetch();

if (!$row) {
    json_out(['active' => false, 'plan' => '-', 'expires' => '-', 'message' => 'invalid key'], 200);
}

if ((int)$row['active'] !== 1) {
    json_out(['active' => false, 'plan' => (string)$row['plan'], 'expires' => '-', 'message' => 'key disabled']);
}

if (!empty($row['expires']) && strtotime((string)$row['expires']) !== false
    && strtotime((string)$row['expires']) < strtotime('today')) {
    json_out(['active' => false, 'plan' => (string)$row['plan'], 'expires' => (string)$row['expires'], 'message' => 'key expired']);
}

if (empty($row['hwid'])) {
    $bind = db()->prepare('UPDATE licenses SET hwid = ? WHERE id = ?');
    $bind->execute([$hwid, $row['id']]);
} elseif (!hash_equals((string)$row['hwid'], $hwid)) {
    json_out(['active' => false, 'plan' => (string)$row['plan'], 'expires' => '-', 'message' => 'bound to another device']);
}

json_out([
    'active' => true,
    'plan' => (string)$row['plan'],
    'expires' => (string)($row['expires'] ?? '-'),
    'message' => 'active',
]);
