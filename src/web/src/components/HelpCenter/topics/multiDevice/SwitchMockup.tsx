"use client";

import type { DeviceId } from "./devices";

import { useState } from "react";
import { useTranslation } from "react-i18next";
import {
  AiOutlineCheck,
  AiOutlineCloudServer,
  AiOutlineDesktop,
  AiOutlineDown,
  AiOutlineSetting,
} from "react-icons/ai";

import { HOME_DEVICE, deviceNameKey, deviceStyle, devices, mdk } from "./devices";
import { MediaIcon, MockWindow } from "./MockWindow";

/**
 * The desktop app's window, switching between devices. Mirrors the real switcher at the
 * top of the left menu (`ServerSwitcher`): this device first, then the managed devices;
 * while another device is shown the switcher turns amber and says "Managing". The pane
 * on the right is that device's own interface — its library, its settings — which is
 * the point the picture has to make.
 *
 * Starts on the NAS so the managing state is what the reader sees first.
 */
const SwitchMockup = () => {
  const { t } = useTranslation();
  const [current, setCurrent] = useState<DeviceId>("nas");
  const managing = current !== HOME_DEVICE;
  const currentName = t(deviceNameKey(current));
  const currentStyle = deviceStyle(current);
  const CurrentIcon = managing ? AiOutlineCloudServer : AiOutlineDesktop;

  return (
    <figure className="flex flex-col gap-2" data-testid="multi-device-switch-mockup">
      <MockWindow title={t(mdk("switch.mock.window"))}>
        <div className="flex flex-col sm:flex-row">
          {/* Left menu, with the switcher on top. */}
          <div className="flex shrink-0 flex-col gap-1 border-b border-default-200 p-2 sm:w-48 sm:border-b-0 sm:border-r">
            <div
              className={`flex items-center gap-2 rounded-lg px-2 py-1 ${
                managing
                  ? "bg-warning/10 text-warning-700 ring-1 ring-warning/50 dark:text-warning"
                  : "text-foreground"
              }`}
              data-testid="mock-switcher"
            >
              <CurrentIcon aria-hidden className="shrink-0 text-lg" />
              <span className="min-w-0 flex-1">
                {managing && (
                  <span className="block text-[10px] font-medium uppercase leading-4 tracking-wide">
                    {t("federation.switcher.managing")}
                  </span>
                )}
                <span className="block truncate text-sm font-medium leading-5">{currentName}</span>
              </span>
              <AiOutlineDown aria-hidden className="shrink-0 text-xs opacity-60" />
            </div>

            <div className="rounded-xl border border-default-200 bg-content1 p-1 shadow-sm">
              <p className="px-2 pb-1 pt-1 text-[10px] font-medium uppercase tracking-wide text-default-400">
                {t("federation.switcher.switchTo")}
              </p>
              <div aria-label={t(mdk("switch.mock.listLabel"))} role="group">
                {devices.map((device) => {
                  const isCurrent = device.id === current;
                  const isHome = device.id === HOME_DEVICE;

                  return (
                    <button
                      key={device.id}
                      aria-pressed={isCurrent}
                      className={`flex w-full items-center gap-2 rounded-lg px-2 py-1 text-left text-xs hover:bg-default-100 ${
                        isCurrent ? "bg-default-100" : ""
                      }`}
                      data-device={device.id}
                      type="button"
                      onClick={() => setCurrent(device.id)}
                    >
                      {isHome ? (
                        <AiOutlineDesktop aria-hidden className="shrink-0" />
                      ) : (
                        <span
                          aria-hidden
                          className="ml-[3px] mr-[3px] h-2 w-2 shrink-0 rounded-full bg-success"
                        />
                      )}
                      <span className="min-w-0 flex-1 truncate">{t(deviceNameKey(device.id))}</span>
                      {isHome && (
                        <span className="shrink-0 text-[10px] text-default-400">
                          {t("federation.thisDevice")}
                        </span>
                      )}
                      {isCurrent && (
                        <AiOutlineCheck aria-hidden className="shrink-0 text-primary" />
                      )}
                    </button>
                  );
                })}
              </div>
              <div aria-hidden className="my-1 h-px bg-default-200" />
              <div
                aria-hidden
                className="flex items-center gap-2 px-2 py-1 text-xs text-default-500"
              >
                <AiOutlineSetting className="shrink-0" />
                <span className="truncate">{t("federation.switcher.manageDevices")}</span>
              </div>
            </div>

            <div aria-hidden className="hidden flex-col gap-1.5 px-2 pt-2 sm:flex">
              <span className="h-2 w-20 rounded bg-default-200" />
              <span className="h-2 w-24 rounded bg-default-200" />
              <span className="h-2 w-16 rounded bg-default-200" />
            </div>
          </div>

          {/* The shown device's own interface. */}
          <div className="flex min-w-0 flex-1 flex-col gap-2 p-3" data-shown={current}>
            <div
              className={`flex items-center gap-2 rounded-lg border px-2.5 py-1.5 ${currentStyle.border} ${currentStyle.tint}`}
            >
              <MediaIcon
                className={`shrink-0 text-base ${currentStyle.text}`}
                kind={currentStyle.media}
              />
              <span className="min-w-0 flex-1 truncate text-xs font-medium text-foreground">
                {t(mdk("switch.mock.ownUi"), { name: currentName })}
              </span>
            </div>
            <div aria-hidden className="grid grid-cols-3 gap-1.5 lg:grid-cols-6">
              {[0, 1, 2, 3, 4, 5].map((index) => (
                <div
                  key={index}
                  className={`flex aspect-square items-center justify-center rounded-md ${currentStyle.tint}`}
                >
                  <MediaIcon className={`text-xl ${currentStyle.text}`} kind={currentStyle.media} />
                </div>
              ))}
            </div>
            <div aria-hidden className="flex flex-col gap-1.5 pt-1">
              <span className="h-2 w-3/4 rounded bg-default-200" />
              <span className="h-2 w-1/2 rounded bg-default-200" />
            </div>
            <p className="text-[11px] text-default-500">
              {managing
                ? t(mdk("switch.mock.remote"), { name: currentName })
                : t(mdk("switch.mock.local"))}
            </p>
          </div>
        </div>
      </MockWindow>
      <figcaption aria-live="polite" className="text-xs text-default-500">
        {managing
          ? t(mdk("switch.mock.caption.remote"), { name: currentName })
          : t(mdk("switch.mock.caption.local"))}
      </figcaption>
    </figure>
  );
};

SwitchMockup.displayName = "SwitchMockup";

export default SwitchMockup;
