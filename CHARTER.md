# Project Charter

| Field | Value |
|---|---|
| **Project name** | ShelterStack — Multi-Tenant Animal Shelter & Rescue Management Platform |

## Project objectives (what we want to achieve)

Build an open-source, cloud-ready SaaS platform that lets independent animal shelters and rescue organizations manage their day-to-day operations from a single hosted instance, while keeping each organization's data fully isolated from the others.

The project pursues three parallel goals:

1. **Solve a real problem.** Many small shelters and rescues run on spreadsheets and ad-hoc tools. A free, self-hostable platform that handles animals, intake, adoptions, fostering, and medical scheduling is a genuine contribution to the non-commercial sector, which generally cannot afford commercial SaaS.
2. **Demonstrate modern cloud-native architecture.** Use .NET Aspire to orchestrate a distributed system (gateway, identity, business services, background workers, and backing resources) and to showcase its observability, service discovery, and deployment tooling.
3. **Serve as a portfolio piece.** Produce a well-documented, deployable, openly licensed codebase that demonstrates backend competence in multi-tenancy, data isolation, authentication, asynchronous processing, and distributed-systems observability.

## Results and scope

| | |
|---|---|
| **Expected products / services** | • A deployable multi-service backend orchestrated with .NET Aspire (API gateway, identity service, one or more business services, background worker service).<br>• A staff-facing Blazor web application (`src/ShelterStack.Web`), Aspire-orchestrated, where shelter staff and volunteers manage day-to-day operations (milestone M3).<br>• Backing infrastructure wired through Aspire: PostgreSQL, Redis, and a message broker (RabbitMQ / Azure Service Bus).<br>• Complete tenant-isolation implementation with automated tests proving cross-tenant data cannot leak.<br>• Primary deployment via Docker Compose — cloud-agnostic and runnable on local/free infrastructure — with Azure Container Apps via `azd` documented as an optional cloud target.<br>• Open-source repository under a strong copyleft license (AGPL-3.0) with README, architecture diagram, setup guide, and deployment instructions. |
| **Main functionalities and/or features** | • **Multi-tenancy:** tenant = shelter/rescue organization; per-tenant data isolation with the boundary enforced at the data layer (EF Core global query filters over a resolved `ITenantContext`).<br>• **Authentication & authorization:** token-based auth with tenant claims; role-based access within each organization (e.g. admin, staff, volunteer).<br>• **Animal management:** animal records, intake history, status tracking (available, fostered, adopted, medical hold).<br>• **Adoption workflow:** adoption applications, applicant records, approval flow.<br>• **Fostering:** foster placements and the people who provide them.<br>• **Medical scheduling:** vaccination and treatment schedules with due dates.<br>• **Background processing (worker service):** vaccination/medical-due reminders, adoption follow-up notifications, scheduled per-tenant reporting, and housekeeping jobs.<br>• **Observability:** OpenTelemetry instrumentation surfaced in the Aspire dashboard, with custom spans tagged by tenant for tenant-aware tracing. |

## Success measures

- The system runs locally with a single command on the Aspire AppHost, with all services and backing resources visible and healthy in the Aspire dashboard.
- The platform can be deployed via Docker Compose following the documented procedure, with no manual undocumented steps; deployment to Azure Container Apps is documented as an optional path.
- An automated test suite demonstrates tenant isolation — a request scoped to Tenant A can never read or modify Tenant B's data. Concretely: at least one cross-tenant isolation test covers **every tenant-scoped entity/endpoint**, and the whole isolation suite is green in CI and treated as a release gate.
- All core functional areas (animals, intake, adoptions, fostering, medical scheduling) are operational end-to-end for **two seeded demo tenants**, each with a representative dataset (e.g. ≥ 20 animals, ≥ 10 adoption applications, ≥ 5 foster placements, ≥ 1 scheduled medical reminder per tenant).
- Background jobs run reliably and their effects are observable (e.g. reminders generated, traces visible in the dashboard).
- The repository is licensed under AGPL-3.0, documented well enough for a newcomer to run it locally and deploy it, and includes an architecture diagram.

