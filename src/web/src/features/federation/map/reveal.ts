/** A vertical extent in the page's client coordinates. */
export interface Extent {
  top: number;
  bottom: number;
}

/** Room kept between what is revealed and the edge of what shows it. */
const MARGIN = 16;

const scrolls = (element: Element) => {
  const { overflowY } = getComputedStyle(element);

  return (
    (overflowY === "auto" || overflowY === "scroll" || overflowY === "overlay") &&
    element.scrollHeight > element.clientHeight
  );
};

/** The nearest ancestor that scrolls up and down, or none when the page itself does. */
const scrollerOf = (element: Element): Element | undefined => {
  for (let parent = element.parentElement; parent; parent = parent.parentElement) {
    if (parent === document.body || parent === document.documentElement) return undefined;
    if (scrolls(parent)) return parent;
  }

  return undefined;
};

/**
 * Scrolls the least that shows `extent` whole where `from` is scrolled — or, when it is taller
 * than what shows, the start of `first` (the part that matters most). Nothing moves when it
 * already shows.
 */
export function reveal(from: Element, extent: Extent, first: Extent = extent, smooth = false) {
  if (typeof window === "undefined") return;
  const scroller = scrollerOf(from);
  const view = scroller
    ? scroller.getBoundingClientRect()
    : { top: 0, bottom: window.innerHeight || document.documentElement.clientHeight };
  const room = view.bottom - view.top - 2 * MARGIN;

  if (room <= 0) return;
  const target = extent.bottom - extent.top <= room ? extent : first;
  let delta = 0;

  if (target.top < view.top + MARGIN) delta = target.top - (view.top + MARGIN);
  else if (target.bottom > view.bottom - MARGIN)
    delta = Math.min(target.bottom - (view.bottom - MARGIN), target.top - (view.top + MARGIN));
  if (Math.abs(delta) < 1) return;
  const options: ScrollToOptions = { top: delta, behavior: smooth ? "smooth" : "auto" };

  if (scroller) {
    if (typeof scroller.scrollBy === "function") scroller.scrollBy(options);
    else scroller.scrollTop += delta;
  } else if (typeof window.scrollBy === "function") window.scrollBy(options);
}
