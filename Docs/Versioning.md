# ApexAutoBid — App Versioning

How the application is versioned, where the version lives, and how it relates to the CI/CD pipeline (`Architecture.md` §8.3, `Requirements.md` §6).

## 1. Scheme

- **Semantic versioning** — `MAJOR.MINOR.PATCH` (e.g., `1.2.0`).
- **One platform-wide version** for the whole application — all backend services and the web app share it. Services ship together and have no external API consumers, so per-service versions would add bookkeeping without benefit.
- `0.x.y` during build-out (Phases 1–9); **`1.0.0` at the first production deployment** (Phase 10). After that: PATCH for fixes, MINOR for features (e.g., the Phase 11 admin dashboard), MAJOR for breaking platform changes.

## 2. Source of truth (file-based)

The version is committed in two files, kept equal, and bumped in the release PR (`develop` → `main`):

| File | Mechanism |
|------|-----------|
| `backend/Directory.Build.props` | `<Version>` property inherited by every .NET service project — flows into assembly metadata and OpenAPI `info.version` |
| `frontend/web-app/package.json` | `version` field — exposed to the app via `process.env.npm_package_version` at build time |

`Directory.Build.props` is created with the solution structure in Phase 1 (Task 1).

## 3. Git tags (release anchor)

After each squash merge of `develop` into `main` that constitutes a release, tag the merge commit on `main`:

```
git tag -a v1.2.0 -m "v1.2.0"
git push origin v1.2.0
```

The tag maps the version to a commit SHA — and since Docker images are tagged with that same SHA (`Architecture.md` §8.3), it also maps the version to the exact images running in production. Rollback to a version = re-deploy the SHA its tag points to. Tagging slots into the release flow in `DeveloperNotes.md` between the squash merge and the `develop` reset.

Optionally create a GitHub Release from the tag with brief notes; no separate `CHANGELOG.md` is maintained — the squash-merge PR descriptions and release notes serve that purpose.

## 4. Relation to Docker image tags

| Tag | Purpose |
|-----|---------|
| `:<commit-sha>` | Immutable deploy/rollback anchor — what the cluster actually references |
| `:latest` | Local convenience only |

The semver is **not** used as an image tag — it's baked *into* the images via the files in §2. This keeps the deploy workflows unchanged; the git tag provides the version → SHA → image mapping.

## 5. Where the version is visible

- **Backend version API** — `GET api/version` (Anon), handled by the gateway itself (not proxied). Returns the platform version from the gateway's assembly metadata, e.g. `{ "version": "1.2.0" }`. Since every service shares the platform version, the gateway's version *is* the backend version — no per-service aggregation needed.
- **Web app footer** — shows both versions: the frontend version from `package.json` and the backend version fetched from `GET api/version`. The two match except briefly while a deployment is rolling out.
- **OpenAPI documents** — each service's `info.version` from the assembly version (visible in Scalar, per service and via the gateway's aggregated docs).

## 6. Release checklist

1. On `develop`: bump `<Version>` in `backend/Directory.Build.props` and `version` in `frontend/web-app/package.json` (same value).
2. Open the PR `develop` → `main`; CI must be green.
3. Squash merge.
4. Tag the merge commit on `main` (`v{version}`) and push the tag; the deploy workflows roll production to the merge commit's SHA-tagged images.
5. Reset `develop` onto `main` per `DeveloperNotes.md`.
