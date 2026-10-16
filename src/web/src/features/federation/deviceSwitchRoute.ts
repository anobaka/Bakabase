/** A transient landing route, deliberately absent from the navigation catalog. */
export const DEVICE_SWITCH_ROUTE = "/device-switch";

/** Only a pathname crosses devices; owner IDs, filters and tickets in queries do not. */
export function deviceSwitchPath(value: string | null | undefined): string {
  const path = value?.split(/[?#]/, 1)[0] ?? "/";

  return path.length <= 512 &&
    /^\/[A-Za-z0-9/_-]*$/.test(path) &&
    !path.includes("//") &&
    path !== DEVICE_SWITCH_ROUTE &&
    !path.startsWith(`${DEVICE_SWITCH_ROUTE}/`)
    ? path
    : "/";
}

export const deviceSwitchRoute = (pathname: string) =>
  `${DEVICE_SWITCH_ROUTE}?path=${encodeURIComponent(deviceSwitchPath(pathname))}`;
