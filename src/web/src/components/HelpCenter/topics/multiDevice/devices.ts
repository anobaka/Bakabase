/**
 * The three example devices every multi-device picture draws. One list, so the
 * topology, the browsing mock-up and the switching mock-up always agree on names and
 * colours — a reader follows "the NAS" from one picture to the next by its colour.
 */
export type DeviceId = "desktop" | "laptop" | "nas";

/** What each device keeps on its own storage, so the mock-ups can show whose it is. */
export type MediaKind = "video" | "comic" | "music";

export interface DeviceStyle {
  id: DeviceId;
  /**
   * Someone can sit at it: the desktop app runs there, with a window. A NAS or Docker
   * server is headless — it is only ever reached from the others, never the one you use.
   */
  hasWindow: boolean;
  media: MediaKind;
  /** Stroke for glyphs and borders. Literal class names so Tailwind can see them. */
  stroke: string;
  fill: string;
  /** Solid fill, for small marks such as badges. */
  solid: string;
  /** Text drawn on {@link solid}: the theme's matching foreground, never a fixed white. */
  solidText: string;
  text: string;
  /** Solid background, for dots. */
  dot: string;
  /** A light tint for chips and thumbnails. */
  tint: string;
  border: string;
}

export const devices: DeviceStyle[] = [
  {
    id: "desktop",
    hasWindow: true,
    media: "video",
    stroke: "stroke-primary",
    fill: "fill-primary/10",
    solid: "fill-primary",
    solidText: "fill-primary-foreground",
    text: "text-primary",
    dot: "bg-primary",
    tint: "bg-primary/15",
    border: "border-primary/40",
  },
  {
    id: "laptop",
    hasWindow: true,
    media: "comic",
    stroke: "stroke-secondary",
    fill: "fill-secondary/10",
    solid: "fill-secondary",
    solidText: "fill-secondary-foreground",
    text: "text-secondary",
    dot: "bg-secondary",
    tint: "bg-secondary/15",
    border: "border-secondary/40",
  },
  {
    id: "nas",
    hasWindow: false,
    media: "music",
    stroke: "stroke-success",
    fill: "fill-success/10",
    solid: "fill-success",
    solidText: "fill-success-foreground",
    text: "text-success",
    dot: "bg-success",
    tint: "bg-success/15",
    border: "border-success/40",
  },
];

export const deviceStyle = (id: DeviceId) => devices.find((device) => device.id === id)!;

/** The device the reader is assumed to sit at in the mock-ups. */
export const HOME_DEVICE: DeviceId = "desktop";

export const mdk = (key: string) => `helpCenter.multiDevice.${key}`;

/** The i18n key of a device's display name. */
export const deviceNameKey = (id: DeviceId) => mdk(`device.${id}`);
