"use client";

import type { DeviceId } from "./devices";

import { useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineEye, AiOutlineSearch } from "react-icons/ai";

import { HOME_DEVICE, deviceNameKey, deviceStyle, devices, mdk } from "./devices";
import { MediaIcon, MockWindow } from "./MockWindow";

type Scope = "local" | "all";

/** Interleaved on purpose: a merged result list mixes devices, it does not stack them. */
const items: { id: string; device: DeviceId }[] = [
  { id: "a", device: "desktop" },
  { id: "b", device: "laptop" },
  { id: "c", device: "nas" },
  { id: "d", device: "desktop" },
  { id: "e", device: "laptop" },
  { id: "f", device: "nas" },
];

/**
 * The multi-device library in miniature: flip the search scope from this device to all
 * devices and the other devices' resources join the same list, each tagged with the
 * device it lives on. The labels are the real page's own (`federation.*`), so the
 * picture uses the words the reader will find there.
 */
const LibraryMockup = () => {
  const { t } = useTranslation();
  const [scope, setScope] = useState<Scope>("all");
  const visible = items.filter((item) => scope === "all" || item.device === HOME_DEVICE);
  const searched = scope === "all" ? devices.map((device) => device.id) : [HOME_DEVICE];

  return (
    <figure className="flex flex-col gap-2" data-testid="multi-device-library-mockup">
      <MockWindow
        badge={
          <span className="flex shrink-0 items-center gap-1 rounded-full bg-default-200 px-2 py-0.5 text-[11px] text-default-600">
            <AiOutlineEye aria-hidden />
            {t("federation.readOnly")}
          </span>
        }
        title={t("federation.title")}
      >
        <div className="flex flex-col gap-3 p-3">
          <div className="flex flex-wrap items-center gap-2">
            <div
              aria-label={t("federation.scope.label")}
              className="inline-flex rounded-lg bg-default-100 p-0.5"
              role="group"
            >
              {(["local", "all"] as const).map((id) => (
                <button
                  key={id}
                  aria-pressed={scope === id}
                  className={`rounded-md px-2.5 py-1 text-xs transition-colors ${
                    scope === id
                      ? "bg-content1 font-medium text-foreground shadow-sm"
                      : "text-default-500 hover:text-default-700"
                  }`}
                  data-scope={id}
                  type="button"
                  onClick={() => setScope(id)}
                >
                  {t(`federation.scope.${id}`)}
                </button>
              ))}
            </div>
            <div
              aria-hidden
              className="hidden min-w-0 flex-1 items-center gap-1.5 rounded-lg border border-default-200 px-2 py-1 text-xs text-default-400 sm:flex"
            >
              <AiOutlineSearch className="shrink-0" />
              <span className="truncate">{t(mdk("browse.mock.search"))}</span>
            </div>
          </div>

          <ul className="grid grid-cols-2 gap-2 sm:grid-cols-3">
            {visible.map((item) => {
              const style = deviceStyle(item.device);

              return (
                <li
                  key={item.id}
                  className="overflow-hidden rounded-lg border border-default-200 bg-content1"
                  data-device={item.device}
                >
                  <div className={`flex aspect-[21/9] items-center justify-center ${style.tint}`}>
                    <MediaIcon className={`text-2xl ${style.text}`} kind={style.media} />
                  </div>
                  <div className="flex flex-col gap-1 p-2">
                    <span className="truncate text-xs font-medium text-foreground">
                      {t(mdk(`browse.item.${item.id}`))}
                    </span>
                    <span className="flex min-w-0 items-center gap-1 text-[11px] text-default-500">
                      <span aria-hidden className={`h-2 w-2 shrink-0 rounded-full ${style.dot}`} />
                      <span className="truncate">{t(deviceNameKey(item.device))}</span>
                    </span>
                  </div>
                </li>
              );
            })}
          </ul>

          <div className="flex flex-wrap items-center gap-x-3 gap-y-1 border-t border-default-100 pt-2 text-[11px] text-default-500">
            <span>{t("federation.coverage")}</span>
            {searched.map((id) => (
              <span key={id} className="flex items-center gap-1">
                <span aria-hidden className={`h-2 w-2 rounded-full ${deviceStyle(id).dot}`} />
                {t(deviceNameKey(id))}
              </span>
            ))}
          </div>
        </div>
      </MockWindow>
      <figcaption aria-live="polite" className="text-xs text-default-500">
        {t(mdk(`browse.mock.caption.${scope}`))}
      </figcaption>
    </figure>
  );
};

LibraryMockup.displayName = "LibraryMockup";

export default LibraryMockup;
