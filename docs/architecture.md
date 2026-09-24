# Document Archive / Document Management System — Architecture

| | |
|---|---|
| **Status** | Approved. **Phases 1–4 done; phase 5 (processing and search) implemented and awaiting approval.** Phases 6–9 not started. |
| **Date** | 2026-09-20 |
| **Decisions** | D1–D14 settled; see [§12](#12-decisions). Versioning is detailed in [ADR 0001](adr/0001-file-versioning-and-metadata-revisions.md). |
| **Stack** | C# 14 · .NET 10 (`net10.0`) · ASP.NET Core 10 · EF Core 10 · PostgreSQL · S3-compatible storage · OpenSearch |
| **Style** | Modular monolith · DDD · Clean Architecture · CQRS |
| **Open items** | See [§12 Unresolved decisions](#12-unresolved-decisions). The recommended defaults apply until they are decided. |

## Contents

0. [Inspection report](#0-inspection-report)
1. [Current architecture summary](#1-current-architecture-summary)
2. [Proposed architecture](#2-proposed-architecture)
3. [Entity / relationship map](#3-entity--relationship-map)
4. [Database table proposal](#4-database-table-proposal)
5. [Permission model](#5-permission-model)
6. [Workflow model](#6-workflow-model)
7. [Storage architecture](#7-storage-architecture)
8. [Search / OCR architecture](#8-search--ocr-architecture)
9. [Migration strategy](#9-migration-strategy)
10. [Implementation phases](#10-implementation-phases)
11. [Changes to the original specification](#11-changes-to-the-original-specification)
12. [Decisions](#12-decisions)
13. [Risks](#13-risks)

---

## 0. Inspection report

The repository was empty at review time: no files and no git repository. **This is a new build.** Nothing exists to reuse, and nothing conflicts with the target architecture.

| # | Item | Finding |
|---|---|---|
| 1 | .NET version | None. **No .NET SDK is installed on the dev machine** (`~/.dotnet` only holds caches). |
| 2 | Solution/project structure | None |
| 3 | Architecture | None |
| 4 | DbContext | None |
| 5 | Entities (User, Group, Role, Permission, Department, Audit, Workflow, Storage) | None |
| 6 | Authentication | None |
| 7 | Authorization | None |
| 8 | File/storage functionality | None |
| 9 | Background jobs | None |
| 10 | Docker setup for this project | None. Docker 29.6 is installed. |
| 11 | Frontend | None. Node 24 / npm 11 are installed. |
| 12 | Testing | None |

### Facts about the dev machine that affect the design

- **Port conflicts.** An unrelated `fleetvision-*` Docker stack is running. It occupies host ports **5432** (its own Postgres), 6379, 8080–8082, 3000–3013, 5023, 6180–6182, 8888, 9092 (Kafka) and 1935. The DMS stack must use its own ports (for example Postgres on **5433**) and must **not** reuse fleetvision's Postgres, Redis or Kafka.
- **Possibly restricted internet.** fleetvision uses `:offline` image tags and "builder" images, and a local proxy seems to be listening on port 10808. NuGet, npm and container registries may not be freely reachable (see Risks).
- **Locale.** The sample contract number `CNT-1405-001` uses a Solar Hijri year. The working assumption is Persian-language documents, an RTL UI and Jalali date display. This is pending confirmation.

---

## 1. Current architecture summary

There is no existing architecture. Every concept in the requirements must be created. The only constraints come from the environment:

- the .NET 10 SDK must be provided, either installed or through a build container;
- ports must not collide with the fleetvision stack;
- builds may need to work offline or through package mirrors.

---

## 2. Proposed architecture

### 2.1 Style and deployment

- **Modular monolith:** one deployable ASP.NET Core 10 application, split into modules that each own their data.
- **Worker mode:** the same image can also start in `worker` mode to run heavy jobs such as OCR and rendering. For small installations, one process can do everything (`Processing:RunWorkers=true`).
- **No message broker and no microservices.**

```
     Browser / mobile browser
              │ HTTPS
     ┌────────▼─────────┐
     │ gateway (nginx)  │  serves SPA, proxies /api, TLS, body-size limits, same origin
     └────────┬─────────┘
     ┌────────▼──────────────────────────┐        ┌───────────────────────────┐
     │ dms-host (ASP.NET Core 10)        │        │ dms-host --role=worker    │
     │ Identity · Authorization · Docs · │        │ same image / same modules │
     │ DocTypes · Storage · Workflow ·   │        │ jobs: scan, extract, OCR, │
     │ Sharing · Search · Audit · Notify │        │ index, renditions, GC     │
     └───┬──────────┬───────────┬────────┘        └──┬───────┬───────┬────────┘
         │          │           │                    │       │       │
   PostgreSQL 18  S3/MinIO   OpenSearch   ◄──────────┘   Apache Tika  Gotenberg
   (metadata,     (files,    (search      OCRmyPDF/Tesseract  (Office→PDF)
    ACL, jobs,     renditions,  index)    ClamAV (virus scan)
    audit)         text)
```

| Concern | Technology | Responsibility |
|---|---|---|
| Metadata, users, ACL, workflow, audit, jobs | PostgreSQL 18 | Source of truth |
| Files, renditions, extracted text | S3-compatible storage (MinIO or equivalent) | Binary content only |
| Full-text and metadata search | OpenSearch | Read model, always rebuildable |
| Async processing | Job queue in Postgres + worker role | Scanning, extraction, OCR, indexing, renditions |
| Text extraction | Apache Tika Server | Behind `ITextExtractor` |
| OCR | OCRmyPDF + Tesseract (`fas`, `eng`) | Behind `IOcrEngine` |
| Office → PDF preview | Gotenberg (LibreOffice) | Behind `IRenditionGenerator` |
| Virus scanning | ClamAV (clamd) | Behind `IMalwareScanner` |

### 2.2 Solution layout

```
DocumentManagement/
├── global.json                 # pins the .NET 10 SDK
├── Directory.Build.props       # net10.0, Nullable, TreatWarningsAsErrors, analyzers
├── Directory.Packages.props    # central package versions
├── DocumentManagement.slnx     # .slnx is the .NET 10 default
├── src/
│   ├── Dms.Host/               # composition root: endpoints, auth, OpenAPI, ProblemDetails, worker role
│   ├── Dms.Migrator/           # one-shot: applies module migrations in order + idempotent seed
│   ├── BuildingBlocks/
│   │   ├── Dms.SharedKernel/   # Entity, AggregateRoot, ValueObject, IDomainEvent, Result/Error,
│   │   │                       # typed IDs, rule-expression language
│   │   ├── Dms.Application/    # ICommand/IQuery/handlers, dispatcher, ICurrentUser,
│   │   │                       # validation + transaction pipeline
│   │   └── Dms.Infrastructure/ # shared-connection unit of work, Postgres job queue,
│   │                           # EF conventions, interceptors, ProblemDetails mapping
│   └── Modules/<Module>/
│       ├── Dms.<Module>                 # Domain/ + Application/ folders. No EF or ASP.NET references
│       ├── Dms.<Module>.Infrastructure  # DbContext, EF configs, adapters, endpoints
│       └── Dms.<Module>.Contracts       # the only project other modules may reference
├── tests/
│   ├── Dms.ArchitectureTests/
│   ├── Dms.<Module>.UnitTests/
│   ├── Dms.IntegrationTests/    # Testcontainers: Postgres, MinIO, OpenSearch
│   └── Dms.ApiTests/            # WebApplicationFactory
├── frontend/
└── deploy/
    ├── docker-compose.yml
    ├── compose.dev.yml
    ├── .env.example
    └── nginx/
```

Each module gets **3 projects, not 4**, to avoid a 40-project solution. The compiler enforces the two boundaries that matter:

- core code vs infrastructure code;
- one module vs another.

The rule that Domain must not depend on Application is enforced by architecture tests (NetArchTest).

**Dependency rules:**

- `Dms.<Module>` → SharedKernel, Dms.Application, and the `*.Contracts` of other modules.
- `Dms.<Module>.Infrastructure` → `Dms.<Module>`, Dms.Infrastructure, EF Core, and SDKs.
- A module never references another module's core or Infrastructure project, and never reads another module's DbContext or tables.
- `Dms.Host` references every Infrastructure project, because it is the composition root.

### 2.3 Modules

| Module | Owns (DB schema) | Aggregates / key concepts |
|---|---|---|
| **Identity** | `identity` | User, Group, UserGroup, UserSession. Handles login/logout. |
| **Authorization** | `authz` | Permission catalog, Role, UserRole, ResourcePermission (ACL). Contains the **one** permission evaluator and the access-scope provider. |
| **Documents** | `documents` | **Document** (with its versions), **Category**, Tag, DocumentTag |
| **DocumentTypes** | `doctypes` | **DocumentType**, DocumentTypeVersion, FieldDefinition, FieldOption, FieldRule, plus the validation engine |
| **Storage** | `storage` | **StorageObject**, Rendition, `IFileStorage`, virus-scan status |
| **Workflow** | `workflow` | **Workflow** + WorkflowVersion (steps, actions), **WorkflowInstance** + WorkflowTask |
| **Sharing** | `sharing` | **DocumentShare**, **ShareLink** |
| **Search** | `search` | Extraction/OCR records, index-state tracking, `ISearchService` (OpenSearch) |
| **Audit** | `audit` | AuditLog (append-only; not an aggregate) |
| **Notifications** | `notify` | In-app notifications; email later |

A shared **rule expression language** lives in SharedKernel. Both DocumentTypes (conditional fields and validation) and Workflow (step conditions) use it.

### 2.4 Cross-cutting decisions

**Persistence**

- One PostgreSQL database, with **one schema and one DbContext per module**. Each DbContext has its own migrations history.
- A request-scoped unit of work shares one `NpgsqlConnection` and one transaction across the module DbContexts. This keeps "create version + write audit + queue jobs" atomic.
- Modules reference each other's rows by ID only.
- A short, explicit list of integrity-critical foreign keys between modules is added in migration SQL. Example: `document_versions.storage_object_id → storage.storage_objects`.
- EF Core stays inside the Infrastructure projects, configured through `IEntityTypeConfiguration<T>` with explicit relationships, indexes and constraints.

**CQRS without a library**

- A small in-house dispatcher (`ICommandHandler<TCmd,TResult>`, `IQueryHandler<TQuery,TResult>`) plus decorators for validation, transactions and logging.
- Commands load aggregates through EF.
- Queries use `AsNoTracking` projections, or `SqlQuery` when needed. No Dapper.
- OpenSearch is only a search read model.

**Domain events and background jobs (no broker)**

- Aggregates raise domain events. Handlers run **inside the same transaction**, before commit.
- These handlers write audit entries and **queue jobs** into `infra.jobs` in that same transaction. That makes the transactional outbox pattern built in.
- Workers take jobs with `FOR UPDATE SKIP LOCKED` and retry with backoff. Handlers are idempotent.
- Recurring jobs run from a `BackgroundService` on a `PeriodicTimer`, guarded by `pg_try_advisory_lock`: share expiry, staging cleanup, SLA checks, integrity scrubbing, and audit partition creation.
- **Rejected alternatives:**
  - **Hangfire** cannot join the EF transaction, so a version could commit without its processing job.
  - **Quartz.NET** is a scheduler, not a queue.
  - **Kafka or RabbitMQ** are not needed inside one deployable application.

**API**

- ASP.NET Core 10 Minimal APIs, grouped per module under `/api/v1`.
- .NET 10 built-in validation for simple DTO checks; FluentValidation (Apache-2.0) for command rules.
- `AddProblemDetails` plus an `IExceptionHandler`. Expected failures come back as `Result<T>` values instead of exceptions.
- OpenAPI 3.1 from `Microsoft.AspNetCore.OpenApi`, with the Scalar UI in development.
- `async`/`await` and `CancellationToken` throughout.
- Optimistic concurrency uses ETag / `If-Match`, backed by the Postgres `xmin` row version.
- `Idempotency-Key` is supported on upload and create calls, because mobile clients retry.
- Configuration uses the Options pattern with environment variables. **No secrets in source**: passwords, storage credentials, connection strings, JWT keys and encryption keys all come from the environment or a secret store.

**Time and IDs**

- `TimeProvider` (built-in); there is no custom clock abstraction.
- All timestamps are `timestamptz` in UTC.
- IDs are UUIDv7 generated with `Guid.CreateVersion7()`. Aggregate roots use strongly-typed IDs.

**Licensing: libraries to avoid**

Several popular .NET libraries became commercial in 2025. They are avoided:

| Avoid | Use instead |
|---|---|
| MediatR | in-house dispatcher |
| AutoMapper | manual mapping |
| FluentAssertions v8+ | Shouldly |
| MassTransit v9 | not needed |
| ImageSharp | SkiaSharp or NetVips |

**Observability**

- OpenTelemetry traces and metrics.
- Structured JSON logs.
- `/health/live` and `/health/ready`, where the ready check covers the DB, storage and search.

**Security baseline**

- Every protected operation is authorized on the server, and client-side permissions are never trusted.
- `ForwardedHeaders` accepts only the configured known proxies, so the client IP recorded in audit logs is trustworthy.
- Upload size is limited at nginx, at Kestrel (per endpoint) and per DocumentType.
- The MIME type is detected from the file's magic bytes; the client-declared value is never trusted.
- Object keys are generated by the server, so filenames never reach storage paths.
- Public share endpoints are rate-limited with ASP.NET Core rate limiting.

---

## 3. Entity / relationship map

```
identity.User ─┬─< UserGroup >── identity.Group
               ├─< authz.UserRole >── authz.Role ─< RolePermission >── authz.Permission (catalog)
               └── manager_id → User

authz.ResourcePermission (ACL entry):
    resource(CATEGORY|DOCUMENT) × subject(USER|GROUP|ROLE) × permission × ALLOW|DENY × inherit

documents.Category ── parent_id → Category   (ltree path)
documents.Document ── category_id → Category
                   ├─ document_type_id → doctypes.DocumentType
                   ├─ current_version_id   → DocumentVersion  (latest created)
                   ├─ effective_version_id → DocumentVersion  (latest approved / no-workflow)
                   ├─< DocumentVersion ── storage_object_id → storage.StorageObject ─< Rendition
                   │        └─ document_type_version_id → doctypes.DocumentTypeVersion
                   └─< DocumentTag >── Tag

doctypes.DocumentType ─< DocumentTypeVersion ─< FieldDefinition ─< FieldOption
                     │                      └─< FieldRule (JSON expression)
                     └─ workflow_id → workflow.Workflow

workflow.Workflow ─< WorkflowVersion ─< WorkflowStep ─< WorkflowStepAction
workflow.WorkflowInstance → DocumentVersion, WorkflowVersion ─< WorkflowTask → WorkflowStep

sharing.DocumentShare → Document, DocumentVersion (required), User
sharing.ShareLink     → Document, DocumentVersion (required)

search.ContentExtraction → StorageObject (text stored as an object in S3)
search.IndexState        → DocumentVersion
audit.AuditLog           (IDs only, no FKs)
infra.Job                (queue / outbox)
```

### 3.1 Aggregates

| Aggregate root | Contains | Invariants it protects |
|---|---|---|
| **Document** | DocumentVersion (created only via `Document.AddVersion()`), tag links | Version numbers are sequential and unique. Versions never change after creation. `current_version_id` and `effective_version_id` follow the business rules. Soft-delete and restore rules. The root holds `latest_version_number` + `xmin`, so all versions never need to be loaded. The database constraint `UNIQUE(document_id, version_number)` is the backstop. |
| **Category** | none | No cycles; sibling names and codes are unique; a category can be deleted only when empty. |
| **DocumentType** | DocumentTypeVersion lifecycle (a published version is an immutable value) | At most one draft. A published schema never changes. A field code keeps its data type across all versions. |
| **Workflow** | WorkflowVersion, steps, actions (published = immutable) | At most one draft. The step graph is valid at publish time. |
| **WorkflowInstance** | WorkflowTask | Legal task transitions, step advancement, completion rules, one running instance per version. |
| **DocumentShare** | none | A share cannot be used once revoked or expired. |
| **ShareLink** | none | Rules for revoked, expired, maximum access count and password lockout. |
| **StorageObject** | none | Lifecycle STAGED → COMMITTED → (PENDING_DELETION → DELETED). The file never changes. |
| **Role**, **User**, **Group** | memberships / permission links | Uniqueness, activation state. |

**Deliberately not aggregates:**

- **Permission** is a static catalog defined in code.
- **AuditLog** is an append-only record.
- **Tag** is reference data.
- **ResourcePermission** rows are plain records; the unique constraint enforces their only invariant.

---

## 4. Database table proposal

### 4.1 Conventions

- PostgreSQL 18 with the extensions `citext`, `ltree` and `pg_trgm`.
- Primary keys are `uuid` (v7) generated by the application.
- Enum-like columns are `text` + a `CHECK` constraint, mapped with `HasConversion<string>()`. This is easier to evolve than Postgres enums.
- Every table has `created_at` / `created_by`. Mutable aggregate roots use `xmin` for optimistic concurrency.
- Foreign keys default to `ON DELETE RESTRICT`. `CASCADE` is used only for children of *draft* definitions (fields of a draft type version, steps of a draft workflow version).
- Users are never hard-deleted (only deactivated), so foreign keys to users are always safe.
- Indexes are added deliberately, not on every column. Partial indexes are used wherever normal queries filter on the live state.

### 4.2 identity

| Table | Key columns | Constraints / indexes |
|---|---|---|
| `users` | username citext, email citext, display_name, manager_id → users, external_id, auth_source, password_hash (null for SSO), is_active, **is_system_admin**, lockout fields, xmin | UQ username; UQ email (partial, not null); UQ (auth_source, external_id); IX manager_id |
| `groups` | code, name, kind (DEPARTMENT/TEAM/OTHER), external_id, is_active | UQ code |
| `user_groups` | PK (user_id, group_id), added_by, added_at | IX group_id |
| `user_sessions` | user_id, refresh_token_hash bytea, expires_at, revoked_at, ip, user_agent, replaced_by | UQ refresh_token_hash; IX (user_id) WHERE revoked_at IS NULL |

### 4.3 authz

| Table | Key columns | Constraints / indexes |
|---|---|---|
| `permissions` | **code** PK, scope (RESOURCE/SYSTEM/BOTH), description | Seeded from code; never edited by users |
| `roles` | code, name, description, is_system | UQ code |
| `role_permissions` | PK (role_id, permission_code) | Only permissions with SYSTEM/BOTH scope (see §5) |
| `user_roles` | PK (user_id, role_id), granted_by, granted_at | IX role_id |
| `resource_permissions` | resource_type, resource_id, subject_type, subject_id, permission_code → permissions, effect (ALLOW/DENY), inherit, reason, created_by/at | **UQ (resource_type, resource_id, subject_type, subject_id, permission_code)**; IX (resource_type, resource_id); IX (subject_type, subject_id); CHECK `inherit = false OR resource_type = 'CATEGORY'` |

Revoking a permission deletes the ACL row. The audit log records the full row as `PERMISSION_REVOKED`, and granting records `PERMISSION_GRANTED`.

### 4.4 documents

| Table | Key columns | Constraints / indexes |
|---|---|---|
| `categories` | parent_id → categories, name, code, description, **path ltree**, is_active, sort_order, created/updated, xmin | UQ (parent_id, code) NULLS NOT DISTINCT; UQ (parent_id, name) NULLS NOT DISTINCT; GiST (path); CHECK parent_id <> id |
| `documents` | title, description, document_type_id, category_id **NOT NULL**, owner_id, **current_version_id**, **effective_version_id**, latest_version_number int, status (ACTIVE/ARCHIVED), deleted_at, deleted_by, delete_reason, created_by/at, updated_by/at, xmin | The two version FKs are `DEFERRABLE INITIALLY DEFERRED`, so a document and its V1 can be inserted in one transaction; CHECK `(deleted_at IS NULL) = (deleted_by IS NULL)`; partial IX on category_id, document_type_id, owner_id, created_at WHERE deleted_at IS NULL; IX deleted_at WHERE NOT NULL (trash); trigram GIN on title |
| `document_versions` | document_id, version_number, storage_object_id, file_name, mime_type (detected), declared_mime_type, file_size bigint, sha256 bytea, document_type_version_id, **dynamic_data jsonb**, change_kind (INITIAL/CONTENT/METADATA/BOTH), change_description, **approval_status** (NOT_REQUIRED/DRAFT/IN_WORKFLOW/APPROVED/REJECTED/CHANGES_REQUESTED/CANCELLED), approved_at, created_by/at | **UQ (document_id, version_number)**; CHECK version_number > 0; CHECK file_size ≥ 0; CHECK octet_length(sha256) = 32; GIN (dynamic_data jsonb_path_ops). **A trigger blocks UPDATE of every column except approval_status/approved_at, and blocks DELETE outside the purge routine.** |
| `tags` | name, normalized_name (lower-cased, Persian/Arabic character and digit normalization) | UQ normalized_name |
| `document_tags` | PK (document_id, tag_id), added_by/at | IX tag_id |

Dynamic values are stored as JSONB, never as EAV. How each field type is stored:

- `DATE`: ISO `YYYY-MM-DD` (Gregorian; the UI displays Jalali).
- `DATETIME`: ISO-8601 UTC.
- `DECIMAL`: a JSON number. The API returns it as a string so JavaScript keeps full precision.
- `USER`, `GROUP` and `DOCUMENT_REFERENCE`: IDs, validated when written.
- `MULTI_SELECT`: an array of option values.

### 4.5 doctypes

| Table | Key columns | Constraints / indexes |
|---|---|---|
| `document_types` | code (never changes), name, description, default_category_id, workflow_id, workflow_mode (NONE/MANUAL/AUTO_ON_VERSION), default_permission_policy jsonb, settings jsonb (allowed extensions/MIME types, max size, external sharing allowed, OCR languages, retention), is_active, xmin | UQ code |
| `document_type_versions` | document_type_id, version_number, status (DRAFT/PUBLISHED/RETIRED), published_at/by | UQ (document_type_id, version_number); partial UQ (document_type_id) WHERE status = 'DRAFT'; a trigger makes it immutable once published |
| `field_definitions` | type_version_id (CASCADE), code, label jsonb `{fa, en}`, field_type, is_required, is_searchable, is_sortable, show_in_list, default_value jsonb, validation jsonb, settings jsonb, display_order, is_active | UQ (type_version_id, code); CHECK code ~ `^[a-z][a-z0-9_]{0,62}$` |
| `field_options` | field_definition_id (CASCADE), value, label jsonb, display_order, is_active | UQ (field_definition_id, value) |
| `field_rules` | type_version_id (CASCADE), kind (SHOW/REQUIRE/VALIDATE), **condition jsonb**, target_field_codes text[], assertion jsonb, message jsonb, display_order | IX type_version_id |

Field types:

- **Supported:** TEXT, LONG_TEXT, INTEGER, DECIMAL, BOOLEAN, DATE, DATETIME, SELECT, MULTI_SELECT, USER, GROUP, DOCUMENT_REFERENCE, URL, EMAIL, PHONE.
- **Future:** FILE, IMAGE, FORMULA, AUTONUMBER, LOOKUP.

Validation rules:

- **Kinds:** required, min/max length, min/max value, regex, date constraints, date comparison, allowed options, conditional validation.
- Regex uses `RegexOptions.NonBacktracking` plus a timeout, to prevent ReDoS.
- The server is always authoritative. The frontend runs the same rule language only for UX.

### 4.6 storage

| Table | Key columns | Constraints / indexes |
|---|---|---|
| `storage_objects` | provider, bucket, object_key, purpose (ORIGINAL/RENDITION/TEXT), original_file_name, detected_mime_type, size, sha256, status (STAGED/COMMITTED/QUARANTINED/PENDING_DELETION/DELETED), scan_status (PENDING/CLEAN/INFECTED/FAILED/SKIPPED), scanned_at, created_by/at, committed_at, deleted_at | UQ (provider, bucket, object_key); IX sha256; IX (created_at) WHERE status = 'STAGED' (cleanup). The row is kept as a tombstone after the file is deleted. |
| `renditions` | source_object_id, kind (PREVIEW_PDF/PAGE_IMAGES/THUMB_S/THUMB_L/PRINT_PDF), rendition_object_id, status, error | UQ (source_object_id, kind) |

### 4.7 workflow

| Table | Key columns | Constraints / indexes |
|---|---|---|
| `workflows` | code, name, description, is_active, xmin | UQ code |
| `workflow_versions` | workflow_id, version_number, status (DRAFT/PUBLISHED/RETIRED), published_at/by | UQ (workflow_id, version_number); partial UQ (workflow_id) WHERE status = 'DRAFT'; immutable once published |
| `workflow_steps` | workflow_version_id (CASCADE), code, name, **sequence**, assignee_type, assignee_id, assignee_field_code, completion_rule (ANY/ALL), is_required, sla_hours, allow_self_approval, **condition jsonb** | UQ (workflow_version_id, code); IX (workflow_version_id, sequence); CHECK that the assignee columns match assignee_type |
| `workflow_step_actions` | step_id (CASCADE), action (APPROVE/REJECT/RETURN/REQUEST_CHANGES/FORWARD/CANCEL), comment_required, target_step_code | UQ (step_id, action) |
| `workflow_instances` | document_id, document_version_id, workflow_version_id, status (RUNNING/APPROVED/REJECTED/CHANGES_REQUESTED/CANCELLED), current_sequence, context jsonb (evaluated conditions), started_by/at, completed_at, cancel_reason, xmin | **Partial UQ (document_version_id) WHERE status = 'RUNNING'** (one open instance per version; several versions of a document may be in flight, decision D7); IX (document_id); IX status |
| `workflow_tasks` | instance_id, step_id, assigned_user_id, assigned_group_id, assigned_role_id, status (PENDING/COMPLETED/SKIPPED/CANCELLED), due_at, completed_at/by, action, comment, forwarded_from_task_id, created_at, xmin | CHECK exactly one assignee; partial IX on each assignee column WHERE status = 'PENDING'; IX (due_at) WHERE status = 'PENDING'; IX instance_id |

### 4.8 sharing

| Table | Key columns | Constraints / indexes |
|---|---|---|
| `document_shares` | document_id, **version_id NOT NULL**, shared_by, shared_with_user_id, permissions smallint flags (VIEW=1, DOWNLOAD=2, PRINT=4), expires_at, created_at, revoked_at/by, message | CHECK (permissions & 1) = 1; partial UQ (version_id, shared_with_user_id) WHERE revoked_at IS NULL; partial IX shared_with_user_id WHERE revoked_at IS NULL; IX document_id |
| `share_links` | document_id, **version_id NOT NULL**, **token_hash bytea**, token_prefix, permissions flags, password_hash, **expires_at NOT NULL**, max_access_count, access_count, failed_attempts, locked_until, created_by/at, revoked_at/by, last_accessed_at | UQ token_hash; CHECK max_access_count > 0; CHECK access_count ≤ coalesce(max_access_count, access_count); IX document_id |

### 4.9 search, audit, notify, infra

| Table | Key columns | Constraints / indexes |
|---|---|---|
| `search.content_extractions` | **storage_object_id** (keyed by file, so metadata-only versions reuse it), method (TEXT_LAYER/OCR/NONE), status, languages, text_object_id, char_count, engine, engine_version, attempts, last_error, completed_at | UQ storage_object_id; IX status |
| `search.index_state` | document_version_id PK, document_id, index_generation, indexed_at, status, last_error | Replaces the spec's `search_documents` table; the actual search document lives in OpenSearch |
| `audit.audit_logs` | id, **occurred_at**, actor_type (USER/SHARE_LINK/SYSTEM/ANONYMOUS), user_id, share_link_id, action, **outcome (SUCCESS/DENIED/FAILED)**, entity_type, entity_id, document_id, version_id, ip_address inet, user_agent, device_info jsonb, correlation_id, metadata jsonb | **Partitioned monthly by occurred_at**; PK (id, occurred_at); IX (document_id, occurred_at DESC); IX (user_id, occurred_at DESC); IX (action, occurred_at); BRIN (occurred_at). **The app DB role has INSERT/SELECT only, plus a trigger that rejects UPDATE/DELETE.** |
| `notify.notifications` | user_id, type, payload jsonb, read_at, created_at | IX (user_id, created_at DESC) WHERE read_at IS NULL |
| `infra.jobs` | queue, type, payload jsonb, idempotency_key, status (QUEUED/RUNNING/SUCCEEDED/FAILED/DEAD), priority, run_after, attempts, max_attempts, locked_by, locked_until, last_error, created_at, completed_at | IX (queue, priority, run_after) WHERE status = 'QUEUED'; partial UQ idempotency_key WHERE status IN ('QUEUED','RUNNING') |
| `infra.idempotency_keys` | user_id, key, request_hash, response, expires_at | PK (user_id, key) |

**Audit event catalog:**

- DOCUMENT_CREATED, DOCUMENT_VIEWED, DOCUMENT_DOWNLOADED, DOCUMENT_PRINTED, DOCUMENT_UPDATED
- VERSION_CREATED, DOCUMENT_DELETED, DOCUMENT_RESTORED, DOCUMENT_PURGED
- PERMISSION_GRANTED, PERMISSION_REVOKED
- DOCUMENT_SHARED, SHARE_REVOKED, SHARE_LINK_ACCESSED, SHARE_LINK_PASSWORD_FAILED
- WORKFLOW_STARTED, WORKFLOW_APPROVED, WORKFLOW_REJECTED, WORKFLOW_RETURNED, WORKFLOW_REQUESTED_CHANGES, WORKFLOW_FORWARDED, WORKFLOW_CANCELLED, WORKFLOW_COMPLETED
- DOCUMENT_TYPE_PUBLISHED, WORKFLOW_PUBLISHED
- ACCESS_DENIED, FILE_INFECTED, INTEGRITY_CHECK_FAILED
- LOGIN, LOGIN_FAILED, LOGOUT
- USER_\*, GROUP_\*, ROLE_\* administration events

Rules for writing audit entries:

- Changes are audited inside the business transaction.
- Read access (view, download, print, share-link access) is written directly, and **fails closed**: if the audit write fails, the content is not served.

### 4.10 Database roles

| Role | Rights |
|---|---|
| `dms_owner` | DDL. Used only by the migrator. |
| `dms_app` | Runtime DML. No DDL. No UPDATE/DELETE on `audit.*`. |
| `dms_readonly` | Reporting, read-only. |

### 4.11 Concurrency

- **Creating a version.**
  - The file is uploaded and staged *before* the transaction.
  - A short transaction then increments `documents.latest_version_number` under an `xmin` check and inserts the version.
  - If two users race, the loser gets a concurrency exception and retries the number allocation **without re-uploading**.
  - `UNIQUE(document_id, version_number)` is the final guarantee.
- **Stale edits.** A client may send `baseVersionId`. If a newer version exists, the server returns `409 Conflict` ("someone created V5 since you opened V4").
- **Metadata PATCH, workflow tasks and ACL changes** use `xmin`-based optimistic concurrency, exposed to clients as ETag / `If-Match`.
- **Share-link access counting** is a single atomic statement:
  `UPDATE … SET access_count = access_count + 1 WHERE id = @id AND revoked_at IS NULL AND expires_at > now() AND (max_access_count IS NULL OR access_count < max_access_count) RETURNING …`

---

## 5. Permission model

### 5.1 Catalog

The catalog is defined in code and seeded by the migrator. `*` marks permissions added beyond the original specification.

| Scope | Permissions |
|---|---|
| **Resource** (ACL on a category or document) | DOCUMENT_VIEW, **DOCUMENT_VIEW_DRAFT\***, DOCUMENT_DOWNLOAD, DOCUMENT_PRINT, DOCUMENT_CREATE (on a category), DOCUMENT_EDIT, DOCUMENT_DELETE, DOCUMENT_RESTORE, DOCUMENT_CREATE_VERSION, DOCUMENT_SHARE, **DOCUMENT_SHARE_EXTERNAL\***, DOCUMENT_MANAGE_PERMISSION, DOCUMENT_EXPORT, WORKFLOW_VIEW, WORKFLOW_APPROVE, WORKFLOW_REJECT, WORKFLOW_RETURN, WORKFLOW_REQUEST_CHANGES |
| **Both** | AUDIT_VIEW. A role grant covers all audit logs; an ACL grant covers that document's or category's audit only. |
| **System** (roles only) | AUDIT_EXPORT, ADMIN_MANAGE_USERS, ADMIN_MANAGE_GROUPS, ADMIN_MANAGE_ROLES, ADMIN_MANAGE_DOCUMENT_TYPES, ADMIN_MANAGE_WORKFLOWS, **ADMIN_MANAGE_CATEGORIES\***, **DOCUMENT_PURGE\*** |

### 5.2 Roles vs resource permissions

- `role_permissions` grants **system** capabilities only.
- Resource access comes **only** from ACL entries (`resource_permissions`).
- A role can be the *subject* of an ACL entry. Example: "Role Auditor: ALLOW DOCUMENT_VIEW on the root category, inherit".
- Every document must have a category (`category_id NOT NULL`, with a root "Documents" category), so an entry on the root covers everything.

### 5.3 Inheritance

| Entry on category C | Applies to |
|---|---|
| `inherit = false` | C itself and the documents directly in C |
| `inherit = true` | C, all its subcategories, and all their documents |

There is no "break inheritance" option; an explicit DENY is used instead.

### 5.4 How a request is decided

For user U, permission P, document D and optional version V, the steps below run in order. The result is always explainable.

1. If U is inactive or unauthenticated, **deny**.
2. If P has system scope, allow when U is a system admin or one of U's roles grants P. Otherwise deny.
3. If D is soft-deleted, only RESTORE, PURGE and AUDIT_VIEW are evaluated. Anything else returns 404.
4. Build U's principals: `{user:U} ∪ {group:g | g ∈ groups(U), g active} ∪ {role:r | r ∈ roles(U)}`.
5. Collect the applicable entries where the subject is one of U's principals and the permission is P:
   - entries on D;
   - entries on D's category;
   - entries on ancestor categories with `inherit = true`.
6. **If any applicable entry is DENY, deny.** Deny wins at every level, over every allow, over shares, and over workflow-task grants.
7. If any applicable entry is ALLOW, allow.
8. Otherwise, check temporary grants. If one applies, allow:
   - an active share on V (not revoked, not expired) that includes P;
   - an open workflow task on V, which grants VIEW, VIEW_DRAFT and that task's action.
9. Otherwise **deny** (the default).
10. **Dependencies.**
    - Any P other than VIEW also requires VIEW to be allowed, so a DENY on VIEW blocks everything.
    - If V is not published (a draft, in workflow, or rejected), VIEW_DRAFT is also required, unless U created V or holds an open task on it.

Permissions checked against a category (for example DOCUMENT_CREATE on category C) use the same algorithm, with C as the resource.

**System administrator (recommended default, pending decision D5):**

- `is_system_admin` grants every system permission and the right to manage ACLs on any resource, so nobody can be locked out.
- Content access (VIEW/DOWNLOAD) still goes through the ACL. An admin can grant themselves access, and that grant is audited.

### 5.5 Defaults set when a document is created

The DocumentType's "default permission policy" writes **explicit ACL entries** at creation time (for example owner → VIEW/DOWNLOAD/EDIT/CREATE_VERSION). Owners get no hidden implicit rights, so every grant can be seen and audited.

### 5.6 Where authorization lives

There is one `IDocumentAuthorizer` in `Authorization.Contracts`:

```csharp
Task<AuthorizationDecision> AuthorizeAsync(UserRef user, PermissionCode permission,
                                           DocumentId document, DocumentVersionId? version,
                                           CancellationToken ct);
// AuthorizationDecision { Allowed, Reason, Source }
```

Convenience wrappers: `CanViewDocument`, `CanDownloadDocument`, `CanPrintDocument`, `CanEditDocument`, `CanCreateVersion`, `CanShareDocument`, `CanManagePermissions`, `CanActOnTask`.

- Command and query handlers call it. Endpoints never contain permission logic.
- Admins get an "effective permissions / why?" endpoint that shows the explanation.

### 5.7 Lists and search: the access scope

`IAccessScopeProvider.GetViewScope(U)` returns:

- `AllowedCategories` and `DeniedCategories`: U's view permission on categories, with inheritance already applied;
- `AllowedDocs` and `DeniedDocs`: from document-level entries, which are rare;
- `SharedVersions` and `TaskVersions`: temporary grants.

A document is visible when:

```
(category ∈ AllowedCategories ∨ doc ∈ AllowedDocs ∨ shared/task grant)
  ∧ category ∉ DeniedCategories
  ∧ doc ∉ DeniedDocs
```

This is exactly steps 4–8 of §5.4. **The same scope is applied as a SQL filter (document browser) and as an OpenSearch filter (search).**

- Permissions are **not** copied into the search index. Changing a permission needs no reindex, and a revoked permission stops working immediately.
- As defense in depth, each returned page is re-checked against Postgres.

### 5.8 Information leakage

- If U cannot VIEW a document, the API returns **404**, not 403, so it does not reveal that the document exists.
- If U can view but not download or print, it returns **403**.
- Denied attempts are audited with `outcome = DENIED`.

### 5.9 Caching

- U's principal set is cached per request, plus a short-lived cache that is invalidated when memberships or role assignments change.
- ACL entries are not cached at first.

### 5.10 Field-level security

Field-level security is not in phase 1. To make it possible later:

- field definitions have stable codes;
- the decision object can carry field-level results;
- DTO projection goes through one mapper per document type version.

---

## 6. Workflow model

### 6.1 Definition and versioning

- A `Workflow` has `WorkflowVersion`s with the lifecycle DRAFT → PUBLISHED → RETIRED.
- A published version, its steps and its actions never change; a database trigger enforces this.
- Each DocumentType has a `workflow_id` and a `workflow_mode` (NONE, MANUAL or AUTO_ON_VERSION).
- A new instance **pins** the workflow version that was published when it started. Later edits never touch existing instances.

### 6.2 Steps

- Steps are ordered by `sequence`. Steps that share a sequence number run **in parallel**.
- Each step has:
  - name, code and sequence;
  - assignee type and assignee;
  - `is_required`;
  - an SLA (`sla_hours`);
  - allowed actions (`workflow_step_actions`);
  - an optional entry condition;
  - `completion_rule` (ANY/ALL);
  - `allow_self_approval`.
- A required step that resolves to no assignee flags the instance for an admin. A non-required step is skipped.

### 6.3 Actions

| Action | Effect |
|---|---|
| APPROVE | Completes the task. When the step's completion rule (ANY/ALL) is met, the instance moves to the next sequence whose condition is true. After the last step: instance → APPROVED, version → APPROVED, `effective_version_id` moves to this version. |
| REJECT *(comment required)* | Instance → REJECTED; version → REJECTED. |
| REQUEST_CHANGES *(comment required)* | Instance → CHANGES_REQUESTED. The author creates V(n+1), which starts a new instance. |
| RETURN | Goes back to `target_step_code` (default: the previous step) in the same instance, for re-review without a content change. |
| FORWARD | Reassigns the task to another user, keeping `forwarded_from_task_id`; audited. |
| CANCEL | By the initiator or an admin. Instance → CANCELLED. |

### 6.4 Conditions

Conditions are stored as a JSON expression tree, **never code**:

```json
{ "all": [ { "field": "amount", "op": "gt", "value": 10000000000 } ] }
```

- **Operators:** `eq`, `ne`, `gt`, `gte`, `lt`, `lte`, `in`, `not_in`, `contains`, `is_empty`, `all`, `any`, `not`.
- The evaluator is a small, pure C# interpreter with depth and size limits. Nothing is compiled or executed from user input.
- Field references are checked against the document type version when the workflow is published.
- A step whose condition is false is marked SKIPPED.
- Version metadata never changes, so conditions always give the same result.

### 6.5 Assignees

| Type | Resolved |
|---|---|
| USER / GROUP / ROLE | Fixed in the definition |
| DOCUMENT_OWNER | When the step starts, from `documents.owner_id` |
| CREATOR | When the step starts, the version's creator |
| MANAGER | When the step starts, via `users.manager_id` of the version creator |
| DYNAMIC_USER_FIELD | When the step starts, from a USER-type field in the version's dynamic data |

Group and role steps:

- **ANY:** one group task; any member can act.
- **ALL:** one task per member, based on a snapshot of the membership taken when the step starts.

### 6.6 Who can act on a task

A user can act when all of these hold:

- the task is open;
- the user is an eligible assignee at the time of the action;
- there is no explicit DENY on the matching workflow permission for the resource.

The author of a version cannot approve it unless `allow_self_approval` is set.

### 6.7 Approval is tied to a version

- Approval is stored on `document_versions.approval_status`, never on the document.
- When an instance is approved, `effective_version_id` moves to that version.
- Decision D7: creating a new version **never rewrites history**. A running instance on Vn is left alone and its outcome is recorded against Vn; V(n+1) starts as `DRAFT` and gets its own instance against its own workflow version. Several instances can therefore be open on one document, so the uniqueness constraint is per version, not per document.
- Example: V3 stays APPROVED (or in review) and V4 starts as DRAFT. `effective_version_id` stays on V3 until V4 is approved, while `current_version_id` points at V4. Readers with only VIEW keep seeing V3.

### 6.8 Concurrency and SLA

- Task and instance rows use `xmin`. If two group members approve at once, the second gets 409.
- A recurring job finds pending tasks past `due_at` and sends notifications. Escalation comes later.

### 6.9 API (adapted)

- `POST /api/v1/documents/{id}/versions/{versionId}/workflow/start`
- `GET /api/v1/workflow/tasks?status=pending`
- `POST /api/v1/workflow/tasks/{id}/approve|reject|return|request-changes|forward`
- `POST /api/v1/workflow/instances/{id}/cancel`

---

## 7. Storage architecture

### 7.1 Abstraction

```csharp
public interface IFileStorage   // Storage.Contracts, no SDK types
{
    Task<StoredObjectInfo> PutAsync(ObjectLocation loc, Stream content, PutOptions opts, CancellationToken ct);
    Task<Stream> GetAsync(ObjectLocation loc, ByteRange? range, CancellationToken ct);
    Task DeleteAsync(ObjectLocation loc, CancellationToken ct);
    Task<bool> ExistsAsync(ObjectLocation loc, CancellationToken ct);
    Task<Uri> GetTemporaryAccessAsync(ObjectLocation loc, TimeSpan ttl, ContentDisposition cd, CancellationToken ct);
}
```

- The implementation is `S3FileStorage` on **AWSSDK.S3** (path-style addressing), so it works with any S3-compatible store.
- The MinIO SDK is deliberately not used, so the storage backend stays swappable.

### 7.2 Buckets and keys

| Bucket | Purpose |
|---|---|
| `dms-originals` | Committed original files. Optionally with Object Lock in GOVERNANCE mode, so the app's own credentials cannot delete files. |
| `dms-renditions` | Previews, thumbnails, print PDFs, extracted text |
| `dms-staging` | Uploads that are not yet committed. A lifecycle rule expires them after 2 days. |
| `dms-quarantine` | Infected files |

Object keys are **generated by the server**: `originals/{yyyy}/{MM}/{storageObjectId}`. The filename never goes into the key, which prevents path traversal and key injection.

### 7.3 Upload pipeline

1. **`POST /api/v1/uploads`** streams the multipart body without buffering it in memory. While streaming to staging, the server:
   - computes the **SHA-256**;
   - detects the MIME type from magic bytes;
   - enforces size and extension limits, both global and per DocumentType;
   - creates a `StorageObject(STAGED)`.
2. **`POST /api/v1/documents`** (or `POST /api/v1/documents/{id}/versions`) is called with the `uploadId` and metadata. One short transaction then:
   - validates the dynamic data;
   - creates the Document/Version;
   - marks the object COMMITTED (a server-side copy to originals);
   - writes the audit entry;
   - queues the jobs `Scan → Extract/OCR → Index → Renditions`.
3. The response returns as soon as the file is safely stored. Heavy processing happens afterwards.

**Filenames:**

- Normalized to NFC, stripped of control characters and path separators, maximum 255 characters.
- Downloads use `Content-Disposition` with RFC 5987 `filename*`, so Persian filenames work.
- Filenames are never used as identifiers.

### 7.4 View, download and print

| Operation | Permission | Served content | Audit |
|---|---|---|---|
| Preview | VIEW | A *preview rendition*: page images or a preview PDF, optionally watermarked with user and time. **Never the original file.** | DOCUMENT_VIEWED |
| Download | DOWNLOAD | The original file (`attachment`) | DOCUMENT_DOWNLOADED |
| Print | PRINT | A print rendition | DOCUMENT_PRINTED |

- If the original PDF were streamed for preview, anyone with VIEW would effectively have DOWNLOAD.
- File types with no preview (DWG, RAR, and others) show metadata only. Download still works for any binary type.

**Access path:**

- By default, content is **streamed through the API**, with HTTP Range support for video, audio and PDF.
- This keeps authorization and audit on every byte, and the object store is never exposed to clients.
- Short-lived (≤ 60 s) signed URLs are an optional setting for very large files.
- There are never permanent public URLs.

### 7.5 Integrity and duplicates

- SHA-256 is computed while the file streams in. `x-amz-checksum-sha256` is sent so the store verifies it too.
- A recurring **integrity scrub** re-hashes objects on a rotating schedule and raises `INTEGRITY_CHECK_FAILED` on a mismatch.
- The `sha256` index powers the notice "this exact file already exists as …".
  - The notice **only mentions documents the user can VIEW**, otherwise it would leak information.
  - There is no physical deduplication across documents, which keeps purging and permission boundaries simple.

### 7.6 Virus scanning

- ClamAV (clamd, separate container) runs as the first job.
- Infected files move to quarantine and are audited as `FILE_INFECTED`.
- Download while a scan is pending: see decision D9.

### 7.7 Deletion

- **Soft delete** (`deleted_at`/`deleted_by`/`delete_reason`) never touches storage. Deleted documents are excluded by an EF named query filter and hidden from search.
- **Restore** requires DOCUMENT_RESTORE.
- **Purge:**
  - requires DOCUMENT_PURGE and a reason, and applies only to soft-deleted documents;
  - marks objects `PENDING_DELETION`, and a delayed job deletes them;
  - keeps tombstone rows with the SHA-256 for audit.
- Backups stay consistent because the database never references a file that was deleted early.

---

## 8. Search / OCR architecture

### 8.1 Processing jobs

Each job is idempotent and keyed by `storage_object_id`.

1. **Scan** with ClamAV.
2. **Extract** with **Apache Tika Server** behind `ITextExtractor`.
   - Tika covers PDF, Office, email (.eml/.msg), RTF, CSV and archive listings.
   - Archive listings are depth- and size-limited to guard against zip bombs.
   - The extracted text is saved as a gzip object in S3.
3. **OCR** behind `IOcrEngine`. It runs when the text layer is empty or too thin, and for images.
   - The first implementation is **OCRmyPDF + Tesseract (`fas` + `eng`)**. It also produces a searchable PDF that can serve as the preview rendition.
   - The OCR provider only appears in the Infrastructure layer.
4. **Renditions:**
   - Office → PDF via **Gotenberg** (LibreOffice);
   - PDF → page images and thumbnails via PDFium (PDFtoImage, MIT);
   - images via SkiaSharp;
   - video/audio thumbnails via ffmpeg (later);
   - CAD files are download-only in v1.
5. **Index** into OpenSearch through `ISearchService`, then update `search.index_state`.

### 8.2 Index design

- The alias `dms-versions` points to `dms-versions-v{n}`. There is **one search document per version**, so search is version-aware.

| Field group | Fields |
|---|---|
| Identity and state | `document_id`, `version_id`, `version_number`, `is_current`, `is_effective`, `approval_status`, `is_deleted` |
| Descriptive | `category_id`, `document_type_code`, `owner_id`, `created_at`, `title`, `description`, `tags`, `file_name`, `mime_type` |
| Metadata | `meta_text` (all searchable fields, full-text) plus typed fields `meta_kw.<type>.<field>`, `meta_num.*` and `meta_date.*` for filters and ranges. Names are prefixed with the document type code, so field names cannot collide across types. |
| Content | `content`, with sub-fields `content.fa` (Persian analyzer plus normalization of Arabic/Persian letters, zero-width non-joiners and digits) and `content.en` |

- A field code keeps its data type across document type versions (enforced at publish), so index mappings never conflict.
- **Why OpenSearch rather than Postgres full-text search:** Postgres has no Persian stemmer or dictionary.

### 8.3 Querying

- `SearchDocuments` builds the query, **ANDs in the access-scope filter from §5.7**, and hides versions the user cannot see (drafts).
- It then re-checks the returned page in Postgres.
- Facet counts are calculated only over documents in the user's access scope, so they reveal nothing about documents the user cannot see.
- The search engine is an additional enforcement layer, **never the source of truth** for authorization.

**On the "12 billion" example:**

- Plain text will not match the number `12000000000`.
- Version-aware *structured* search does work: `amount = 12e9` finds V2.
- Parsing of number phrases (including Persian digits and words) can be added in the search phase if required.

### 8.4 Rebuilding and degraded mode

- The index can always be rebuilt from Postgres plus the stored text objects, with **no re-OCR**.
- Mapping changes create a new index and then swap the alias.
- If OpenSearch is down, title and metadata search falls back to Postgres (trigram index + JSONB).

---

## 9. Migration strategy

1. **Tooling.** EF Core 10 migrations per module DbContext, with a history table in each module's schema. Hand-written SQL inside migrations covers:
   - extensions;
   - immutability and append-only triggers;
   - audit partitions;
   - deferrable foreign keys;
   - the short list of cross-module foreign keys;
   - grants.
2. **Running migrations.**
   - `Dms.Migrator` runs as a one-shot compose service using `dms_owner`, **before** the API starts. The API never migrates at startup.
   - Order: infra → identity → authz → storage → doctypes → documents → workflow → sharing → search → audit → notify.
3. **Seeding** is idempotent:
   - the permission catalog (code is the source of truth);
   - the built-in roles;
   - the root category;
   - a bootstrap admin created from environment variables, with a forced password change.
4. **Rules.**
   - Migrations only move forward in production.
   - Breaking changes use expand/contract.
   - An idempotent SQL script (`dotnet ef migrations script --idempotent`) is generated and reviewed for each release.
5. **Search.** Index versions change by swapping the alias. Reindexing is a queued job.
6. **Backups.**
   - Postgres uses WAL/PITR (pgBackRest).
   - Object storage uses bucket versioning or replication.
   - After a restore, the SHA-256 scrub verifies the files.
7. **Legacy import.** Out of scope for now. A bulk-import tool is planned if existing file shares or an older system must be migrated (decision D11).

---

## 10. Implementation phases

This order differs from the original specification in two ways, marked ✱:

- **Authorization comes before the document endpoints**, so no endpoint ever exists without it.
- **The frontend grows with each phase**, so mobile and RTL problems show up early.

Every phase needs explicit approval before it starts.

| Phase | Scope | Exit criteria (tests) |
|---|---|---|
| **1 Foundation** ✱ **(done)** | Solution, building blocks, compose (Postgres), Migrator, Identity (local login, sessions), Authorization core (catalog, roles, ACL, evaluator, access scope), Audit writer, job queue, architecture tests | Evaluator unit tests for every §5 rule: deny precedence, inheritance, override, view/download/print separation, default deny |
| **2 Documents + Storage** ✱ **(done)** | Categories, minimal DocumentType (no fields yet), upload/stage/commit, versions, current vs effective version, download, soft delete/restore/purge, tags, SHA-256, idempotency, **frontend shell** (RTL, responsive, document browser, upload, details, version history) | Integration: upload; create V1/V2; get current/previous version; **concurrent version creation**; immutability trigger; soft delete/restore; audit events; unauthorized → 404/403 |
| **3 Dynamic document types** **(done)** | Type versioning, fields, options, rules language (shared C#/TS evaluator), validation, per-version metadata, admin UI | Validation matrix; conditional fields; old documents still read with their original schema version |
| **4 Workflow** **(done)** | Definitions, versioning, instances, tasks, conditions, assignees, SLA job, task inbox UI | Transitions; workflow version isolation; approval tied to a version; new version after approval; auto-cancel on supersede; self-approval block |
| **5 Processing + Search** **(implemented, awaiting approval)** | ClamAV, Tika, OCR, renditions/preview/print, OpenSearch, secured search UI | Search authorization (no title/metadata/facet leaks); version-aware hits; OCR of a scanned Persian sample; reindex |
| **6 Sharing** | Internal shares, external links (hashed token, password, count, expiry, rate limit) | Expiry; revocation; max count under concurrency; version pinning; share vs DENY |
| **7 Audit hardening + notifications** | Partition management, tamper-evident hash sealing, export, audit viewer, notifications | Append-only enforcement at the DB role level; audit completeness per operation |
| **8 Admin UI completion** | Users, groups, roles, ACL editor with "why?" explanations, categories | End-to-end tests (Playwright, phone and desktop viewports) |
| **9 Hardening** | Security test suite, load tests (k6), pen-test checklist, backup/restore drill | Performance targets based on the sizing answers (D11) |

**Phase 2 as built: where it differs from the plan above, and what it leaves for later.**

- **One bucket, not four.** Staged and committed files share one bucket (`dms-objects`, or a
  directory for the filesystem provider); the lifecycle lives in `storage_objects.status`, and
  committing is a status change rather than a server-side copy. Quarantine gets its own location
  when ClamAV arrives in phase 5.
- **Filesystem storage is the default provider** so the stack runs without MinIO. `S3FileStorage`
  is implemented behind the same `IFileStorage`; the bucket is not created automatically yet.
- **One root category.** "No parent" means "under the root"; a unique index refuses a second root.
- **Owner defaults** (section 5.5) are the example set, VIEW, DOWNLOAD, EDIT and CREATE_VERSION,
  written as ordinary audited ACL rows by `IResourceAclWriter`. Phase 3 moves them onto the
  document type's permission policy.
- **Concurrent version creation** is serialised with `SELECT … FOR UPDATE` on the document row, so
  every concurrent upload gets the next number and none has to retry. The unique index on
  `(document_id, version_number, revision_number)` and `xmin` stay as backstops.
- **Idempotency-Key** is honoured on "create document" and "add version" (`infra.idempotency_keys`,
  written in the same transaction, kept one day). A retried upload simply stages a second copy that
  the staged-upload collector removes. Expired keys are not yet swept.
- **Not in phase 2:** metadata-only revisions over the API (the domain supports them; the endpoint
  needs field definitions, phase 3), ETag / `If-Match` on metadata edits, preview and print (they
  need renditions, phase 5, and so does the `DOCUMENT_VIEWED` event), HTTP Range for S3 downloads,
  and duplicate notices that exclude documents only visible through shares (phase 6).
- **Filenames** keep ZWNJ and ZWJ, which Persian writes inside words; every other format character,
  bidi overrides in particular, is still removed.

**Phase 3 as built.**

- **Rule language** lives in `Dms.SharedKernel.Rules` (parser with depth 8 / 100 nodes, total
  evaluator) and `frontend/src/lib/rules.ts`. Both run the same vectors in
  `tests/rules/rule-cases.json`; the only known divergence is number precision beyond 2^53, which
  only affects form hints.
- **Validation** is `MetadataValidator` (pure): unknown keys are refused, values are normalised
  (Persian digits, trimmed text, UTC date-times, ordered multi-select), SHOW rules are resolved to
  a fixed point and hidden values are dropped, then REQUIRE, declarative limits and VALIDATE rules.
  USER and GROUP ids are checked against Identity; DOCUMENT_REFERENCE must point at a document the
  author can VIEW, otherwise it is refused exactly like a missing one. Messages are Persian.
- **Schemas** are edited as a whole draft (`PUT …/draft`) and checked on save and again on publish
  (`SchemaDesignValidator`): codes, options, settings that fit the type, patterns that compile under
  `NonBacktracking`, defaults that pass their own field, rules that parse and name real fields, no
  SHOW rule on its own targets, and a field code never changes type across versions. Publishing
  starts the next draft as a copy. Triggers make published fields, options, rules and versions
  immutable in the database.
- **Metadata edits** (`PUT /documents/{id}/metadata`) follow ADR 0001: a new revision V(n).(r+1) on
  the same storage object under NEW_REVISION; under IN_PLACE the current row is rewritten with a
  before/after audit record, except when an approval-relevant field (in the old or new schema)
  changes or the schema is upgraded, which always makes a revision. The version trigger accepts a
  `dynamic_data` change only in a transaction that set `dms.metadata_in_place`.
- **Schema binding.** A document binds to the latest published schema at creation. A new file
  without new metadata carries the previous row's metadata and schema; with metadata it binds to
  the latest schema. A metadata edit keeps the row's schema unless `upgradeSchema` is set. Every
  row is always read with its own schema version.
- **API shape.** DECIMAL values leave the API as strings; strongly typed ids serialise as plain
  GUIDs. Field errors come back as `errors: { "<field or path>": [messages] }`.
- **Not in phase 3:** owner default ACLs from the type's permission policy (still the fixed §5.5
  set), `show_in_list` columns in the browser, search over metadata (phase 5), and field-level
  security (§5.10).

**Phase 4 as built.**

- **"Auto-cancel on supersede"** is read together with D7. Creating V(n+1) never touches Vn's
  run. When a *newer* version is **approved**, still-running instances on older versions of the
  same document are cancelled ("Superseded by V…") and those versions are marked CANCELLED,
  because approving them afterwards could only move readers backwards. `effective_version_id`
  also only ever moves forward.
- **Module shape.** `Dms.Workflow` depends only on contracts. Documents exposes
  `IDocumentApprovalGateway` (read a version, record an outcome) and calls `IVersionCreatedHook`
  after every new version or revision; Workflow implements the hook (AUTO_ON_VERSION starts the
  run in the same transaction) and `ITemporaryGrantSource` (open tasks as grants). Without the
  Workflow module Documents runs unchanged.
- **Type binding.** `workflowId` and `workflowMode` live in the document type's settings. With a
  mode other than NONE, new rows start as DRAFT, and filing is refused up front if the workflow is
  missing, inactive or unpublished, so no version is ever created that could never be approved.
- **Step actions** are stored as a JSONB column on `workflow_steps` rather than a
  `workflow_step_actions` table: they are frozen with their step either way (triggers make
  published steps and versions immutable).
- **RETURN** starts a new *round*: tasks from before the return stay in the history but no longer
  count, and the target sequence is worked again. The default target is the latest earlier step
  that actually had tasks.
- **Self-approval.** Assignee resolution leaves the author out unless the step allows it; a
  required step that then has nobody stops the run with an `attention_reason` and a
  WORKFLOW_NEEDS_ATTENTION audit record, for the initiator or an administrator to cancel. A group
  member who is the author sees the shared task but is refused (403 `workflow.self_approval`).
- **Concurrency.** Actions lock the instance row; of two members approving one shared task, the
  second gets 409. A closed task answers its assignee with 409 before any permission check.
- **Task grants** give exactly what section 5.4 lists: VIEW, VIEW_DRAFT, WORKFLOW_VIEW and the
  step's actions, on the document. **Not DOWNLOAD**: a reviewer without an ACL entry can see the
  metadata but not the bytes. Since phase 5 they see the watermarked preview (VIEW is enough),
  which covers review without handing out the original. This is a product decision to revisit.
- **SLA.** A job every 15 minutes records each overdue task once (WORKFLOW_TASK_OVERDUE);
  notifications arrive in phase 7, escalation later.
- **Also added:** `PUT /admin/users/{id}/manager` (with cycle detection) for MANAGER steps,
  `IRoleMembershipReader`, active group members in `IGroupMembershipReader`.

**Phase 5 as built.**

- **One processing job per file.** Attaching a file (the commit) queues `storage.process-object`
  in the same transaction: the ClamAV scan (INSTREAM over TCP; a failed scan throws and retries,
  so the file stays blocked, decision D9; a positive one quarantines it with `FILE_INFECTED`),
  then page images and a thumbnail, then the listeners. Search is a listener: it queues text
  extraction, which queues indexing. Staged uploads are no longer scanned; nobody can read them,
  and the collector removes abandoned ones.
- **Renditions are page images (WebP)**, not a preview PDF: PDFium for PDF, Skia for images
  (which also strips EXIF), Gotenberg (LibreOffice) for Office files when `GotenbergUrl` is set;
  anything else is download-only (`NotSupported`). Preview and print serve these images through
  the API with the full version gates (VIEW or PRINT, drafts, scan), stamped with the viewer's
  username and the time. `DOCUMENT_VIEWED` is written once when the viewer opens
  (`POST …/preview`), `DOCUMENT_PRINTED` when printing starts; single pages are not audited.
- **Derived objects** (page images, extracted text) record the file they came from
  (`derived_from_id`), and purging a document deletes them with the original.
- **Text** comes from Tika Server with Tesseract `fas+eng` (the image is `deploy/tika`; the stock
  one has no Persian). A PDF is read for its text layer first and only OCR'd when that is thin.
  The text is stored gzip'd as a derived object, keyed by file (`search.content_extractions`), so
  a metadata revision reuses it and a rebuild never OCRs again. Failed extractions are retried
  hourly, three times, then wait for an administrator.
- **Index**: one search document per version-revision, with `is_current`, `is_effective`,
  `is_published`, `created_by`, the category ancestor list, typed metadata under
  `meta.{TYPE}.{field}__{num|date|bool|txt|kw}` (numbers are indexed as doubles, so decimals
  beyond 2^53 lose precision in range filters only) and the text. Persian is normalised (Arabic
  yeh and kaf, digits); text fields are analysed with the zero-width non-joiner both as a space
  and removed, so "می‌شود", "می شود" and "میشود" all match. Mappings are strict; the alias points
  at a generation, and a reindex builds a new one and swaps atomically.
- **Search** ANDs the section 5.7 access scope into the query (so hits, totals and facet counts
  are scope-limited), finds unpublished versions only for their author (reviewers and VIEW_DRAFT
  holders do not find drafts through search yet), and re-checks every returned hit against
  Postgres. Permissions are not in the index, so a revoked grant needs no reindex. Without
  OpenSearch, or when it fails, search falls back to titles in Postgres (`degraded: true`).
  Moving a category reindexes every document below it.
- **Deployment.** The worker is its own container (`Dms__Role=worker`); OpenSearch, Tika, ClamAV
  and Gotenberg are compose profiles (`search`, `scan`, `office`), each optional.
- **OCR quality (R2)** was checked on a rendered Persian page only; real scans still need a look.
- **Not in phase 5:** HTTP Range on S3, signed URLs, metadata facets, number-phrase parsing, the
  integrity scrub, video thumbnails, per-type OCR settings, and a searchable PDF as the preview.

**Test stack:**

- xUnit v3, Shouldly, NSubstitute;
- Testcontainers (PostgreSQL, MinIO, OpenSearch), `WebApplicationFactory`, Respawn;
- NetArchTest for architecture rules;
- Vitest and Playwright for the frontend.

**Tests required by the specification, mapped to phases:**

| Tests | Phase |
|---|---|
| Permissions, inheritance, override, DENY, view vs download, view vs print, unauthorized attempts | 1–2 |
| Version immutability, concurrent versions, soft delete/restore, SHA-256, audit generation | 2 |
| Dynamic field validation, conditional fields | 3 |
| Workflow transitions, workflow version isolation, approval per version | 4 |
| Search authorization | 5 |
| Share expiration and revocation | 6 |

---

## 11. Changes to the original specification

| # | CURRENT (spec) | TARGET / RECOMMENDATION | REASON |
|---|---|---|---|
| 1 | `Document.DynamicData` | **Store metadata on the version row, and never change it. File versioning and metadata revisioning are separate axes: `(version_number, revision_number)`. A metadata-only edit creates a new revision that reuses the same file, and whether it does so at all is per document type configuration.** See [ADR 0001](adr/0001-file-versioning-and-metadata-revisions.md). | The spec's search example (V1 = 8 billion, V2 = 12 billion) needs versioned metadata. Otherwise V3 could be approved at 8 billion and then edited to 12 billion without a new version, skipping the senior-approval step. Forcing a full file version for every typo fix would duplicate large binaries instead. |
| 2 | Single `CurrentVersionId` | `current_version_id` (latest) + `effective_version_id` (latest approved, or latest when there is no workflow) | Readers keep seeing approved V3 while V4 is a draft. |
| 3 | Permission as an aggregate | A static catalog seeded from code | Admin-created permissions would never be checked by any code. |
| 4 | Roles contain DOCUMENT_\* permissions | Roles hold **system** permissions. Resource access comes from ACL entries, and roles can be ACL subjects. | Keeps role and resource permissions separate, and the resolution rules predictable. |
| 5 | `field_conditions` and `workflow_conditions` tables | JSON expression columns in one shared rule language | One safe evaluator, validated at publish time. The same rules run in the browser for showing and hiding fields. |
| 6 | `workflow_actions` | `workflow_step_actions` (allowed actions per step + return target) | Transitions become configuration instead of code. |
| 7 | `search_documents` table | The OpenSearch index is the search document; Postgres keeps `index_state` and extraction records | Avoids storing text twice while keeping the index rebuildable. |
| 8 | `DocumentShare.VersionId` optional; a single `Permission` | Version required for **all** shares; permissions stored as a flag set (VIEW/DOWNLOAD/PRINT) | Consistent with version-specific sharing. A share often grants view and download together. |
| 9 | Permission list | Add DOCUMENT_VIEW_DRAFT, DOCUMENT_SHARE_EXTERNAL, DOCUMENT_PURGE, ADMIN_MANAGE_CATEGORIES | Drafts, external links and purging are riskier than their base permissions. Categories needed an admin permission. |
| 10 | AuditLog fields | Add outcome, actor_type, share_link_id and correlation_id; partition monthly | Denied attempts and anonymous link access must be auditable. Audit volume will be high. |
| 11 | MediatR listed as an option | In-house dispatcher | MediatR became commercial in 2025, and only about 50 lines are needed. |
| 12 | Phase order (authorization in 4, frontend in 9) | Authorization in phase 1; frontend built incrementally from phase 2 | Avoids building insecure endpoints and a late UI rewrite. |

---

## 12. Decisions

All of these are settled. The two that remain open are inputs from the business, not architecture choices.

| ID | Decision | Outcome |
|---|---|---|
| D1 | .NET 10 toolchain | **SDK installed user-locally in `~/.dotnet`.** The Docker daemon on the dev machine cannot reach mcr.microsoft.com, so the container build route is unavailable there. The Dockerfile still builds from `mcr.microsoft.com/dotnet/sdk:10.0` wherever the registry is reachable. |
| D2 | Authentication | **Local accounts now.** Users, groups and memberships live in our own schema, so authorization never depends on an external directory. `auth_source` and `external_id` on users and groups are the seams for OIDC or AD later; adding them changes no authorization code. Tokens: short-lived JWT access token plus a rotating refresh token whose SHA-256 is stored, with reuse detection. |
| D3 | Frontend | **React + TypeScript + Vite + MUI.** Phase 1 ships only a sign-in shell; no existing frontend was replaced because there was none. |
| D4 | Localisation | **Persian first, RTL, Jalali display.** Presentation only: `timestamptz` in UTC in the database, ISO-8601 on the wire, Jalali rendered in the browser via `Intl`. |
| D5 | Administrator access | **Administrators do not get document content.** `is_system_admin` grants every system permission and the single ACL bypass for `DOCUMENT_MANAGE_PERMISSION` (so nobody can be locked out); every use of that bypass writes an `ADMIN_PERMISSION_OVERRIDE` audit record. Content access goes through the ordinary ACL. |
| D6 | Draft visibility | **Author, assigned reviewer, or `DOCUMENT_VIEW_DRAFT`.** Everyone else is refused, in listings and in search as well as on direct access. |
| D7 | Version approval | **History is never rewritten.** V3 keeps its approved (or in-review) state, V4 starts as `DRAFT` and runs its own workflow instance, `effective_version_id` stays on V3 until V4 is approved, `current_version_id` points at V4. |
| D8 | Share links | **Re-validated on every access** against the sharer's current authorization, the document state and the link's own expiry, count and revocation. Links stay pinned to one version. |
| D9 | Malware scanning | **Quarantine until clean.** View, download, print and share are all refused while the scan is pending, failed or positive, including for the uploader, who can still see the scan status. Implemented in the evaluator now, enforced end to end when uploads land in phase 2. |
| D10 | Nested groups | **Flat groups in v1.** |
| D11 | Legacy archive import | **Deferred.** No import code; the storage and metadata model leaves room for a bulk importer. |
| D12 | Retention and legal hold | **Deferred.** No retention code; `DocumentType.settings` and the purge path are where it will attach. |
| D13 | Root namespace | **`Dms.*`** |
| D14 | Audit of repeated views | **Log every view.** Monthly partitions absorb the volume. |

### Still needed from the business

| Question | Why it matters |
|---|---|
| Scale: number of documents, total storage, largest file, concurrent users | Sizes OpenSearch, decides whether uploads need resumable/direct-to-storage support, and sets audit retention. |
| Does Active Directory exist, and should AD groups map to Groups? | Decides when the OIDC/LDAP seam is used and whether group sync is needed. |

## 13. Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | **VIEW without DOWNLOAD, and PRINT, are deterrents, not DRM.** A user who can see a page can photograph or screenshot it. | Enforce the difference on the server: preview renditions only, every access audited, optional watermark. State this limit clearly to stakeholders. |
| R2 | **Persian OCR quality.** Tesseract `fas` is mediocre on low-quality scans. | `IOcrEngine` lets PaddleOCR or a commercial engine be plugged in. Evaluate on real sample scans in phase 5. |
| R3 | **MinIO's community edition changed a lot in 2025** (console features removed, pre-built images no longer published, AGPL license). | Verify the image source and license before committing. The plain S3 API means SeaweedFS, Garage or Ceph RGW can replace it without code changes. |
| R4 | **OpenSearch needs a lot of resources** (≥ 4 GB RAM in production, security plugin with TLS). | Size the cluster from D11. The Postgres fallback provides degraded-mode search. |
| R5 | **Restricted internet or offline builds.** | Set up a package mirror (NuGet, npm) and a container-image mirror. Bundle Tesseract language data and the Tika/Gotenberg images. Cloud OCR is ruled out. |
| R6 | **Audit volume.** Logging every view gets large. | Monthly partitions, BRIN index, retention by detaching partitions (DBA role only). Decision D14. |
| R7 | **DENY cannot be overridden by a more specific ALLOW.** That is the deterministic rule the spec asks for, but it can surprise admins. | The "effective permissions / why?" tool; admin training. |
| R8 | **Very large files over mobile networks.** | Streaming uploads work up to a configurable limit. Add resumable uploads (tus) or direct multipart uploads to storage if multi-GB video or CAD files are common (D11). |
| R9 | **Port collisions with the fleetvision stack** on the dev machine. | The DMS compose file uses non-conflicting ports (Postgres 5433, etc.) and exposes only the gateway plus dev-only ports. |
| R10 | **Cross-module transaction complexity** (one shared connection across module DbContexts). | Implemented once in the unit-of-work building block and covered by integration tests in phase 1. |
