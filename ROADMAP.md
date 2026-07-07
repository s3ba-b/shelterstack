# Roadmap

ShelterStack is delivered **iteratively-incrementally**: each milestone is a working, demoable slice of the system — API → service → data → tests — rather than a horizontal layer built in isolation. Milestones are defined by *working software*, not calendar dates, and are ordered by dependency.

- **Walking skeleton first** (M0). Tenant isolation is a cross-cutting concern, so the architectural spine — Aspire orchestration, tenant resolution, data-layer isolation — is built before any business feature. Every later feature is tenant-scoped from birth instead of being retrofitted.
- **Vertical slices** (M2, M4, M5). Each domain area is built end-to-end as a self-contained increment, tenant-scoped and isolation-tested as it lands. The staff web application (M3) is born right after the first slice and then grows with each later slice.
- **Isolation as a continuous gate.** The cross-tenant isolation test suite grows with every increment and runs in CI; a failing isolation test blocks a milestone from being considered done.

## Milestones

| Milestone | Definition of done |
|---|---|
| **M0 — Walking skeleton** | Aspire AppHost orchestrates a gateway + one business service + PostgreSQL/Redis/broker, all healthy in the dashboard. `ITenantContext` resolution and EF Core global query filters are wired, and the first automated cross-tenant isolation test passes. |
| **M1 — Identity & access** | Token-based auth with tenant claims; roles within an organization (admin, staff, volunteer); isolation enforced through the auth pipeline. |
| **M2 — Animals (first vertical slice)** | Animal records, intake history, and status tracking working end-to-end (API → service → data), fully tenant-scoped, with isolation tests covering the new entities. |
| **M3 — Staff web application** | A Blazor staff-facing web app (`src/ShelterStack.Web`), orchestrated by the Aspire AppHost and built on ServiceDefaults, authenticating through the M1 identity/tenant pipeline. The app shell — sign-in, tenant-aware navigation, layout, and a UI language toggle (English/Polish) whose choice is remembered per user — is established, and the **Animals** experience (records, intake history, status) works end-to-end against the live Gateway API. Strictly tenant-scoped: a signed-in user only ever sees their own organization's data, guaranteed by the existing data-layer filters. Later domain milestones extend this same app, adding their UI strings to both languages. |
| **M4 — Adoption & fostering** | Adoption applications + approval flow and foster placements, end-to-end and tenant-scoped, with the corresponding staff-UI screens added to the web app. Applicant/foster records ship with the export + rectification capability mapped in [PRIVACY.md](PRIVACY.md#data-subject-rights--product-feature--milestone). |
| **M5 — Medical scheduling & background worker** | Vaccination/treatment schedules with due dates, and their staff-UI screens in the web app; the worker service generates reminders, adoption follow-ups, and per-tenant reporting; OpenTelemetry spans tagged by tenant are visible in the dashboard. The worker's housekeeping job implements the erasure/anonymization mapped in [PRIVACY.md](PRIVACY.md#data-subject-rights--product-feature--milestone), and every data-subject-rights action (export, erasure) is recorded in an immutable, per-tenant audit log. |
| **M6 — Hardening & release** | Full isolation suite green in CI as a release gate; Docker Compose deployment with documentation; architecture diagram and README; optional Azure deployment documented. |

Each milestone above also exists as a [GitHub milestone](https://github.com/s3ba-b/shelterstack/milestones) with this same definition of done.

## Post-v1 extensions

Beyond the original 6-month M0–M6 arc (see the charter's [Constraints](CHARTER.md#constraints)),
one further milestone is planned. Unlike M0–M6 it deliberately extends the project's scope —
see the charter's Constraints and Risks for why this is a conscious, narrow exception rather
than scope creep.

| Milestone | Definition of done |
|---|---|
| **M7 — Cross-tenant federation & transfers** | A shelter can propose, and a destination shelter can accept, a controlled transfer of an animal record (with its intake and medical history) to another tenant. The transfer only proceeds after **explicit, mutual consent** from both tenants; a **negative-path isolation test** proves that without that consent, no data crosses the tenant boundary. The full transfer (proposal, consent, migration) is recorded in the audit log introduced in M5. Optionally extends to shared visibility of available foster capacity across consenting tenants. |

## Possible further directions

These are candidates surfaced by a competitive analysis against existing shelter-management
software — not commitments, and not yet broken into milestones with a definition of done.
Revisit once M7 lands. The full analysis (market map, strategic gap, source links) is in
[issue #112](https://github.com/s3ba-b/shelterstack/issues/112).

- **Open, event-driven integrations** — per-tenant webhooks on domain events (e.g. "animal
  available"), plus connectors to push listings to external adoption portals and query/update
  microchip registries. EU/PL variant: exports to dlaschroniska.pl and the Safe-Animal microchip
  registry.
- **Offline-first staff web app** — the Blazor app (M3) working offline in low-signal kennel
  areas, syncing on reconnect, with a live, multi-user kennel status board (e.g. via SignalR).
- **Capacity-for-care analytics** — operational KPIs (length of stay, live-release rate,
  capacity utilization) derived from the OpenTelemetry instrumentation already in place from M5.
- **GDPR as a product feature** — surface the data-subject rights capabilities from M4/M5
  (export, anonymisation, audit log) as a visible "data subject rights panel" for tenant admins,
  plus a ready-made DPA template for operators. US competitors lack this; it is a concrete EU
  purchasing argument.
- **Polish statutory reporting (GIW / gminy)** — one-click generation of the statutory reports
  that Polish shelters submit to municipalities and the Veterinary Inspectorate (GIW); decisive
  for winning municipal contracts.
- **Public adoption portal (M8 candidate)** — a read-only, per-tenant adoption listing page
  (static or separately served). Deliberately excluded from v1; the single biggest gap vs.
  eSCHRONISKO/PetNest for small foundations.

## How to start the next milestone

Only the **current** milestone has issues filed against it — the backlog is deliberately not pre-populated end to end. When the current milestone's issues are all closed:

1. Take the next milestone's row from the table above.
2. Break it down into a small set of issues, each deliverable as a single issue → branch → PR → merge unit (see [CONTRIBUTING.md](CONTRIBUTING.md)). Order them by dependency — foundation/infra before features.
3. Every tenant-scoped entity or endpoint introduced needs its own cross-tenant isolation test (non-negotiable — see [CLAUDE.md](CLAUDE.md)).
4. File the issues against the milestone in GitHub before starting work on it.

This is the same process used to break down M0 into its initial five issues.

## Out of scope (this version)

Payment processing, public-facing adopter portals, and mobile apps are explicitly out of scope. The internal **staff-facing web UI is now in scope (M3)** — it is distinct from the excluded public-facing adopter portal. See the project charter's constraints ([CHARTER.md](CHARTER.md)) for the full list and rationale.
