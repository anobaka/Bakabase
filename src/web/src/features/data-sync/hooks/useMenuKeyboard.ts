import type { KeyboardEvent, RefObject } from "react";

import { useEffect } from "react";

const itemSelector = '[role="menuitem"], [role="menuitemradio"], [role="menuitemcheckbox"]';

/**
 * A small menu's keyboard, as the ARIA menu pattern has it: opening the menu puts focus on its
 * chosen item (else its first), the arrow keys, Home and End move between the items — none of
 * which is in the Tab order — and Tab closes it, leaving the keyboard on its button. Escape is
 * the menu's own (it closes only the menu, never the details around it).
 */
export function useMenuKeyboard(
  open: boolean,
  menu: RefObject<HTMLElement>,
  trigger: RefObject<HTMLElement>,
  close: () => void,
) {
  const items = () => Array.from(menu.current?.querySelectorAll<HTMLElement>(itemSelector) ?? []);

  useEffect(() => {
    if (!open) return;
    const list = items();

    (list.find((item) => item.getAttribute("aria-checked") === "true") ?? list[0])?.focus();
  }, [open]);

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
