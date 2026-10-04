# راهنمای API اپ مشتری (اندروید، ویندوز، مک)

این سند برای تیمی است که اپ VPN مشتری را می‌نویسد. همهٔ مسیرها زیر `https://<دامنهٔ پنل>/api/app` هستند. بدنه‌ها JSON است و متن خطاها فارسی است و می‌شود مستقیم به کاربر نشانش داد.

## ۱. نمای کلی

- دو نوع ورود داریم:
  - **مشتری** (`mode: "customer"`): با شماره موبایل یا ایمیل و رمز، یا با کد یک‌بارمصرف. همهٔ سرویس‌های مشتری را می‌بیند.
  - **اکانت RADIUS** (`mode: "radius"`): با نام کاربری و رمز همان سرویس. فقط همان یک سرویس را می‌بیند. اگر سرویس هیبرید باشد، همهٔ پروتکل‌هایش (RADIUS، WireGuard و V2Ray) را می‌گیرد.
- هر ورود یک «دستگاه» ثبت می‌کند. تعداد دستگاه‌ها سقف دارد (پیش‌فرض ۳، و پنل می‌تواند برای هر مشتری یا اکانت سقف جدا بگذارد).
- دو توکن داریم:
  - **access token** کوتاه‌عمر است (حدود یک ساعت؛ مقدار دقیقش در `accessExpiresIn` به ثانیه می‌آید). روی هر درخواست این هدر را بفرستید: `Authorization: Bearer <accessToken>`.
  - **refresh token** ۹۰ روز اعتبار دارد و **با هر بار استفاده عوض می‌شود**. همیشه آخرین مقدار را در جای امن ذخیره کنید: Keystore در اندروید، Credential Manager در ویندوز، Keychain در مک.
- مدیر پنل می‌تواند کل اپ را خاموش کند. در این حالت همهٔ مسیرها `403` با کد `APP_DISABLED` برمی‌گردانند. با این خطا کاربر را خارج نکنید و توکن‌ها را نگه دارید: وقتی اپ دوباره روشن شود، با همان توکن‌ها کار ادامه پیدا می‌کند.

## ۲. شکل خطا

```json
{ "error": "DEVICE_LIMIT", "message": "متن فارسی برای کاربر", "details": { } }
```

| HTTP | `error` | یعنی | اپ چه کند |
|---|---|---|---|
| 400 | `VALIDATION_ERROR` | بدنهٔ درخواست اشتباه است | باگ اپ است |
| 401 | `UNAUTHORIZED` | رمز اشتباه، access token منقضی، یا دستگاه از پنل خارج شده | یک بار refresh کنید؛ اگر refresh هم 401 داد، کاربر را به صفحهٔ ورود ببرید |
| 401 | `REFRESH_STALE` | refresh tokenی را فرستادید که همین چند ثانیه پیش عوض شده | با جدیدترین refresh token دوباره بفرستید |
| 403 | `APP_DISABLED` | اپ در این پنل خاموش است | پیام را نشان دهید و توکن‌ها را نگه دارید |
| 403 | `FORBIDDEN` | حساب مشتری در این پنل فعال نیست | پیام را نشان دهید |
| 404 | `NOT_FOUND` | سرویس یا دستگاه مال این کاربر نیست | — |
| 409 | `DEVICE_LIMIT` | سقف دستگاه پر است | بخش ۴ |
| 429 | `LOCATION_COOLDOWN` یا rate limit | زود به زود درخواست داده‌اید | پیام را نشان دهید |

## ۳. ورود

### `POST /api/app/login`

```json
{
  "mode": "customer",
  "identifier": "09121234567",
  "password": "...",
  "device": { "name": "Pixel 8", "platform": "android", "appVersion": "1.0.0" },
  "replaceDeviceId": "اختیاری - بخش ۴"
}
```

برای اکانت RADIUS، به جای `identifier` این را بفرستید: `"mode": "radius", "username": "..."`.

`device.platform` حداکثر ۱۶ نویسه است و فقط برای نمایش به کار می‌رود: `android`، `windows`، `macos`، `ios`. `device.name` را از اسم دستگاه بگیرید، چون کاربر و ادمین با همین اسم دستگاه‌ها را از هم تشخیص می‌دهند.

پاسخ `200`:

