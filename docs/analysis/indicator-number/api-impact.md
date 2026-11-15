# اثر روی API

مسیرها همان‌طور که پیاده‌سازی شده‌اند (2026-11-15).

## جدید
| متد و مسیر | توضیح | مجوز |
|---|---|---|
| `POST /api/v1/documents/{id}/indicator` | ثبت در اندیکاتور و برگرداندن `{ number }` | `DOCUMENT_REGISTER_INDICATOR` روی سند |
| `GET /api/v1/admin/document-types/indicator-settings` | خواندن الگوی پیش‌فرض | `ADMIN_MANAGE_DOCUMENT_TYPES` |
| `PUT /api/v1/admin/document-types/indicator-settings` | تغییر الگوی پیش‌فرض (`{ defaultPattern }`) | `ADMIN_MANAGE_DOCUMENT_TYPES` |
| `POST /api/v1/admin/document-types/indicator-pattern/preview` | اعتبارسنجی الگو و برگرداندن شماره نمونه (`{ pattern, documentTypeId? }`) | `ADMIN_MANAGE_DOCUMENT_TYPES` |
| `GET /api/v1/admin/indicator-numbers` | تعداد اسناد بدون شماره، با اسناد حذف‌شده | `ADMIN_MANAGE_DOCUMENT_TYPES` |
| `POST /api/v1/admin/indicator-numbers/backfill` | شروع شماره‌دهی اسناد موجود در پس‌زمینه (پاسخ 202 با `{ unnumbered }`) | `ADMIN_MANAGE_DOCUMENT_TYPES` |

پاسخ‌های خطا: `409 document.indicator_already_registered`، `409 document.indicator_duplicate`، `400 indicator_pattern.invalid`، `400 indicator_pattern.default_needs_type`، `409 indicator_pattern.collides` (همراه نام دو نوع سند)، `403`.

## تغییر در موارد موجود
- **جزئیات سند:** `indicatorNumber`، `indicatorRegisteredAt` و `indicatorRegisteredBy` (null یعنی سیستم شماره داده). `DOCUMENT_REGISTER_INDICATOR` در `allowedActions` می‌آید وقتی سند شماره ندارد و کاربر مجوز دارد، حتی در سال مالی بسته.
- **آیتم‌های فهرست و سطل بازیافت:** `indicatorNumber`.
- **`GET /api/v1/documents`:** پارامتر `indicatorNumber` (تطابق دقیق، بدون حساسیت به حروف). جعبه `search` هم شماره دقیق را پیدا می‌کند.
- **تنظیمات نوع سند (`PUT` موجود):** `settings.indicatorPattern`؛ خالی یعنی الگوی پیش‌فرض.
- **نتایج جستجو:** `indicatorNumber`.
- **`IRenditionService.OpenPageAsync`:** پارامتر اختیاری `cornerText` برای درج شماره در گوشه صفحه.

همه تغییرات فقط فیلد اضافه می‌کنند و کلاینت‌های فعلی (از جمله اپ موبایل) خراب نمی‌شوند.
