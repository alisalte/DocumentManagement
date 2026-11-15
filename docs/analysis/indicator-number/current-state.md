# وضعیت فعلی کد

| حوزه | آنچه هست | محل |
|---|---|---|
| سند | `Document` با `FiscalYear` (سال مالی شمسی)، `DocumentTypeId`، `CategoryId`، `WatermarkText`. هیچ شماره سندی ندارد. | `src/Modules/Documents/Dms.Documents/Domain/Document.cs` |
| نوع سند | `DocumentType.Code` الان وجود دارد: یکتا (`ux_document_types_code`)، حداکثر ۶۴ کاراکتر، حروف بزرگ و **تغییرناپذیر**. نوع پیش‌فرض با کد `GENERAL` seed می‌شود. تنظیمات نوع سند در `DocumentTypeSettings` به‌صورت jsonb ذخیره می‌شود. | `Dms.DocumentTypes/Domain/DocumentType.cs`، `DocumentTypeContracts.cs` |
| دسته‌بندی | `Category.Code` وجود دارد. | `Dms.Documents/Domain/Category.cs` |
| سال مالی | `PersianFiscalYear.Of(date)`. سال‌های بسته فقط‌خواندنی‌اند (`FiscalYearRules.RejectClosed`) و تغییرات سند در آن‌ها رد می‌شود. | `FiscalYearRules.cs` |
| سند رسمی (Record) | سند declare‌شده تغییرناپذیر است (`IRecordImmutabilityGuard`). | `RecordCommands.cs` |
| مجوزها | کاتالوگ ثابت در کد (`PermissionCodes`) که در جدول `authz.permissions` آینه می‌شود. مجوزهای `System` و `Both` خودکار به نقش مدیر داده می‌شوند. | `PermissionCatalog.cs`، `Seeders.cs` |
| لاگ فعالیت | `IAuditWriter` و ثابت‌های `AuditActions`. متن فارسی ساده در فرانت ساخته می‌شود. | `AuditContracts.cs`، `frontend/src/pages/admin/auditStrings.ts`، `auditDescribe.ts` |
| چاپ و پیش‌نمایش | تصویر هر صفحه هنگام ارائه ساخته می‌شود و واترمارک در همان لحظه با `IWatermarker` (Skia + HarfBuzz) رویش کشیده می‌شود. لینک‌های اشتراک عمومی هم از همین مسیر استفاده می‌کنند. | `Storage/.../Renderers.cs`، `Processing.cs`، `Previews.cs`، `PublicLinks.cs` |
| جستجو | OpenSearch با alias و امکان بازسازی ایندکس. سند ایندکس‌شده `document_type_code` دارد ولی شماره ندارد. فهرست اسناد (`DocumentListFilter`) در Postgres فیلتر می‌شود. | `Indexing.cs`، `OpenSearchEngine.cs`، `DocumentReadModel.cs` |
| ایجاد سند | دو مسیر: `CreateDocumentHandler` و `ImportEngine` (که تاریخ ایجاد قدیمی را از manifest می‌گیرد). | `DocumentCommands.cs:421`، `ImportEngine.cs:552` |
| مقداردهی یک‌باره | الگوی `IDataSeeder`، مثل `FiscalYearBackfillSeeder`. | `DocumentsModule.cs:194` |
| فرانت | `DocumentPage`، `BrowsePage`، `SearchPage`، `admin/DocumentTypeEditorPage`، `admin/RolesPage`. | `frontend/src/pages` |
