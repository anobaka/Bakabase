import { useLayoutEffect, useRef, useState } from "react";

/**
 * Follows the width an element is given — what a drawing lays itself out for. The drawings
 * decide their layout from their own width rather than the window's: the same drawing stands
 * in the page's whole width, beside the details, or in the device map's narrower details column.
 */
export function useElementWidth<T extends HTMLElement = HTMLDivElement>(initial: number) {
  const ref = useRef<T>(null);
  const [width, setWidth] = useState(initial);

  useLayoutEffect(() => {
    const element = ref.current;

    if (!element) return;
    const measure = () => {
      const measured = element.getBoundingClientRect().width;

      if (measured > 0) setWidth(Math.round(measured));
    };

    measure();
    if (typeof ResizeObserver !== "function") return;
    const observer = new ResizeObserver(measure);

    observer.observe(element);

    return () => observer.disconnect();
  }, []);

  return { ref, width };
}
