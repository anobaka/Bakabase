import { useState } from "react";
import { useTranslation } from "react-i18next";

import DeviceDot from "./DeviceDot";

import { buttonClass, primaryClass } from "@/features/federation/components/common";
import BApi from "@/sdk/BApi";
import { useUiOptionsStore } from "@/stores/options";

/** Changes only this server's display preference, never the remote device's identity. */
export default function DeviceColorEditor({ serverId, name }: { serverId: string; name: string }) {
  const { t } = useTranslation();
  const configured = useUiOptionsStore((state) => state.data.deviceColors?.[serverId]);
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState(configured || "#3b82f6");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string>();

  const save = async (color: string | null) => {
    if (saving) return;
    setSaving(true);
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
      setOpen(false);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setSaving(false);
    }
  };

  if (!open)
    return (
      <button
        aria-label={t("deviceAppearance.editFor", { name })}
        className={`${buttonClass} !px-2 !py-1 text-xs`}
        type="button"
        onClick={() => {
          setDraft(configured || "#3b82f6");
          setError(undefined);
          setOpen(true);
        }}
      >
        <DeviceDot serverId={serverId} />
        {t("deviceAppearance.color")}
      </button>
    );

  return (
    <form
      className="space-y-2 rounded-lg bg-default-50 p-3 text-sm"
      onSubmit={(event) => {
        event.preventDefault();
        void save(draft);
      }}
    >
      <label className="flex items-center gap-2">
        <input
          aria-label={t("deviceAppearance.editFor", { name })}
          className="h-8 w-10 cursor-pointer rounded border border-divider bg-transparent p-0.5"
          disabled={saving}
          type="color"
          value={draft}
          onChange={(event) => setDraft(event.target.value)}
        />
        <span>{t("deviceAppearance.editFor", { name })}</span>
      </label>
      <p className="text-xs text-default-500">{t("deviceAppearance.scope")}</p>
      {error && (
        <p className="text-xs text-danger" role="alert">
          {error}
        </p>
      )}
      <div className="flex flex-wrap gap-2">
        <button className={primaryClass} disabled={saving} type="submit">
          {t("federation.save")}
        </button>
        <button
          className={buttonClass}
          disabled={saving}
          type="button"
          onClick={() => void save(null)}
        >
          {t("deviceAppearance.automatic")}
        </button>
        <button
          className={buttonClass}
          disabled={saving}
          type="button"
          onClick={() => setOpen(false)}
        >
          {t("federation.cancel")}
        </button>
      </div>
    </form>
  );
}
