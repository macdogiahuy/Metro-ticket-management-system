# 🚇 Metro Ticket Booking & Management System

> Full-stack platform for urban metro ticket booking, digital ticketing,
> QR validation, and metro operations management.

![Status](https://img.shields.io/badge/status-in_development-yellow)
![Frontend](https://img.shields.io/badge/frontend-React%20%2B%20TypeScript-61DAFB)
![Backend](https://img.shields.io/badge/backend-ASP.NET%20Core-512BD4)
![Database](https://img.shields.io/badge/database-PostgreSQL-4169E1)
![Docker](https://img.shields.io/badge/container-Docker-2496ED)

## 1. Overview

The **Metro Ticket Booking & Management System** is a student-built
full-stack web application that digitizes passenger ticketing and
supports basic metro operations.

The platform serves three main roles:

-   **Passenger:** search journeys, view fares, book tickets, pay, view
    digital tickets, and request eligible cancellations/refunds.
-   **Station Staff:** scan QR codes and validate tickets.
-   **Administrator:** manage users, stations, metro lines, routes,
    schedules, fares, bookings, refunds, and reports.

This project uses a **single Git monorepository** containing the
frontend, backend, infrastructure configuration, documentation, and CI
workflows. Frontend and backend remain independently buildable and
deployable applications.

> **Project scope:** Educational and portfolio project. It is not a
> certified railway fare-collection or safety-critical system. Do not
> connect it to real metro gates or process real payments without proper
> security, operational, legal, and reliability reviews.

### 1.1 Delivery scope (course project)

The requirements baseline is **SRS v1.2** (`docs/requirements/Metro_SRS_v1.2.docx`)
with **UC v1.6** as supporting input; where they disagree, SRS v1.2 wins
(§1.4). The team implements the **27 Must-have use cases** for the demo:

``` text
UC01–UC19, UC22–UC25, UC29, UC31, UC33, UC36
```

- **UC01–UC04** account (register, login, logout, password reset)
- **UC05–UC06** profile and concession verification
- **UC07–UC10** lines, stations, journeys, fares, service status
- **UC11–UC12** purchase and payment retry (MoMo TEST)
- **UC13–UC15** my tickets, booking and payment history
- **UC16–UC19** gate validation, denial handling, staff lookup and QR reissue
- **UC22–UC25** accounts/roles, passenger data, network and product admin
- **UC29, UC31** schedules/trips and service notices
- **UC33** ticket and revenue reports
- **UC36** automatic trip suggestion for single-trip tickets (a suggestion,
  not a seat hold)

**Out of scope — not built for the demo** (the database schema and SQL
functions already support most of them): UC28 full audit workspace, UC37
advanced capacity, UC39–UC41 buying for others / identity / reallocation,
UC44 accessibility requests, UC45 on-board incidents, UC46 lost property,
UC47 demand forecasting. UC20–UC21, UC26–UC27, UC30, UC32, UC34–UC35,
UC38 and UC42–UC43 are **Stretch** and only start if the Must-have scope
and the schedule allow it. See `docs/planning/TASK-ASSIGNMENT.md` §10.

Also out of scope per SRS §1.3: real money, physical gates, seat
reservation, 3-day tickets, tax invoices, biometrics/OCR, voluntary
refunds or disruption compensation, SMS/push, offline validation, and
home delivery of lost items.

## 2. Repository Structure

``` text
metro-ticket-system/
├── frontend/                       # React + TypeScript
│   ├── public/
│   ├── src/
│   ├── tests/
│   ├── .env.example
│   ├── package.json
│   └── README.md                   # Optional detailed FE guide
│
├── backend/                        # ASP.NET Core Web API
│   ├── src/
│   │   ├── Metro.API/
│   │   ├── Metro.Application/
│   │   ├── Metro.Domain/
│   │   └── Metro.Infrastructure/
│   ├── tests/
│   ├── docs/
│   ├── Metro.sln
│   └── README.md                   # Optional detailed BE guide
│
├── infra/
│   └── docker/
│       └── docker-compose.yml
│
├── docs/
│   ├── architecture/
│   ├── database/
│   ├── api/
│   ├── requirements/
│   └── diagrams/
│
├── .github/
│   └── workflows/
│       ├── frontend-ci.yml
│       └── backend-ci.yml
│
├── .gitignore
├── .env.example
└── README.md
```

The root README documents the whole system. Optional frontend/backend
README files can contain more detailed setup instructions for each
application.

## 3. Technology Stack

### Frontend

  Category       Technology                       Purpose
  -------------- -------------------------------- ------------------------------------
  Framework      React                            Component-based UI
  Language       TypeScript                       Type safety
  Build tool     Vite                             Development and production builds
  UI library     Ant Design                       Forms, tables, dialogs, navigation
  Styling        Tailwind CSS                     Utility styling where useful
  Routing        React Router                     Client-side navigation
  Server state   TanStack Query                   API fetching, caching, mutations
  Client state   Zustand                          Shared client-side state
  HTTP client    Axios                            REST API communication
  Forms          React Hook Form                  Form state
  Validation     Zod                              Client-side validation
  Charts         Recharts                         Admin dashboard
  QR scanning    html5-qrcode or equivalent       Camera-based QR scanning
  Testing        Vitest + React Testing Library   Unit/component tests
  Code quality   ESLint + Prettier                Linting and formatting

State conventions: - TanStack Query owns server state. - Zustand is for
small client-side state, not a duplicate API cache. - React Hook Form
owns form state. - The backend remains authoritative for permissions,
bookings, payments, and ticket status.

### Backend

  -------------------------------------------------------------------------------
  Category                Technology                      Purpose
  ----------------------- ------------------------------- -----------------------
  Runtime                 .NET 9 (SDK 9.0.320,        Application runtime
                          pinned in
                          `backend/global.json`)

  Framework               ASP.NET Core Web API            REST API

  Language                C#                              Backend implementation

  ORM                     Entity Framework Core           Data access

  Database                PostgreSQL                      Primary relational
                                                          database

  EF provider             Npgsql                          PostgreSQL integration

  Identity                ASP.NET Core Identity           User/password
                                                          management

  Authentication          JWT bearer                      API authentication

  Authorization           Role/policy-based               Access control

  Validation              FluentValidation                Request/use-case
                                                          validation

  API docs                OpenAPI / Swagger UI            API contract

  Logging                 Microsoft.Extensions.Logging;   Application logs
                          Serilog optional                

  Cache                   Redis (optional)                Cache and short-lived
                                                          data

  Testing                 xUnit                           Automated tests

  Mocking                 NSubstitute or Moq              Test doubles
  -------------------------------------------------------------------------------

Use the pinned SDK from `backend/global.json` (.NET 9, 9.0.320). Do not
upgrade the target framework mid-project; revisit only after the course
delivery.

### Infrastructure and DevOps

  -----------------------------------------------------------------------
  Category                Technology              Purpose
  ----------------------- ----------------------- -----------------------
  Containers              Docker                  Consistent local
                                                  environment

  Orchestration           Docker Compose          Local PostgreSQL,
                                                  Redis, optional app
                                                  services

  Source control          Git + GitHub            Collaboration

  CI                      GitHub Actions          Build, test, lint

  Frontend hosting        Cloudflare Pages Free   Static frontend hosting

  Backend hosting         Render Free             Containerized API demo

  PostgreSQL hosting      Neon Free               Managed PostgreSQL

  Redis hosting           Upstash Free (optional) Managed Redis
  -----------------------------------------------------------------------

Free tiers have quotas and policies that can change. They are suitable
for a student demo, not guaranteed production availability.

## 4. System Architecture

### High-level architecture

``` mermaid
flowchart TD
    P[Passenger Browser]
    S[Station Staff Browser]
    A[Admin Browser]
    FE[React + TypeScript Frontend]
    API[ASP.NET Core Web API]
    APP[Application Layer]
    DOMAIN[Domain Layer]
    INFRA[Infrastructure Layer]
    DB[(PostgreSQL)]
    REDIS[(Redis - Optional)]
    PAY[Payment Provider / Sandbox]

    P --> FE
    S --> FE
    A --> FE
    FE -->|HTTPS REST API / JSON| API
    API --> APP
    APP --> DOMAIN
    INFRA --> APP
    INFRA --> DOMAIN
    INFRA --> DB
    INFRA --> REDIS
    INFRA --> PAY
```

### Architecture decisions

-   **Monorepo:** FE and BE share one Git repository.
-   **Independent applications:** separate dependencies, build commands,
    environment variables, and deployment pipelines.
-   **Modular Monolith:** one backend deployment with clear
    business-module boundaries.
-   **Clean Architecture:** Domain, Application, Infrastructure, and API
    layers.
-   **Feature-based frontend:** UI code grouped by business capability.
-   **REST API:** FE communicates with BE over HTTPS and JSON.
-   **PostgreSQL is the source of truth:** Redis is optional and
    non-authoritative.

## 5. Backend Architecture

``` text
backend/
├── src/
│   ├── Metro.API/
│   │   ├── Controllers/
│   │   ├── Middleware/
│   │   ├── Filters/
│   │   ├── Extensions/
│   │   ├── Configurations/
│   │   └── Program.cs
│   ├── Metro.Application/
│   │   ├── Common/
│   │   │   ├── Abstractions/
│   │   │   ├── Behaviors/
│   │   │   ├── Exceptions/
│   │   │   └── Models/
│   │   ├── Features/
│   │   │   ├── Authentication/
│   │   │   ├── Users/
│   │   │   ├── Stations/
│   │   │   ├── Lines/
│   │   │   ├── Routes/
│   │   │   ├── Schedules/
│   │   │   ├── Fares/
│   │   │   ├── Bookings/
│   │   │   ├── Tickets/
│   │   │   ├── Payments/
│   │   │   ├── Refunds/
│   │   │   ├── TicketValidation/
│   │   │   └── Reports/
│   │   └── DependencyInjection.cs
│   ├── Metro.Domain/
│   │   ├── Entities/
│   │   ├── ValueObjects/
│   │   ├── Enums/
│   │   ├── Events/
│   │   ├── Exceptions/
│   │   └── Common/
│   └── Metro.Infrastructure/
│       ├── Persistence/
│       │   ├── Context/
│       │   ├── Configurations/
│       │   ├── Migrations/
│       │   └── Repositories/
│       ├── Identity/
│       ├── Payments/
│       ├── QRCode/
│       ├── Caching/
│       └── DependencyInjection.cs
├── tests/
│   ├── Metro.UnitTests/
│   └── Metro.IntegrationTests/
├── docs/
└── Metro.sln
```

  -----------------------------------------------------------------------
  Layer                               Responsibility
  ----------------------------------- -----------------------------------
  Domain                              Entities, value objects, enums,
                                      domain events, business invariants

  Application                         Use cases, commands/queries, DTOs,
                                      validators, interfaces

  Infrastructure                      EF Core, repositories, Identity
                                      persistence, Redis, payment
                                      adapters

  API                                 HTTP endpoints, middleware,
                                      authentication setup, dependency
                                      composition
  -----------------------------------------------------------------------

Dependency rules: - Domain has no dependency on other application layers
or infrastructure. - Application depends on Domain. - Infrastructure
implements abstractions defined by Application/Domain. - API is the
composition root. - Application must not directly depend on
Infrastructure. - Controllers remain thin; business rules belong in
Domain/Application.

Use feature-oriented use cases and CQRS concepts where they improve
clarity. MediatR is optional, not mandatory.

Example:

``` text
Features/Bookings/
├── Commands/
│   ├── CreateBooking/
│   └── CancelBooking/
├── Queries/
│   ├── GetBookingById/
│   └── GetMyBookings/
└── DTOs/
```

## 6. Frontend Architecture

``` text
frontend/
├── public/
├── src/
│   ├── app/
│   │   ├── providers/
│   │   ├── router/
│   │   └── layouts/
│   ├── components/
│   │   ├── common/
│   │   ├── ui/
│   │   └── feedback/
│   ├── features/
│   │   ├── auth/
│   │   ├── stations/
│   │   ├── lines/
│   │   ├── routes/
│   │   ├── schedules/
│   │   ├── fares/
│   │   ├── bookings/
│   │   ├── tickets/
│   │   ├── payments/
│   │   ├── refunds/
│   │   ├── staff/
│   │   ├── dashboard/
│   │   └── reports/
│   ├── hooks/
│   ├── lib/
│   │   ├── axios.ts
│   │   └── query-client.ts
│   ├── stores/
│   ├── types/
│   ├── constants/
│   ├── styles/
│   ├── App.tsx
│   └── main.tsx
├── tests/
├── .env.example
├── package.json
└── tsconfig.json
```

Each feature should own its API functions, hooks, components, pages,
schemas, and types where practical. Shared components should remain
generic and avoid feature-specific business logic.

## 7. Roles and Functional Modules

  -----------------------------------------------------------------------
  Role                                Main permissions
  ----------------------------------- -----------------------------------
  Passenger                           Search journeys, view fares,
                                      book/pay, view tickets, request
                                      eligible refunds

  Staff                               Scan QR codes and validate tickets

  Admin                               Manage users, stations, lines,
                                      routes, schedules, fares, bookings,
                                      refunds, reports
  -----------------------------------------------------------------------

Enforce all permissions in the backend. Frontend route guards and hidden
buttons are not security controls.

  Module                Capabilities
  --------------------- ---------------------------------------------------
  Authentication        Register, login, logout, refresh token
  User management       Profiles, roles, account status
  Station management    Station information and status
  Line management       Metro line information
  Route management      Ordered station paths and journey search
  Schedule management   Train schedules
  Fare management       Fare rules and ticket products
  Booking               Booking creation, expiry, cancellation
  Payment               Payment initiation and provider verification
  Ticket                Digital ticket issuance and validity
  QR validation         Ticket verification and usage state
  Refund                Eligibility, requests, processing, reconciliation
  Reporting             Booking, payment, usage, revenue summaries
  Audit                 Sensitive admin and validation events

## 8. Database Design

PostgreSQL 17 is the primary database. The physical schema is versioned with
plain SQL migrations under `infra/database/migrations` (not EF Core
migrations); EF Core only maps the schema. See:
`infra/database/documentation/DATABASE_DESIGN.md`, `ERD.md`,
`BUSINESS_RULE_MAPPING.md`, and `DEPLOYMENT_GUIDE.md`.

Actual schema (SRS v1.2, migration `006_srs_v1_2_alignment.sql`):

- **Schema `metro`** — 93 business tables plus 3 views, 329 indexes and
  297 foreign keys, all operator-scoped through composite keys.
- **Schema `public`** — 7 ASP.NET Core Identity tables.
- **13 `SECURITY DEFINER` workflow functions** — payment confirmation and
  issuance, free issuance for children under 6, unpaid cancellation,
  ticket revocation, disruption ticket change, accessibility slot
  confirmation/release, and overdue lost-property marking, among others.
- **25 retention triggers** that reject `DELETE`/`TRUNCATE` on history,
  finance and audit tables at statement level.

Main table groups:

  -----------------------------------------------------------------------
  Group                               Tables
  ----------------------------------- -----------------------------------
  Identity (`public`)                 `AspNetUsers`, `AspNetRoles`,
                                      `AspNetUserRoles`, `AspNetUserClaims`,
                                      `AspNetRoleClaims`, `AspNetUserLogins`,
                                      `AspNetUserTokens`

  Network                             `operators`, `lines`, `stations`,
                                      `line_stations`, `journeys`,
                                      `journey_legs`

  Operations                          `trainsets`, `cars`, `planned_trips`,
                                      `planned_stop_times`,
                                      `trip_allocations`,
                                      `capacity_profiles`,
                                      `load_observations`,
                                      `trip_eta_updates`

  Fare and catalog                    `ticket_products`, `fare_policies`,
                                      `fare_policy_versions`, `fare_rules`,
                                      `concession_policies`

  Order and ticketing                 `orders`, `order_versions`,
                                      `order_items`, `trip_suggestions`,
                                      `purchase_consents`, `tickets`,
                                      `ticket_credentials`,
                                      `guardian_links`,
                                      `ticket_change_requests`

  Payment and refund                  `payments`, `payment_events`,
                                      `refund_requests`,
                                      `refund_transactions`,
                                      `refund_attempts`

  Passenger support                   `accessibility_requests`,
                                      `station_support_capacities`,
                                      `lost_reports`, `found_items`,
                                      `lost_item_claims`,
                                      `lost_item_handovers`

  Reporting and audit                 `data_quality_days`,
                                      `demand_forecasts`,
                                      `audit_logs`, `notifications`,
                                      `notification_deliveries`
  -----------------------------------------------------------------------

The database `README.md` explains the deploy/test commands; the design
documents above have the full 93-table list. EF Core entity names use
PascalCase and map to these snake_case tables.

Important distinctions: - Station is a physical station. - Line is a
metro service line. - Journey is a path option between two stations. -
PlannedTrip is a scheduled departure with a load profile. - Order is a
passenger purchase containing one passenger and one ticket product per
SRS v1.2, with versioned snapshots in `order_versions`. - Ticket is an
individual travel entitlement with a validity window; overlapping active
validity for the same holder is rejected by the GiST exclusion
constraint `ex_tickets_no_overlapping_validity`. - TripSuggestion is a
hint for a single-trip order and is not a seat hold. - Payment records
the payment lifecycle and provider references. - RefundRequest records
an erroneous-charge or disruption refund. - TicketChangeRequest records
the single allowed disruption change per ticket.

> The SRS v1.1 capacity-hold chain (`capacity_profiles`,
> `trip_capacity_segments`, `capacity_holds`,
> `capacity_hold_segments` and the `hold_trip_capacity` family of
> functions) still exists physically but is **legacy**: no current flow
> creates holds or quotas.

``` mermaid
erDiagram
    ORDERS ||--|{ ORDER_VERSIONS : versions
    ORDERS ||--|{ ORDER_ITEMS : contains
    ORDER_VERSIONS ||--o| TRIP_SUGGESTIONS : suggests
    ORDERS ||--o{ PURCHASE_CONSENTS : requires
    ORDER_ITEMS ||--o{ TICKETS : issues
    TICKETS ||--o{ TICKET_CREDENTIALS : represented_by
    TICKETS ||--o{ TICKET_CHANGE_REQUESTS : changes
    TICKETS ||--o{ JOURNEYS : used_for
    ORDERS ||--o{ PAYMENTS : paid_by
    PAYMENTS ||--o{ PAYMENT_EVENTS : receives
    TICKETS ||--o{ REFUND_REQUESTS : refunds
```

The full ERD, including operator/network/operations tables and the
legacy chain, is in `infra/database/documentation/ERD.md`.

## 9. Core Business Workflows

### Booking and payment

``` mermaid
sequenceDiagram
    actor Passenger
    participant FE as React Frontend
    participant API as ASP.NET Core API
    participant DB as PostgreSQL
    participant PG as Payment Provider

    Passenger->>FE: Select journey and ticket
    FE->>API: Search route and request fare quote
    API->>DB: Read route and fare rules
    API-->>FE: Return options and fare
    Passenger->>FE: Confirm booking
    FE->>API: Create booking
    API->>DB: Save Pending booking
    API-->>FE: Return booking/payment instructions
    FE->>PG: Open hosted checkout
    PG-->>API: Signed payment webhook
    API->>API: Verify signature and idempotency
    API->>DB: Confirm payment and issue ticket
    FE->>API: Fetch booking/ticket state
    API-->>FE: Return authoritative ticket data
```

Rules: 1. Backend calculates the fare; never trust a browser-supplied
total. 2. Booking starts in a pending state. 3. Verify payment using a
signed callback or server-to-server verification. 4. Never issue a paid
ticket based only on a browser redirect. 5. Process payment callbacks
idempotently. 6. Define pending-booking expiry and cancellation rules.
7. Use database transactions for payment confirmation and ticket
issuance.

### Cancellation, erroneous charge and disruption change

Per SRS v1.2, keep these concepts separate:

-   **Unpaid order cancellation** — the order expires or is cancelled and
    the suggested trip is released; no refund transaction exists.
-   **Erroneous-charge refund** — a payment was collected wrongly and is
    returned through the original payment method. This is not a
    voluntary refund.
-   **Disruption ticket change** — a ticket affected by an operator
    disruption is reissued for another date/trip **at most once per
    ticket**; the old QR is revoked.
-   Voluntary refunds, refunds with a fee, price-difference payouts and
    disruption compensation are **out of scope**.

Workflow: 1. Backend checks ownership, ticket/order state and policy. 2.
Backend calculates the amount to return (no fee). 3. Ticket is blocked
from use while the refund is processed. 4. Backend submits an idempotent
refund request to the provider. 5. The result is confirmed through a
webhook or provider status query. 6. Ticket, order and refund states are
updated consistently in one transaction. 7. The passenger sees the
result and the refund history.

Never credit a user balance directly unless the system implements an
actual internal wallet. Do not retry an ambiguous refund blindly after a
timeout; reconcile the original provider refund reference first.

### QR ticket validation

``` mermaid
sequenceDiagram
    actor Staff
    participant FE as Staff Web UI
    participant API as ASP.NET Core API
    participant DB as PostgreSQL

    Staff->>FE: Scan QR code
    FE->>API: POST /api/tickets/validate
    API->>DB: Find ticket and inspect status
    API->>API: Verify validity and journey rules
    API->>DB: Atomically mark ticket used if valid
    API-->>FE: Return validation result and reason
```

QR data should contain an opaque, unpredictable token or signed
reference, not personal information or trusted fare data. Use
transactions and concurrency-safe updates to prevent duplicate use of
single-use tickets.

## 10. API Conventions

All endpoints are under `/api/v1`. The frozen contract (API freeze 04/12)
is documented in `docs/planning/TASK-ASSIGNMENT.md` §6.

  Group      Endpoints
  ---------- ---------------------------------------------------------------------------------
  Auth       `POST /auth/register`, `/auth/login`, `/auth/logout`, `/auth/refresh`, `/auth/forgot-password`
  Profile    `GET /me`, `PUT /me`, `/me/eligibility`
  Catalog    `GET /lines`, `/stations`, `/journeys`, `/fares/quote`, `/service-status`, `/trips/{id}/load`
  Booking    `POST /orders`, `GET /orders/{id}`, `GET /orders/{id}/suggestion`, `POST /orders/{id}/consent`, `DELETE /orders/{id}`
  Payment    `POST /payments`, `POST /payments/{id}/retry`, `POST /webhooks/momo`
  Tickets    `GET /tickets`, `/tickets/{id}`, `/tickets/{id}/qr`, `GET /orders`, `GET /payments`, `POST /orders/{id}/issue-free`
  Gate       `POST /tickets/validate`, `GET /tickets/lookup`
  Admin      `GET/POST /admin/accounts`, `/admin/roles`, `/admin/lines`, `/admin/stations`, `/admin/products`, `/admin/fares`, `/admin/schedules`, `/admin/trips`, `/admin/trip-load`, `/admin/notices`
  Reports    `GET /reports/revenue`, `/reports/tickets`, `/reports/revenue/export`
  Cancel /   `POST /orders/{id}/cancel`, `POST /refunds`, `POST /refunds/{id}/approve`
  refund     
  Change     `POST /tickets/{id}/change-requests`, `POST /ticket-change-requests/{id}/approve`

Suggested HTTP status codes: `200` success, `201` created, `204` no
content, `400` invalid request, `401` unauthenticated, `403` forbidden,
`404` not found, `409` state/concurrency conflict, `422` optional
business validation convention, `500` unexpected server error.

Error responses use RFC 7807 ProblemDetails with a stable machine-readable
`code`, for example:

``` json
{
  "type": "https://metro.example/errors/booking-capacity-exceeded",
  "title": "Trip load is full",
  "status": 409,
  "code": "TRIP_LOAD_EXCEEDED",
  "detail": "The selected trip has reached its manual load limit.",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
}
```

Successful responses return the resource or a small envelope
(`items`, `page`, `pageSize`, `total`); the backend never trusts a
client-supplied price, role, payment state or ticket status.

Use OpenAPI as the API contract and keep frontend types aligned.
Consider generating TypeScript types from OpenAPI when the contract
stabilizes.

## 11. Redis Strategy

Redis is optional. Start with PostgreSQL only and add Redis when there
is a concrete use case.

Suitable uses: - Cache station and line metadata. - Cache route lookup
data with a clear TTL/invalidation policy. - Store short-lived
rate-limit counters. - Store temporary non-authoritative workflow data.

Do not store authoritative booking, payment, or ticket state only in
Redis. PostgreSQL remains the source of truth. The system must remain
correct when Redis is unavailable or its cache is empty.

## 12. Local Development

### Prerequisites

-   Git.
-   Node.js LTS and npm (or one package manager selected by the team).
-   .NET 9 SDK (pinned in `backend/global.json`).
-   Docker Desktop / Docker Engine with Compose.
-   VS Code, Visual Studio, or Rider.

### Clone

``` bash
git clone <REPOSITORY_URL>
cd metro-ticket-system
```

### Environment files

Create local files from: - Root `.env.example` -
`backend/.env.example` - `frontend/.env.example`

Never commit actual `.env` files or secrets.

### Start PostgreSQL and Redis

Example `infra/docker/docker-compose.yml`:

``` yaml
services:
  postgres:
    image: postgres:17-alpine
    container_name: metro-postgres
    restart: unless-stopped
    environment:
      POSTGRES_DB: metro_ticket
      POSTGRES_USER: metro_user
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
    ports:
      - "5432:5432"
    volumes:
      - postgres_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U metro_user -d metro_ticket"]
      interval: 10s
      timeout: 5s
      retries: 5

  redis:
    image: redis:7-alpine
    container_name: metro-redis
    restart: unless-stopped
    ports:
      - "6379:6379"
    command: ["redis-server", "--appendonly", "yes"]
    volumes:
      - redis_data:/data

volumes:
  postgres_data:
  redis_data:
```

From the repository root:

``` bash
docker compose --env-file .env -f infra/docker/docker-compose.yml up -d
```

Example local PostgreSQL connection string for an API running on the
host:

``` text
Host=localhost;Port=5432;Database=metro_ticket;Username=metro_user;Password=your-local-password
```

For an API container on the same Compose network, use `Host=postgres`
instead of `localhost`.

### Run backend

``` bash
cd backend
dotnet restore
dotnet build
dotnet ef database update --project src/Metro.Infrastructure --startup-project src/Metro.API
dotnet run --project src/Metro.API
```

The EF command may need adjustment to match the actual DbContext and
migration assembly.

### Run frontend

In another terminal:

``` bash
cd frontend
npm install
npm run dev
```

Vite normally serves the frontend at `http://localhost:5173`. Set
`VITE_API_BASE_URL` to the local API URL printed by ASP.NET Core.

## 13. Docker Strategy

During early development, it is acceptable to run PostgreSQL and Redis
in Docker while running FE and BE directly on the host for easier
debugging.

For a fully containerized environment, add: - Multi-stage Dockerfile for
ASP.NET Core. - Frontend Dockerfile or static build stage. - API and
frontend services in Compose. - Health checks and service
dependencies. - Environment-based service URLs.

Inside a container, `localhost` refers to that container. Use Compose
service names for container-to-container connections.

## 14. Free Student Deployment

Target: **\$0/month where free-tier eligibility and quotas permit**.
Provider plans and policies can change. Free tiers are for demos and
portfolios, not production uptime guarantees.

Suggested topology:

``` text
Users
  |
  v
Cloudflare Pages Free
(React static frontend)
  |
  | HTTPS REST API
  v
Render Free Web Service
(ASP.NET Core Docker container)
  |
  +------> Neon Free PostgreSQL
  |
  +------> Upstash Free Redis (optional)
```

  Component    Provider                            Target cost Notes
  ------------ ------------------ ---------------------------- ---------------------------------
  Frontend     Cloudflare Pages                            \$0 Static React hosting
  Backend      Render Free                                 \$0 May sleep when idle
  PostgreSQL   Neon Free                                   \$0 Compute/storage/transfer quotas
  Redis        Upstash Free                                \$0 Optional, usage quotas
  CI           GitHub Actions       \$0 within included limits Monitor minutes

Limitations: - Render Free services can spin down after inactivity and
take time to wake up. - Neon Free has compute, storage, and
data-transfer limits. - Upstash Free has command, storage, and bandwidth
quotas. - Cloudflare Pages and GitHub Actions also have plan limits. -
Do not use artificial keep-alive traffic to bypass provider sleep
policies. - Do not store uploads on ephemeral application filesystems. -
Review billing settings and disable paid upgrades where possible.

Provider documentation: - Cloudflare Pages:
https://developers.cloudflare.com/pages/ - Render free services:
https://render.com/docs/free - Neon pricing: https://neon.com/pricing -
Neon plans: https://neon.com/docs/introduction/plans - Upstash Redis:
https://upstash.com/pricing/redis - GitHub Actions:
https://docs.github.com/actions

## 15. Environment Variables and Secrets

Root `.env.example`:

``` env
POSTGRES_PASSWORD=replace-with-local-password
```

Backend environment example:

``` env
ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_URLS=http://+:8080

ConnectionStrings__DefaultConnection=
Redis__ConnectionString=

Jwt__Issuer=
Jwt__Audience=
Jwt__SigningKey=

Frontend__AllowedOrigins=http://localhost:5173

Payment__Provider=
Payment__PublicKey=
Payment__SecretKey=
Payment__WebhookSecret=
```

Frontend `.env.example`:

``` env
VITE_API_BASE_URL=http://localhost:8080
```

### Neon (Postgres + Object Storage)

`neon deploy` provisions `neon.ts` and writes the cloud credentials to
`.env.local` (git-ignored). The backend reads them at startup:

``` env
DATABASE_URL=postgresql://...        # pooled, used by EF Core/Npgsql
DATABASE_URL_UNPOOLED=postgresql://...  # direct, used for migrations/psql
AWS_ENDPOINT_URL_S3=https://...
AWS_REGION=ap-southeast-1
AWS_ACCESS_KEY_ID=...
AWS_SECRET_ACCESS_KEY=...
```

See [docs/NEON-SETUP.md](docs/NEON-SETUP.md) for provisioning, schema
deployment, and health/diagnostics endpoints.

Vite exposes variables prefixed with `VITE_` to browser code. Never put
database passwords, JWT signing keys, private API keys, or payment
secrets in frontend variables. Store production secrets in
hosting-provider environment settings and configure CORS for the exact
frontend origin.

## 16. Security and Reliability

-   Use ASP.NET Core Identity password hashing.
-   Validate JWT issuer, audience, signature, expiry, and signing key.
-   Enforce authorization in backend endpoints and use cases.
-   Validate all input and business rules on the server.
-   Never trust client-supplied prices, roles, payment states, or ticket
    status.
-   Use HTTPS for deployed services.
-   Verify payment webhooks and process them idempotently.
-   Never log passwords, tokens, or payment secrets.
-   Use transactions and concurrency protection for ticket issuance and
    validation.
-   Keep Redis non-authoritative.
-   Add audit logs for sensitive administrative and ticket-validation
    actions.
-   Define data retention and deletion rules.
-   Do not store card numbers or CVV; use a provider's hosted/tokenized
    payment flow.

## 17. Testing Strategy

  ----------------------------------------------------------------------------
  Test level              Frontend                Backend
  ----------------------- ----------------------- ----------------------------
  Unit                    Utilities, schemas,     Domain rules, handlers,
                          hooks                   validators

  Component               Forms, ticket cards,    N/A
                          status UI               

  Integration             API clients and query   API, EF Core, PostgreSQL,
                          hooks                   authorization

  End-to-end              Passenger/staff         Booking/payment/validation
                          journeys                workflows

  Contract                OpenAPI type            OpenAPI request/response
                          compatibility           validation
  ----------------------------------------------------------------------------

Critical scenarios: - Passenger cannot access admin-only endpoints. -
Invalid station IDs are rejected. - Fare is calculated by backend
rules. - An unpaid booking cannot issue a paid ticket. - Duplicate
payment webhooks do not issue duplicate tickets. - Expired or used
tickets are rejected. - Concurrent validation cannot consume the same
single-use ticket twice. - Refunds cannot exceed the refundable
amount. - Ticket cannot be re-enabled while refund outcome is unknown. -
Redis outage does not corrupt booking/payment/ticket state. - UI handles
loading, empty, success, validation, authorization, and network errors.

## 18. CI/CD with GitHub Actions

Use separate workflows with path filters because this is a monorepo.

### Frontend workflow

Trigger on `frontend/**` or frontend workflow changes: 1. Checkout. 2.
Set up Node.js. 3. Install dependencies from lockfile (`npm ci` if using
npm). 4. Run lint and type checking. 5. Run tests. 6. Build production
assets. 7. Deploy to Cloudflare Pages after successful checks on the
deployment branch.

### Backend workflow

Trigger on `backend/**` or backend workflow changes: 1. Checkout. 2.
Install SDK pinned in `global.json`. 3. Restore NuGet packages. 4. Build
Release. 5. Run unit and integration tests with PostgreSQL. 6. Build
Docker image. 7. Deploy to Render after successful checks.

Shared documentation or infrastructure changes may trigger a root-level
validation workflow. Apply production database migrations through a
controlled release step, not independently from every application
instance.

## 19. Git Workflow

Suggested branches:

``` text
main
develop
feature/<feature-name>
fix/<issue-name>
hotfix/<issue-name>
```

Examples:

``` text
feature/authentication
feature/station-management
feature/route-search
feature/ticket-booking
feature/payment-integration
feature/refund-management
feature/qr-validation
```

Team rules: - Create a branch for each feature or bug fix. - Open pull
requests into `develop`. - Protect `main` and require review where
practical. - Keep commits focused and descriptive. - Review EF Core
migrations before merging. - Keep FE and BE API contracts
synchronized. - Never commit `.env`, credentials, or personal data. -
Use CODEOWNERS or folder ownership if the team grows.

## 20. Development Roadmap

  -----------------------------------------------------------------------
  Phase                               Deliverable
  ----------------------------------- -----------------------------------
  1\. Analysis                        SRS, actors, use cases, business
                                      rules

  2\. Design                          ERD, architecture, API contract, UI
                                      wireframes

  3\. Foundation                      Monorepo, FE/BE projects, Docker,
                                      CI

  4\. Identity                        Registration, login, roles,
                                      authorization

  5\. Metro data                      Stations, lines, routes, route
                                      search

  6\. Operations                      Schedules, fares, ticket types

  7\. Ticketing                       Booking lifecycle and ticket
                                      issuance

  8\. Payment                         Sandbox, webhook verification,
                                      idempotency

  9\. Cancellation/refund             Refund policy, requests,
                                      processing, reconciliation

  10\. Validation                     QR generation and staff validation

  11\. Administration                 Management screens and reports

  12\. Quality/release                Tests, security review, free-tier
                                      deployment
  -----------------------------------------------------------------------

## 21. Definition of Done

A feature is complete when: - Business rules and state transitions are
documented. - Backend validation and authorization are implemented. -
API request/response behavior is documented. - Frontend loading, empty,
success, and error states are handled. - Relevant tests pass. - Database
changes include reviewed migrations. - No secrets are committed. - The
feature works locally. - CI checks pass. - The pull request is reviewed
and merged.

## 22. License

Add a license selected and approved by all contributors. MIT is a common
option for student projects, subject to contributor agreement and
third-party dependency licenses.

## 23. Useful Documentation

-   React: https://react.dev/
-   TypeScript: https://www.typescriptlang.org/docs/
-   Vite: https://vite.dev/guide/
-   ASP.NET Core: https://learn.microsoft.com/aspnet/core/
-   .NET support policy:
    https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core
-   Entity Framework Core: https://learn.microsoft.com/ef/core/
-   Npgsql: https://www.npgsql.org/efcore/
-   PostgreSQL: https://www.postgresql.org/docs/
-   Redis: https://redis.io/docs/
-   Docker Compose: https://docs.docker.com/compose/
-   TanStack Query: https://tanstack.com/query/latest
-   Cloudflare Pages: https://developers.cloudflare.com/pages/
-   Render: https://render.com/docs/
-   Neon: https://neon.com/docs/
-   Upstash: https://upstash.com/docs/redis/overall/getstarted
