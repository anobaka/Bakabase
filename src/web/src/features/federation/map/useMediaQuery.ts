import { useEffect, useState } from "react";

const matches = (query: string) =>
  typeof window !== "undefined" &&
  typeof window.matchMedia === "function" &&
  !!window.matchMedia(query)?.matches;

/** Whether a media query matches now, following it as the window changes. */
export function useMediaQuery(query: string) {
  const [value, setValue] = useState(() => matches(query));

  useEffect(() => {
    if (typeof window === "undefined" || typeof window.matchMedia !== "function") return;
    const list = window.matchMedia(query);

    if (!list) return;
    const update = () => setValue(list.matches);

    update();
    if (typeof list.addEventListener === "function") {
      list.addEventListener("change", update);

      return () => list.removeEventListener("change", update);
    }
    list.addListener?.(update);

    return () => list.removeListener?.(update);
  }, [query]);

  return value;
}
