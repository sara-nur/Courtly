# Courtly

**A tennis-court reservation and management system.**

Players discover, book, and pay for tennis courts from a **mobile app**; administrators manage courts, reservations, users, and reporting from a **desktop app**. Courtly is built as a small microservice system — a .NET 10 REST API and a separate background worker communicate over RabbitMQ, backed by PostgreSQL — with a single Flutter codebase producing both front-ends.

---

## Overview

**Two applications, one Flutter project:**

- **Mobile client (Android)** — browse and search courts, view court details and reviews, book a time slot, pay in-app with Stripe, manage bookings (history, cancel, refund), receive live notifications, edit profile, and get explainable court recommendations.
- **Desktop admin (Windows)** — dashboard with KPIs and charts, reservation management, court catalog with images/amenities/map location, user management, reference-data CRUD, downloadable and printable PDF reports, and news management.

**Backend highlights:**

- JWT authentication with role-based authorization (**Admin / Staff / User**).
- Centralized reservation state machine (Pending → Confirmed → Completed / Cancelled) with an audit trail and server-side overlap protection.
- Server-side Stripe payments (sandbox), finalized via webhook, including refunds.
- Real-time notifications over SignalR with a polling fallback; transactional emails handled by the worker.
- Content-based + popularity recommender with human-readable explanations.

## Architecture

```
  Flutter apps ──HTTP / JWT──▶  Courtly.Api  ──publish──▶  RabbitMQ  ──consume──▶  Courtly.Worker
       ▲                            │                                                   │
       └──────── SignalR ───────────┘                                          e-mail (SMTP)
                                     │
                        PostgreSQL (database 200067) · Stripe (sandbox)
```

- **Courtly.Api** — the main REST service; also hosts the SignalR hub.
- **Courtly.Worker** — a separate service/container that consumes RabbitMQ messages to send emails and create/push notifications.
- **PostgreSQL** — the relational database (named `200067`).
- **RabbitMQ** — the message broker between the two services.

## Tech stack

| Layer | Technology |
|---|---|
| API & Worker | .NET 10, ASP.NET Core, EF Core |
| Database | PostgreSQL 16 |
| Messaging | RabbitMQ 3.13 |
| Real-time | SignalR |
| Payments | Stripe (sandbox) |
| PDF reports | QuestPDF |
| Front-end | Flutter — Windows desktop + Android mobile |
| Infrastructure | Docker Compose |

---

## Getting started

There are two ways to run Courtly:

- **Option A — Run the prebuilt release** (recommended for reviewers). Needs only **Docker Desktop**, plus an **Android emulator** for the mobile app. No Flutter, Visual Studio, or .NET SDK required.
- **Option B — Run from source** (for developers who want to build or modify the code).

Either way, start with **Configuration** and **Start the backend** below.

### Prerequisites

**To test the prebuilt apps:**
- Docker Desktop
- An Android emulator (via Android Studio) — only needed for the mobile app

**To build from source (developers):**
- .NET 10 SDK
- Flutter (stable, **3.35 or newer**) with the desktop toolchain for your OS:
  - **Windows** — Visual Studio 2022 with the *Desktop development with C++* workload, including the **C++ ATL** component
  - **macOS** — Xcode
  - plus the Android toolchain for the mobile client
- Docker Desktop

### 1. Configuration (`.env`)

All configuration and secrets live in a single `.env` file at the repository root.

- **Reviewers** — the working configuration is provided as a password-protected archive, `.env-tajne.zip` (submitted via the DL system; password **`fit`**). Extract it into the repository root:
  ```bash
  unzip -P fit .env-tajne.zip     # macOS / Linux
  tar  -xf   .env-tajne.zip       # Windows PowerShell
  ```
  This produces a ready-to-run `.env` — nothing to fill in.
- **Developers** — copy the template and provide your own keys:
  ```bash
  cp .env.example .env
  ```

### 2. Start the backend (Docker)

From the repository root:
```bash
docker compose up --build
```
This builds and runs all four services — PostgreSQL, RabbitMQ, the API, and the worker — and seeds sample data automatically on first run.

- API: `http://localhost:5000` (health check: `http://localhost:5000/health`)
- RabbitMQ management UI: `http://localhost:15672`
- `docker compose ps` should report all services as healthy.

> **macOS, port 5000:** if the API fails to bind port 5000, turn off *AirPlay Receiver* (System Settings → General → AirDrop & Handoff), which reserves that port on macOS.

### Option A — Run the prebuilt release (recommended for reviewers)

Download **`fit-build-<date>.zip`** from the project's **GitHub Release** and extract it; it contains the Windows desktop executable and the Android APK. **Ensure the backend is running first** (step 2 — `http://localhost:5000/health` should respond).

