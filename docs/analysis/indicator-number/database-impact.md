# اثر روی پایگاه داده

## اسکیمای documents (مایگریشن جدید)

| تغییر | جزئیات |
|---|---|
| `documents.indicator_number` | `varchar(100) NULL` |
| `documents.indicator_sequence` | `int NULL` |
| `documents.indicator_registered_at` | `timestamptz NULL` |
| `documents.indicator_registered_by` | `uuid NULL` |
| `ux_documents_indicator_number` | ایندکس یکتای جزئی روی `lower(indicator_number)` با شرط `WHERE indicator_number IS NOT NULL`. اسناد حذف‌شده را هم شامل می‌شود، پس شماره سوخت‌شده دوباره قابل استفاده نیست. |
| جدول `indicator_counters` | `document_type_id uuid`، `fiscal_year int`، `last_value int NOT NULL`، با PK روی (document_type_id, fiscal_year) |

## اسکیمای document_types (مایگریشن جدید)

| تغییر | جزئیات |
|---|---|
| `settings` (jsonb) | کلید جدید `indicatorPattern`. ستون جدیدی لازم نیست و ردیف‌های قبلی null می‌گیرند. |
| جدول `indicator_settings` | `id smallint PK CHECK (id = 1)`، `default_pattern varchar(60) NOT NULL`، `updated_at`، `updated_by`. با `{TYPE}-{YEAR}-{SEQ:5}` seed می‌شود. |

## authz
- ردیف مجوز جدید را `PermissionCatalogSeeder` خودکار اضافه می‌کند.

## داده
- Backfill همه اسناد موجود، شامل اسناد حذف‌شده و بایگانی‌شده (سؤال AQ4)، و ساخت ردیف‌های `indicator_counters`.
- **بازگشت (rollback):** مایگریشن ستون‌ها و جدول‌ها را برمی‌دارد. شماره‌های داده‌شده از بین می‌روند، پس بعد از اینکه شماره‌ها رسماً استفاده شدند، بازگشت عملاً ممکن نیست.
