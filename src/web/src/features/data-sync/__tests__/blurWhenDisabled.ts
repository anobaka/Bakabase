/**
 * What Chromium does and jsdom does not: a focused control that is disabled loses focus, to the
 * page's body — before the page does anything else. (jsdom will not blur what cannot take focus,
 * so the control is briefly enabled to let go of it.) The device map's tests do the same.
 */
export const blurWhenDisabled = () => {
  const observer = new MutationObserver((records) => {
    for (const record of records) {
      const target = record.target as HTMLElement;

      if (target === document.activeElement && target.matches(":disabled")) {
        target.removeAttribute("disabled");
        target.blur();
        target.setAttribute("disabled", "");
      }
    }
  });

  observer.observe(document.body, {
    attributes: true,
    attributeFilter: ["disabled"],
    subtree: true,
  });

  return () => observer.disconnect();
};
