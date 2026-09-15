import { useRef, useState } from "react";

/** These editors own closing so a rejected save preserves the draft instead of dismissing it. */
export function useProfileModalSave<T>(
  onSubmit: ((value: T) => unknown | Promise<unknown>) | undefined,
  fallbackError: string,
) {
  const [visible, setVisible] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string>();
  const pending = useRef(false);

  const close = () => {
    if (!pending.current) setVisible(false);
  };

  const save = async (value: T) => {
    if (pending.current) return;
    pending.current = true;
    setSaving(true);
    setError(undefined);
    try {
      await onSubmit?.(value);
      setVisible(false);
    } catch (cause) {
      setError(cause instanceof Error && cause.message ? cause.message : fallbackError);
    } finally {
      pending.current = false;
      setSaving(false);
    }
  };

  return { visible, saving, error, close, save };
}

/** Shared inputs store dotted extensions; presets must use the same form to avoid duplicate matches. */
export const normalizeProfileExtensions = (extensions: string[] = []) =>
  Array.from(
    new Set(
      extensions
        .map((extension) => extension.trim().replace(/^\.+/, "").toLowerCase())
        .filter(Boolean)
        .map((extension) => `.${extension}`),
    ),
  );
