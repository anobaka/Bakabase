/**
 * Classifies a raw updater error into a translation key.
 *
 * The backend surfaces `Exception.Message` verbatim. For a TLS failure .NET puts
 * the useful part in the *inner* exception, so the outer message the user ends up
 * seeing is "The SSL connection could not be established, see inner exception." —
 * indistinguishable from a flaky connection, which is exactly how an expired
 * certificate on the update endpoint gets misread as bad network.
 *
 * Matching on message text is admittedly a heuristic, but these strings come from
 * the .NET/Velopack stack rather than from user data, and an unrecognised message
 * simply falls back to the generic key plus the raw detail — so a miss costs
 * nothing beyond today's behaviour.
 */
export type UpdateErrorKind = "certificate" | "connectivity" | "unknown";

export interface UpdateErrorDescription {
  kind: UpdateErrorKind;
  /** Translation key for the human-readable explanation. */
  messageKey: string;
  /** The original backend text, kept so the real cause is never hidden. */
  detail?: string;
}

const certificateMarkers = [
  "ssl connection could not be established",
  "remotecertificate",
  "certificate is invalid",
  "certificate has expired",
  "nottimevalid",
  "untrustedroot",
  "partialchain",
  "authentication failed because the remote party",
];

const connectivityMarkers = [
  "no such host is known",
  "name or service not known",
  "connection refused",
  "actively refused",
  "timed out",
  "timeout",
  "network is unreachable",
  "unable to connect to the remote server",
  "a task was canceled",
  "temporary failure in name resolution",
  "proxy",
];

export function describeUpdateError(error?: string): UpdateErrorDescription | undefined {
  if (!error || error.trim().length === 0) {
    return undefined;
  }

  const haystack = error.toLowerCase();

  // Certificate first: a TLS failure often also mentions "connection", so the more
  // specific classification has to win.
  if (certificateMarkers.some((m) => haystack.includes(m))) {
    return {
      kind: "certificate",
      messageKey: "appUpdate.error.certificate",
      detail: error,
    };
  }

  if (connectivityMarkers.some((m) => haystack.includes(m))) {
    return {
      kind: "connectivity",
      messageKey: "appUpdate.error.connectivity",
      detail: error,
    };
  }

  return {
    kind: "unknown",
    messageKey: "appUpdate.error.unknown",
    detail: error,
  };
}
