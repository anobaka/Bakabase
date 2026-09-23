/** This is a hint file, never an options/database/credential import format. */
export interface ConnectionHints {
  format: "bakabase-client-connection-hints";
  version: 1;
  servers: {
    name?: string;
    address: string;
    pathMappings: { serverPath: string; localPath: string }[];
  }[];
}

export const MAX_HINT_FILE_BYTES = 256 * 1024;
export const CONNECTION_HINT_DRAFT_KEY = "federation.connection-hints.v1";

export function parseConnectionHints(value: unknown): ConnectionHints {
  const invalid = () => {
    throw new Error("InvalidConnectionHints");
  };
  const object = (item: unknown): Record<string, unknown> =>
    item && typeof item === "object" && !Array.isArray(item)
      ? (item as Record<string, unknown>)
      : invalid();
  const text = (item: unknown, max: number): string =>
    typeof item === "string" && item.length <= max && !/[\u0000-\u001f]/.test(item)
      ? item.trim()
      : invalid();
  const input = object(value);

  if (
    input.format !== "bakabase-client-connection-hints" ||
    input.version !== 1 ||
    !Array.isArray(input.servers) ||
    input.servers.length > 64
  )
    return invalid();

  return {
    format: "bakabase-client-connection-hints",
    version: 1,
    servers: input.servers.map((item) => {
      const server = object(item);
      const address = text(server.address, 2048);
      let url: URL;

      try {
        url = new URL(address.includes("://") ? address : `http://${address}`);
      } catch {
        return invalid();
      }
      if (
        !["http:", "https:"].includes(url.protocol) ||
        url.username ||
        url.password ||
        url.search ||
        url.hash ||
        !url.hostname
      )
        return invalid();
      if (!Array.isArray(server.pathMappings) || server.pathMappings.length > 128) return invalid();

      return {
        ...(server.name == null ? {} : { name: text(server.name, 256) }),
        address: url.href.replace(/\/$/, ""),
        pathMappings: server.pathMappings.map((item) => {
          const mapping = object(item);

          return {
            serverPath: text(mapping.serverPath, 4096),
            localPath: text(mapping.localPath, 4096),
          };
        }),
      };
    }),
  };
}

/** Repeated imports merge only safe hints; they never construct peers, grants or active mappings. */
export function mergeConnectionHints(
  previous: ConnectionHints | undefined,
  incoming: unknown,
): ConnectionHints {
  const next = parseConnectionHints(incoming);
  const servers = new Map<string, ConnectionHints["servers"][number]>();

  for (const server of [
    ...(previous ? parseConnectionHints(previous).servers : []),
    ...next.servers,
  ]) {
    const existing = servers.get(server.address);
    const mappings = new Map<string, { serverPath: string; localPath: string }>();

    for (const mapping of [...(existing?.pathMappings ?? []), ...server.pathMappings])
      mappings.set(JSON.stringify([mapping.serverPath, mapping.localPath]), mapping);
    servers.set(server.address, {
      ...(existing?.name || server.name ? { name: existing?.name || server.name } : {}),
      address: server.address,
      pathMappings: [...mappings.values()],
    });
  }
  const merged = parseConnectionHints({ ...next, servers: [...servers.values()] });

  if (new TextEncoder().encode(JSON.stringify(merged)).byteLength > MAX_HINT_FILE_BYTES)
    throw new Error("InvalidConnectionHints");

  return merged;
}

export function loadConnectionHintDraft(): ConnectionHints | undefined {
  const raw = localStorage.getItem(CONNECTION_HINT_DRAFT_KEY);

  if (!raw) return undefined;
  if (new TextEncoder().encode(raw).byteLength > MAX_HINT_FILE_BYTES)
    throw new Error("InvalidConnectionHints");

  return mergeConnectionHints(undefined, JSON.parse(raw));
}

export function saveConnectionHintDraft(hints: ConnectionHints) {
  const safe = mergeConnectionHints(undefined, hints);
  const json = JSON.stringify(safe);

  if (localStorage.getItem(CONNECTION_HINT_DRAFT_KEY) !== json)
    localStorage.setItem(CONNECTION_HINT_DRAFT_KEY, json);
}

export function clearConnectionHintDraft() {
  localStorage.removeItem(CONNECTION_HINT_DRAFT_KEY);
}
