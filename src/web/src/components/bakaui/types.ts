type Color = "default" | "primary" | "secondary" | "success" | "warning";
type OverlayPlacement =
  | "top"
  | "bottom"
  | "right"
  | "left"
  | "top-start"
  | "top-end"
  | "bottom-start"
  | "bottom-end"
  | "left-start"
  | "left-end"
  | "right-start"
  | "right-end";

type DestroyableProps = {
  onDestroyed?: () => any;
};

export type { Color, OverlayPlacement, DestroyableProps };
