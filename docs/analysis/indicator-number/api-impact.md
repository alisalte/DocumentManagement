# اثر روی API

## جدید
| متد و مسیر | توضیح | مجوز |
|---|---|---|
| `POST /api/documents/{id}/indicator` | ثبت در اندیکاتور و برگرداندن `{ number, registeredAt, registeredBy }` | `DOCUMENT_REGISTER_INDICATOR` روی سند |
| `GET /api/document-types/indicator-settings` | خواندن الگوی پیش‌فرض | `ADMIN_MANAGE_DOCUMENT_TYPES` |
| `PUT /api/document-types/indicator-settings` | تغییر الگوی پیش‌فرض | `ADMIN_MANAGE_DOCUMENT_TYPES` |
| `POST /api/document-types/indicator-settings/backfill` | شروع شماره‌دهی اسناد موجود در پس‌زمینه و برگرداندن تعداد اسناد بدون شماره | `ADMIN_MANAGE_DOCUMENT_TYPES` |
| `POST /api/document-types/indicator-pattern/preview` | اعتبارسنجی الگو و برگرداندن شماره نمونه | `ADMIN_MANAGE_DOCUMENT_TYPES` |

پاسخ‌های خطا: `409 document.indicator_already_registered`، `409 document.indicator_duplicate`، `400 indicator_pattern.invalid`، `409 indicator_pattern.collides` (همراه نام نوع سندی که با آن تداخل دارد)، `403`.

## تغییر در موارد موجود
- **DTO سند و آیتم‌های فهرست:** فیلدهای `indicatorNumber`، `indicatorRegisteredAt` و `indicatorRegisteredBy` اضافه می‌شوند. همچنین `canRegisterIndicator` در فهرست اقدام‌های مجاز سند می‌آید.
- **`DocumentListFilter`:** پارامتر `indicatorNumber` اضافه می‌شود.
- **تنظیمات نوع سند (`PUT` موجود):** `settings.indicatorPattern` اضافه می‌شود.
- **نتایج جستجو:** فیلد `indicatorNumber` اضافه می‌شود.

همه تغییرات فقط فیلد اضافه می‌کنند و کلاینت‌های فعلی (از جمله اپ موبایل) خراب نمی‌شوند.
