<?php
declare(strict_types=1);
session_start();

// ================== تنظیمات ==================
// حتماً این رمز رو قبل از آپلود روی هاست عوض کنید!
$ADMIN_PASSWORD = 'Mra2321364!@';
// فایل connections.json باید کنار همین فایل PHP روی هاست باشد
$DATA_FILE = __DIR__ . '/connections.json';

$validTypes = ['openvpn', 'l2tp', 'pptp', 'sstp'];
$validProtos = ['udp', 'tcp'];

// ================== ورود / خروج ==================
$loginError = '';
if ($_SERVER['REQUEST_METHOD'] === 'POST' && isset($_POST['login_password'])) {
    if (hash_equals($ADMIN_PASSWORD, (string)$_POST['login_password'])) {
        $_SESSION['authed'] = true;
        header('Location: ' . basename(__FILE__));
        exit;
    } else {
        $loginError = 'رمز عبور اشتباه است.';
    }
}
if (isset($_GET['logout'])) {
    $_SESSION = [];
    session_destroy();
    header('Location: ' . basename(__FILE__));
    exit;
}
$isAuthed = !empty($_SESSION['authed']);

// ================== توابع کمکی ==================
// برخلاف مخالفت قبلی PHP به حروف بزرگ/کوچک حساس است، اما فایل واقعی روی هاست یک بار دستی دستکاری شده بود
// و همزمان هم کلید کوچک ("packageVersion") و هم کلید بزرگ ("PackageVersion") داشت — این تابع هردو رو پیدا میکنه
// بدون توجه به حروف کوچک/بزرگ، مقدار رو برمی‌گردونه و کلیدهای تکراری رو حذف می‌کنه.
function pluckCaseInsensitive(array &$data, string $canonicalKey, $default)
{
    $value = $default;
    $found = false;
    foreach (array_keys($data) as $k) {
        if (strcasecmp($k, $canonicalKey) === 0) {
            if (!$found) {
                $value = $data[$k];
                $found = true;
            }
            unset($data[$k]);
        }
    }
    return $value;
}

function loadPackage(string $path): array
{
    $raw = file_exists($path) ? (string)file_get_contents($path) : '';
    $data = json_decode($raw, true);
    if (!is_array($data)) {
        $data = [];
    }

    $packageVersion = pluckCaseInsensitive($data, 'PackageVersion', 0);
    $name = pluckCaseInsensitive($data, 'Name', 'NETFASTVIP');
    $connections = pluckCaseInsensitive($data, 'Connections', []);
    $baseOvpn = pluckCaseInsensitive($data, 'BaseOvpn', '');
    $ca = pluckCaseInsensitive($data, 'Ca', '');

    return [
        'PackageVersion' => (int)$packageVersion,
        'Name' => (string)$name,
        'Connections' => is_array($connections) ? array_values($connections) : [],
        'BaseOvpn' => (string)$baseOvpn,
        'Ca' => (string)$ca,
        '__rest' => $data, // بقیه‌ی فیلدهای اضافی (Theme, DnsMode, SmartSwitch, ...) دست‌نخورده حفظ می‌شن
    ];
}