## Stakeholders

Stakeholders include both people and non-human factors whose existence shapes the
system's requirements.

| Stakeholder | Type | Stake / influence |
|---|---|---|
| Shelter / rescue staff & volunteers | Human, direct | Primary users; manage animals, intake, adoptions, fostering, medical. Roles: admin, staff, volunteer. |
| Shelter / rescue organizations | Human, direct | Tenants and data **controllers**; require strict isolation of their data. |
| Operator (whoever hosts a shared instance) | Human, direct | Data **processor**; carries the GDPR/operational obligations in [PRIVACY.md](PRIVACY.md). |
| Adoption applicants, fosters, volunteers | Human, indirect | Data subjects whose personal data is processed; their rights drive concrete features. |
| Gminy (municipalities financing statutory shelter care) | Human, indirect | Fund and oversee statutory stray-animal care; their accountability and reporting needs (flagged by the Dec 2024 NIK audit) shape post-v1 reporting features (see [ROADMAP.md](ROADMAP.md), M8 candidate). |
| Destination tenant (M7 transfer recipient) | Human, indirect | Another shelter/rescue that must give explicit consent before receiving a transferred animal record and its associated data. |
| Project author / contributors | Human, direct | Build and maintain the platform; bound by the AGPL/DCO. |
| GDPR / RODO regulation | Inanimate, indirect | Imposes data-protection requirements (see [PRIVACY.md](PRIVACY.md)). |
| .NET Aspire (upstream) | Inanimate, direct | Frequent releases and breaking changes constrain structure and upgrade cadence. |
| Cloud sub-processors (host, email, etc.) | Inanimate, indirect | Region and contractual constraints (EU residency, SCC). |

## Constraints

- **Solo developer, part-time effort** over roughly a 6-month window — scope stays focused on the backend plus a staff-facing web application (Blazor, milestone M3); public-facing adopter portals and mobile apps remain out of scope.
- **Technology constraints:** .NET 10 with the latest stable Aspire; the architecture follows Aspire's orchestration model, which influences project structure and deployment choices. The exact Aspire version is pinned in the repository (README / project files) rather than in this charter, since Aspire ships frequently and the pinned number would go stale here.
- **Aspire release cadence:** Aspire ships frequently and has had breaking changes between minor versions, so dependencies must be kept current (via `aspire update`) and version-pinned deliberately.
- **Non-commercial framing:** no budget for paid cloud services beyond free tiers / personal credits; deployment targets and resource choices must remain affordable or free for demonstration purposes.
- **Domain accuracy:** the shelter/rescue domain model is a reasonable simplification, not a validated reflection of any specific organization's real-world processes.
- **Scope boundaries:** payment processing, public-facing adopter portals, and mobile apps are explicitly out of scope for this version. The internal staff-facing web UI is in scope (a Blazor app, milestone M3) and is distinct from the excluded public-facing adopter portal. As a separate, deliberate exception, the post-v1 **M7** milestone introduces one narrow, consent-gated extension to the tenant-isolation boundary (a controlled cross-tenant transfer) — see Risks below; it does not reopen the payments/portal/mobile exclusions.

## Working methodology

The project follows an **iterative-incremental** approach, suited to a solo, part-time effort: work is delivered as a sequence of increments, each one a working, demoable slice of the system rather than a horizontal layer built in isolation.

