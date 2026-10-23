<?php
declare(strict_types=1);

function h(?string $value): string
{
    return htmlspecialchars((string)$value, ENT_QUOTES, 'UTF-8');
}

function panel_head(string $title): void
{
    echo '<!doctype html><html lang="en"><head><meta charset="utf-8">';
    echo '<meta name="viewport" content="width=device-width,initial-scale=1">';
    echo '<title>' . h($title) . ' - Rose</title>';
    echo '<style>
:root{
  --bg:#0c0a0e; --rail:#100d13; --card:#16121b; --card2:#1c1722;
  --line:#282130; --ink:#ece8f1; --muted:#928a9c; --pink:#e85a96;
  --pinkdim:#7a2a4a; --ok:#5ae094; --bad:#ec4a60; --warn:#e8c15a;
}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--ink);
  font:14px/1.45 "Segoe UI",system-ui,-apple-system,sans-serif}
a{color:var(--pink);text-decoration:none}
a:hover{text-decoration:underline}
.shell{display:flex;min-height:100vh}
.rail{width:196px;flex:0 0 196px;background:var(--rail);border-right:1px solid var(--line);
  padding:18px 0;position:sticky;top:0;height:100vh}
.rail .mark{font-weight:700;font-size:16px;padding:0 20px 18px;letter-spacing:.3px}
.rail a{display:block;color:var(--muted);padding:9px 20px;font-size:13px}
.rail a:hover{color:var(--ink);text-decoration:none;background:#181320}
.rail a.on{color:var(--ink);background:var(--pinkdim)}
.rail .foot{position:absolute;bottom:16px;left:0;right:0;padding:0 20px;
  color:var(--muted);font-size:11px}
.main{flex:1;min-width:0;padding:22px 26px 60px}
.head{display:flex;align-items:baseline;gap:12px;margin-bottom:18px;flex-wrap:wrap}
.head h1{font-size:19px;margin:0;font-weight:600}
.head .sub{color:var(--muted);font-size:12px}
.stats{display:grid;grid-template-columns:repeat(auto-fit,minmax(160px,1fr));gap:12px;margin-bottom:20px}
.stat{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:14px 16px}
.stat .label{color:var(--muted);font-size:11px;text-transform:uppercase;letter-spacing:.6px}
.stat .value{font-size:24px;font-weight:600;margin-top:6px}
.card{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:16px 18px;margin-bottom:16px}
.card h2{font-size:13px;margin:0 0 12px;font-weight:600;letter-spacing:.2px}
table{width:100%;border-collapse:collapse;font-size:13px}
th,td{text-align:left;padding:9px 10px;border-bottom:1px solid var(--line);vertical-align:top}
th{color:var(--muted);font-weight:500;font-size:11px;text-transform:uppercase;letter-spacing:.5px}
tr:last-child td{border-bottom:0}
tr:hover td{background:#191420}
.mono{font-family:Consolas,"Cascadia Mono",monospace;font-size:12px}
.pill{display:inline-block;padding:2px 8px;border-radius:999px;font-size:11px;
  border:1px solid var(--line);background:var(--card2);color:var(--muted)}
.pill.ok{color:var(--ok);border-color:#1f4a33}
.pill.bad{color:var(--bad);border-color:#4a1f28}
.pill.warn{color:var(--warn);border-color:#4a3d1f}
.btn{display:inline-block;padding:9px 16px;border-radius:8px;border:0;cursor:pointer;
  background:var(--pink);color:#fff;font-weight:600;font-size:13px;font-family:inherit}
.btn:hover{background:#f06aa4;text-decoration:none}
.btn.ghost{background:var(--card2);border:1px solid var(--line);color:var(--ink)}
.btn.ghost:hover{background:#241d2e}
.btn.small{padding:5px 10px;font-size:12px}
input[type=text],input[type=password],input[type=date],select,textarea{
  width:100%;padding:9px 11px;border-radius:8px;border:1px solid var(--line);
  background:var(--card2);color:var(--ink);font:inherit;font-family:inherit}
input:focus,select:focus,textarea:focus{outline:none;border-color:var(--pinkdim)}
label{display:block;font-size:11px;color:var(--muted);text-transform:uppercase;
  letter-spacing:.5px;margin:12px 0 6px}
.row{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:14px}
.msg{padding:10px 14px;border-radius:8px;margin-bottom:14px;font-size:13px;
  border:1px solid var(--line);background:var(--card2)}
.msg.ok{border-color:#1f4a33;color:var(--ok)}
.msg.bad{border-color:#4a1f28;color:var(--bad)}
.empty{color:var(--muted);font-size:13px;padding:14px 2px}
.split{display:grid;grid-template-columns:1fr;gap:14px}
.kv{display:grid;grid-template-columns:150px 1fr;gap:6px 12px;font-size:13px}
.kv .k{color:var(--muted)}
.copy{background:none;border:0;color:var(--muted);cursor:pointer;font-size:11px;padding:0 0 0 6px}
.copy:hover{color:var(--pink)}
.scroll{max-height:420px;overflow:auto}
.login{max-width:360px;margin:12vh auto;background:var(--card);border:1px solid var(--line);
  border-radius:12px;padding:26px}
.login h1{margin:0 0 18px;font-size:18px;font-weight:600}
.login .btn{width:100%;margin-top:18px}
@media (max-width:820px){
  .shell{display:block}
  .rail{width:auto;height:auto;position:static;display:flex;flex-wrap:wrap;gap:2px;padding:12px}
  .rail .mark{width:100%;padding:0 8px 10px}
  .rail a{padding:7px 12px;border-radius:8px}
  .rail .foot{display:none}
  .main{padding:16px 14px 40px}
  .kv{grid-template-columns:120px 1fr}
}
</style></head><body>';
}

function panel_nav(string $active): void
{
    $items = [
        'captures' => 'Captures',
        'builds' => 'Builds',
        'licenses' => 'Licenses',
        'settings' => 'Settings',
    ];

    echo '<div class="shell"><aside class="rail"><div class="mark">&#9670; Rose</div>';
    echo '<a href="index.php?p=captures" class="' . ($active === 'captures' ? 'on' : '') . '">Captures</a>';
    echo '<a href="index.php?p=builds" class="' . ($active === 'builds' ? 'on' : '') . '">Builds</a>';
    echo '<a href="index.php?p=licenses" class="' . ($active === 'licenses' ? 'on' : '') . '">Licenses</a>';
    echo '<a href="index.php?p=settings" class="' . ($active === 'settings' ? 'on' : '') . '">Settings</a>';
    echo '<a href="logout.php">Sign out</a>';
    echo '<div class="foot">v1.0.0 &middot; Rose Software</div></aside><main class="main">';
}

function panel_foot(string $active = 'captures'): void
{
    echo '</main></div>';
    echo '<script>
document.querySelectorAll("[data-copy]").forEach(function (button) {
  button.addEventListener("click", function () {
    var text = button.getAttribute("data-copy");
    navigator.clipboard.writeText(text).then(function () {
      button.textContent = "copied";
      setTimeout(function () { button.textContent = "copy"; }, 1200);
    });
  });
});
document.querySelectorAll("form[data-confirm]").forEach(function (form) {
  form.addEventListener("submit", function (event) {
    if (!confirm(form.getAttribute("data-confirm"))) { event.preventDefault(); }
  });
});
</script></body></html>';
}

function flash_take(): array
{
    panel_session();
    $msg = $_SESSION['flash'] ?? null;
    unset($_SESSION['flash']);
    return is_array($msg) ? $msg : [];
}

function flash_set(string $kind, string $text): void
{
    panel_session();
    $_SESSION['flash'] = ['kind' => $kind, 'text' => $text];
}

function render_flash(): void
{
    $msg = flash_take();
    if ($msg) {
        echo '<div class="msg ' . h($msg['kind']) . '">' . h($msg['text']) . '</div>';
    }
}
