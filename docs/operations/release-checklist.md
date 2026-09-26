# Release checklist

Mark each item for every production-bound build.

## Build

- [ ] `scripts/test.sh` green (unit + integration)
- [ ] `cd frontend && npm run lint && npm run build && npx vitest run`
- [ ] `scripts/e2e.sh` green on phone and desktop (when UI changed)
- [ ] Image tags recorded (api, worker, web, migrator)
- [ ] `GET /version` on the candidate build matches the tag

## Configuration

- [ ] `.env` / secret store complete vs `deploy/.env.example`
- [ ] CORS origin is HTTPS production frontend
- [ ] Rate limits reviewed (`Dms:RateLimits:*`)
- [ ] Storage provider and paths/buckets correct
- [ ] Previous audit seal keys retained across rotations

## Data

- [ ] Forward-only migrations reviewed (`dotnet ef migrations script --idempotent` per module)
- [ ] Backup taken immediately before migrate
- [ ] Retention settings on document types reviewed (phase 10)

## Sign-off

- [ ] Pen-test checklist current for this release
- [ ] Load suite within [performance targets](../hardening/performance-targets.md)
- [ ] Backup/restore drill passed this quarter
