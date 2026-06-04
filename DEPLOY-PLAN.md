# DeepDrftHome CD Pipeline Plan

Status: **DRAFT — awaiting Daniel's answers to the Open Questions below.**
Modeled on the working Skipper deploy infrastructure (`C:\Development\skipper`).
This is a plan only. No files are created until Daniel signs off on the open questions —
several of them change the installer materially.

---

## 0. Read this first — the brief contradicts the code

The task brief states DeepDrftHome uses **"SQLite, not PostgreSQL. No database role, no
database creation step in the installer."** **The current code does not match that.** As of
this writing the codebase is **PostgreSQL via Npgsql**, with **two** databases:

| Evidence | File | Line |
|---|---|---|
| `options.UseNpgsql(...DefaultConnection...)` | `DeepDrftAPI/Program.cs` | 60 |
| `options.Auth` connection for AuthBlocks Identity (second DB) | `DeepDrftAPI/Program.cs` | 76 |
| `optionsBuilder.UseNpgsql(connectionString)` | `DeepDrftData/Data/DeepDrftContextFactory.cs` | 29 |
| `Npgsql.EntityFrameworkCore.PostgreSQL 10.0.1` package | `DeepDrftAPI.csproj` / `DeepDrftData.csproj` | 15 / 20 |
| `Cerebellum.BlazorBlocks.Data.Postgres 10.3.30` | `DeepDrftData.csproj` | 22 |
| Migration uses `Npgsql:ValueGenerationStrategy` / IdentityByDefaultColumn | `DeepDrftData/Migrations/20260519021400_InitialCreate.cs` | 20 |
| `connections.json` shape is `{ ConnectionStrings: { DefaultConnection, Auth } }` (TCP strings, not a file path) | `DeepDrftAPI/CLAUDE.md` | — |

There is **no `UseSqlite` and no SQLite package anywhere** in the solution. (The root
`CLAUDE.md` and `DeepDrftData/CLAUDE.md` both *say* SQLite; those docs are stale relative to
the code. doc-keeper's problem, not this plan's — flagged here only because the brief
inherited the stale claim.)

**Consequence for the installer:** the brief's central simplification — "drop Skipper's
PostgreSQL step" — is **wrong as written**. If the code stays as-is, DeepDrftHome needs
*more* PostgreSQL than Skipper, not less: Skipper provisions one database, DeepDrftHome needs
**two** (track metadata + AuthBlocks Identity), and EF runs **two** migration paths (the
DeepDrft `InitialCreate` bundle *plus* AuthBlocks' own `UseAuthBlocksStartupAsync()` which
applies its Identity migrations and seeds the admin user on boot).

This is **Open Question 1** and it gates everything else. The rest of this plan is written
**twice where it matters**: a **Path A (PostgreSQL — matches current code)** as the default,
and a **Path B (SQLite — matches the brief)** as the alternative if Daniel intends to migrate
the data layer first. Do not start implementation until Daniel picks a path.

---

## 1. OPEN QUESTIONS — Daniel must answer before dev-ops starts

> These are blocking. Answers reshape the installer and the API workflow.

**Q1 — Database engine (BLOCKING, reshapes installer + API workflow).**
The code is PostgreSQL with two databases. The brief assumes SQLite with none. Which is true
for deployment?
- **(A) PostgreSQL, as the code stands.** Installer keeps Skipper's PG step, extended to
  create *two* databases and one role. API workflow bundles the DeepDrft migration and relies
  on `UseAuthBlocksStartupAsync()` for the Auth DB at boot. **(Recommended default — it
  matches what compiles and runs today.)**
- **(B) SQLite.** Requires a code change first (swap `UseNpgsql`→`UseSqlite`, regenerate
  migrations, decide where AuthBlocks Identity lives — AuthBlocks may not support SQLite).
  That is staff-engineer work that must land *before* this pipeline is meaningful. If Daniel
  wants B, the right sequencing is: data-layer migration PR first, deploy plan second.

