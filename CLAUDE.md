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
  Aspire-orchestrated backing resources. RabbitMQ now carries real traffic — see
  the integration-events note below.
- A staff-facing frontend is in scope: a Blazor web app (`src/ShelterStack.Web`,
  .NET 10, Aspire-orchestrated). Don't treat the frontend as out of scope — only
  the public-facing adopter portal and mobile apps remain excluded.
- The Web app is styled with **Tailwind CSS v4** (see the styling note below). The
  UI has a non-binding *visual* design reference: static HTML/CSS mock-ups (login,
  overview dashboard, animals list, animal detail, M4 adoptions preview) live in
  the separate, private `s3ba-b/open-shelter-mockups` repo — clone it
  (`gh repo clone s3ba-b/open-shelter-mockups`) and reproduce its look (teal/green
  theme, sidebar with org switcher, nav groups, badges, timeline, etc.). The
  mock-ups are the visual reference only; the design **tokens** now live in
  `src/ShelterStack.Web/Styles/app.tailwind.css` (`@theme`), so keeping the two in
  sync as the mock-ups evolve is a manual step, not a copy of their `app.css`. The
  landing page's "design preview" gallery (`docs/images/preview/`) is sourced from
  screenshots of these same mock-ups (see issue #28).
- Frontend styling — **Tailwind CSS v4 via the standalone CLI (no Node/npm)**.
  `src/ShelterStack.Web/Styles/app.tailwind.css` is the source: `@theme` holds the
  design tokens (the source of truth), a small `@layer components` set carries the
  reused/C#-selected primitives (`.card`, `.btn*`, `.badge*`, `.field`, `.timeline`,
  status/thumb classes, etc.), and page/layout composition uses utility classes
  inline in the `.razor` files. It compiles to `wwwroot/app.css` (generated,
  gitignored — never edit by hand). The pinned CLI binary (`TailwindVersion` in
  `ShelterStack.Web.csproj`) is auto-downloaded per-OS/arch into a gitignored `obj/`
  cache on first build; a build-time binary to pin/vendor is the cost of this
  choice, and it must stay restorable in CI and any future devcontainer (see #25).
  There are **no** scoped `.razor.css` files — Tailwind is the single styling system.
- Docker Compose is the primary deployment target; Azure Container Apps (`azd`)
  is an optional, documented secondary path.

## Commands

- Build / test: `dotnet build`, `dotnet test`.
- Run the whole app (orchestrated): `aspire run` from `src/ShelterStack.AppHost`
  — don't `dotnet run` individual services; Aspire wires their dependencies and
  configuration.
- Web CSS is generated, not hand-written: `dotnet build` (and therefore CI and
  `aspire run`) runs the Tailwind CLI as an MSBuild target, producing
  `src/ShelterStack.Web/wwwroot/app.css` from `Styles/app.tailwind.css` with no
  extra step. For the dev loop, `aspire run` also starts a Tailwind `--watch`
  sidecar (run mode only) so edits to markup/tokens regenerate the CSS live. Edit
  `Styles/app.tailwind.css`, never the generated `wwwroot/app.css`.
- Format (CI gate): `dotnet csharpier .` — this repo uses CSharpier, not
  `dotnet format`.
- EF Core migrations live per service: each service owns its `DbContext` under
  its own `Data/` folder (there is no separate Infrastructure project), so
  project and startup project are the same service, e.g.:
  `dotnet ef migrations add <Name> -p src/ShelterStack.Identity.Api -s src/ShelterStack.Identity.Api`
  then `dotnet ef database update -p ... -s ...`.
- NuGet versions are managed centrally (`Directory.Packages.props`): a `.csproj`
  carries `<PackageReference Include="X" />` with **no** `Version` attribute, and
  the version lives in a `<PackageVersion>` there. Adding a package means an entry
  in both. Keep one version per package across the solution — the EF Core entries
  in particular must move in lockstep, since the Npgsql provider depends on EF as
  a range and a split reintroduces the MSB3277 conflicts of #107.

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
- Web styling is Tailwind CSS utilities inline in `.razor` markup, over the
  tokens in `Styles/app.tailwind.css`. Reach for a `@theme` token
  (`bg-surface`, `text-muted`, `rounded-card`, status/thumb colors, …) rather
  than a hard-coded hex. Add a class to the `@layer components` set only when a
  pattern is genuinely reused across pages or is selected in C# (as the status
  badges are); one-off page layout stays inline. Do **not** add scoped
  `.razor.css` files — Tailwind is the only styling system.

## Integration events (service-to-service)

Services talk over the Aspire `messaging` (RabbitMQ) resource using the raw
`RabbitMQ.Client` via `Aspire.RabbitMQ.Client` — there is no MassTransit or
NServiceBus, and adding one is an architectural decision, not a convention.
Established by M4's adoption flow (`Adoptions.Api` publishes `AdoptionApproved`,
`Animals.Api` consumes it and publishes `AnimalStatusChangeRejected` back):

- One durable topic exchange `shelterstack.events`, one durable queue per
  consuming service (`<service>.<event>`), each dead-lettering to
  `shelterstack.events.dead-letter`. A message a consumer can't handle is
  dead-lettered, never requeued in a loop.
- **Event contracts are duplicated per service**, not extracted to a shared
  assembly — same convention as `ITenantContext`, `TokenAuth`, and `DemoTenants`.
  The binding contract is the JSON on the wire. Keep both copies in step.
- **Events carry `TenantId` in the body, and the consumer must push it through
  the normal `ITenantContext`** (a `StaticTenantContext` built from the message)
  so the same global query filters apply. A tenant id from a message is never a
  trusted claim — the broker must not become a route across the tenant boundary
  that HTTP isn't (see #106). Every new event needs a cross-tenant isolation test
  covering the broker path.
- A cross-service write is asynchronous, so the HTTP call has already returned by
  the time it can fail. Anything that can be refused downstream needs a
  **compensating event** and a visible state for staff (as `NeedsAttention` is),
  not just a log line. A synchronous pre-check is a nicety for the common case;
  the compensating path is the correctness guarantee.

Don't introduce Mediator/CQRS, FluentValidation, a `Result<T>` flow-control
pattern, Scalar, or FluentAssertions on a whim — none are in use today, so
adopting one is an architectural decision (file a `[Decision]`/issue first),
not a convention to apply silently.

## Project website

`docs/` is the GitHub Pages source for the public landing page (served from
`main` at https://shelterstack.org/ — the custom domain lives in `docs/CNAME`;
the old s3ba-b.github.io/shelterstack URL 301-redirects) — it is not a general
docs folder, don't repurpose it. It's a hand-written static page (no build step).
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
