# Contributing to ShelterStack

Thanks for your interest in contributing! This document describes how work flows
through the project.

## Workflow: issue → branch → PR → merge

1. **Start from an issue.** Every change should map to a GitHub issue. If one
   doesn't exist, open it first using the issue templates. Only the **current**
   milestone is pre-populated with issues — see [ROADMAP.md](ROADMAP.md) for how
   to break down the next milestone once the current one's issues are closed.
2. **Branch off `main`.** Use a descriptive branch name:
   - `feat/<short-description>` for features
   - `fix/<short-description>` for bug fixes
   - `chore/<short-description>` for maintenance
   - `docs/<short-description>` for documentation
3. **Commit in small, logical units.** Write clear commit messages in the
   imperative mood (e.g. "Add tenant resolution middleware").
4. **Open a pull request** against `main`. Fill in the PR template and link the
   issue with `Closes #<n>`.
5. **CI must pass** and the PR must be reviewed before merge.
6. **Merge** — prefer squash merge to keep `main` history clean. Delete the
   branch after merge.

Direct commits to `main` are not allowed; `main` is protected.

> **Note:** `main` history was rewritten on 2026-06-19 to correct commit author
> metadata, so commit SHAs from before that date may have changed — old commit
> links can point to commits no longer reachable from `main`.

## Development setup

```bash
git clone git@github.com:s3ba-b/shelterstack.git
cd shelterstack
dotnet restore
```

### Optional: CodeGraph index for AI-assisted development

If you use Claude Code (or another [CodeGraph](https://github.com/colbymchenry/codegraph)-aware
assistant), you can build a local knowledge graph of the solution's symbols and
call paths to answer "where is X" / "what calls Y" across the `src/ShelterStack.*`
and `tests/ShelterStack.*` projects without a grep-and-read loop:

```bash
npm i -g @colbymchenry/codegraph   # one-time, requires Node.js
codegraph init                     # run from the repo root
```

This is **purely optional local tooling** — the project builds, tests, and runs
identically without it. The generated `.codegraph/` directory is a derived,
point-in-time index (like `bin/`/`obj/`) and is gitignored, so each contributor
builds it locally.

## Running tests

```bash
dotnet test
```

## Code style

- Follow standard .NET / C# conventions.
- Every tenant-scoped entity or endpoint must be covered by a cross-tenant
  isolation test — this suite is a release gate (see the project charter,
  [CHARTER.md](CHARTER.md)).
- Keep changes focused; avoid unrelated refactors in the same PR.

## Reporting bugs

Open an issue using the **Bug report** template.

## Security

Please do not file security vulnerabilities as public issues. See
[SECURITY.md](SECURITY.md).

## Contributor License Agreement

Contributions are accepted under a Developer Certificate of Origin (DCO) —
sign off your commits with `git commit -s` to certify you wrote the contribution
and agree to license it under the project's AGPL-3.0 license.
