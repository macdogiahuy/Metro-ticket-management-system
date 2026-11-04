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
  Runtime                 .NET 10 LTS                     Application runtime

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

Use the latest supported patch of .NET 10 and pin the SDK in
`global.json`.

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

PostgreSQL is the recommended primary database. Use EF Core migrations
to version schema changes.

Initial entities:

  -----------------------------------------------------------------------
  Group                               Tables
  ----------------------------------- -----------------------------------
  Identity                            `Users`, `Roles`, `UserRoles`,
                                      `RefreshTokens`

  Metro network                       `Stations`, `Lines`, `Routes`,
                                      `RouteStations`

  Operations                          `TrainSchedules`

  Fare                                `TicketTypes`, `FareRules`

  Ticketing                           `Bookings`, `BookingItems`,
                                      `Tickets`

  Payment                             `Payments`, `PaymentTransactions`

  Refund                              `RefundRequests`, `RefundItems`,
                                      `RefundTransactions`,
                                      `RefundPolicies`

  System                              `Notifications`, `AuditLogs`
  -----------------------------------------------------------------------

Important distinctions: - Station is a physical station. - Line is a
metro service line. - Route is a journey/path option. - RouteStation
records ordered station membership. - Schedule represents planned
service times. - Booking is a passenger order. - Ticket is an individual
travel entitlement. - Payment records payment lifecycle and provider
references. - RefundRequest records a cancellation/refund request. -
RefundTransaction records an actual operation with the payment provider.

One booking may contain multiple tickets. Individual tickets may have
different usage and refund states.

``` mermaid
erDiagram
    USERS ||--o{ BOOKINGS : creates
    LINES ||--o{ ROUTES : contains
    ROUTES ||--o{ ROUTE_STATIONS : has
    STATIONS ||--o{ ROUTE_STATIONS : appears_in
    BOOKINGS ||--o{ BOOKING_ITEMS : contains
    BOOKING_ITEMS ||--o{ TICKETS : issues
    BOOKINGS ||--o{ PAYMENTS : has
    PAYMENTS ||--o{ PAYMENT_TRANSACTIONS : records
    BOOKINGS ||--o{ REFUND_REQUESTS : receives
    REFUND_REQUESTS ||--o{ REFUND_ITEMS : contains
    REFUND_REQUESTS ||--o{ REFUND_TRANSACTIONS : processes
```

This is an initial model sketch. Confirm cardinalities, constraints,
indexes, and deletion rules in the final ERD.

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

### Cancellation and refund

Keep these concepts separate: - Booking cancellation: cancellation of an
order. - Ticket cancellation: revocation of an individual travel
entitlement. - Refund: returning money through the original payment
method where supported.

Suggested configurable policy: - Unpaid booking: cancel/expire without
refund transaction. - Paid, unused ticket: eligible only if the
applicable policy allows it. - Used ticket: normally not eligible for
standard refund. - Expired ticket: normally not eligible for standard
refund. - Operator disruption: may qualify for full refund or
replacement ticket. - Day/month passes: use separate policy rules from
single-trip tickets.

Refund workflow: 1. Passenger requests cancellation/refund. 2. Backend
checks ownership, ticket state, time limits, and policy. 3. Backend
calculates refundable amount and fee. 4. Ticket is blocked from use
while refund is being processed. 5. Backend submits an idempotent refund
request to the payment provider. 6. Backend confirms the result through
webhook or provider status query. 7. Ticket and refund states are
updated consistently. 8. Passenger receives the result and refund
history.

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

Example endpoints:

  Method   Endpoint                             Purpose
  -------- ------------------------------------ -------------------------
  `POST`   `/api/auth/register`                 Register
  `POST`   `/api/auth/login`                    Login
  `POST`   `/api/auth/refresh`                  Refresh token
  `GET`    `/api/stations`                      List stations
  `GET`    `/api/routes/search`                 Search journeys
  `GET`    `/api/schedules`                     Query schedules
  `GET`    `/api/fares/quote`                   Calculate fare quote
  `POST`   `/api/bookings`                      Create booking
  `GET`    `/api/bookings/me`                   Current user's bookings
  `POST`   `/api/payments`                      Start payment
  `POST`   `/api/payments/webhook/{provider}`   Payment callback
  `POST`   `/api/refunds/quote`                 Preview refund
  `POST`   `/api/refunds/requests`              Create refund request
  `GET`    `/api/refunds/me`                    Refund history
  `GET`    `/api/tickets/{id}`                  Ticket detail
  `POST`   `/api/tickets/validate`              Validate ticket

Suggested HTTP status codes: `200` success, `201` created, `204` no
content, `400` invalid request, `401` unauthenticated, `403` forbidden,
`404` not found, `409` state/concurrency conflict, `422` optional
business validation convention, `500` unexpected server error.

Example response:

``` json
{
  "success": true,
  "message": "Booking created successfully",
  "data": {
    "bookingId": "example-booking-id",
    "status": "Pending"
  },
  "errors": []
}
```

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
-   .NET 10 SDK.
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