```json
{
  "accessToken": "...",
  "accessExpiresIn": 3600,
  "refreshToken": "<deviceId>.<secret>",
  "refreshExpiresAt": "2027-01-02T10:00:00.000Z",
  "device": { "id": "uuid", "name": "Pixel 8", "platform": "android", "appVersion": "1.0.0",
              "createdAt": "...", "lastSeenAt": "...", "current": true },
  "account": { "kind": "customer", "id": "uuid", "phone": "+98912...", "email": null, "fullName": "...", "name": "..." }
}
```

برای اکانت RADIUS، `account` این شکل را دارد: `{ "kind": "radius", "id", "username", "name", "serviceState", "canConnect" }`.

### ورود با کد یک‌بارمصرف (فقط مشتری)

۱. `POST /api/customer/login/code/start` با بدنهٔ `{ "phone": "0912..." }` یا `{ "email": "..." }`. اگر هر دو کانال پیامک و تلگرام فعال باشند، می‌توانید `"channel": "sms" | "telegram"` هم بفرستید.
   پاسخ: `{ verificationId, channel, target, expiresAt, resendAfterSeconds, botUrl }`. وقتی `channel` برابر `telegram` است، کاربر کد را در ربات می‌گیرد و `botUrl` لینک ربات است.
۲. `POST /api/app/login/code/confirm` با بدنهٔ `{ "verificationId", "code", "device", "replaceDeviceId?" }`. پاسخ همان پاسخ login است.

اگر ورود با `DEVICE_LIMIT` رد شود، کد مصرف نمی‌شود. بعد از اینکه کاربر یک دستگاه را برای خروج انتخاب کرد، همان کد را دوباره بفرستید.

## ۴. سقف دستگاه

اگر سقف پر باشد، پاسخ `409` با این بدنه می‌آید:

```json
{ "error": "DEVICE_LIMIT", "message": "...",
  "details": { "limit": 3, "devices": [ { "id", "name", "platform", "appVersion", "createdAt", "lastSeenAt" } ] } }
```

فهرست دستگاه‌ها را به کاربر نشان دهید تا یکی را برای خروج انتخاب کند. بعد همان درخواست ورود را با `"replaceDeviceId": "<id>"` دوباره بفرستید. خارج کردن آن دستگاه و ورود دستگاه جدید با هم و در یک مرحله انجام می‌شوند.

## ۵. تمدید نشست

### `POST /api/app/refresh`

```json
{ "refreshToken": "...", "device": { "appVersion": "1.0.1" } }
```

پاسخ همان شکل پاسخ login است، با یک access token و یک refresh token **جدید**.

- refresh token قبلی همان لحظه باطل می‌شود. اگر کسی بعداً refresh token قبلی را بفرستد، سرور فرض می‌کند توکن دزدیده شده و **آن دستگاه را خارج می‌کند**. پس:
  - توکن جدید را قبل از هر کار دیگری ذخیره کنید.
  - هیچ‌وقت دو refresh هم‌زمان نفرستید. درخواست‌های هم‌زمان را پشت یک قفل یا یک صف واحد بگذارید.
  - اگر شبکه قطع شد و پاسخ نرسید، ظرف ۳۰ ثانیه می‌توانید با همان توکن قبلی دوباره امتحان کنید. در این حالت پاسخ `REFRESH_STALE` است و نشانهٔ دزدی حساب نمی‌شود.
- هر جا access token با `401` رد شد، یک بار refresh کنید و درخواست را تکرار کنید.
- اگر کاربر رمزش را عوض کند، یا ادمین مشتری را غیرفعال کند یا دستگاه را خارج کند، refresh با `401` رد می‌شود. در این حالت کاربر را به صفحهٔ ورود ببرید.

## ۶. حساب و دستگاه‌ها (نیاز به access token)

| مسیر | کار | پاسخ |
|---|---|---|
| `GET /api/app/me` | کاربر واردشده و دستگاه‌هایش | `{ account, deviceLimit, devices: [ { ..., current } ] }` |
| `POST /api/app/logout` | خروج همین دستگاه | `{ ok: true }` |
| `DELETE /api/app/devices/:id` | خارج کردن یکی دیگر از دستگاه‌های خود کاربر | `{ ok: true }` |

توکن دستگاهی که خارج شده، از همان درخواست بعدی با `401` رد می‌شود.

## ۷. سرویس‌ها

### `GET /api/app/services`

