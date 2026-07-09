# Upgrade Options — ScreenBux2

Assessment: 5 projects on net8.0 / net8.0-windows → net10.0; no incompatible packages, no security vulnerabilities; 4 recommended package upgrades; shallow 2-tier dependency graph (Shared → Agent/WebClient/WebServer/Service).

## Strategy

### Upgrade Strategy
All projects are on modern .NET (net8.0) with a shallow dependency graph and no high-risk migrations, so a single atomic pass is the fastest, lowest-overhead approach for this small solution.

| Value | Description |
|-------|-------------|
| **All-at-Once** (selected) | Retarget all 5 projects to net10.0 and update packages together in one pass, then validate the full solution build and tests. Fastest, no multi-targeting overhead. |
| Top-Down | Upgrade entry-point apps first while temporarily multi-targeting the shared library so the solution stays buildable throughout, then consolidate. More overhead — better suited to large or CI-gated solutions. |
