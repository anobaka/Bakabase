import { RemoteDevicePlatform } from "@/sdk/constants";

/**
 * The label for the platform a paired device reported. Shared by the remote-access
 * settings and the devices page, which list the same devices.
 */
export const remoteDevicePlatformLabelKey = (platform?: RemoteDevicePlatform) => {
  switch (platform) {
    case RemoteDevicePlatform.Windows:
      return "configuration.remoteAccess.platform.windows";
    case RemoteDevicePlatform.MacOS:
      return "configuration.remoteAccess.platform.macOS";
    case RemoteDevicePlatform.Linux:
      return "configuration.remoteAccess.platform.linux";
    case RemoteDevicePlatform.Android:
      return "configuration.remoteAccess.platform.android";
    case RemoteDevicePlatform.IOS:
      return "configuration.remoteAccess.platform.iOS";
    default:
      return "configuration.remoteAccess.platform.unknown";
  }
};