- **Walking skeleton first.** The opening increment establishes the architectural spine — Aspire orchestration plus tenant resolution and data-layer isolation — *before* any business feature. Because tenant isolation is a cross-cutting concern, building it first means every later feature is tenant-scoped from birth instead of being retrofitted (and avoids the costly retrofit the compliance section warns about).
- **Vertical slices.** After the skeleton, each domain area (animals, adoption/fostering, medical scheduling) is built end-to-end — API → service → data → tests — as a self-contained increment, kept tenant-scoped and covered by isolation tests as it lands.
- **Done = running software.** Progress is measured by demoable increments visible and healthy in the Aspire dashboard, not by calendar dates; milestones below are deliberately date-free and ordered by dependency.
- **Isolation as a continuous gate.** The automated cross-tenant test suite grows with each increment and runs in CI; a failing isolation test blocks the increment from being considered done.

## Milestones (incremental delivery)

Work proceeds iteratively-incrementally. The first milestone is a **walking skeleton** that puts the architectural spine — including tenant isolation, a cross-cutting concern — in place before any business feature, so every later feature is tenant-scoped from birth rather than retrofitted. Milestones are defined by working, demoable software (their "done" is the running system, not a calendar date), and each one is intended to stand on its own as a portfolio-usable increment.

The full milestone breakdown — each with its definition of done — is maintained as the **single source of truth** in [ROADMAP.md](ROADMAP.md) (and mirrored as GitHub milestones). This charter deliberately does not restate those definitions, so the two copies can't drift; the arc at a glance is:

- **M0 — Walking skeleton** — architectural spine: Aspire orchestration plus tenant resolution and data-layer isolation, before any business feature.
- **M1 — Identity & access** — token-based auth with tenant claims and intra-organization roles.
- **M2 — Animals (first vertical slice)** — animal records, intake history, and status tracking, end-to-end and tenant-scoped.
- **M3 — Staff web application** — Blazor staff-facing app over the identity/tenant pipeline, growing with each later slice.
- **M4 — Adoption & fostering** — adoption applications + approval flow and foster placements, with their staff-UI screens.
- **M5 — Medical scheduling & background worker** — schedules with due dates, plus the worker service and tenant-tagged observability.
- **M6 — Hardening & release** — full isolation suite as a CI release gate, Docker Compose deployment, architecture diagram, and README.

Beyond this original arc, [ROADMAP.md](ROADMAP.md) also plans a **post-v1 extension, M7 —
Cross-tenant federation & transfers**, a deliberate, narrow exception to the scope boundaries
below (see Constraints and Risks).

## Risks

| Risk | Impact | Mitigation |
|---|---|---|
| **Cross-tenant data leak** | Highest severity — a security failure that undermines the project's core premise. | Enforce isolation at the data layer (global query filters over a resolved tenant context); maintain an automated cross-tenant test per tenant-scoped entity; treat any failing isolation test as a release blocker (CI gate). Isolation tests must run against the real DI-wired host, not a bare `DbContext` instance — a regression where a tenant-scoped `DbContext` is registered pooled (`AddDbContextPool`) instead of scoped would let one request's resolved tenant leak into a different request's reused instance, and a test that constructs the `DbContext` directly would never exercise that registration to catch it. The same class of bug can also pass silently in `Production` (where ASP.NET Core's DI scope validation defaults off) while crashing loudly in `Development` (where it defaults on) — don't rely on the crash as the safety net. |
| **Aspire breaking changes between versions** | Time lost to churn; the charter itself notes frequent releases. | Pin the Aspire version deliberately, update on a controlled schedule via `aspire update`, and isolate Aspire-specific wiring so churn stays contained. |
| **Scope creep in the shelter/rescue domain** | Solo, part-time capacity gets consumed modelling edge cases. | Keep the domain a deliberate simplification; anything outside the listed feature set is deferred, not absorbed. |
| **Solo / part-time capacity** | Later milestones may slip. | Incremental milestones with the walking skeleton first, so the project is demoable and portfolio-usable even if M5–M6 slip. |
| **Cross-tenant transfer (M7) as the one sanctioned boundary crossing** | Same severity class as the cross-tenant data leak risk above — a flawed implementation has the same effect: one tenant's data reaching another tenant without authorization. | Require explicit, mutual consent from both tenants before any transfer; record the full transfer in the audit log (see ROADMAP.md M5/M7); cover the path with a dedicated **negative-path isolation test** (no consent → no data crosses), run in the same CI isolation gate as every other tenant-scoped test. |

