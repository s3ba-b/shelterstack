# CLAUDE.md

## What this project is

ShelterStack — a multi-tenant SaaS platform for animal shelters and rescue
organizations (animals, intake, adoptions, fostering, medical scheduling), built
on .NET 10 / .NET Aspire to demonstrate distributed-systems architecture and
multi-tenant data isolation. The milestone breakdown lives in
[ROADMAP.md](ROADMAP.md) in this repo. Full objectives, success measures,
constraints, and licensing rationale live in the project charter
([CHARTER.md](CHARTER.md)) in this repo. Shared domain and technical vocabulary
(tenant, intake, foster, medical hold, etc.) lives in [GLOSSARY.md](GLOSSARY.md)
— use those terms as defined rather than inventing synonyms.

## Stack

- .NET 10, orchestrated with .NET Aspire — `src/ShelterStack.AppHost` is the
  orchestration entry point, `src/ShelterStack.ServiceDefaults` carries shared
  OpenTelemetry/health/resilience wiring used by every service.
- PostgreSQL, Redis, and a message broker (RabbitMQ / Azure Service Bus) as
  Aspire-orchestrated backing resources (added as services land).
- A staff-facing frontend is in scope: a Blazor web app (`src/ShelterStack.Web`,
  .NET 10, Aspire-orchestrated). Don't treat the frontend as out of scope — only
  the public-facing adopter portal and mobile apps remain excluded.
- The Web app's UI has a non-binding design reference: static HTML/CSS mock-ups
  (login, overview dashboard, animals list, animal detail, M4 adoptions preview)
  live in the separate, private `s3ba-b/open-shelter-mockups` repo — clone it
  (`gh repo clone s3ba-b/open-shelter-mockups`) and port its markup/`app.css`
  design tokens (teal/green theme, sidebar with org switcher, nav groups, badges,
  timeline, etc.) rather than inventing new styling. The landing page's "design
  preview" gallery (`docs/images/preview/`) is sourced from screenshots of these
  same mock-ups (see issue #28).
- Docker Compose is the primary deployment target; Azure Container Apps (`azd`)
  is an optional, documented secondary path.

## Commands

- Build / test: `dotnet build`, `dotnet test`.
- Run the whole app (orchestrated): `aspire run` from `src/ShelterStack.AppHost`
  — don't `dotnet run` individual services; Aspire wires their dependencies and
  configuration.
- Format (CI gate): `dotnet csharpier .` — this repo uses CSharpier, not
  `dotnet format`.
- EF Core migrations live per service: each service owns its `DbContext` under
  its own `Data/` folder (there is no separate Infrastructure project), so
  project and startup project are the same service, e.g.:
  `dotnet ef migrations add <Name> -p src/ShelterStack.Identity.Api -s src/ShelterStack.Identity.Api`
  then `dotnet ef database update -p ... -s ...`.

## Non-negotiable architectural rule: tenant isolation

Every tenant-scoped entity and endpoint must enforce isolation at the data layer
via EF Core global query filters over a resolved `ITenantContext`, and must ship
with an automated cross-tenant isolation test. The isolation suite is a CI
release gate — a failing isolation test blocks merge. This is not optional
hardening; it is the project's core technical premise (see the charter's Risks
section: cross-tenant data leak is the highest-severity risk).

## Workflow

issue → branch (`feat/`, `fix/`, `chore/`, `docs/`) → PR (`Closes #N`) → CI green
→ squash merge. Direct commits to `main` are not allowed; `main` is branch
protected. See [CONTRIBUTING.md](CONTRIBUTING.md).

## Code conventions

The solution is service-oriented (one project per service, orchestrated by the
AppHost), not a layered single app — there is no `Domain`/`Application`/
`Infrastructure` split. Each service is self-contained, co-locating its
`Data/` (entities + `DbContext`), `Auth/`, `Tenancy/`, and `Migrations/`.

Match the existing code:

- File-scoped namespaces; `sealed record` for request/response contracts and
  DTOs; primary constructors for DI.
- Pass `CancellationToken` to async methods.
- Access the database through EF Core `DbContext` directly — no repository
  pattern. Tenant-scoped entities go through the EF Core global query filters
  over `ITenantContext` (see the tenant-isolation rule above).
- Write explicit mappings between entities and contracts — no AutoMapper.
- Minimal APIs (`app.MapGet`/`MapPost`) for endpoints; keep them thin.

Don't introduce Mediator/CQRS, FluentValidation, a `Result<T>` flow-control
pattern, Scalar, or FluentAssertions on a whim — none are in use today, so
adopting one is an architectural decision (file a `[Decision]`/issue first),
not a convention to apply silently.

## Project website

`docs/` is the GitHub Pages source for the public landing page (served from
`main` at https://s3ba-b.github.io/shelterstack/) — it is not a general docs
folder, don't repurpose it. It's a hand-written static page (no build step).
The roadmap section's "Done"/"In progress" markers and the hero badge are
populated live via a client-side fetch against the repo's GitHub milestones
API (now public, so no auth needed) — see the script in `docs/index.html`
just before the lightbox script. The hand-written fallback text in the HTML
only matters if that fetch fails, is rate-limited, or runs with JS off, so
keep it roughly current, but there's no manual step required when a
milestone finishes.

The page must stay understandable to non-technical visitors (shelter staff,
volunteers), not only developers, while keeping its value as a technical
reference. Follow "simple first, technical second": the upper sections (hero,
"why this exists", feature cards, roadmap, license) use plain, benefit-focused
language with no jargon; technical terms (Aspire, EF Core, OpenTelemetry,
multi-tenancy, OpenTelemetry spans, etc.) stay confined to the architecture
section, which is explicitly marked "For developers" and flagged as skippable.
Page language is English by default, with an optional Polish translation: an
in-page EN/PL toggle (top-right) swaps copy via a `docs/translations.js`
dictionary and remembers the choice. English ships in the HTML (so no-JS
visitors and crawlers get a complete page) and Polish is applied on top — keep
both languages at key parity when editing copy. The mock-up screenshots stay
English regardless of the toggle.

## Licensing

AGPL-3.0. Any party that runs this software as a network service must offer the
complete corresponding source, including modifications, to users of that
service (§13). Sign off commits (`git commit -s`, DCO) to contribute.

## Working by milestone

Determine the current milestone from the GitHub milestones and issues — don't
hardcode it here. Milestone status is source-of-truth on GitHub, and the
landing page reads it live (see "Project website" above), so no doc edit is
needed when a milestone finishes — just file the issues.

As a rule, only the current milestone has issues filed against it (exception:
a long-lived `[Decision]` issue may be filed early against a future milestone
to track an open question — that doesn't make that milestone "current"). When
its issues are all closed, break down the next milestone from
[ROADMAP.md](ROADMAP.md) into issues before starting work on it — don't assume
someone else has already done this.
