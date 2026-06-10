# Courtly

Tennis-court reservation system — *Razvoj softvera II* seminar (Sara Nuredinovski, index IB200067, FIT "Džemal Bijedić", AY 2025/2026).

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

- .NET 10 SDK (LTS)
- Flutter (stable) with macOS desktop + Android toolchains enabled
- Docker + Docker Compose (from feature 2 on)

## Running

> Config lives in `.env` (copy from `.env.example` and fill in). DB name is `200067`.
> Use a dedicated RabbitMQ user (not `guest` — it is loopback-only and is refused across the Docker network).

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

**Frontend**
```bash
cd frontend
flutter pub get
flutter run -d macos -t lib/main_admin.dart     # desktop admin
flutter run -d emulator-5554 -t lib/main_client.dart --dart-define=API_BASE_URL=http://10.0.2.2:5000   # mobile client
```

## Test credentials

Seeded on startup (feature 4). Every account uses the password **`test`**. See
[`docs/seed-data.md`](docs/seed-data.md) for the full list of seeded data.

| App | Username | Password | Role |
|---|---|---|---|
| Admin (desktop) | `desktop` | `test` | Admin |
| Staff (desktop) | `staff` | `test` | Staff |
| Client (mobile) | `mobile` | `test` | User |
| Client (mobile) | `emma` | `test` | User |

> Seed data is created automatically: `docker compose up` applies migrations (which insert reference data +
> roles) and then runs an idempotent runtime seeder (users, courts with images, time slots, sample
> reservations/payments/reviews/news). Re-running never duplicates rows. To reset from scratch:
> `docker compose down -v && docker compose up --build`.