// $forcedVersion: اگر عدد مشخص بده بشه، دقیقاً همون عدد ذخیره می‌شود (ورودی دستی)، ورنه یکی بهش اضافه می‌شود (حالت افزودن/ویرایش/حذف کانکشن)
function savePackage(string $path, array $pkg, ?int $forcedVersion = null): void
{
    $version = $forcedVersion !== null ? $forcedVersion : ((int)$pkg['PackageVersion'] + 1);
    $out = [
        'PackageVersion' => $version,
        'Name' => $pkg['Name'],
        'Connections' => array_values($pkg['Connections']),
        'BaseOvpn' => $pkg['BaseOvpn'],
        'Ca' => $pkg['Ca'],
    ];
    foreach ($pkg['__rest'] ?? [] as $k => $v) {
        if (!array_key_exists($k, $out)) {
            $out[$k] = $v;
        }
    }
    $json = json_encode($out, JSON_PRETTY_PRINT | JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
    file_put_contents($path, $json);
}

$message = '';
$error = '';
$pkg = ['PackageVersion' => 0, 'Name' => 'NETFASTVIP', 'Connections' => [], 'BaseOvpn' => '', 'Ca' => ''];
$editEntry = null;
$editIndexForForm = null;

if ($isAuthed) {
    $pkg = loadPackage($DATA_FILE);

    // ---- افزودن/ویرایش ----
    if ($_SERVER['REQUEST_METHOD'] === 'POST' && ($_POST['action'] ?? '') === 'save') {
        $name = trim((string)($_POST['Name'] ?? ''));
        $type = (string)($_POST['Type'] ?? 'openvpn');
        $server = trim((string)($_POST['Server'] ?? ''));
        $portRaw = trim((string)($_POST['Port'] ?? ''));
        $proto = (string)($_POST['Proto'] ?? 'udp');
        $psk = trim((string)($_POST['Psk'] ?? ''));
        $editIndex = (isset($_POST['edit_index']) && $_POST['edit_index'] !== '') ? (int)$_POST['edit_index'] : null;

        if ($name === '' || $server === '') {
            $error = 'نام و آدرس سرور الزامی است.';
        } elseif (!in_array($type, $validTypes, true)) {
            $error = 'نوع کانکشن نامعتبر است.';
        } elseif (!in_array($proto, $validProtos, true)) {
            $error = 'پروتکل نامعتبر است.';
        } else {
            $port = $portRaw === '' ? null : (int)$portRaw;
            $entry = [
                'Name' => $name,
                'Type' => $type,
                'Server' => $server,
                'Port' => $port,
                'Proto' => $proto,
                'Psk' => $psk,
                'Username' => '',
                'Password' => '',
                'Source' => 'official',
                'OvpnInline' => '',
            ];

            $dupIndex = null;
            foreach ($pkg['Connections'] as $i => $c) {
                if ($i !== $editIndex && strcasecmp((string)($c['Name'] ?? ''), $name) === 0) {
                    $dupIndex = $i;
                    break;
                }
            }

            if ($dupIndex !== null) {
                $error = 'کانکشنی با همین نام از قبل وجود دارد.';
            } else {
                if ($editIndex !== null && isset($pkg['Connections'][$editIndex])) {
                    $pkg['Connections'][$editIndex] = $entry;
                    $message = 'کانکشن «' . $name . '» ویرایش شد.';
                } else {
                    $pkg['Connections'][] = $entry;
                    $message = 'کانکشن «' . $name . '» اضافه شد.';
                }
                $pkg['Connections'] = array_values($pkg['Connections']);
                savePackage($DATA_FILE, $pkg);
                $pkg = loadPackage($DATA_FILE);
            }
        }
    }

    // ---- حذف ----
    if ($_SERVER['REQUEST_METHOD'] === 'POST' && ($_POST['action'] ?? '') === 'delete') {
        $delIndex = (int)($_POST['delete_index'] ?? -1);
        if (isset($pkg['Connections'][$delIndex])) {
            $deletedName = (string)($pkg['Connections'][$delIndex]['Name'] ?? '');
            array_splice($pkg['Connections'], $delIndex, 1);
            savePackage($DATA_FILE, $pkg);
            $pkg = loadPackage($DATA_FILE);
            $message = 'کانکشن «' . $deletedName . '» حذف شد.';
        }
    }

    // ---- تنظیم دستی شماره نسخه (PackageVersion) ----
    if ($_SERVER['REQUEST_METHOD'] === 'POST' && ($_POST['action'] ?? '') === 'set_version') {
        $newVersion = (int)($_POST['PackageVersion'] ?? $pkg['PackageVersion']);
        if ($newVersion < 0) {
            $newVersion = 0;
        }
        savePackage($DATA_FILE, $pkg, $newVersion);
        $pkg = loadPackage($DATA_FILE);
        $message = 'شماره‌ی نسخه‌ی پکج به ' . $newVersion . ' تغییر کرد.';
    }

    // ---- آپلود فایل base.ovpn و/یا گواهی CA ----
    if ($_SERVER['REQUEST_METHOD'] === 'POST' && ($_POST['action'] ?? '') === 'save_files') {
        $changed = false;
        if (isset($_FILES['base_ovpn_file']) && $_FILES['base_ovpn_file']['error'] === UPLOAD_ERR_OK && $_FILES['base_ovpn_file']['size'] > 0) {
            $pkg['BaseOvpn'] = (string)file_get_contents($_FILES['base_ovpn_file']['tmp_name']);
            $changed = true;
        }
        if (isset($_FILES['ca_file']) && $_FILES['ca_file']['error'] === UPLOAD_ERR_OK && $_FILES['ca_file']['size'] > 0) {
            $pkg['Ca'] = base64_encode((string)file_get_contents($_FILES['ca_file']['tmp_name']));
            $changed = true;
        }
        if ($changed) {
            savePackage($DATA_FILE, $pkg);
            $pkg = loadPackage($DATA_FILE);
            $message = 'فایل(های) base.ovpn / CA به‌روزرسانی شد (نسخه‌ی پکج خودکار یکی زیاد شد).';
        } else {
            $error = 'هیچ فایلی انتخاب نشده بود.';
        }
    }

    if (isset($_GET['edit'])) {
        $idx = (int)$_GET['edit'];
        if (isset($pkg['Connections'][$idx])) {
            $editEntry = $pkg['Connections'][$idx];
            $editIndexForForm = $idx;
        }
    }
}
?>
<!DOCTYPE html>
<html lang="fa" dir="rtl">
<head>
<meta charset="UTF-8">
<meta name="robots" content="noindex, nofollow">
<title>مدیریت کانکشن‌های NETFASTVIP</title>
<style>
  body { font-family: Tahoma, Arial, sans-serif; background:#0f1115; color:#e6e6e6; margin:0; padding:24px; }
  .box { max-width: 420px; margin: 80px auto; background:#1a1d24; padding:28px; border-radius:12px; }
  .wrap { max-width: 980px; margin: 0 auto; }
  h1 { font-size: 20px; margin-bottom: 18px; }
  h2 { font-size: 16px; margin-top:0; }
  input, select { width:100%; padding:8px; margin:6px 0 14px; border-radius:6px; border:1px solid #333; background:#12141a; color:#eee; box-sizing:border-box; }
  label { font-size:13px; color:#aaa; }
  button { background:#3b82f6; color:#fff; border:none; padding:10px 18px; border-radius:6px; cursor:pointer; font-size:14px; }
  button.danger { background:#ef4444; }
  table { width:100%; border-collapse: collapse; margin-top:10px; font-size:13px;}
  th, td { padding:8px 10px; border-bottom:1px solid #2a2d34; text-align:right; }
  th { color:#9ca3af; font-weight:600; }
  .msg { background:#14532d; color:#bbf7d0; padding:10px 14px; border-radius:8px; margin-bottom:16px; }
  .err { background:#7f1d1d; color:#fecaca; padding:10px 14px; border-radius:8px; margin-bottom:16px; }
  .grid { display:grid; grid-template-columns: 1fr 1fr 1fr; gap:0 16px; }
  .card { background:#1a1d24; padding:20px; border-radius:12px; margin-bottom:24px; }
  .top { display:flex; justify-content:space-between; align-items:center; flex-wrap:wrap; gap:10px; }
  a.logout { color:#f87171; text-decoration:none; font-size:13px; }
  .ver { color:#6b7280; font-size:12px; }
</style>
</head>
<body>
<?php if (!$isAuthed): ?>
  <div class="box">
    <h1>ورود به پنل مدیریت کانکشن‌ها</h1>
    <?php if ($loginError !== ''): ?><div class="err"><?= htmlspecialchars($loginError) ?></div><?php endif; ?>
    <form method="post">
      <label>رمز عبور</label>
      <input type="password" name="login_password" autofocus>
      <button type="submit">ورود</button>
    </form>
  </div>
<?php else: ?>
  <div class="wrap">
    <div class="top">
      <h1>مدیریت کانکشن‌های NETFASTVIP <span class="ver">— نسخه فعلی پکج: <?= (int)$pkg['PackageVersion'] ?></span></h1>
      <a class="logout" href="?logout=1">خروج</a>
    </div>

    <?php if ($message !== ''): ?><div class="msg"><?= htmlspecialchars($message) ?></div><?php endif; ?>
    <?php if ($error !== ''): ?><div class="err"><?= htmlspecialchars($error) ?></div><?php endif; ?>

    <div class="card">
      <h2>شماره‌ی نسخه‌ی پکج (PackageVersion)</h2>
      <p style="color:#9ca3af;font-size:13px;margin-top:-6px;">هر وقت این عدد رو از عدد فعلی توی برنامه‌ی کاربرا بیشتر کنید، پیغام آپدیت به اونا ��شون داده می‌شود. با افزودن/ویرایش/حذف کانکشن هم این عدد خودکار یکی زیاد می‌شود.</p>
      <form method="post" style="display:flex; gap:12px; align-items:flex-end; max-width:320px;">
        <input type="hidden" name="action" value="set_version">
        <div style="flex:1;">
          <label>عدد فعلی</label>
          <input type="number" name="PackageVersion" value="<?= (int)$pkg['PackageVersion'] ?>" min="0" style="margin-bottom:0;">
        </div>
        <button type="submit" style="margin-bottom:0;">ذخیره‌ی عدد</button>
      </form>
    </div>

    <div class="card">
      <h2>فایل‌های base.ovpn و گواهی CA</h2>
      <p style="color:#9ca3af;font-size:13px;margin-top:-6px;">
        base.ovpn: <?= $pkg['BaseOvpn'] !== '' ? '✅ تنظیم شده (' . strlen($pkg['BaseOvpn']) . ' کاراکتر)' : '❌ خالی است' ?><br>
        گواهی CA (ca.crt): <?= $pkg['Ca'] !== '' ? '✅ تنظیم شده (' . strlen((string)base64_decode($pkg['Ca'])) . ' بایت)' : '❌ خالی است' ?>
      </p>
      <form method="post" enctype="multipart/form-data">
        <input type="hidden" name="action" value="save_files">
        <label>فایل base.ovpn — اختیاری (اگر انتخاب نشود دست‌نخورده می‌ماند)</label>
        <input type="file" name="base_ovpn_file" accept=".ovpn,.txt,.conf">
        <label>فایل گواهی CA — ca.crt — اختیاری (اگر انتخاب نشود دست‌نخورده می‌ماند)</label>
        <input type="file" name="ca_file" accept=".crt,.cer,.pem">
        <button type="submit">آپلود و ذخیره</button>
      </form>
    </div>

    <div class="card">
      <h2><?= $editEntry ? 'ویرایش کانکشن' : 'افزودن کانکشن جدید' ?></h2>
      <form method="post">
        <input type="hidden" name="action" value="save">
        <?php if ($editIndexForForm !== null): ?>
          <input type="hidden" name="edit_index" value="<?= (int)$editIndexForForm ?>">
        <?php endif; ?>
        <div class="grid">
          <div>
            <label>نام کانکشن (Name)</label>
            <input type="text" name="Name" required value="<?= htmlspecialchars((string)($editEntry['Name'] ?? '')) ?>">
          </div>
          <div>
            <label>نوع (Type)</label>
            <select name="Type">
              <?php foreach ($validTypes as $t): ?>
                <option value="<?= $t ?>" <?= (($editEntry['Type'] ?? 'openvpn') === $t) ? 'selected' : '' ?>><?= $t ?></option>
              <?php endforeach; ?>
            </select>
          </div>
          <div>
            <label>پروتکل (Proto)</label>
            <select name="Proto">
              <?php foreach ($validProtos as $p): ?>
                <option value="<?= $p ?>" <?= (($editEntry['Proto'] ?? 'udp') === $p) ? 'selected' : '' ?>><?= $p ?></option>
              <?php endforeach; ?>
            </select>
          </div>
          <div>
            <label>آدرس سرور (Server)</label>
            <input type="text" name="Server" required value="<?= htmlspecialchars((string)($editEntry['Server'] ?? '')) ?>">
          </div>
          <div>
            <label>پورت (Port) — اختیاری</label>
            <input type="number" name="Port" value="<?= htmlspecialchars((string)($editEntry['Port'] ?? '')) ?>">
          </div>
          <div>
            <label>Psk — فقط برای L2TP</label>
            <input type="text" name="Psk" value="<?= htmlspecialchars((string)($editEntry['Psk'] ?? '')) ?>">
          </div>
        </div>
        <button type="submit"><?= $editEntry ? 'ذخیره تغییرات' : 'افزودن کانکشن' ?></button>
        <?php if ($editEntry): ?>
          <a href="<?= basename(__FILE__) ?>" style="margin-inline-start:10px;color:#9ca3af;">انصراف از ویرایش</a>
        <?php endif; ?>
      </form>
    </div>

    <div class="card">
      <h2>لیست کانکشن‌ها (<?= count($pkg['Connections']) ?>)</h2>
      <table>
        <tr><th>نام</th><th>نوع</th><th>سرور</th><th>پورت</th><th>پروتکل</th><th></th></tr>
        <?php foreach ($pkg['Connections'] as $i => $c): ?>
          <tr>
            <td><?= htmlspecialchars((string)($c['Name'] ?? '')) ?></td>
            <td><?= htmlspecialchars((string)($c['Type'] ?? '')) ?></td>
            <td><?= htmlspecialchars((string)($c['Server'] ?? '')) ?></td>
            <td><?= htmlspecialchars((string)($c['Port'] ?? '')) ?></td>
            <td><?= htmlspecialchars((string)($c['Proto'] ?? '')) ?></td>
            <td>
              <a href="?edit=<?= (int)$i ?>" style="color:#60a5fa; text-decoration:none; margin-inline-end:10px;">ویرایش</a>
              <form method="post" style="display:inline" onsubmit="return confirm('حذف شود؟');">
                <input type="hidden" name="action" value="delete">
                <input type="hidden" name="delete_index" value="<?= (int)$i ?>">
                <button type="submit" class="danger" style="padding:4px 10px;font-size:12px;">حذف</button>
              </form>
            </td>
          </tr>
        <?php endforeach; ?>
      </table>
    </div>
  </div>
<?php endif; ?>
</body>
</html>
