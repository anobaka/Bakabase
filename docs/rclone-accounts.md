# rclone accounts and Microsoft authorization

Bakabase uses the optional, managed **rclone 1.75.2** executable for configured
PikPak, OneDrive/SharePoint and Pixeldrain account paths. Public sharing links use
separate capabilities: authorizing an account does not automatically convert an
arbitrary sharing URL into an accessible account path. Choose a configured drive
and a path available to that account when using an account download method.

PikPak credentials and Pixeldrain API keys are write-only inputs. Account names,
configured state and the last explicit validation result are returned by account
APIs; passwords, OAuth tokens, API keys and native configuration state are not.
Preflight checks local configuration and its generation. The explicit
**Validate** action lists the configured account root over the network. During
Microsoft authorization, drive identification checks the current user's OneDrive
and its root; the optional preview reads the selected drive's root entries.

## Microsoft authorization modes

[rclone's OneDrive documentation](https://rclone.org/onedrive/#configuration)
supports personal OneDrive, business OneDrive and SharePoint document libraries.
Its built-in Microsoft application is used by default; ordinary setup does not
require creating an Azure application. Organizations can restrict consent or
require administrator approval. The default **manual callback** mode uses this
built-in application. The optional **automatic HTTPS callback** mode uses your
own Microsoft application. Both support the global Microsoft cloud and delegated
user authorization; application-only authorization is outside this integration.

Bakabase requests the documented read-only scopes:
`Files.Read Files.Read.All Sites.Read.All offline_access`.
`Sites.Read.All` allows the native configuration flow to search SharePoint sites.
The application never uses a destructive sync operation for acquisition.
Microsoft can also return previously consented `User.Read` or standard OpenID
scopes. Those are tolerated in the token response, without requesting them or
using an ID token; write permissions and unknown scopes are rejected.

## Docker and remote servers

The default **manual callback** flow uses the rclone executable already
managed by the server. The browser computer does not need rclone, and Docker
does not need an additional published port. Token exchange and refresh remain
inside native rclone; Bakabase does not implement a second Microsoft OAuth client.

1. Create a OneDrive account entry, then explicitly start authorization.
2. The server starts native noninteractive configuration with
   `config_is_local=false` and the read-only scopes above. Browser authorization
   then starts a managed native `rclone authorize --auth-no-open-browser` process
   for that configuration question.
3. Click **Open Microsoft sign-in**. Bakabase reads the native loopback server's
   redirect without following it, and provides that Microsoft authorization URL
   to the browser. Complete login and consent yourself.
4. Microsoft redirects to `http://localhost:53682/` on the **browser computer**.
   The browser may report that it cannot connect. Immediately copy the complete
   address from its address bar into Bakabase's callback-address input. This is
   an authorization result, not the original login URL; do not share or store it.
5. Bakabase checks the callback against the active question and native state,
   and forwards it only to the same native rclone process on the server's fixed
   loopback endpoint. Native rclone exchanges the code, and its authorization
   result continues the existing drive, SharePoint site and library questions.
6. Only native completion publishes the allowlisted account configuration. An
   existing working account remains unchanged while a replacement authorization
   is pending, cancelled, expired or rejected.

The callback contains a short-lived authorization code. Paste it immediately:
the authorization session's lifetime does not extend the code's lifetime.
Authorization must restart if the server or its native authorization process
restarts. Native rclone's fixed loopback listener permits only one server-hosted
browser authorization at a time. Closing or cancelling the dialog releases it.
An invalid callback is rejected before contacting native rclone.

**Advanced command-line authorization** retains rclone's official
[headless setup](https://rclone.org/remote_setup/). Run the displayed
`rclone authorize "onedrive"` command on a computer with a browser (the same
rclone version is recommended), then submit its authorization result in the
secret field. Switching to this method stops any server-hosted browser flow.
The server rejects additional configuration options embedded in that result.
No Microsoft login or consent is performed automatically by Bakabase.

This default flow still requires one manual callback-address paste. Merely
proxying the login entry through Bakabase's normal HTTP port cannot change
Microsoft's registered localhost redirect. This applies when accessing Docker
from the same computer and when accessing a remote NAS: `localhost` always names
the browser computer. A connection error on that page does not by itself mean
Microsoft sign-in failed. Return to the original Bakabase tab and paste the
original complete callback address, without replacing its hostname or port.

## Automatic HTTPS callback with your own Microsoft application

In **Third-party configuration → OneDrive / SharePoint**, choose automatic
authorization and configure the application's client ID, tenant, client secret
**Value** (not the secret's ID), and fixed callback URL. The default tenant is
`common`; `organizations`, `consumers`, or an explicit tenant ID can restrict the
sign-in audience. The application's registered account types must match it.
Leaving the secret input blank preserves the stored secret; removing it is an
explicit action. Secrets are never returned to the browser.

1. Register an application in Microsoft Entra with the required personal and/or
   organizational account types. Add a **Web** redirect URI, not a SPA redirect.
2. Give it the delegated read-only permissions listed above, and create a client
   secret. The operator supplies its Value directly in Bakabase.
3. Register a stable URL such as
   `https://bakabase.example.com/oauth/callback/microsoftGraph`. Enter exactly
   that URL in Bakabase. Its path is fixed; query strings, fragments, user-info,
   and plain HTTP are rejected. The URL must route to this server, directly or
   through your HTTPS reverse proxy. It must not point at a desktop relay.
4. Save the settings, then explicitly authorize an account and click the
   Microsoft sign-in button. Complete login and consent in the new browser tab.
5. The callback tab reports completion or failure without displaying credentials.
   Return to the original Bakabase tab; it checks the session and continues the
   native drive, site and document-library questions. Only completing those
   questions replaces the account's working profile.

The callback URL is configured independently from the URL used to open Bakabase.
It is never constructed from `Host`, `Forwarded`, or the currently viewed page.
No central Bakabase authorization service or additional OAuth port is involved.
Reverse proxies must forward the exact callback path and should omit its query
string from access logs. When serving the UI through HTTPS with HTTP upstream,
preserve the original `Host` including any non-default public port. The owner UI
guard accepts that TLS termination only with browser same-origin metadata; it
does not trust arbitrary forwarding headers. Remote access must be enabled. Requiring paired devices
does not block this one registered callback; settings and account APIs retain
their existing owner permissions.

Automatic authorization uses a provider-neutral OAuth broker with a registered
Microsoft provider, one-use state and PKCE S256. It takes a private snapshot of
the client configuration for each flow, redeems the code on the server, and feeds
the token into the existing rclone configuration questions. The final rclone
profile retains that same application's client credentials and tenant endpoints
so native rclone can refresh it. Changing the global authorization settings does
not rewrite previously configured account profiles. Token exchange follows the
same rclone tool/site/global proxy selection as native authorization.

Pending automatic flows are bounded by the account authorization session and are
invalid after cancellation, expiration, consumption or a server restart. Changed
application settings apply to new sessions. Cancel and restart authorization to
use new application credentials or callback configuration in a pending flow.
Automatic callback support does not add device-code authorization or automatic
local desktop callbacks.

## Public sharing links and account authorization

A public SharePoint **Anyone** link allows access without signing in, subject to
its sharing settings and download restrictions. Bakabase's supported public
SharePoint folder links use anonymous acquisition; configuring a Microsoft
account is not required. Never ask the recipient to authorize the sharing
owner's account. A restricted share needs an identity that already has access;
OAuth consent does not grant access to somebody else's private files.

Account authorization allows Bakabase/rclone to read files that the signed-in
account can access in the selected drive or document library. It chooses an
access root, not a cloud destination. Account downloads read remote files into
the Bakabase server's local staging area, verify them, then deliver them to the
chosen local directory. There is no preliminary copy into the user's OneDrive.
The local staging area is not a cloud transfer. Docker deployments deliver to
the server's mounted filesystem, not automatically to the browser computer.

Current capabilities remain distinct: account downloads require an explicit
account and an internal path within its configured root. Authorizing an account
does not automatically translate arbitrary personal OneDrive sharing URLs or
sign-in-required SharePoint sharing URLs into those paths. Public sharing
support remains limited to the reviewed public folder formats. Selecting a
drive cannot fix an expired, unsupported, restricted or download-blocked link.

References: [Microsoft sharing settings](https://support.microsoft.com/en-us/sharepoint/lists/sharepoint-sharing-and-permissions/external-or-guest-sharing-in-onedrive-sharepoint-and-lists)
and [rclone's OneDrive backend](https://rclone.org/onedrive/).

## Choosing a drive or document library

Choosing **OneDrive Personal or Business** selects a connection type, then native
rclone discovers drives. Bakabase deduplicates candidates by their exact native
ID, preserving the submitted value. Readable names are preferred for duplicate
IDs; different IDs with the same name remain distinct.

For an active drive question reached by explicitly choosing **OneDrive Personal
or Business**, Bakabase identifies the signed-in user's OneDrive using Microsoft's fixed `GET /me/drive` endpoint. Only an exact candidate ID
match with a successful root access check is recommended as **My OneDrive**.
Names, ID shapes, drive types and the native first/default choice are not proof
of identity or access. SharePoint/advanced connection branches and older sessions
without a recorded connection type retain explicit candidate selection and
preview, without a guessed personal-drive recommendation. If identification fails or the drive cannot be accessed,
no other candidate is silently substituted as the recommendation. Microsoft
notes that `/me/drive` can provision a licensed user's OneDrive; this additional
lookup is limited to the connection branch where native rclone has already
performed that request, rather than running it during SharePoint setup.

Other candidates appear in advanced selection. **Preview root** reads a bounded
first page of root entries without downloading, modifying or submitting the
choice. It shows file/folder names and access results to help distinguish
similarly named libraries. The preview is not a complete inventory, a recursive
browser or a guarantee that every descendant is readable. Unchecked candidates
are shown as unchecked; an opaque name does not make a drive invalid. Original
native names and IDs remain available in details.

Discovery and preview are bound to the active server-side authorization session,
question and allowlisted candidate ID. They never accept arbitrary Graph URLs or
remote paths, expose credentials, replace a working account or finish native
configuration. A network failure can be retried without selecting another drive.
The operator explicitly continues after choosing a drive, and native rclone
still validates it before final confirmation. If Microsoft rejects that
request, Bakabase displays a classified error and follows native recovery.

Reference: [Microsoft Graph: get the current user's OneDrive](https://learn.microsoft.com/en-us/graph/api/drive-get?view=graph-rest-1.0).

## Storage and lifecycle

Each server stores account metadata and private configurations under its own
AppData directory. rclone's private account configuration is the sole authority
for refreshable tokens; metadata does not duplicate them. Native child processes
clear inherited `RCLONE_*` settings and use their own configuration, cache and
temporary paths, while retaining Bakabase's configured tool/site/global proxy
precedence. Secrets and native error text are not included in process arguments
or public errors. Files and directories are owner-private on Unix; Windows uses
the application's AppData access boundary.

Authorization sessions expire after 15 minutes. A background sweep runs at host
startup and every minute, removing unfinished private credential files; active
configuration calls are bounded to two minutes. The separately managed browser
authorization process is bounded by its session lifetime and stopped on cancel
or shutdown. Cancel also clears private session
configuration. Completed/cancelled session status can remain as a secret-free
receipt. Deleting an account removes only its local credentials and metadata,
not already copied files or delivered artifacts. Revoke Microsoft consent in
Microsoft account/organization settings if remote authorization must also end.

## Verified upstream contract

Reviewed against **v1.75.2** on 2026-10-21:

- [Native non-interactive configuration protocol](https://rclone.org/commands/rclone_config_create/)
  and [its implementation](https://github.com/rclone/rclone/blob/v1.75.2/cmd/config/config.go).
- [Headless authorize command](https://rclone.org/commands/rclone_authorize/),
  [authorization output](https://github.com/rclone/rclone/blob/v1.75.2/fs/config/authorize.go)
  and [OAuth state machine](https://github.com/rclone/rclone/blob/v1.75.2/lib/oauthutil/oauthutil.go).
- [OneDrive/SharePoint drive and site questions](https://github.com/rclone/rclone/blob/v1.75.2/backend/onedrive/onedrive.go)
  and [native flag environment binding](https://github.com/rclone/rclone/blob/v1.75.2/fs/config/flags/flags.go).
- [Suggested choices, boolean defaults and error recovery states](https://github.com/rclone/rclone/blob/v1.75.2/fs/backend_config.go).
- [Microsoft authorization-code flow and PKCE](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-auth-code-flow),
  [registered redirect URI restrictions](https://learn.microsoft.com/en-us/entra/identity-platform/reply-url),
  and [refresh-token client binding](https://learn.microsoft.com/en-us/entra/identity-platform/refresh-tokens).

The real official macOS arm64 binary was SHA-256 verified and exercised in an
isolated AppData fixture to the native `config_token` question; environment-based
state/result continuation rejected an intentionally invalid offline answer.
This requires no Microsoft account. A real authenticated Microsoft login,
tenant consent and document-library download still require a user's account and
are not implied by that pre-authorization check.

The browser bridge additionally verifies the native Microsoft redirect and
loopback listener without performing login. Callback validation, replay,
cancellation, concurrency and stale-session behavior have separate regression
coverage; these checks do not substitute for a real account authorization.

Automatic-mode regression fixtures exercise PKCE/code redemption, callback denial,
replay, expiry, cancellation and native configuration continuation using a controlled
token transport. The real official 1.75.2 binary also verifies custom-application
pre-authorization and private native configuration round trips without signing in.
These checks do not establish a real application's tenant consent, reachable HTTPS
callback or access to a user's document libraries; those require deployment-specific
Microsoft application configuration and user acceptance.
