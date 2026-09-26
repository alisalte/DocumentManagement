# Pen-test checklist (phase 9)

Manual / tool-assisted review before go-live. Mark each row when verified against a staging
deployment that mirrors production (TLS, `dms_app` role, seal key, CORS locked to the real origin).

## Authentication and sessions

- [ ] Wrong password and unknown username return the same status and body shape (no account enumeration)
- [ ] Refresh-token reuse is refused and audited (`TOKEN_REUSE_DETECTED`)
- [ ] Forced password change blocks every API except change-password / sign-out
- [ ] Login rate limit returns HTTP 429 after `Dms:RateLimits:LoginPerMinute` (default 10)
- [ ] JWT without a valid signature is rejected; expired tokens are rejected
- [ ] Deactivating a user clears effective permissions on the next authorized call

## Authorization

- [ ] Direct document URL for a document the caller cannot see → 404 (not 403 with a title)
- [ ] Search never returns titles/snippets/facets for documents outside the caller's scope
- [ ] VIEW without DOWNLOAD cannot fetch the original file; PRINT uses watermarked renditions only
- [ ] Explicit DENY beats every ALLOW, including inherited and more specific allows
- [ ] `ADMIN_MANAGE_USERS` cannot promote to system admin or touch an existing administrator
- [ ] Role managers cannot assign permissions or roles they do not hold
- [ ] Administrator ACL bypass is audited as `ADMIN_PERMISSION_OVERRIDE` for list/grant/revoke/explain

## Sharing and anonymous surfaces

- [ ] External links re-check the sharer's rights on every open
- [ ] Expired, revoked and exhausted links refuse access
- [ ] Wrong link password locks after `LinkPasswordMaxAttempts`
- [ ] Share-link open rate limit returns 429
- [ ] OpenAPI / Scalar are not exposed outside Development

## Transport and browser

- [ ] TLS only in production; HSTS present (`Strict-Transport-Security`)
- [ ] `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`
- [ ] CORS allow-list matches the real frontend origin only; credentials not `*`
- [ ] Download responses do not embed user-controlled HTML as `text/html`

## Data and audit

- [ ] Runtime DB role cannot UPDATE/DELETE/TRUNCATE `audit.audit_logs` or seals
- [ ] Seal verification detects a tampered row
- [ ] Audit export requires `AUDIT_EXPORT` and writes `AUDIT_EXPORTED`
- [ ] Object storage / filesystem paths are not guessable from document ids alone

## Operations

- [ ] Secrets (`Dms__Jwt__SigningKey`, `Dms__Audit__SealKey`, DB passwords) are not in the image or git
- [ ] Backup contains DB + object store; restore drill completed ([backup-restore.md](backup-restore.md))
- [ ] Health endpoints do not leak connection strings or versions beyond what ops needs
