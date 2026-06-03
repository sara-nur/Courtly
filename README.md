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

> Config lives in `.env` (copy from `.env.example`). DB name is `200067`.

**Backend**
```bash
cd backend && dotnet build
# docker compose up --build      # full stack — added in feature 2
```

**Frontend**
```bash
cd frontend
flutter pub get
flutter run -d macos -t lib/main_admin.dart     # desktop admin
flutter run -d emulator-5554 -t lib/main_client.dart --dart-define=API_BASE_URL=http://10.0.2.2:5000   # mobile client
```

## Test credentials

_To be populated once seed data lands (feature 4)._

| App | Username | Password | Role |
|---|---|---|---|
| Admin (desktop) | _tbd_ | _tbd_ | Admin |
| Client (mobile) | _tbd_ | _tbd_ | User |
