# Contribution and review policy

The protected development branch is `main`. Do not develop directly on `main` after the baseline commit.

1. Create a focused branch named `feature/...`, `fix/...`, `dependency/...`, or `docs/...`.
2. Keep generated output, runtime data, and historical alternatives out of commits.
3. Require at least one maintainer review before merging changes that affect QA scoring, BIOS/power actions, packaging, deployment, dependencies, or ServiceNow behavior.
4. Require a zero-warning Release build and relevant automated/manual evidence.
5. Build packages only from a committed revision. Package acceptance records the evidence and creates an annotated `accepted-LaptopQATestingV4-Iteration-*` tag at that exact source commit.
6. Push `main`, reviewed branches, and tags to an access-controlled private remote. Never publish bundled tools or internal configuration to a public repository without legal/security review.