**Windows desktop admin** — run the executable (no Flutter or Visual Studio needed):
```powershell
Expand-Archive .\fit-build-<date>.zip .\courtly-build -Force
.\courtly-build\desktop\build\windows\x64\runner\Release\courtly.exe
```
Sign in with an Admin or Staff account (see [Test credentials](#test-credentials)).

**Android mobile client** — install the APK on a running **Google Android emulator**:
```powershell
# 1. Start an emulator: Android Studio → Device Manager → create & launch a device.
# 2. Install the APK (adb ships with the Android SDK):
& "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" devices     # expect: emulator-5554   device
& "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" install ".\courtly-build\mobile\build\app\outputs\flutter-apk\app-release.apk"
```
Open **Courtly** in the emulator and sign in with a client account.

> The APK targets `http://10.0.2.2:5000` — the Google emulator's alias for the host machine's `localhost`. Use a **Google Android emulator** (not a third-party emulator or a physical device) so it can reach the backend without rebuilding.

### Option B — Run from source (developers)

Front-end:
```bash
cd frontend
flutter pub get
```
**Desktop admin** (API host is `localhost` on both platforms):
```bash
flutter run -d windows -t lib/main_admin.dart --dart-define=API_BASE_URL=http://localhost:5000   # Windows
flutter run -d macos   -t lib/main_admin.dart --dart-define=API_BASE_URL=http://localhost:5000   # macOS
```
**Mobile client:**
```bash
flutter run -d emulator-5554   -t lib/main_client.dart --dart-define=API_BASE_URL=http://10.0.2.2:5000   # Android emulator
flutter run -d "iPhone 17 Pro" -t lib/main_client.dart --dart-define=API_BASE_URL=http://localhost:5000  # iOS Simulator (macOS)
```

Optionally, run the backend without Docker (set the database and RabbitMQ hosts to `localhost` in `.env`):
```bash
cd backend
dotnet run --project src/Courtly.Api      # API on http://localhost:5000
dotnet run --project src/Courtly.Worker   # background worker
```

> The API base URL is read once via `String.fromEnvironment('API_BASE_URL')`.

---

## Test credentials

All seeded accounts use the password **`test`**.

| Application | Username | Role |
|---|---|---|
| Desktop admin | `desktop` | Admin |
| Desktop admin | `staff` | Staff |
| Mobile client | `mobile` | User |
| Mobile client | `emma` | User |

- **Stripe test card** (mobile payment): `4242 4242 4242 4242`, any future expiry date, any CVC and postal code.
- **Email:** by default, outgoing mail is captured locally (Mailpit) and not delivered to real inboxes. To receive real email, configure SMTP in `.env` and set a real address on a client account.
- **Reset the data:** `docker compose down -v && docker compose up --build` recreates and re-seeds the database.

---

## Troubleshooting

| Symptom | Resolution |
|---|---|
| App can't sign in / shows no data | Start the backend first — `http://localhost:5000/health` must respond before opening the apps. |
| `adb` "not recognized" (Windows) | Call it by full path: `& "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" …`, or add `platform-tools` to PATH. |
| Mobile app can't reach the backend | Use a **Google** Android emulator (it maps `10.0.2.2` to the host's `localhost`). |
| "Change photo" shows no images | A fresh emulator has an empty gallery — drag an image onto the emulator window first, then pick it. |
| Building from source fails on `initialValue` | Flutter is older than 3.35 — run `flutter upgrade`. |
| Windows build: `atlstr.h` not found | Add the **C++ ATL** component to Visual Studio 2022 (Installer → Modify → Individual components). |
| macOS: port 5000 already in use | Disable AirPlay Receiver (see step 2). |

---

## Building & release

Binaries are **not** committed to the repository; they are produced by CI and attached to a GitHub Release.

**CI (`.github/workflows/release.yml`).** Pushing a `predaja-YYYY-MM-DD` tag (or running the workflow manually) builds both binaries and drafts a release:

- Android client APK — built from `lib/main_client.dart` with `API_BASE_URL=http://10.0.2.2:5000`
- Windows admin `.exe` — built from `lib/main_admin.dart` with `API_BASE_URL=http://localhost:5000`

packaged as `fit-build-<date>.zip`:
```
fit-build-<date>.zip
├── mobile/build/app/outputs/flutter-apk/app-release.apk
└── desktop/build/windows/x64/runner/Release/…
```

**Build locally** (optional; run on the matching OS):
```bash
cd frontend && flutter clean
flutter build windows --release -t lib/main_admin.dart  --dart-define=API_BASE_URL=http://localhost:5000
flutter build apk     --release -t lib/main_client.dart --dart-define=API_BASE_URL=http://10.0.2.2:5000
```

**Publish.** Enable release immutability (Settings → Releases) before publishing the draft, then submit the tag-specific release link (`…/releases/tag/predaja-YYYY-MM-DD`).

**Secrets.** The `.env` is never committed. It is packaged as `.env-tajne.zip` (`zip -P fit .env-tajne.zip .env`) and provided **only through the private DL system** — never committed to this public repository nor attached to a release.

---

## Automated tests (developers)

```bash
cd backend  && dotnet test        # backend unit tests
cd frontend && flutter test       # widget tests
cd frontend && flutter analyze    # static analysis
```

---

## Project structure

```
backend/                       .NET 10 solution — Domain, Contracts, Infrastructure, Application, Api, Worker, Tests
frontend/                      Flutter project — main_admin.dart (desktop) + main_client.dart (mobile)
docker-compose.yml             PostgreSQL, RabbitMQ, API, and worker services
.env.example                   documented configuration keys
recommender-dokumentacija.md   recommender algorithm documentation
.github/workflows/             CI release pipeline
```
