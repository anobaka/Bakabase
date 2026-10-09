/** Navigation only: never include remote paths, commands, or a server address in an app link. */
export type LocalTool = "file-processor" | "file-name-modifier";

export const localToolRoute = (tool: LocalTool) => `/${tool}`;
export const localToolAppUrl = (tool: LocalTool) => `bakabase://tools/${tool}`;

/** A user-supplied loopback origin, without credentials, remote hosts, or hidden routes. */
export function localToolServerUrl(address: string, tool: LocalTool): string | undefined {
  try {
    const url = new URL(address.trim());

    if (
      !["http:", "https:"].includes(url.protocol) ||
      !["localhost", "127.0.0.1", "[::1]"].includes(url.hostname) ||
      url.username ||
      url.password ||
      url.pathname !== "/" ||
      url.search ||
      url.hash
    )
      return undefined;

    return `${url.origin}/#${localToolRoute(tool)}`;
  } catch {
    return undefined;
  }
}
