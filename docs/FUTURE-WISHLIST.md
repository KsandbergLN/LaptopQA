# Future wishlist

This is a forward-looking backlog for Laptop QA. These items are not implemented or approved for production yet; each requires design review, security review, and validation on the tested-device matrix.

## Intune API automation

Add Microsoft Intune/Microsoft Graph integration for approved automation workflows, such as looking up device records, submitting device details, and recording QA outcomes.

Before implementation, define the tenant/app-registration model, least-privilege permissions, administrator consent, audit logging, retry/idempotency behavior, and a manual fallback when the API is unavailable. Secrets and tokens must stay outside the repository and package output.

## ServiceNow API automation

Add supported ServiceNow REST API integration for creating or updating the relevant QA and fulfillment records without relying on window, Edge, or clipboard timing.

Before implementation, define the scoped integration account, least-privilege roles, secret storage and rotation, field mapping, duplicate protection, audit trail, timeout/retry behavior, and a clear manual fallback. The existing best-effort workflow should remain available during rollout.

## Signed and allowlisted Windows launch

Sign the Windows executable and establish the organization’s allowlisting policy (for example, trusted-publisher, WDAC, AppLocker, or endpoint-control rules) so the approved executable can be launched directly and the VBS launcher can eventually be deprecated.

Retire the VBS launcher only after signing, policy deployment, package-path behavior, upgrades, rollback, and offline operation have been validated across the tested-device matrix. Until then, the VBS launcher remains the supported technician entry point.

## Delivery criteria

Each wishlist item should have an owner, threat/risk assessment, configuration and secret-management plan, automated tests where practical, documented rollback, and an acceptance record before it becomes part of the supported workflow.
