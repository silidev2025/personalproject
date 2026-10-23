<?php
declare(strict_types=1);

/**
 * Template for config.php. setup.php writes the real file; copy this to
 * config.php by hand only if you want to pre-set values before deploying.
 *
 * Never commit config.php - it holds the admin hash and the webhook.
 */
return [
    'db' => __DIR__ . '/data/panel.sqlite',

    'admin_user' => 'admin',
    // password_hash('...', PASSWORD_BCRYPT) - setup.php fills this in
    'admin_hash' => '',

    'api_salt' => '',

    // Discord webhook for capture notifications. Empty disables it.
    'webhook' => '',

    'max_body' => 4 * 1024 * 1024,
    'session_ttl' => 43200,
];
