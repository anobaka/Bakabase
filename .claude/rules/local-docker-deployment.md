# Local Docker Deployment

For completed application changes, updating the existing local Docker server
is a default part of the development workflow. Once the relevant checks pass,
deploy and verify the change before reporting completion. Do not defer this
until a separate deployment request or PR merge. The user's explicit timing
and deployment instructions take precedence. Changes with no effect on the
running server, such as documentation, agent rules or tests alone, do not
require rebuilding or restarting it.

## Deployment requirements

1. **Reuse the existing deployment.** Inspect the active Docker context,
   container and Compose configuration to identify the local server. Preserve
   its environment file, overrides, data and media mounts, published ports,
   user preferences and device/pairing identity. Preserve additional assets
   or image configuration, such as a locally deployed Tampermonkey script.
   If the local server or its configuration cannot be identified, report what
   is missing instead of creating a replacement deployment.
2. **Build the completed checkout.** Use `docker/source.sh` to retain the
   checkout's NBGV version. Pass the existing `BAKABASE_ENV_FILE` and
   `BAKABASE_COMPOSE_OVERRIDE` when they live outside this checkout. Build and
   verify the replacement image before stopping the running server; retain
   the previous image for recovery. Record the source revision and version.
3. **Preserve runtime data.** Take a consistent backup of the effective
   AppData directory before replacement, following [the AppData rules](appdata-paths.md),
   including exclusion of `.bakabase.lock`. Keep backups outside AppData.
   Recreate the existing service through `docker/compose.sh` so deployment
   mount and endpoint metadata are derived from the actual configuration.
4. **Verify the running result.** Wait for readiness with a bounded timeout.
   Confirm the expected version/image, unchanged mounts and identities, the
   relevant APIs and delivery of the new frontend or script assets. For UI
   changes, verify the affected interaction in the deployed browser UI.
   A successful image build alone does not establish that deployment worked.
   If replacement or verification fails, recover the previous working service
   and report the failure; preserve user data and account for database schema
   compatibility before reverting binaries.
5. **Report completion.** Include the deployed version/revision, verification
   result, access address and any remaining limitation. If merging is part of
   the authorized task, update the local server to the merged revision when
   its application contents or version differ, and verify it again.
