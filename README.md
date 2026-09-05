# SnapCRM — سرویسِ CRM و مارکتینگ‌اتومیشن (فاز ۱)

سرویسِ **کاملاً جدا و ایزوله** برای CRM مشتری‌ها و ارسالِ ایمیلِ حرفه‌ای. به سیستم‌های
زندهٔ فعلی (food-server / transport-server / اپ‌ها) **هیچ دستی نمی‌زنه**.

> فاز ۱ = CRM + ایمیلِ مشتری‌ها · Agent نیمه‌خودکار · سرویسِ تخصصیِ ایمیل (Brevo).

---

## اصولِ امنیت («هیچی خراب نشه»)

1. **دیتابیسِ جدا**: SnapCRM دیتابیسِ خودش (`SnapCrm_Db`) را دارد. هیچ‌وقت روی
   `FoodOrder_Db` نمی‌نویسه.
2. **خواندنِ فقط‌خواندنی**: داده‌های مشتری/سفارش فقط با یک لاگینِ **db_datareader** (یا
   replica) خونده می‌شن — فقط `SELECT`.
3. **Kill-switch**: `Crm:SendingEnabled=false` یعنی هیچ ایمیلی ارسال نمی‌شه. پیش‌فرض خاموشه.
4. **صفِ تأیید (نیمه‌خودکار)**: هر کمپینِ جدید تا **تأییدِ انسان** ارسال نمی‌شه. فقط
   کمپین‌های روتینِ از‌قبل‌تأییدشده خودکار می‌رن.
5. **Rate limit + ساعتِ مجاز**: سقفِ روزانه، اندازهٔ batch، و بازهٔ ساعتِ ارسال.
6. **رضایت (Consent) + لغوِ اشتراک**: فقط به مشتریِ opt-in ایمیل می‌ره؛ هر ایمیل لینکِ
   امنِ Abmelden داره؛ bounce/spam خودکار suppress می‌شه (GDPR).

---

## معماری

```
food DB ──(read-only SELECT)──▶ SyncService ──▶ SnapCrm_Db ──▶ Segmentation
                                                      │
                                Planner(Agent) ──▶ Approval Queue ──(تأییدِ تو)──▶ Dispatcher ──▶ Brevo ──▶ 📧
                                                      ▲                                              │
                                                Brevo Webhook ◀──(open/click/bounce/unsub)───────────┘
```

---

## راه‌اندازی (قدم‌به‌قدم)

### ۱) دیتابیسِ CRM + لاگینِ فقط‌خواندنی (روی همون SQL Server)
```sql
-- دیتابیسِ مستقلِ CRM
CREATE DATABASE SnapCrm_Db;

-- لاگینِ فقط‌خواندنی برای food DB (SnapCRM فقط با این می‌خونه)
CREATE LOGIN snapcrm_ro WITH PASSWORD = 'یک‌رمزِ‌قوی';
USE FoodOrder_Db;
CREATE USER snapcrm_ro FOR LOGIN snapcrm_ro;
ALTER ROLE db_datareader ADD MEMBER snapcrm_ro;   -- فقط خواندن، بدونِ نوشتن

-- لاگینِ خواندن/نوشتن فقط روی دیتابیسِ CRM
USE SnapCrm_Db;
CREATE USER snapcrm_ro FOR LOGIN snapcrm_ro;
ALTER ROLE db_owner ADD MEMBER snapcrm_ro;
```

### ۲) اکانتِ Brevo (سرویسِ ایمیل)
- در brevo.com ثبت‌نام کن → **SMTP & API → API Keys** → کلید بساز.
- **احرازِ دامنه** snap-food.eu: در Brevo رکوردهای **SPF / DKIM / DMARC** را می‌ده؛ در DNS
  اضافه کن تا ایمیل‌ها به Spam نرن.
- **Webhook**: در Brevo یک webhook به `https://crm.snap-food.eu/api/webhooks/brevo?secret=XXX`
  بساز (رویدادهای opened/clicked/hard_bounce/unsubscribed).

### ۳) فایلِ `.env` (از روی `.env.example`)
مقادیر را پر کن (کلیدها را هیچ‌وقت commit نکن).

### ۴) اجرا
```bash
docker compose build
docker compose up -d
curl http://localhost:8090/health     # باید sendingEnabled:false بده
```

### ۵) تستِ امن قبل از ارسالِ واقعی
1. `Crm:SendingEnabled` را **false** نگه دار.
2. سینک را بذار اجرا شه، `GET /api/customers/stats` را ببین.
3. مشتری‌های opt-in را مشخص کن (فعلاً هیچ‌کس mailable نیست تا رضایت ثبت نشه).
4. یک کمپینِ تستی به یک لیستِ کوچک (ایمیلِ خودت) بساز و **Approve** کن.
5. تازه بعدش `SendingEnabled=true` را روشن کن.

---

## API (خلاصه)

| متد | مسیر | کار |
|---|---|---|
| GET | `/health` | وضعیت + kill-switch |
| GET | `/api/customers/stats` | تعدادِ کل/mailable/dormant/vip |
| GET | `/api/customers` | لیستِ مشتری‌ها (جستجو/صفحه) |
| GET | `/api/segments/builtin` | segmentهای آماده |
| POST | `/api/segments` | ساختِ segment |
| GET | `/api/segments/{id}/count` | تعدادِ مخاطبِ segment |
| GET/POST | `/api/campaigns` | لیست/ساختِ کمپین |
| POST | `/api/campaigns/{id}/build-recipients` | فریزِ مخاطب‌ها |
| POST | `/api/campaigns/{id}/submit` | ارسال به صفِ تأیید |
| POST | `/api/campaigns/{id}/send-batch` | ارسالِ دستیِ یک batch (اگر Approved) |
| GET | `/api/approvals/pending` | صفِ تأیید |
| POST | `/api/approvals/{id}/approve` | تأیید |
| POST | `/api/approvals/{id}/reject` | رد |
| GET | `/unsubscribe?t=` | لغوِ اشتراکِ عمومی (در ایمیل‌ها) |
| POST | `/api/webhooks/brevo` | رویدادهای ESP |
| — | `/jobs` | داشبوردِ Hangfire (پشتِ auth بذار) |

---

## نکاتِ مهمِ فاز ۱

- **Consent هنوز خالیه**: تا رضایتِ مشتری‌ها ثبت نشه، هیچ‌کس mailable نیست (عمداً، برای
  امنیتِ قانونی). قدمِ بعد: از کجا opt-in می‌گیریم (چک‌باکسِ ثبت‌نام / کمپینِ double-opt-in).
- **HTMLِ ایمیل** placeholderِ `{{unsubscribe_url}}` را باید داشته باشه (خودکار پر می‌شه).
- **تولیدِ متن با AI**: در `PlannerService.BuildBody` جای اتصالِ مدلِ زبانی آماده‌ست.
- **schema سینک**: کوئریِ read-only در `FoodDbSyncSource` بر اساسِ جدول‌های Users/Orders نوشته
  شده؛ اگر نامِ ستون‌ها فرق داشت فقط همون SELECT را تنظیم کن.

---

## نقشهٔ راه بعد از فاز ۱
- فاز ۲: پوشِ FCM + داشبوردِ Analytics (Firebase/GA) + اتوماسیونِ چرخهٔ عمر.
- فاز ۳: B2B (رستوران/شاپ) + سوشال‌مدیا + Google Ads.
- فاز ۴: پنلِ Next.js + Agentِ کامل‌تر (A/B، بهینه‌سازیِ خودکار).
