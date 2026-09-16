# Future wishlist

This is a forward-looking backlog for Laptop QA. These items are not implemented or approved for production yet; each requires design review, security review, and validation on the tested-device matrix.

Last reviewed: 2026-09-16. The current supported workflow intentionally uses configured links, best-effort Edge/clipboard assistance, and the VBS launcher. The wishlist items below describe future replacements or extensions; they do not change the accepted Windows `V4` package or the macOS cached-session companion.

## Intune API automation

Add Microsoft Intune/Microsoft Graph integration for approved automation workflows, such as looking up device records, submitting device details, uploading hashes, and recording QA outcomes. Until then, Windows continues to use the configured Intune links and manual confirmation checkboxes, while macOS remains unable to collect or upload the Windows hash.

Before implementation, define the tenant/app-registration model, least-privilege permissions, administrator consent, audit logging, retry/idempotency behavior, and a manual fallback when the API is unavailable. Secrets and tokens must stay outside the repository and package output.

## ServiceNow API automation

Add supported ServiceNow REST API integration for creating or updating the relevant QA and fulfillment records without relying on window, Edge, or clipboard timing. The current Create Ticket action remains best effort and always provides a manual clipboard fallback; it must not be treated as API-backed submission.

Before implementation, define the scoped integration account, least-privilege roles, secret storage and rotation, field mapping, duplicate protection, audit trail, timeout/retry behavior, and a clear manual fallback. The existing best-effort workflow should remain available during rollout.

## Signed and allowlisted Windows launch

Sign the Windows executable and establish the organization’s allowlisting policy (for example, trusted-publisher, WDAC, AppLocker, or endpoint-control rules) so the approved executable can be launched directly and the VBS launcher can eventually be deprecated. The current VBS launcher remains required for normal technician use and is the supported portable-package entry point.

Retire the VBS launcher only after signing, policy deployment, package-path behavior, upgrades, rollback, and offline operation have been validated across the tested-device matrix. Until then, the VBS launcher remains the supported technician entry point.

## Delivery criteria

Each wishlist item should have an owner, threat/risk assessment, configuration and secret-management plan, automated tests where practical, documented rollback, and an acceptance record before it becomes part of the supported workflow.
