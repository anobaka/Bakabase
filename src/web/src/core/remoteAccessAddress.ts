import { ClientMode, RemoteAccessMode } from "@/sdk/constants";

/** A shareable HTTP(S) origin. The server performs the authoritative validation. */
export function normalizeRemoteAccessAddress(value: string): string | undefined {
  try {
    const url = new URL(value.trim());
    const host = url.hostname.toLowerCase().replace(/\.$/, "");

    if (
      !["http:", "https:"].includes(url.protocol) ||
      url.username ||
      url.password ||
      url.search ||
      url.hash ||
      url.pathname !== "/" ||
      host === "localhost" ||
      host.endsWith(".localhost") ||
      host === "host.docker.internal" ||
      host === "0.0.0.0" ||
      host === "[::]" ||
      host === "[::1]" ||
      /^127\./.test(host) ||
      /^169\.254\./.test(host) ||
      /^198\.(18|19)\./.test(host) ||
      /^\[fe[89ab]/i.test(host)
    )
      return undefined;

    return url.origin;
  } catch {
    return undefined;
  }
}

export interface AddressObservationContext {
  isLocal: boolean;
  mode: RemoteAccessMode;
  clientMode: ClientMode;
  serverReachable?: boolean;
  paired?: boolean;
  /** The relay's real upstream address; never its loopback browser origin. */
  serverAddress?: string;
}

export function addressCandidateForContext(
  context: AddressObservationContext,
  apiEndpoint: string,
  browserOrigin: string,
): string | undefined {
  if (
    context.serverReachable === false ||
    !(context.isLocal || context.paired || context.mode === RemoteAccessMode.Unrestricted)
  )
    return undefined;

  if (context.clientMode === ClientMode.PureClient)
    return context.serverAddress ? normalizeRemoteAccessAddress(context.serverAddress) : undefined;

  try {
    return normalizeRemoteAccessAddress(
      new URL(apiEndpoint || browserOrigin, `${browserOrigin}/`).href,
    );
  } catch {
    return undefined;
  }
}

/** Coalesce concurrent calls; refresh accepted candidates hourly and retry failures after 30 seconds. */
export function createAddressObserver(observe: (address: string) => Promise<boolean>) {
  const retryAfter = new Map<string, number>();

  return async (context: AddressObservationContext, apiEndpoint: string, browserOrigin: string) => {
    const address = addressCandidateForContext(context, apiEndpoint, browserOrigin);

    if (!address || (retryAfter.get(address) ?? 0) > Date.now()) return false;
    retryAfter.set(address, Infinity);
    let accepted = false;

    try {
      accepted = await observe(address);

      return accepted;
    } catch {
      // Automatic collection must never interrupt startup or display an error toast.
      return false;
    } finally {
      retryAfter.set(address, Date.now() + (accepted ? 60 * 60_000 : 30_000));
    }
  };
}
