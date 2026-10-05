/** Add a separate Baidu access code without replacing a code already embedded in the share URL. */
export function getDownloadUrl(link?: string | null, code?: string | null): string {
  const value = link?.trim() ?? "";
  const accessCode = code?.trim();

  if (!value || !accessCode) return value;
  try {
    const url = new URL(value);

    if (
      !["http:", "https:"].includes(url.protocol) ||
      !["pan.baidu.com", "yun.baidu.com"].includes(url.hostname.toLowerCase()) ||
      url.searchParams.has("pwd")
    )
      return value;
    url.searchParams.set("pwd", accessCode);

    return url.toString();
  } catch {
    return value;
  }
}

export type LinkHealthStatus = "available" | "unavailable" | "unknown" | "unsupported";

/** Unsupported probes are distinct from attempted checks with an inconclusive result. */
export function getLinkHealthStatus(
  health?: { status?: string; reason?: string | null } | null,
): LinkHealthStatus {
  if (
    health?.status === "available" ||
    health?.status === "unavailable" ||
    health?.status === "unsupported"
  )
    return health.status;

  return [
    "unsupportedProvider",
    "unsupportedUrl",
    "unsupportedShareUrl",
    "megaFolderOrUnsupportedShareUrl",
  ].includes(health?.reason ?? "")
    ? "unsupported"
    : "unknown";
}
