import { useState } from "react";
import { useTranslation } from "react-i18next";

import DeviceDot from "./DeviceDot";
import { deviceColor } from "./resourceIdentity";

import { buttonClass, primaryClass } from "@/features/federation/components/common";
import BApi from "@/sdk/BApi";
import { useUiOptionsStore } from "@/stores/options";

/** Changes only this server's display preference, never the remote device's identity. */
export default function DeviceColorEditor({
  serverId,
  name,
  embedded = false,
  onSavingChange,
}: {
  serverId: string;
  name: string;
  embedded?: boolean;
  onSavingChange?: (saving: boolean) => void;
}) {
  const { t } = useTranslation();
  const currentColor = useUiOptionsStore((state) => deviceColor(serverId, state.data.deviceColors));
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState<string>();
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string>();

  const save = async (color: string | null) => {
    if (saving) return;
    setSaving(true);
    onSavingChange?.(true);
    setSaved(false);
    setError(undefined);
    try {
      const result = await BApi.options.setDeviceColor(serverId, { color: color ?? undefined });

      if (result.code) throw new Error(result.message || t("deviceAppearance.failed"));
      // The options hub updates other browsers. Update this row immediately without waiting for it.
      const store = useUiOptionsStore.getState();
      const colors = { ...store.data.deviceColors };

      if (color) colors[serverId] = color;
      else delete colors[serverId];
      store.update({ deviceColors: colors });
      setDraft(undefined);
      setSaved(true);
      setOpen(false);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setSaving(false);
      onSavingChange?.(false);
    }
  };

  if (!open && !embedded)
    return (
      <button
        aria-label={t("deviceAppearance.editFor", { name })}
        className={`${buttonClass} !px-2 !py-1 text-xs`}
        type="button"
        onClick={() => {
          setDraft(undefined);
          setSaved(false);
          setError(undefined);
          setOpen(true);
        }}
      >
        <DeviceDot name={name} serverId={serverId} />
        {t("deviceAppearance.color")}
      </button>
    );

  return (
    <form
      className={embedded ? "space-y-2 text-sm" : "space-y-2 rounded-lg bg-default-50 p-3 text-sm"}
      onSubmit={(event) => {
        event.preventDefault();
        void save(draft ?? currentColor);
      }}
    >
      <label className="flex items-center gap-2">
        <input
          aria-label={t("deviceAppearance.editFor", { name })}
          className="h-8 w-10 cursor-pointer rounded border border-divider bg-transparent p-0.5"
          disabled={saving}
          type="color"
          value={draft ?? currentColor}
          onChange={(event) => {
            setDraft(event.target.value);
            setSaved(false);
          }}
        />
        <span>{t("deviceAppearance.editFor", { name })}</span>
      </label>
      <p className="text-xs text-default-500">{t("deviceAppearance.scope")}</p>
      {embedded && saved && (
        <p className="text-xs text-success" role="status">
          {t("deviceAppearance.colorSaved")}
        </p>
      )}
      {error && (
        <p className="text-xs text-danger" role="alert">
          {error}
        </p>
      )}
      <div className="flex flex-wrap gap-2">
        <button className={primaryClass} disabled={saving} type="submit">
          {t(embedded ? "deviceAppearance.saveColor" : "federation.save")}
        </button>
        <button
          className={buttonClass}
          disabled={saving}
          type="button"
          onClick={() => void save(null)}
        >
          {t("deviceAppearance.automatic")}
        </button>
        {!embedded && (
          <button
            className={buttonClass}
            disabled={saving}
            type="button"
            onClick={() => setOpen(false)}
          >
            {t("federation.cancel")}
          </button>
        )}
      </div>
    </form>
  );
}
