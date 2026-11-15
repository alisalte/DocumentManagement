# اثر روی دامنه

## Documents
- `Document` این ویژگی‌ها را می‌گیرد: `IndicatorNumber` (string?)، `IndicatorSequence` (int?)، `IndicatorRegisteredAt` (DateTimeOffset?)، `IndicatorRegisteredBy` (UserId?، null یعنی سیستم).
- `Document.RegisterIndicator(number, sequence, by, now)`: اگر شماره از قبل داشته باشد `Error.Conflict("document.indicator_already_registered")` برمی‌گرداند. متدی برای تغییر یا پاک کردن شماره وجود ندارد (BR-4).
- `Restore` و `SoftDelete` به شماره دست نمی‌زنند (A5، BR-5).
- خطاهای جدید در `DocumentErrors`: `indicator_already_registered`، `indicator_duplicate`.

## DocumentTypes
- `DocumentTypeSettings.IndicatorPattern` (string?).
- مقدار جدید `IndicatorPattern`: پارس، اعتبارسنجی، `Format(typeCode, year, categoryCode, seq)`، و `Resolve(typeCode)` برای بررسی تکراری بودن.
- `IndicatorSettings` (تک‌ردیفی): `DefaultPattern`.

## Authorization
- `PermissionCodes.DocumentRegisterIndicator = "DOCUMENT_REGISTER_INDICATOR"` با Scope برابر `Both` و `RequiresView = true`. هم با نقش (سراسری) و هم با ACL روی دسته‌بندی قابل اعطاست. مدیر سیستم آن را خودکار می‌گیرد.

## Audit
- `AuditActions.DocumentIndicatorRegistered = "DOCUMENT_INDICATOR_REGISTERED"` با متادیتای `number` و `source` (`manual`، `import` یا `backfill`).
- `AuditActions.IndicatorPatternChanged` برای تغییر الگوی نوع سند یا الگوی پیش‌فرض، همراه مقدار قبل و بعد.
