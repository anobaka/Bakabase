import { RemoteDevicePlatform } from "@/sdk/constants";

/**
 * The platform this window runs on. The map is only ever shown in this device's own window,
 * on the machine that runs its server, so this is this device's platform too. Phones first:
 * Android says Linux and an iPad can say Macintosh.
 */
export const localPlatform = (
  userAgent = typeof navigator === "undefined" ? "" : navigator.userAgent,
): RemoteDevicePlatform | undefined => {
  if (/Android/i.test(userAgent)) return RemoteDevicePlatform.Android;
  if (/iPhone|iPad|iPod/i.test(userAgent)) return RemoteDevicePlatform.IOS;
  if (/Windows/i.test(userAgent)) return RemoteDevicePlatform.Windows;
  if (/Macintosh|Mac OS X/i.test(userAgent)) return RemoteDevicePlatform.MacOS;
  if (/Linux|X11/i.test(userAgent)) return RemoteDevicePlatform.Linux;

  return undefined;
};
