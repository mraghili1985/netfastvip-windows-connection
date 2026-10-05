# راهنمای سیستم بیلد اختصاصی برندها (White-Label Builder)

این سیستم به شما و نمایندگان/فروشندگان پنل این امکان را می‌دهد که برای هر برند، یک نسخه کاملاً مستقل و اختصاصی از برنامه Windows VPN با نام، آیکون، مانیفست، کارت شبکه و تنظیمات اتصال اختصاصی تولید کنید.

---

## ۱. ویژگی‌های اختصاصی در هر بیلد

هنگام ساخت یک برند جدید، این بخش‌ها به صورت ۱۰۰٪ خودکار شخصی‌سازی می‌شوند:

1. **نام فایل اجرایی (`.exe`):**  
   نام فایل اجرایی از `NETFASTVIP.exe` به `[BrandName].exe` تغییر پیدا می‌کند (مثلاً `StarVPN.exe`).
2. **مانیفست ویندوز (`app.manifest`):**  
   شناسه برنامه در رجیستری و کنترل سیستم به `[BrandName].app` تبدیل می‌شود.
3. **کارت شبکه ویندوز (Wintun / TAP Adapters):**  
   هنگام اجرای برنامه، کارت شبکه ویندوز خودکار به نام همان برند نام‌گذاری می‌شود (مثلاً `StarVPN VPN`).
4. **کانکشن‌های ویندوز (IKEv2 / SSTP):**  
   پروفایل‌های فون‌بوک ویندوز با پیشوند `[BrandName]-ikev2` ایجاد می‌شوند.
5. **آیکون برنامه (`app.ico`):**  
   لوگوی اختصاصی برند در آیکون فایل EXE، پنجره‌ها و تسک‌بار جایگزین می‌شود.
6. **فایل کانفیگ (`app-config.json`):**  
   آدرس پنل، کانال تلگرام، پشتیبانی و عنوان پنجره‌ها برای همان برند ست می‌شود.
7. **لانچر اختصاصی (`[BrandName].vbs`):**  
   فایل اجرای سایلنت و بدون پنجره سیاه با نام همان برند ایجاد می‌شود.

---

## ۲. روش اول: بیلد از طریق گیت‌هاب (GitHub Actions)

نیازی به نصب هیچ ابزاری روی کامپیوتر یا سرور خود ندارید. سرورهای ابری گیت‌هاب بیلد را انجام می‌دهند.

### اجرای دستی از سایت گیت‌هاب:
1. وارد مخزن گیت‌هاب خود شوید.
2. به تب **Actions** بروید.
3. از ستون سمت چپ، ورک‌فلو **`Build Custom Brand VPN Client`** را انتخاب کنید.
4. روی دکمه **Run workflow** کلیک کنید.
5. فرم ورودی‌ها را پر کنید:
   * **Brand Name:** نام برند (فقط حروف انگلیسی و عدد، مثلاً `StarVPN`)
   * **Portal API URL:** آدرس API پنل فروشنده (مثلاً `https://panel.starvpn.com/`)
   * **Telegram Channel URL:** لینک کانال تلگرام برند
   * **Telegram Support URL:** لینک پشتیبانی تلگرام
   * **Custom .ico URL:** (اختیاری) لینک مستقیم دانلود فایل لوگو با فرمت `.ico`
   * **Version:** نسخه برنامه (پیش‌فرض: `3.0.2`)
6. روی دکمه سبز رنگ **Run workflow** کلیک کنید.
7. پس از حدود ۲ الی ۳ دقیقه، بیلد سبز شده و فایل زیپ آماده در بخش **Artifacts** قابل دانلود است:
   `StarVPN-Windows-v3.0.2.zip`

---

## ۳. روش دوم: اجرای اتوماتیک از درون پنل کاربری (API / Webhook)

اگر می‌خواهید نمایندگان در پنل کاربری شما با زدن یک دکمه «تولید برنامه اختصاصی من»، بیلد را دریافت کنند:

### درخواست به API گیت‌هاب:
کافیست از سمت بک‌اند پنل (PHP، Node.js، Python یا cURL) این درخواست را ارسال کنید:

```bash
curl -X POST \
  -H "Accept: application/vnd.github+json" \
  -H "Authorization: Bearer <GITHUB_PERSONAL_ACCESS_TOKEN>" \
  https://api.github.com/repos/mraghili1985/netfastvip-windows-connection/actions/workflows/build-brand.yml/dispatches \
  -d '{
    "ref": "feature/white-label-builder",
    "inputs": {
      "brand_name": "StarVPN",
      "portal_url": "https://panel.starvpn.com/",
      "telegram_url": "https://t.me/starvpn",
      "support_url": "https://t.me/starvpn_support",
      "icon_url": "https://my-site.com/assets/starvpn.ico",
      "version": "3.0.2"
    }
  }'
```

*نکته:* به یک **GitHub Personal Access Token (PAT)** با دسترسی `repo` و `actions` نیاز دارید که در تنظیمات Developer Settings گیت‌هاب ساخته می‌شود.

---

## ۴. روش سوم: بیلد محلی در ویندوز خودتان با پاورشل

اگر مایل بودید خودتان در کامپیوتر شخصی برای یک برند بیلد بگیرید، بدون نیاز به گیت‌هاب:

```powershell
.\build-brand.ps1 `
  -BrandName "StarVPN" `
  -PortalUrl "https://panel.starvpn.com/" `
  -TelegramUrl "https://t.me/starvpn" `
  -OutputDir "dist"
```

خروجی در پوشه `dist\StarVPN-Windows-v3.0.2.zip` آماده خواهد بود.