## Licensing strategy

The project's licensing is chosen to keep the platform freely usable by shelters and rescues while preventing any party from capturing it into a closed, exploitative commercial product. Charging to sustain the product (hosting, support, ongoing development) is acceptable; closing off the code to build a proprietary money-maker that gives nothing back is what we guard against.

- **License: AGPL-3.0.** Strong copyleft with the network-use ("ASP") clause. Anyone may use, self-host, and even charge for the platform, but anyone who offers it as a service must publish their complete source, including modifications, under the same license. For a multi-tenant SaaS this removes the incentive to fork-and-close: any competitor's improvements must return to the community, so no proprietary moat can form.
- **Trademark / name control.** The project name and identity ("ShelterStack") are protected separately from the code. A license governs the code; it does not stop someone from trading on the name. Reserving the name prevents forks from passing themselves off as the official project.
- **Contributor License Agreement (CLA) or DCO.** Required from contributors so copyright stays consolidated enough to enforce the license and, if ever needed, to relicense. Without it, each contributor retains rights over their own contributions.
- **Optional dual licensing.** As the rights holder (backed by the CLA), the project may additionally offer paid commercial licenses to organizations that do not want to comply with AGPL's source-disclosure terms — putting control of any commercial use in the project's hands rather than forbidding it outright.

## Legal & compliance considerations

*Note: this is a forward-looking checklist for an eventual public launch, not legal advice. Before offering the platform publicly in Poland / the EU, obtain professional legal and data-protection (DPO) review.* These items matter most if the platform is ever hosted for real organizations rather than run only as a demo. The detailed working reference — personal-data categories, legal bases, retention, data-subject-rights-to-feature mapping, breach runbook, and EU launch checklist — lives in [PRIVACY.md](PRIVACY.md).

- **GDPR / RODO is the dominant obligation.** The platform processes personal data of people (adoption applicants, fosters, volunteers, staff). When hosting for multiple organizations, the operator is typically a **processor** and each shelter a **controller**, which requires a **Data Processing Agreement (DPA, Art. 28)** with every tenant, a **record of processing (Art. 30)**, a privacy policy / information notice (Art. 13/14), and breach notification to **UODO** within 72 hours (Art. 33/34).
- **Data-subject rights as a technical requirement.** Access, rectification, erasure, and portability (Art. 15–22) must be supported in code: the system must be able to **export and permanently delete a specific person's data**. Designing this early avoids costly retrofitting. In the MVP these operations are initiated by a **tenant admin via the staff web app / API** (there is no public adopter portal in scope), so the capability exists in code without contradicting the deferred public-facing portal.
- **EU data residency & sub-processors.** Personal data should stay in **EU regions**; any cloud sub-processors (Azure, etc.) must be disclosed and contractually bound, with a valid transfer mechanism (SCC / Data Privacy Framework) for anything leaving the EEA (Chapter V).
- **Security measures (Art. 32).** The tenant-isolation work — encryption, access control, and the automated cross-tenant isolation tests — is a direct compliance asset and should be documented as such.
- **Terms of service (regulamin) & privacy policy.** Required under the Polish Act on electronic services; pair with limitation-of-liability / SLA terms if the service is paid.
- **Cookie / ePrivacy consent** for any tracking in the UI.
- **AGPL network clause.** Operating the platform publicly obliges the operator to offer its source to users. As the rights holder this is a formality, but the source-offer must actually be provided.
- **Business & tax registration** (CEIDG/company, VAT, possibly VAT-OSS) apply once fees are charged.
- **Likely out of scope for now:** DSA, NIS2, and the AI Act — revisit only if a public adopter portal, covered-sector classification, or AI features are introduced. The European Accessibility Act (in force since 28 June 2025) could apply if a consumer-facing portal is added.
