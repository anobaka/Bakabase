/** A presentation hint, never permission to run a native action. The access
 * context/relay still decides whether that action can execute on this device. */
export const isDesktopAppWindow = (userAgent = navigator.userAgent): boolean =>
  /(?:^|\s)BakabaseDesktop\/\d+(?:\.\d+)*(?:\s|$)/.test(userAgent);
