# Agent Documentation Workflow

## Goal
Keep `Documentation/` useful for future humans and AI agents by favoring a small index plus focused topic docs instead of large duplicated writeups.

## Rules
1. Read `Documentation/FeatureIndex.md` before starting a broad repository search.
2. If the relevant feature is already documented, use that doc to drive a focused code search instead of re-exploring the whole repo.
3. If the feature is missing or the doc is stale, update the docs as part of the task instead of leaving the repository in the same undocumented state.

## Preferred Structure
- One short entry in `Documentation/FeatureIndex.md` per feature/workstream.
- One focused markdown file per substantial feature or workflow.
- Existing historical/reference docs can stay when still useful, but they should be linked from the index or archived clearly.

## What To Capture
- feature purpose
- main entry points and file ownership
- important packet flows or external behavior contracts
- AO2 parity quirks
- known pitfalls
- tests that cover the feature
- important missing test coverage

## What To Avoid
- giant catch-all documents
- repeating the same file list across many docs
- stale docs that contradict the code
- documenting trivial implementation details that are obvious from one quick code read

## Solution Explorer Maintenance
If you add, remove, or rename files in `Documentation/`, update the solution's `Documentation` solution folder so the docs remain visible inside Visual Studio.
