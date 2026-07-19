# ShelterStack

> An open-source, cloud-ready SaaS platform that lets independent animal shelters and rescue organizations manage their day-to-day operations from a single hosted instance, while keeping each organization's data fully isolated from the others.

<!-- Badges: fill in once CI exists and the repo is public -->
[![CI](https://github.com/s3ba-b/shelterstack/actions/workflows/ci.yml/badge.svg)](https://github.com/s3ba-b/shelterstack/actions/workflows/ci.yml)
[![License: AGPL v3](https://img.shields.io/badge/License-AGPL_v3-blue.svg)](LICENSE)

🌐 Landing page: https://shelterstack.org/

## Overview

Many small shelters and rescues run on spreadsheets and ad-hoc tools. ShelterStack is a free, self-hostable, multi-tenant platform that handles animals, intake, adoptions, fostering, and medical scheduling — built on .NET Aspire to demonstrate a modern, observable, distributed-systems architecture.

See the [project charter](CHARTER.md) for the full objectives, scope, and constraints, and the [roadmap](ROADMAP.md) for the milestone-by-milestone plan. New here? The [glossary](GLOSSARY.md) explains the domain and technical terms; [PRIVACY.md](PRIVACY.md) is the forward-looking GDPR/RODO working reference.

## Features

- **Multi-tenancy** — tenant = shelter/rescue organization, with per-tenant data isolation enforced at the data layer (EF Core global query filters over a resolved `ITenantContext`).
- **Authentication & authorization** — token-based auth with tenant claims; role-based access within each organization (admin, staff, volunteer).
- **Animal management** — animal records, intake history, status tracking (available, fostered, adopted, medical hold).
- **Adoption workflow** — adoption applications, applicant records, approval flow.
- **Fostering** — foster placements and the people who provide them.
- **Medical scheduling** — vaccination and treatment schedules with due dates.
- **Background processing** — vaccination/medical-due reminders, adoption follow-up notifications, scheduled per-tenant reporting.
- **Observability** — OpenTelemetry instrumentation surfaced in the Aspire dashboard, with custom spans tagged by tenant.

## Tech stack

- .NET 10, orchestrated with .NET Aspire (gateway, identity service, business services, background worker)
- PostgreSQL, Redis, and a message broker (RabbitMQ / Azure Service Bus) as backing resources
- Docker Compose as the primary deployment target; Azure Container Apps (`azd`) documented as an optional cloud path
- AGPL-3.0 licensed

## Getting started

### Prerequisites

- .NET 10 SDK
- [Aspire CLI](https://aspire.dev) (`aspire`)
- Docker, running — the AppHost provisions PostgreSQL, Redis, and RabbitMQ as
  local containers

### Run locally

```bash
# Starts the backing resources (PostgreSQL, Redis, RabbitMQ) plus the
# animals-api and gateway services. More business services land in later
# milestones.
aspire run
```

`aspire run` auto-detects the AppHost project in the repo and builds/runs it; no
need to pass `--project`. If you don't have the Aspire CLI installed, `dotnet
run --project src/ShelterStack.AppHost` works as a fallback. Open the Aspire
dashboard link printed on startup to confirm the resources and services report
healthy.

## Architecture

<!-- Add an architecture diagram and a short description as the project takes shape. -->
_TODO: architecture diagram + overview._

## Deployment

<!-- Document the deploy procedure so a newcomer can follow it with no undocumented steps. -->
_TODO: deployment instructions (Docker Compose primary, Azure Container Apps optional)._

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). This project uses an issue → branch → PR → merge flow.

## License

Licensed under the [GNU Affero General Public License v3.0](LICENSE). See [NOTICE](NOTICE) for attribution.

Operating this software as a network service obliges you to make your complete corresponding source — including modifications — available to users of that service (AGPL-3.0 §13).