**Q2 — Target host(s).** Beta (dev branch) host and prod (master) host. Same as Skipper
(`dch7.cerebellumsoftworks.com` beta / `prod.cerebellumsoftworks.com` prod), or separate
DeepDrft hosts? Co-tenanting on the Skipper hosts is fine *if* ports and the app user don't
collide (they won't — different user, different ports) and if Daniel is comfortable with two
verticals on one box.

**Q3 — App system user.** Suggest **`deepdrft`** (Skipper uses `skipper`). Confirm. If
co-tenant with Skipper on the same host, a distinct user is mandatory.

**Q4 — Domain names.** Three services, so up to three vhosts:
- `DeepDrftPublic` (public listener site) — suggest `deepdrft.com` (apex).
- `DeepDrftManager` (CMS) — suggest `cms.deepdrft.com` or `manage.deepdrft.com`.
- `DeepDrftAPI` — **does it get a public domain, or is it localhost-only behind the other
  two?** In Skipper, `SkipperAPI` has **no public vhost** — it binds `localhost:5002` and is
  reached server-to-server only. **But DeepDrft is different:** the WASM client in the
  browser proxies through `DeepDrftPublic`'s `TrackProxyController`, so Public→API is
  server-side and needs no public API domain. However, `DeepDrftManager` makes auth calls and
  the `AuthBlocksWeb` login flow against the API — confirm whether those are server-side
  (no public API domain needed) or whether the browser ever hits the API directly (then it
  needs a domain + CORS entry). **Best read of the code: API stays internal, no public vhost**
  — but Daniel/dev-ops should confirm the AuthBlocksWeb login redirect topology.

**Q5 — Gitea vs GitHub.** The repo's remote is GitHub
(`https://github.com/daniel-c-harvey/DeepDrftHome.git`). Gitea workflows (`package-install.yml`
uses `gitea.token` and the Gitea releases API) only run on a Gitea instance. Options:
- **(a)** Mirror/push to Gitea, workflows run there (Skipper's model verbatim).
- **(b)** Stay on GitHub Actions — same YAML, but: `gitea.token`→`github.token`, the Gitea
  releases API call in `package-install.yml`→`actions/upload-release-asset` or `gh release`,
  and `actions/upload-artifact@v3`→`@v4` (v3 is deprecated on GitHub).
- **(c)** Both (mirror).
Recommend **(a)** if a Gitea instance already hosts Skipper's CI — keeps one model. Otherwise
**(b)**. This decision changes `package-install.yml` and the artifact action versions only;
the deploy jobs are identical either way.

**Q6 — Gitea/CI secret names for SSH deploy keys.** Suggest **`DEEPDRFT_DCH7_SSH_DEPLOY`**
(beta) and **`DEEPDRFT_PROD_SSH_DEPLOY`** (prod), mirroring Skipper's
`SKIPPER_DCH7_SSH_DEPLOY` / `SKIPPER_DCHPROD_SSH_DEPLOY`. Confirm.

**Q7 — EF design-time factory: RESOLVED, with a caveat.**
`DeepDrftData/Data/DeepDrftContextFactory.cs` **exists** and implements
`IDesignTimeDbContextFactory<DeepDrftContext>`. So `dotnet ef migrations bundle` will not fail
for lack of a factory. **Caveat:** the factory reads `environment/connections.json` at a
*relative* path and **throws `FileNotFoundException` if it's missing** (factory lines 14–18).
In CI there is no `environment/connections.json`. Two mitigations, pick one in
implementation:
  - Have CI write a throwaway `environment/connections.json` with a dummy connection string
    before the bundle step (the bundle only needs the *provider*, not a live DB), **or**
  - Confirm `dotnet ef migrations bundle` uses the snapshot/provider without invoking the
    factory's connection (it generally does for bundling, but the factory's eager file read
    runs at context-construction time and will throw). Safest: write the dummy file in CI.
  This is a CI-step detail, not a blocker — flag for dev-ops.

**Q8 — FileDatabase vault path on host.** Suggest **`${APP_HOME}/api/deepdrft/vaults`** (sits
beside the API binary tree, consistent with Skipper's `${APP_HOME}/api/<app>/` layout). This
path goes into the `filedatabase.json` credential file. The directory must be **created by the
installer** (it is host state, never in the deploy archive) and pre-seeded empty — the API
creates the `tracks` vault on first boot (`Startup.InitializeTrackVault`). Confirm path.

**Q9 — `connections.json` delivery mechanism.** Skipper delivers `skipper-db-conn.json` as a
systemd `LoadCredential`. DeepDrftAPI loads **four** credential files
(`filedatabase.json`, `apikey.json`, `connections.json`, `authblocks.json`) via
`CredentialTools.ResolvePathOrThrow`, which resolves `$CREDENTIALS_DIRECTORY/<name>` in
production. So all four must be `LoadCredential=` lines in `deepdrftapi.service`, named to
match the `ResolvePathOrThrow("filedatabase"|"apikey"|"connections"|"authblocks", ...)` keys.
Confirm naming convention (the resolve key is the bare name — `filedatabase`, `apikey`,
`connections`, `authblocks` — so the credential files must be named exactly those, no `.json`
in the LoadCredential id). **This is the most error-prone deviation from Skipper — see §5.**

---

## 2. Service inventory and port plan

Three deployable services (Skipper has four: public/web/api/auth; DeepDrft folds auth into
the API, and has no separate "web app" — Manager *is* the second site).

| Service | csproj (flat layout) | Published binary | Suggested port | Public vhost? | Special CI |
|---|---|---|---|---|---|
| DeepDrftPublic | `DeepDrftPublic/DeepDrftPublic.csproj` | `DeepDrftPublic` | `localhost:5000` | yes (apex) | **wasm-tools workload**; TS compiles automatically via `Microsoft.TypeScript.MSBuild` at build (no extra step) |
| DeepDrftManager | `DeepDrftManager/DeepDrftManager.csproj` | `DeepDrftManager` | `localhost:5001` | yes (cms subdomain) | none (server Blazor only) |
| DeepDrftAPI | `DeepDrftAPI/DeepDrftAPI.csproj` | `DeepDrftAPI` | `localhost:5002` | likely no (internal) | EF migrations bundle; AuthBlocks self-migrates on boot |

**Layout note (deviation from Skipper):** Skipper's csprojs are nested
(`SkipperPublic/SkipperPublic/SkipperPublic.csproj`). DeepDrft's are **flat**
(`DeepDrftPublic/DeepDrftPublic.csproj`). The workflow `dotnet publish` paths must use the
flat form. Easy to get wrong by copy-paste from Skipper — call it out to dev-ops.

---

## 3. Gitea workflows to create — `.gitea/workflows/`

Four files, mirroring Skipper. Branch→host mapping identical to Skipper unless Q2 says
otherwise: `master`→prod, `dev`→beta.

### 3.1 `deploy-public.yml`
- **Purpose:** build DeepDrftPublic (WASM), package, rsync to host, SSH-trigger `deploy-public`.
- **Path triggers:**
  ```
  DeepDrftPublic/**
  DeepDrftPublic.Client/**
  DeepDrftShared.Client/**
  DeepDrftModels/**
  .gitea/workflows/deploy-public.yml
  ```
- **Build:** checkout → `setup-dotnet 10.0.x` → `dotnet workload install wasm-tools` →
  `dotnet publish DeepDrftPublic/DeepDrftPublic.csproj -c Release -r linux-x64 --self-contained
  -o DeepDrftPublic/publish` → tar `deepdrft-public.tar.gz` → upload artifact.
- **Deviation from Skipper:** flat csproj path; project-reference fan-in includes
  `DeepDrftPublic.Client` and `DeepDrftShared.Client` (the RCLs the public site consumes). No
  separate TS step — `Microsoft.TypeScript.MSBuild` runs inside `dotnet publish`.
- **Deploy job:** verbatim Skipper shape — download artifact, install ssh/rsync, select
  prod/beta key by branch, rsync to `deepdrft@$HOST:`, `ssh ... deepdrft@$HOST deploy-public`.

### 3.2 `deploy-web.yml` → rename to **`deploy-manager.yml`**
- **Purpose:** build DeepDrftManager (server Blazor), package, rsync, SSH-trigger
  `deploy-manager`.
- **Path triggers:**
  ```
  DeepDrftManager/**
  DeepDrftShared.Client/**
  DeepDrftModels/**
  .gitea/workflows/deploy-manager.yml
  ```
- **Build:** no wasm-tools. `dotnet publish DeepDrftManager/DeepDrftManager.csproj -c Release
  -r linux-x64 --self-contained -o DeepDrftManager/publish` → tar `deepdrft-manager.tar.gz`.
  (Skipper's web workflow passes `-p:WasmBuildNative=false`; DeepDrftManager has **no WASM**,
  so that flag is unnecessary — omit it.)
- **Deploy job:** verbatim shape; trigger word `deploy-manager`.

### 3.3 `deploy-api.yml`
- **Purpose:** build DeepDrftAPI, bundle EF migrations, rsync archive+bundle+unit, SSH-trigger
  `deploy-api`.
- **Path triggers:**
  ```
  DeepDrftAPI/**
  DeepDrftData/**
  DeepDrftContent/**
  DeepDrftModels/**
  .gitea/workflows/deploy-api.yml
  deploy/systemd/deepdrftapi.service
  ```
- **Build:** checkout → setup-dotnet → install `dotnet-ef` → restore (`-r linux-x64`) → build
  (`-r linux-x64 --self-contained --no-restore`) → publish (`--no-build`) → **bundle**:
  ```
  dotnet ef migrations bundle \
    --project DeepDrftData/DeepDrftData.csproj \
    --startup-project DeepDrftAPI/DeepDrftAPI.csproj \
    --context DeepDrftContext \
    --configuration Release \
    --output deepdrft-migrations-bundle \
    --self-contained -r linux-x64
  ```
  → tar `deepdrft-api.tar.gz` → upload `{archive, bundle}`.
- **Deviations from Skipper (Path A / PostgreSQL):**
  1. **Dummy `environment/connections.json` step before the bundle** (see Q7) — write a file
     with a parseable Npgsql connection string so `DeepDrftContextFactory` doesn't throw.
  2. The bundle only covers **`DeepDrftContext`** (track metadata). **The AuthBlocks Identity
     database is NOT in this bundle** — it is migrated + seeded at runtime by
     `app.Services.UseAuthBlocksStartupAsync()` on first boot (Program.cs line 116). This is a
     real difference from Skipper's single-DB model. The deploy script must therefore ensure
     **both** databases exist before the service starts; the DeepDrft bundle handles schema
     for DB #1, AuthBlocks self-migrates DB #2 on start.
  3. The deploy trigger needs **the connection target for two DBs**, not one. Either pass both
     DB names as args, or (cleaner) let the deploy script read them from the host's
     `connections.json` credential rather than from CI args. **Recommend: deploy script reads
     the DB name(s) from the credential file** — avoids leaking DB names into CI and
     authorized_keys command logs. (Skipper passes the DB name as a CI arg; DeepDrft having two
     DBs makes the arg approach clumsier.)
- **Deviation from Skipper (Path B / SQLite):** no bundle DB connection needed — the bundle
  applies to a file. AuthBlocks-on-SQLite is an open question for staff-engineer. This whole
  subsection collapses if Daniel picks SQLite, but only after the code migration lands.
- **Deploy job:** Skipper shape; rsync `deepdrft-api.tar.gz`, `deepdrft-migrations-bundle`,
  `deploy/systemd/deepdrftapi.service`; trigger `deploy-api`.

### 3.4 `package-install.yml`
- **Purpose:** on any `deploy/**` change, tarball `deploy/` (minus `bootstrap.sh`) and cut a
  release named `install-<date>-<sha>` (prod) / `beta-<date>-<sha>` (dev).
- **Deviation:** rename artifact to `deepdrft-install.tar.gz`; Gitea vs GitHub release
  mechanics per Q5. Otherwise verbatim.

---

## 4. `deploy/` folder to create — file-by-file

Mirrors Skipper's `deploy/` exactly in structure. Counts below assume **Path A (PostgreSQL)**.

### 4.1 `install.sh` — one-shot host installer
Step-by-step, with deviations called out:

| Step | Skipper | DeepDrft (Path A) |
|---|---|---|
| 0 Preflight | checks `psql`, `pg_isready`, `nginx`, `rrsync` | **same** (PG still required). Path B would drop the psql/pg_isready checks. |
| 0b Params | APP_USER, APP_HOME, PG_ROLE, DB name, domains, certbot email | APP_USER=`deepdrft`; **two** DB names (`DB_META` e.g. `deepdrft-meta`, `DB_AUTH` e.g. `deepdrft-auth`); domains for **public + manager** (+ optional api); certbot email. |
| 1 Create user | `useradd --system` | same, user `deepdrft` |
| 2 Linger | `loginctl enable-linger` | same |
| 3 Dir layout | `{public,web,api/skipper}/{bin,environment}` | **`{public,manager,api/deepdrft}/{bin,environment}`** + **`api/deepdrft/vaults`** (FileDatabase — new, replaces nothing in Skipper) |
| 4 Deploy scripts | install ssh-wrapper + 3 deploy scripts | install ssh-wrapper + **deploy-public.sh, deploy-manager.sh, deploy-api.sh** |
| 5 Systemd units | 3 units | **deepdrftpublic.service, deepdrftmanager.service, deepdrftapi.service** |
| 6 Credentials | runs creds writer; expects 6 files | runs creds writer; **expects N files** (see §5 — likely 5 or 6) |
| 7 PostgreSQL | one role, one DB | **one role, TWO databases** (`DB_META` + `DB_AUTH`), both owned by the role; peer-auth verify for both. **The key installer deviation.** Path B: this step is *deleted*, replaced by creating the empty FileDatabase vault dir (which Path A also does, in step 3). |
| 8 authorized_keys | forced-command + restrict, ed25519 only | same; secret name hint `DEEPDRFT_<HOST>_SSH_DEPLOY` |
| 9 nginx | 2 vhosts (public + app) | **2 vhosts (public + manager)**; API gets a vhost only if Q4 says it's public |
| 10 Summary | reminders + certbot + smoke tests | same, updated names; certbot `-d` list = public + manager (+ api if public) |

**Path B collapse:** if SQLite, step 0 drops PG preflight, step 7 is removed entirely, the
DB-name params in 0b vanish, and `connections.json` becomes a file-path config rather than two
TCP strings. Everything else is unchanged.

### 4.2 `bootstrap.sh` — curl-and-run entry point
- Verbatim from Skipper except: app name strings, OS prereqs. **Path A keeps `postgresql` in
  the apt install list; Path B removes it.** Tarball name `deepdrft-install.tar.gz`.

### 4.3 `ssh-wrapper.sh` — forced-command dispatcher
- Verbatim shape. Dispatch table:
  ```
  rsync --server*   -> rrsync ${APP_HOME}/staging
  deploy-public     -> deploy-public.sh
  deploy-manager    -> deploy-manager.sh        # renamed from deploy-web
  deploy-api [args] -> deploy-api.sh
  ```
- Deviation: `deploy-web`→`deploy-manager`. If Path A keeps CI-passed DB-name args off the
  wire (recommended in §3.3), `deploy-api` takes **no** trailing arg and the arg-parsing branch
  simplifies to the bare `deploy-public` form.

### 4.4 `deploy-public.sh`
- Verbatim from Skipper's `deploy-public.sh`: swap `bin`→`bin.prev`, extract
  `deepdrft-public.tar.gz` into `public/bin`, restart `deepdrftpublic.service`.
- **Deviation:** DeepDrftPublic also loads `environment/api.json` via `CredentialTools`. In
  prod that resolves through the systemd `LoadCredential` (see §5), **not** a copied
  environment file — so unlike Skipper's `deploy-web.sh`, the public deploy script may not need
  the "apply environment files" block **if** `api.json` is delivered via LoadCredential. Decide
  per §5 (credential-delivery uniformity). If `api.json` is delivered as a plain env file
  instead, keep the env-file copy block like Skipper's web script.

### 4.5 `deploy-manager.sh` (was `deploy-web.sh`)
- Verbatim Skipper `deploy-web.sh` shape; archive `deepdrft-manager.tar.gz`, approot
  `${APP_HOME}/manager`, unit `deepdrftmanager.service`. Manager loads `api.json` — same
  env-delivery decision as §4.4.

### 4.6 `deploy-api.sh`
- Skipper shape **plus DeepDrft's dual-DB reality:**
  1. Apply the **DeepDrft metadata** migration bundle before swapping the binary (as Skipper
     does). Connection string: **read from the host `connections.json` credential** (or env),
     not a CI arg — Path A. Use the `DefaultConnection` (meta DB).
  2. Do **not** try to migrate the Auth DB here — `UseAuthBlocksStartupAsync()` does that at
     service start. The script just needs the Auth DB to **exist** (installer step 7 created
     it) so the boot-time migration succeeds.
  3. Swap binary tree into `api/deepdrft/bin`, restart `deepdrftapi.service`.
  4. **Do not touch the vault directory** (`api/deepdrft/vaults`) on deploy — it is persistent
     data, lives outside `bin`, and is referenced by the `filedatabase.json` credential. This
     is a new "leave-alone" invariant with no Skipper analogue.
- **Path B:** bundle applies to the SQLite file; drop the connection-string logic.

### 4.7 `setup-step10-creds.sh` — interactive credential writer
Biggest single divergence from Skipper. See §5 for the full credential matrix. Structurally
the same script (template dir, sed substitution, `write_cred`, `need_cred`, idempotent skip,
deploys systemd units at the end), but the **set of credential files and the prompts differ**.
Skipper's AuthBlocks→Skipper rename-migration block has no DeepDrft analogue — **drop it**.

### 4.8 `systemd/` — user units
Three `.service` files, modeled on Skipper's:

- **`deepdrftpublic.service`** — `WorkingDirectory=%h/public/bin`,
  `ExecStart=%h/public/bin/DeepDrftPublic`, `ASPNETCORE_URLS=http://localhost:5000`,
  `LoadCredential=api:%h/.config/credentials/api-public.json` (name per §5).
- **`deepdrftmanager.service`** — `%h/manager/bin/DeepDrftManager`, port 5001,
  `LoadCredential=api:%h/.config/credentials/api-manager.json`.
- **`deepdrftapi.service`** — `%h/api/deepdrft/bin/DeepDrftAPI`, port 5002,
  `After=...postgresql.service` (Path A only), **four** `LoadCredential` lines:
  ```
  LoadCredential=filedatabase:%h/.config/credentials/filedatabase.json
  LoadCredential=apikey:%h/.config/credentials/apikey.json
  LoadCredential=connections:%h/.config/credentials/connections.json
  LoadCredential=authblocks:%h/.config/credentials/authblocks.json
  ```
  **Critical:** the LoadCredential **id** (left of the colon) must equal the
  `CredentialTools.ResolvePathOrThrow("<id>", ...)` key in code — `filedatabase`, `apikey`,
  `connections`, `authblocks`. Get these wrong and the API throws on startup. (Skipper's ids
  were `skipper-db-conn`, `skipper-jwt`, etc.; DeepDrft's are the bare resolve keys.)

### 4.9 `nginx/` — vhost templates
Two templates (Skipper has two), `__DOMAIN_*__` placeholders:
- `deepdrft-public.conf` → `proxy_pass http://localhost:5000` (apex domain).
- `deepdrft-manager.conf` → `proxy_pass http://localhost:5001` (cms subdomain).
- **API vhost only if Q4 says the API is public.** Default: omit it; the API is reached
  server-to-server on localhost:5002.
- Same Blazor-Server WebSocket upgrade headers as Skipper (both Public and Manager use
  interactive server circuits, so the `Upgrade`/`Connection` headers are required for both).

### 4.10 `credentials/` — JSON templates with placeholders
See §5 for the full list and shapes.

---

## 5. Credential matrix — the load-bearing deviation

DeepDrft's credential surface is **shaped differently** from Skipper's. Skipper's API reads
named files (`skipper-db-conn`, `skipper-jwt`, `skipper-email`, `skipper-admin`); DeepDrft's
API reads **four bundled files** whose names must match the `ResolvePathOrThrow` keys, and the
two sites each read one `api.json`-shaped file.

| Credential file (suggested) | Consumed by | Resolve key / LoadCredential id | Shape (from code) | Secrets to prompt |
|---|---|---|---|---|
| `filedatabase.json` | DeepDrftAPI | `filedatabase` | `{ "FileDatabaseSettings": { "VaultPath": "${APP_HOME}/api/deepdrft/vaults" } }` | none (path templated at install) |
| `apikey.json` | DeepDrftAPI | `apikey` | `{ "ApiKeySettings": { "ApiKey": "..." } }` | API key (generate with `openssl rand -hex 32`, like Skipper's JWT) |
| `connections.json` | DeepDrftAPI | `connections` | `{ "ConnectionStrings": { "DefaultConnection": "...", "Auth": "..." } }` | PG password (shared by both strings), DB names templated |
| `authblocks.json` | DeepDrftAPI | `authblocks` | `{ "AuthBlocks": { "Jwt": {Secret,Issuer,Audience}, "Email": {Host,Token}, "Admin": {UserName,Email,Password}, "SupportEmail" } }` | JWT secret (gen), email host+token, admin user/email/password |
| `api-public.json` | DeepDrftPublic | `api` | `{ "Api": { "ContentApiUrl": "http://localhost:5002" } }` | none (static URL) |
| `api-manager.json` | DeepDrftManager | `api` | `{ "Api": { "ContentApiUrl": "http://localhost:5002", "ContentApiKey": "..." } }` | reuse the **same** API key as `apikey.json` |

Notes / decisions for dev-ops:
- **The API key appears twice** — in `apikey.json` (API validates against it) and
  `api-manager.json` (Manager sends it). `setup-step10-creds.sh` must generate it **once** and
  write it to both, exactly like Skipper reuses the Mailtrap token across two files.
- **The PG password appears in both connection strings** in `connections.json` (one prompt,
  two substitutions) — Path A only.
- **`api-public.json` and `api-manager.json` are different files** because the resolve key is
  the same (`api`) but they live in different services' credential sets. Under systemd
  `LoadCredential`, each service mounts its own file as `$CREDENTIALS_DIRECTORY/api`. So the
  filenames on disk can differ; the LoadCredential **id** is `api` for both services. Confirm
  this is how `CredentialTools` resolves in prod (`$CREDENTIALS_DIRECTORY/api`) — it almost
  certainly does given the API's four-file pattern, but verify against `CredentialTools`
  source in NetBlocks before finalizing.
- **Expected credential count** in `install.sh` step 6 / `setup-step10-creds.sh` verify:
  per-service. The API host needs 4; Public needs 1; Manager needs 1. If all live in one
  shared `~/.config/credentials/`, that's **6 files**. If split per-service-home, the counts
  are 4/1/1. **Recommend one shared credential dir** (Skipper's model) → expected count **6**.
- **Email provider:** Skipper uses Mailtrap. DeepDrft's `authblocks.json` Email block is
  `{Host, Token}` — provider-agnostic. Prompt for host + token generically.
- **No `LeadCaptureEmail` / public marketing config** — DeepDrftPublic has no email/lead
  capture (unlike SkipperPublic). Its only credential is the API URL. Simpler than Skipper.

---

## 6. What copies verbatim vs. what changes — summary

**Verbatim (string-renames only):** `bootstrap.sh` (Path A), `ssh-wrapper.sh`,
`deploy-public.sh`, `deploy-manager.sh`, all three deploy-job halves of the workflows, the
nginx WebSocket-header bodies, the authorized_keys/forced-command/linger/systemd-enable
machinery in `install.sh`.

**Materially different (needs design attention):**
1. **`install.sh` step 7** — two PostgreSQL databases + one role (Path A), or deleted (Path B).
2. **`deploy-api.yml`** — dummy `connections.json` for the EF bundle; bundle covers only the
   metadata context; AuthBlocks self-migrates at boot.
3. **`deploy-api.sh`** — reads DB connection from host credential not CI arg; leaves the vault
   dir untouched.
4. **`deepdrftapi.service`** — four `LoadCredential` lines with ids matching the resolve keys.
5. **`setup-step10-creds.sh` + `credentials/`** — the six-file matrix in §5; API-key reuse
   across two files; no AuthBlocks-rename migration block; no lead-capture config.
6. **FileDatabase vault directory** — a host-state directory with no Skipper analogue;
   created by the installer, referenced by `filedatabase.json`, never in any archive, never
   touched on deploy.

**Renames throughout:** `skipper`→`deepdrft`, `web`/`SkipperHaven`→`manager`/`DeepDrftManager`,
flat csproj paths (no double-nesting), artifact names `deepdrft-*`.

---

## 7. Recommended sequencing for dev-ops (once Q1–Q9 answered)

1. Settle Q1 (engine). **If Path B, stop** — staff-engineer migrates the data layer first;
   this plan's §3.3/§4.1-step7/§5-connections all change.
2. Create `deploy/systemd/*.service` first (they define the contract: ports, binary paths,
   credential ids). Verify the four API LoadCredential ids against `CredentialTools` source.
3. Create `credentials/*.json` templates + `setup-step10-creds.sh` against the §5 matrix.
4. Create `install.sh` (the dual-DB step 7 is the risky part — test against a throwaway host).
5. Create `ssh-wrapper.sh` + three `deploy-*.sh`.
6. Create `bootstrap.sh`.
7. Create the four workflows last (they depend on the trigger words and archive names the
   deploy scripts expect).
8. Dry-run the four-layer SSH smoke test from `install.sh`'s step-10 summary before the first
   real push.

---

## 8. Risks / things that will bite

- **The PostgreSQL/SQLite contradiction (Q1)** is the dominant risk. Building the SQLite
  installer the brief describes against the PostgreSQL code that exists would produce an
  installer that cannot run the app. Resolve Q1 in writing before any file is created.
- **Two databases, two migration mechanisms** (DeepDrft bundle + AuthBlocks boot-time). If the
  Auth DB doesn't exist when the API starts, `UseAuthBlocksStartupAsync()` fails and the
  service won't come up. Installer step 7 *must* create both, and ordering matters
  (`After=postgresql.service`).
- **LoadCredential id ↔ resolve key coupling.** A typo (`filedb` vs `filedatabase`) yields a
  clean-looking deploy and a service that throws on startup. The four ids are
  non-negotiable: `filedatabase`, `apikey`, `connections`, `authblocks`.
- **CI EF bundle factory throw (Q7).** `DeepDrftContextFactory` eagerly reads
  `environment/connections.json`; absent in CI → bundle step fails. Write a dummy file first.
- **GitHub vs Gitea (Q5).** `package-install.yml` and `upload-artifact@v3` are Gitea/older-API
  specific. On GitHub they need adjustment; the deploy jobs don't.
- **Flat csproj paths.** Copy-paste from Skipper's nested paths will fail the publish step.