```json
{ "services": [ {
  "id": "uuid",
  "base": "radius | wireguard | v2ray",
  "protocols": ["radius", "wireguard", "v2ray"],
  "name": "...", "displayNo": "WG-482913", "username": "...",
  "status": "active | expired | traffic_exhausted | suspended | ...",
  "canConnect": true, "online": false,
  "packageName": "...", "expireAt": "...", "firstConnectedAt": "...",
  "totalTrafficBytes": 0, "usedTrafficBytes": 0,
  "serverLabel": "...", "location": { "id": "uuid", "name": "آلمان" }
} ] }
```

- `protocols` همهٔ راه‌های اتصال این سرویس را می‌گوید. در سرویس هیبرید بیش از یکی است.
- `totalTrafficBytes` برابر `0` یا `null` یعنی حجم نامحدود. `expireAt` برابر `null` یعنی بی‌تاریخ، یا سرویسی که هنوز اولین اتصالش انجام نشده.
- اگر `canConnect` برابر `false` بود، دکمهٔ اتصال را غیرفعال کنید و `status` را به کاربر نشان دهید.

### `GET /api/app/services/:id/connection`

همهٔ چیزی را که برای اتصال لازم است برمی‌گرداند. پروتکلی که سرویس ندارد `null` است. این پاسخ رمز دارد، پس آن را کش نکنید و در لاگ ننویسید.

```json
{
  "service": { "...": "همان آیتم فهرست" },
  "radius": {
    "username": "...", "password": "...",
    "profiles": [ { "id", "label", "description", "fileName": "de.ovpn", "text": "متن کامل فایل ovpn" } ]
  },
  "wireguard": {
    "fileName": "DE-WG-482913.conf", "text": "متن کامل فایل conf",
    "subscriptionUrl": "https://.../api/sub/...",
    "locationId": "uuid",
    "locations": [ { "id", "name", "isDefault", "available" } ],
    "locationWaitSeconds": 0,
    "configPerLocation": false
  },
  "v2ray": {
    "subscriptionUrl": "https://.../api/sub/...",
    "links": ["vless://...", "vmess://..."],
    "extras": [ { "kind": "telegram | amnezia", "server", "link", "config" } ],
    "endedReason": null
  }
}
```

- **RADIUS (OpenVPN):** متن `profiles[].text` را مستقیم به OpenVPN بدهید و `username` و `password` را برای ورود به کار ببرید. هر پروفایل معمولاً یک سرور یا لوکیشن است.
- **WireGuard:** متن `text` یک فایل کامل `.conf` است. لوکیشن جزو خود فایل نیست و جای سرویس روی سرور است. برای عوض کردن آن، مسیر زیر را صدا بزنید.
- **V2Ray:** `links` همان کانفیگ‌ها هستند. `subscriptionUrl` را می‌شود به هستهٔ Xray/sing-box داد. اگر `endedReason` پر باشد، سرویس تمام شده و `links` خالی است.
- اگر یک بخش خراب باشد، مثلاً سرور V2Ray در دسترس نباشد، فقط همان بخش به این شکل می‌آید و بقیهٔ بخش‌ها سالم می‌آیند: `{ "error": { "code", "message" } }`.

### `POST /api/app/services/:id/location`

```json
{ "locationId": "uuid" }
```

پاسخ: `{ "configChanged": true, "wireguard": { ...همان بخش wireguard بالا } }`. اگر `configChanged` برابر `true` بود، فایل جدید را جای فایل قبلی بگذارید و تونل را دوباره وصل کنید.

- فقط لوکیشن‌هایی را برای انتخاب نشان دهید که `available` آن‌ها `true` است.
- بین دو جابه‌جایی فاصلهٔ زمانی لازم است. اگر زود درخواست بدهید، پاسخ `429 LOCATION_COOLDOWN` است.

## ۸. ترتیب پیشنهادی در اپ

۱. ورود، و ذخیرهٔ هر دو توکن در جای امن.
۲. گرفتن `GET /services` و نشان دادن کارت هر سرویس.
۳. با زدن «اتصال»، گرفتن `GET /services/:id/connection` و ساختن تونل با پروتکلی که کاربر انتخاب کرده یا پیش‌فرض است.
۴. یک صفحهٔ «دستگاه‌ها» با `GET /me`، و دکمهٔ خروج برای هر دستگاه.
۵. موقع باز شدن اپ: اگر access token منقضی شده، یک بار refresh کنید. اگر refresh با 401 رد شد، صفحهٔ ورود را نشان دهید.

خرید و تمدید سرویس هنوز در API اپ نیست و برای فاز بعد است. تا آن موقع، برای خرید و تمدید، کاربر را به پورتال وب بفرستید.
