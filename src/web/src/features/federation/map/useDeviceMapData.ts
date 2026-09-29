/*
 * The device map's data is the devices page's: one hook reads both pages' listings
 * (`hooks/useDevicesData`). Kept under its old name for the map's own modules.
 */
export type { DiscoveryState, MapSource } from "../hooks/useDevicesData";
export {
  IDLE_POLL_MS,
  LIVE_POLL_MS,
  useDevicesData as useDeviceMapData,
} from "../hooks/useDevicesData";
