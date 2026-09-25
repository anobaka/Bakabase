import type { KeyboardEvent, RefObject } from "react";

import { useEffect, useRef } from "react";

const itemSelector = '[role="menuitem"], [role="menuitemradio"], [role="menuitemcheckbox"]';

/**
 * A small menu's keyboard, as the ARIA menu pattern has it: opening the menu puts focus on its
 * chosen item (else its first), the arrow keys, Home and End move between the items — none of
 * which is in the Tab order — and Tab closes it, leaving the keyboard on its button.
 *
 * Escape closes the menu, and only the menu, leaving the keyboard on its button. It is taken on
 * `root` — the element holding the button and the menu — by a listener of the element's own:
 * the details around a menu close on Escape through a listener on their own element
 * (`useEscapeKey`), which the event reaches before any React handler, since React listens at
 * the page's root.
 */
export function useMenuKeyboard(
  open: boolean,
  menu: RefObject<HTMLElement>,
  trigger: RefObject<HTMLElement>,
  close: () => void,
  root: RefObject<HTMLElement>,
) {
  const items = () => Array.from(menu.current?.querySelectorAll<HTMLElement>(itemSelector) ?? []);
  const latestClose = useRef(close);

  latestClose.current = close;

  useEffect(() => {
    if (!open) return;
    const list = items();

    (list.find((item) => item.getAttribute("aria-checked") === "true") ?? list[0])?.focus();
  }, [open]);

  useEffect(() => {
    const element = root.current;

    if (!open || !element) return;
    const onKeyDown = (event: globalThis.KeyboardEvent) => {
      if (event.key !== "Escape") return;
      event.preventDefault();
      event.stopPropagation();
      latestClose.current();
      trigger.current?.focus();
    };

    element.addEventListener("keydown", onKeyDown);

    return () => element.removeEventListener("keydown", onKeyDown);
  }, [open, root, trigger]);

  return (event: KeyboardEvent<HTMLElement>) => {
    const list = items();

    if (!list.length) return;
    const at = list.indexOf(document.activeElement as HTMLElement);
    const move = (to: number) => {
      event.preventDefault();
      list[(to + list.length) % list.length].focus();
    };

    switch (event.key) {
      case "ArrowDown":
        move(at + 1);
        break;
      case "ArrowUp":
        move(at < 0 ? list.length - 1 : at - 1);
        break;
      case "Home":
        move(0);
        break;
      case "End":
        move(list.length - 1);
        break;
      case "Tab":
        event.preventDefault();
        close();
        trigger.current?.focus();
        break;
    }
  };
}
