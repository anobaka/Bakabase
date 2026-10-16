/* eslint-disable jsx-a11y/no-autofocus -- Focus the dialog's primary naming field when it opens. */
import { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { HiOutlineCheck, HiOutlineCollection, HiOutlineColorSwatch } from "react-icons/hi";

import { Button, Input, Modal } from "@/components/bakaui";

type LibraryAppearance = {
  name: string;
  color: string;
};

type Props = {
  library?: { name: string; color?: string | null };
  onSave: (value: LibraryAppearance) => Promise<void>;
  onClose: () => void;
};

const presetColors = [
  "#6366f1",
  "#8b5cf6",
  "#db2777",
  "#d97706",
  "#059669",
  "#0891b2",
  "#2563eb",
  "#64748b",
];

export default function MediaLibraryEditor({ library, onSave, onClose }: Props) {
  const { t } = useTranslation();
  const [name, setName] = useState(library?.name ?? "");
  const [color, setColor] = useState(library?.color || presetColors[0]);
  const [nameTouched, setNameTouched] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string>();
  const savingRef = useRef(false);
  const nameEmpty = !name.trim();
  const nameInvalid = nameEmpty && (nameTouched || name.length > 0);
  const pickerColor = /^#[\da-f]{6}$/i.test(color)
    ? color
    : /^#[\da-f]{3}$/i.test(color)
      ? `#${color
          .slice(1)
          .split("")
          .map((digit) => digit + digit)
          .join("")}`
      : presetColors[0];

  const close = () => {
    if (!savingRef.current) onClose();
  };

  const save = async () => {
    if (savingRef.current) return;
    if (nameEmpty) {
      setNameTouched(true);

      return;
    }
    savingRef.current = true;
    setSaving(true);
    setError(undefined);
    try {
      await onSave({ name: name.trim(), color });
      onClose();
    } catch {
      setError(t("mediaLibrary.error.save"));
    } finally {
      savingRef.current = false;
      setSaving(false);
    }
  };

  return (
    <Modal
      visible
      classNames={{
        base: "rounded-2xl border border-default-200/70",
        header: "pb-1 pt-6",
      }}
      footer={false}
      hideCloseButton={saving}
      isDismissable={!saving}
      isKeyboardDismissDisabled={saving}
      size="md"
      title={t(library ? "mediaLibrary.editor.editTitle" : "mediaLibrary.editor.createTitle")}
      onClose={close}
    >
      <form
        className="flex flex-col gap-6 pb-5"
        onSubmit={(event) => {
          event.preventDefault();
          void save();
        }}
      >
        <p className="text-sm leading-6 text-default-500">{t("mediaLibrary.editor.description")}</p>

        <Input
          autoFocus
          isRequired
          autoComplete="off"
          classNames={{ inputWrapper: "border-default-200" }}
          errorMessage={nameInvalid ? t("mediaLibrary.error.nameEmpty") : undefined}
          isDisabled={saving}
          isInvalid={nameInvalid}
          label={t("mediaLibrary.editor.name.label")}
          labelPlacement="outside"
          placeholder={t("mediaLibrary.editor.name.placeholder")}
          radius="lg"
          value={name}
          variant="bordered"
          onBlur={() => setNameTouched(true)}
          onValueChange={setName}
        />

        <fieldset className="min-w-0" disabled={saving}>
          <legend className="mb-3 text-sm font-medium text-foreground">
            {t("mediaLibrary.editor.color.label")}
          </legend>
          <div className="flex flex-wrap items-center gap-2">
            {presetColors.map((preset) => {
              const selected = color.toLowerCase() === preset;

              return (
                <button
                  key={preset}
                  aria-label={t("mediaLibrary.editor.color.option", { color: preset })}
                  aria-pressed={selected}
                  className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full border-2 border-background text-white shadow-sm outline-none transition-transform hover:scale-110 focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 focus-visible:ring-offset-background disabled:cursor-not-allowed disabled:opacity-50 disabled:hover:scale-100"
                  style={{ backgroundColor: preset }}
                  type="button"
                  onClick={() => setColor(preset)}
                >
                  {selected && <HiOutlineCheck aria-hidden className="text-lg" strokeWidth={2.5} />}
                </button>
              );
            })}
            <label className="relative ml-1 inline-flex h-8 items-center gap-2 overflow-hidden rounded-full border border-default-200 px-3 text-xs text-default-600 transition-colors hover:bg-default-100 focus-within:ring-2 focus-within:ring-primary focus-within:ring-offset-2 focus-within:ring-offset-background">
              <HiOutlineColorSwatch aria-hidden className="text-base" />
              {t("mediaLibrary.editor.color.custom")}
              <input
                aria-label={t("mediaLibrary.editor.color.custom")}
                className="absolute inset-0 h-full w-full cursor-pointer opacity-0 disabled:cursor-not-allowed"
                disabled={saving}
                type="color"
                value={pickerColor}
                onChange={(event) => {
                  if (!savingRef.current) setColor(event.target.value);
                }}
                onKeyDown={(event) => {
                  // Escape cancels the native color picker without discarding the draft.
                  if (event.key === "Escape") event.stopPropagation();
                }}
              />
            </label>
          </div>
        </fieldset>

        <div className="rounded-xl border border-default-200/70 bg-default-50/70 p-4">
          <p className="mb-3 text-xs font-medium text-default-500">
            {t("mediaLibrary.editor.preview")}
          </p>
          <div className="flex items-center gap-3">
            <div
              className="relative flex h-11 w-11 shrink-0 items-center justify-center overflow-hidden rounded-xl"
              style={{ color }}
            >
              <span
                aria-hidden
                className="absolute inset-0 opacity-10"
                style={{ backgroundColor: color }}
              />
              <HiOutlineCollection aria-hidden className="relative text-2xl" />
            </div>
            <span className="min-w-0 break-words text-sm font-semibold text-foreground">
              {name.trim() || t("mediaLibrary.editor.name.placeholder")}
            </span>
          </div>
        </div>

        {error && (
          <p className="rounded-xl bg-danger/10 px-4 py-3 text-sm text-danger" role="alert">
            {error}
          </p>
        )}

        <div className="flex justify-end gap-2 border-t border-default-100 pt-5">
          <Button isDisabled={saving} radius="lg" type="button" variant="light" onPress={close}>
            {t("mediaLibrary.action.cancel")}
          </Button>
          <Button
            color="primary"
            isDisabled={nameEmpty || saving}
            isLoading={saving}
            radius="lg"
            type="submit"
          >
            {t(library ? "mediaLibrary.action.save" : "mediaLibrary.action.create")}
          </Button>
        </div>
      </form>
    </Modal>
  );
}
