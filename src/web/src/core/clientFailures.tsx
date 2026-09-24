import { ClientForwardingFailure } from "@/sdk/constants";
import { toast } from "@/components/bakaui";
import i18n from "@/i18n";

/**
 * Refusals that came from the desktop app's relay itself rather than from the server it
 * shows.
 *
 * The relay stamps `X-Bakabase-Client` on anything it answers on its own behalf — a path
 * with no local mapping, an action this build cannot run yet, a request from outside its
 * own window. Every one of those can come back from any of the several dozen endpoints
 * that touch a path, so they are recognised once, here, rather than at each call site.
 *
 * It matters because the generic handler says "GET /tool/open failed", which is true
 * and useless: nothing failed, this computer simply does not know where that folder is on
 * its disks, and the answer is a setting the user has not filled in yet.
 *
 * The header keeps its name from the removed thin client: a managed server's older UI
 * reads it, and it is a wire name, not something the user sees.
 */

/** Header the relay stamps on its own refusals. */
export const CLIENT_FAILURE_HEADER = "X-Bakabase-Client";

/** Where the user goes to say where a server's libraries are on this machine. */
const PATH_MAPPING_ROUTE = "#/client-path-mapping";

/**
 * The failure the relay reported, or undefined when the answer was the server's.
 *
 * The header travels as the enum's *name* rather than its number — it is a header, and
 * a bare `5` in a network log tells nobody anything.
 */
export function readClientFailure(response?: Response): ClientForwardingFailure | undefined {
  const name = response?.headers?.get(CLIENT_FAILURE_HEADER);

  if (!name) {
    return undefined;
  }

  const value = ClientForwardingFailure[name as keyof typeof ClientForwardingFailure];

  return typeof value === "number" ? value : undefined;
}

/**
 * Reports a refusal the relay made on this computer in terms the user can act on.
 *
 * @returns true when this was the relay's refusal and has been reported, so the
 * caller should not also raise its own error toast.
 */
export function reportClientFailure(response: Response | undefined, error: any): boolean {
  const failure = readClientFailure(response);

  if (failure === undefined || failure === ClientForwardingFailure.None) {
    return false;
  }

  const t = i18n.t.bind(i18n);
  // The relay writes its own message and it is more specific than anything that can
  // be said from here — it names the path, or the route it cannot run yet.
  const description = error?.message ?? error?.error?.message;

  switch (failure) {
    case ClientForwardingFailure.PathNotMapped:
      toast.warning({
        title: t("client.failure.pathNotMapped"),
        description,
        endContent: (
          <a className="text-primary underline" href={PATH_MAPPING_ROUTE}>
            {t<string>("client.failure.addMapping")}
          </a>
        ),
      });
      break;

    case ClientForwardingFailure.NeedsNewerClient:
      toast.warning({ title: t("client.failure.needsNewerClient"), description });
      break;

    case ClientForwardingFailure.NotConnected:
      toast.warning({ title: t("client.failure.notConnected"), description });
      break;

    case ClientForwardingFailure.ServerUnreachable:
      toast.danger({ title: t("client.failure.serverUnreachable"), description });
      break;

    default:
      // ForeignCaller, and anything added later. A request that never came from this
      // window has no user to guide, so the plain message is the honest answer.
      toast.danger({ title: t("client.failure.refused"), description });
  }

  return true;
}
