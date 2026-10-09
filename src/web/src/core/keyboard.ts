/** Keyboard conventions belong to the browser's device, never the connected server. */
type BrowserNavigator = {
  platform?: string;
  userAgent?: string;
  userAgentData?: { platform?: string };
};

export const usesAppleKeys = (
  browser: BrowserNavigator | undefined = typeof navigator === "undefined" ? undefined : navigator,
): boolean => {
  const platform = browser?.userAgentData?.platform || browser?.platform;

  return /mac|iphone|ipad|ipod/i.test(platform || browser?.userAgent || "");
};

export const primaryModifierKey = () => (usesAppleKeys() ? "Meta" : "Control");
export const primaryModifierLabel = () => (usesAppleKeys() ? "⌘" : "Ctrl");
export const altModifierLabel = () => (usesAppleKeys() ? "⌥" : "Alt");
export const shiftModifierLabel = () => (usesAppleKeys() ? "⇧" : "Shift");
export const primaryShortcutLabel = (key: string) => `${primaryModifierLabel()}+${key}`;
export const deleteShortcutLabel = () => (usesAppleKeys() ? "⌘+⌫" : "Delete");

type Modifiers = Pick<KeyboardEvent, "ctrlKey" | "metaKey" | "altKey" | "shiftKey">;

export const isPrimaryModifierPressed = (event: Pick<Modifiers, "ctrlKey" | "metaKey">) =>
  usesAppleKeys() ? event.metaKey && !event.ctrlKey : event.ctrlKey && !event.metaKey;

export const hasKeyboardModifier = (event: Modifiers) =>
  event.ctrlKey || event.metaKey || event.altKey || event.shiftKey;

export const matchesPrimaryShortcut = (event: KeyboardEvent, key: string) =>
  isPrimaryModifierPressed(event) &&
  !event.altKey &&
  !event.shiftKey &&
  !event.isComposing &&
  !event.repeat &&
  !event.defaultPrevented &&
  event.key.toLowerCase() === key.toLowerCase();

export const isDeleteShortcut = (event: KeyboardEvent) =>
  (event.key === "Delete" && !hasKeyboardModifier(event)) ||
  (usesAppleKeys() && matchesPrimaryShortcut(event, "Backspace"));

export const isEditableTarget = (target: EventTarget | null) =>
  target instanceof Element &&
  !!target.closest(
    "input, textarea, select, [role='textbox'], [role='combobox'], [contenteditable]:not([contenteditable='false'])",
  );

export const shouldIgnoreGlobalShortcut = (event: KeyboardEvent) =>
  event.defaultPrevented ||
  event.isComposing ||
  event.keyCode === 229 ||
  isEditableTarget(event.target) ||
  (event.target instanceof Element &&
    !!event.target.closest("[role='dialog'], [role='menu'], [role='menuitem'], .szh-menu"));
