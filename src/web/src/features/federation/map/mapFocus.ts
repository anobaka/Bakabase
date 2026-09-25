import type { MapSelection } from "./DeviceMapCanvas";

import { SELF_ID } from "./graph";

/*
 * The map's controls by what they stand for. The drawing and the list are two renderings of
 * the same devices and relationships: each control carries its device's `data-node` or its
 * relationship's `data-edge` in both. Which rendering is shown depends on the width the map
 * is given — opening the details beside it can turn the drawing into the list, closing them
 * the list back into the drawing — and every control of one is replaced by the other's. So
 * where the keyboard was is remembered by what it was on, never by the element, and found
 * again in whichever rendering is there now.
 */

/** A value for an attribute selector: quotes and backslashes escaped. */
export const attributeValue = (value: string) => value.replace(/["\\]/g, "\\$&");

/** What a control of the map stands for, when `element` is one inside `region`. */
export const mapItemOf = (
  element: EventTarget | Element | null | undefined,
  region: Element | null | undefined,
): MapSelection | undefined => {
  if (!region || !(element instanceof Element) || !region.contains(element)) return undefined;
  const control = element.closest("[data-node],[data-edge]");

  if (!control || !region.contains(control)) return undefined;
  const edge = control.getAttribute("data-edge");

  if (edge) return { type: "edge", id: edge };
  const node = control.getAttribute("data-node");

  return node ? { type: "node", id: node } : undefined;
};

/** Focus that is nowhere: on the page's body, or on an element no longer in the page. */
export const focusLost = (active: Element | null = document.activeElement) =>
  !active || active === document.body || !active.isConnected;

/**
 * Gives the keyboard to what stands for `item` in `region` now: the relationship itself, else
 * its device, else this device. Answers whether anything took it.
 */
export const focusMapItem = (region: Element | null | undefined, item: MapSelection) => {
  if (!region) return false;
  const find = (attribute: "data-node" | "data-edge", id: string) =>
    region.querySelector<HTMLElement | SVGElement>(`[${attribute}="${attributeValue(id)}"]`);
  // A relationship's id is `${kind}:${nodeId}`.
  const nodeId = item.type === "node" ? item.id : item.id.slice(item.id.indexOf(":") + 1);
  const target =
    (item.type === "edge" ? find("data-edge", item.id) : null) ??
    find("data-node", nodeId) ??
    find("data-node", SELF_ID);

  target?.focus();

  return !!target && document.activeElement === target;
};
