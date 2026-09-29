import { useLayoutEffect, useRef } from "react";

/**
 * A form waiting behind a button that the form then takes the place of ("+ Add a device to
 * manage"). Opened from that button, the keyboard moves into the form's first field — rather
 * than falling to nothing when the button goes, which would hand it to the section's heading
 * above every card (`devices/useSectionFocusKeeper.ts`) and scroll the form away.
 *
 * Focused in a layout effect, before the focus keeper looks: the field already has the
 * keyboard by the time it asks where it went. A form opened any other way (a link that asked
 * for it) is left to whoever opened it.
 */
export function useFocusOnOpen<T extends HTMLElement>(open: boolean) {
  const target = useRef<T>(null);
  const requested = useRef(false);

  useLayoutEffect(() => {
    if (!open || !requested.current) return;
    requested.current = false;
    target.current?.focus();
  }, [open]);

  return {
    /** The field that takes the keyboard. */
    target,
    /** Call from the button, together with opening the form. */
    request: () => {
      requested.current = true;
    },
  };
}
