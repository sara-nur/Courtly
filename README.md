# Courtly

Tennis-court reservation system

A .NET 10 REST API + separate RabbitMQ worker over PostgreSQL, with a single Flutter project producing a **desktop admin app** and a **mobile client app**.

## Repository layout

```
backend/    # .NET 10 solution — Domain, Contracts, Infrastructure, Application, Api, Worker, Tests
frontend/   # single Flutter project — main_admin.dart (desktop) + main_client.dart (mobile)
docs/        # build plan, feature roadmap, resources
docker-compose.yml   # (added in feature 2) postgres, rabbitmq, api, worker
.env.example         # documented config keys — copy to .env and fill in
```

## Prerequisites

> **Just testing the app?** You only need **Docker Desktop** (plus an **Android emulator** for the mobile app).
> No Flutter, Visual Studio, or .NET SDK required — jump to **[Testing on Windows](#testing-on-windows-no-build-tools)**.
> The list below is for **building / developing** from source.

- .NET 10 SDK (LTS)
- Flutter (stable, **3.35+**) with the **desktop toolchain for your OS** — **Windows** (Visual Studio 2022 with the "Desktop development with C++" workload, **including the C++ ATL component**) or **macOS** (Xcode) — plus the **Android** toolchain for the mobile client (and optionally iOS on macOS)
- Docker + Docker Compose (Docker Desktop on Windows/macOS)

## Testing on Windows (no build tools)

Fastest way to try the app — **no Flutter, no Visual Studio, no .NET SDK**, just **Docker Desktop**
(and an **Android emulator** for the mobile app). Everything runs from the GitHub Release + Docker.
All commands are **PowerShell**. Don't `flutter run` / rebuild to test — the prebuilt binaries avoid the
whole Windows toolchain (Flutter version, C++ build tools, ATL, …).

**1. Config** — get the ready-to-run `.env` from the `.env-tajne.zip` you received:
```powershell
tar -xf .env-tajne.zip            # password: fit  → creates .env at the repo root
```

**2. Backend** — Docker builds and runs it (no .NET SDK needed):
```powershell
docker compose up --build         # API on http://localhost:5000
curl http://localhost:5000/health # must respond before opening the apps
```

**3. Desktop admin** — download `fit-build-<date>.zip` from the Release, then run the prebuilt `.exe`:
```powershell
Expand-Archive .\fit-build-<date>.zip .\courtly-build -Force
.\courtly-build\desktop\build\windows\x64\runner\Release\courtly.exe
```
Sign in `desktop` / `test`.

**4. Mobile client** — Android emulator + the prebuilt APK:
```powershell
winget install --id Google.AndroidStudio    # once; then open it → Device Manager → create & start an emulator
# with the emulator booted (Android home screen visible):
& "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" devices    # expect: emulator-5554   device
& "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" install ".\courtly-build\mobile\build\app\outputs\flutter-apk\app-release.apk"
```
Open **Courtly** in the emulator, sign in `mobile` / `test`.

**Gotchas**
- **Order matters:** backend up (`/health` responds) → *then* open the apps.
- **`adb` "not recognized":** it isn't on PATH — call it by full path as shown (`…\Android\Sdk\platform-tools\adb.exe`).
- **Use a Google emulator** (it maps `10.0.2.2` → your PC's `localhost`, where the API runs). Physical phones / BlueStacks won't reach the backend without rebuilding the APK.
- **"Change photo" shows nothing:** a fresh emulator has an empty gallery — drag any image onto the emulator window first, then pick it.
- **Stripe test card:** `4242 4242 4242 4242`, any future expiry, any CVC/ZIP.

## Running

> **Configuration (`.env`).** All secrets/config live in a single `.env` at the repo root (DB name is `200067`).
> - **Reviewers / graders:** the working config ships as a password-protected archive. Unzip it into the repo root —
>   `unzip -P fit .env-tajne.zip` (password: **`fit`**) — which creates a ready-to-run `.env`. Nothing to fill in.
>   (On Windows: right-click the zip → Extract, enter `fit`, or `tar -xf .env-tajne.zip` in PowerShell.)
> - **Developers:** `cp .env.example .env` and fill in the keys (Postgres, JWT, Stripe **test** keys, SMTP, RabbitMQ, …).
> - Use a dedicated RabbitMQ user (not `guest` — it is loopback-only and is refused across the Docker network).

**Full stack (Docker)**
```bash
docker compose up --build      # postgres + rabbitmq + api + worker, all healthchecked
```
- API: http://localhost:5000 — health probe at http://localhost:5000/health
- RabbitMQ management UI: http://localhost:15672 (log in with `RABBITMQ_USER`/`RABBITMQ_PASSWORD`)
- `docker compose ps` should show all four services `healthy`.

> **macOS note — port 5000.** macOS "AirPlay Receiver" listens on port 5000, which the API publishes.
> If `docker compose up` fails with `bind: address already in use` on port 5000, turn it off:
> **System Settings → General → AirDrop & Handoff → AirPlay Receiver → Off** (reversible), or remap the
> API's published port in `docker-compose.yml`.

**Backend (run directly, without Docker)**
```bash
cd backend && dotnet build
# Set the DB host / RABBITMQ_HOST to localhost in .env when running this way.
dotnet run --project src/Courtly.Api      # API on http://localhost:5000
dotnet run --project src/Courtly.Worker   # connects to RabbitMQ
```

**Frontend** — one Flutter project, two entrypoints. Start with:
```bash
cd frontend
flutter pub get
flutter test                                     # widget tests
```

*Desktop admin* (`lib/main_admin.dart`) — themed top-nav (Dashboard · Reservations · Courts · Users · Reports); sign in with an Admin/Staff account. Needs the API running. Use `localhost` as the API host on both OSes.
```bash
# Windows
flutter run -d windows -t lib/main_admin.dart --dart-define=API_BASE_URL=http://localhost:5000
# macOS
flutter run -d macos   -t lib/main_admin.dart --dart-define=API_BASE_URL=http://localhost:5000
```

*Mobile client* (`lib/main_client.dart`) — bottom nav (Home / Search / Bookings / Notifications / Profile): browse/search courts, book + pay in-app (Stripe), bookings history, reviews, recommendations, live notifications, profile. Sign in with a mobile/User account.
```bash
flutter run -d emulator-5554   -t lib/main_client.dart --dart-define=API_BASE_URL=http://10.0.2.2:5000   # Android emulator (10.0.2.2 = host)
flutter run -d "iPhone 17 Pro" -t lib/main_client.dart --dart-define=API_BASE_URL=http://localhost:5000  # iOS Simulator (macOS)
```

> `API_BASE_URL` is read once via `String.fromEnvironment('API_BASE_URL')`; if omitted it defaults to
> `http://localhost:5000` (admin) / `http://10.0.2.2:5000` (mobile emulator).

> **iOS Simulator (mobile client).** One-time: `xcodebuild -downloadPlatform iOS` to install the runtime, then
> `open -a Simulator` to boot an iPhone before `flutter run`. The simulator reaches the host as `localhost`
> (use `http://localhost:5000`, not `10.0.2.2`). The macOS admin app is sandboxed, so its network + keychain
> entitlements are committed; no extra setup needed.

## Test credentials

Seeded on startup (feature 4). Every account uses the password **`test`**. See
[`docs/seed-data.md`](docs/seed-data.md) for the full list of seeded data.

| App | Username | Password | Role |
|---|---|---|---|
| Admin (desktop) | `desktop` | `test` | Admin |
| Staff (desktop) | `staff` | `test` | Staff |
| Client (mobile) | `mobile` | `test` | User |
| Client (mobile) | `emma` | `test` | User |

Note: 
> In order to receive emails you would need to update the mobile user or ema user, and input the real emal address. Alternatively, you could create a new account with the correct email address. I was testing with my real email address. 

> Seed data is created automatically: `docker compose up` applies migrations (which insert reference data +
> roles) and then runs an idempotent runtime seeder (users, courts with images, time slots, sample
> reservations/payments/reviews/news). Re-running never duplicates rows. To reset from scratch:
> `docker compose down -v && docker compose up --build`.

## Release build & submission

Binaries are **not** committed — they are produced by CI and attached to a GitHub Release.

**CI (`.github/workflows/release.yml`).** Pushing a `predaja-YYYY-MM-DD` tag (or running the
workflow manually from the Actions tab) builds both binaries and drafts a release:

- **client APK** (Android) — `lib/main_client.dart`, `API_BASE_URL=http://10.0.2.2:5000`
- **admin `.exe`** (Windows) — `lib/main_admin.dart`, `API_BASE_URL=http://localhost:5000`

They are packed into `fit-build-<date>.zip` with the required layout:

```
fit-build-<date>.zip
├── mobile/build/app/outputs/flutter-apk/app-release.apk
└── desktop/build/windows/x64/runner/Release/…        (Courtly admin .exe + data)
```

**Build locally** (optional — the same outputs CI produces; run on the matching OS):
```bash
cd frontend && flutter clean
# Windows desktop admin .exe  →  build/windows/x64/runner/Release/
flutter build windows --release -t lib/main_admin.dart  --dart-define=API_BASE_URL=http://localhost:5000
# Android client APK          →  build/app/outputs/flutter-apk/app-release.apk
flutter build apk     --release -t lib/main_client.dart --dart-define=API_BASE_URL=http://10.0.2.2:5000
```
> The Windows `.exe` can only be built on Windows (or the `windows-latest` CI job); the APK builds on any OS.

**Publishing (immutable).** In **Settings → Releases**, enable **release immutability** first
(applies to future releases only). The workflow creates the release as a **draft** — verify the
ZIP contents, then **Publish**. Submit the tag-specific release link
(`…/releases/tag/predaja-YYYY-MM-DD`), never `releases/latest`.

**Secrets.** The `.env` is git-ignored and lives only on your machine. For grading, package the
working config as a password-protected archive:

```bash
zip -P fit .env-tajne.zip .env      # password: fit
```

Submit `.env-tajne.zip` **only through the private DL system** (with the password `fit`). It is
git-ignored, and it must **never** be committed to this public repo or attached to a GitHub Release —
with a known password, a public archive exposes every secret. Never put a plain `.env` anywhere public
either.

**Clean-environment run** (what a grader does): fresh clone → `unzip -P fit .env-tajne.zip` →
`docker compose up --build` → all four services healthy, log in with the credentials above —
with **no** code, port, or connection-string edits.
