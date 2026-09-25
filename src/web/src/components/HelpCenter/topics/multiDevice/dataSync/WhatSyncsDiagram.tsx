"use client";

import type { ReactNode } from "react";

import { useId } from "react";
import { useTranslation } from "react-i18next";
import {
  AiOutlineAppstore,
  AiOutlineFolder,
  AiOutlineKey,
  AiOutlineLock,
  AiOutlinePlayCircle,
  AiOutlineSchedule,
  AiOutlineSetting,
} from "react-icons/ai";

import { DevicePair, dsk } from "./shared";

/**
 * The kinds of definitions data sync carries, in the order the help shows them. The same set
 * as the server's `DataSyncKinds`; the help test fails when a kind is added without its words.
 */
export const syncedKinds = ["customProperty", "extensionGroup"] as const;

/** What never leaves a device, whatever is synced. */
export const neverSynced: { id: string; icon: ReactNode }[] = [
  { id: "library", icon: <AiOutlineAppstore /> },
  { id: "paths", icon: <AiOutlineFolder /> },
  { id: "programs", icon: <AiOutlinePlayCircle /> },
  { id: "secrets", icon: <AiOutlineKey /> },
  { id: "tasks", icon: <AiOutlineSchedule /> },
  { id: "settings", icon: <AiOutlineSetting /> },
];

/**
 * A property as a sketch: its name, choices in their colours, a tag with its group, and a
 * branch of levels. Shapes only, so it reads the same in every language.
 */
const PropertySketch = () => (
  <div
    aria-hidden
    className="flex flex-col gap-1.5 rounded-md border border-default-200 bg-default-50 p-2"
  >
    <div className="flex flex-wrap items-center gap-1">
      <span className="mr-1 h-2 w-12 rounded-full bg-default-400" />
      <span className="h-3.5 w-9 rounded-full border border-danger/40 bg-danger/20" />
      <span className="h-3.5 w-11 rounded-full border border-warning/40 bg-warning/20" />
      <span className="h-3.5 w-8 rounded-full border border-success/40 bg-success/20" />
    </div>
    <div className="flex flex-wrap items-center gap-1">
      <span className="flex h-3.5 items-center overflow-hidden rounded-full border border-default-300">
        <span className="h-full w-5 bg-default-300" />
        <span className="h-full w-9 bg-content1" />
      </span>
      <span className="flex items-center gap-0.5 pl-1 text-[10px] text-default-400">
        <span className="h-2.5 w-6 rounded-sm bg-default-300" />
        ›
        <span className="h-2.5 w-7 rounded-sm bg-default-300" />
        ›
        <span className="h-2.5 w-5 rounded-sm bg-default-300" />
      </span>
    </div>
  </div>
);

/** Sample extensions: file names are not prose, so they stay the same in every language. */
const EXTENSIONS = [".mp4", ".mkv", ".avi"];

const ExtensionSketch = () => (
  <div
    aria-hidden
    className="flex flex-wrap items-center gap-1 rounded-md border border-default-200 bg-default-50 p-2"
  >
    <span className="mr-1 h-2 w-10 rounded-full bg-default-400" />
    {EXTENSIONS.map((extension) => (
      <span
        key={extension}
        className="rounded border border-default-300 bg-content1 px-1.5 font-mono text-[10px] text-default-600"
      >
        {extension}
      </span>
    ))}
  </div>
);

const sketches: Record<(typeof syncedKinds)[number], ReactNode> = {
  customProperty: <PropertySketch />,
  extensionGroup: <ExtensionSketch />,
};

/**
 * What travels and what never does. The two devices, with an arrow each way, head the kinds
 * listed below them; everything else is shelved under a lock, because each device keeps it.
 */
const WhatSyncsDiagram = () => {
  const { t } = useTranslation();
  const titleId = `${useId()}-what`;

  return (
    <figure
      aria-labelledby={titleId}
      className="@container flex flex-col gap-2"
      data-testid="data-sync-what"
    >
      <h4 className="text-sm font-medium" id={titleId}>
        {t(dsk("types.title"))}
      </h4>

      <div className="flex flex-col gap-3 rounded-xl border border-default-200 bg-default-50 p-3">
        <div className="flex flex-col items-center gap-1">
          <DevicePair toHere="active" toThere="active" />
          <span className="text-center text-xs text-default-500">{t(dsk("types.travels"))}</span>
        </div>
        <ul className="grid grid-cols-1 gap-2 @min-[34rem]:grid-cols-2">
          {syncedKinds.map((kind) => (
            <li
              key={kind}
              className="flex flex-col gap-2 rounded-lg border border-default-200 bg-content1 p-3"
              data-kind={kind}
            >
              {sketches[kind]}
              <div className="min-w-0">
                <div className="text-sm font-medium">{t(dsk(`types.${kind}.title`))}</div>
                <p className="mt-0.5 text-xs text-default-600">{t(dsk(`types.${kind}.desc`))}</p>
              </div>
            </li>
          ))}
        </ul>
        <p className="text-xs text-default-500">{t(dsk("types.more"))}</p>
      </div>

      <div
        className="flex flex-col gap-2 rounded-xl border border-dashed border-default-300 p-3"
        data-testid="data-sync-never"
      >
        <div className="flex items-center gap-2">
          <span
            aria-hidden
            className="flex h-6 w-6 shrink-0 items-center justify-center rounded-md bg-default-100 text-default-600"
          >
            <AiOutlineLock />
          </span>
          <span className="text-sm font-medium">{t(dsk("never.title"))}</span>
        </div>
        <p className="text-xs text-default-500">{t(dsk("never.desc"))}</p>
        <ul className="grid grid-cols-1 gap-1.5 @min-[30rem]:grid-cols-2 @min-[46rem]:grid-cols-3">
          {neverSynced.map((item) => (
            <li
              key={item.id}
              className="flex items-start gap-2 rounded-md bg-default-100 px-2.5 py-2 text-xs text-default-700"
              data-never={item.id}
            >
              <span aria-hidden className="mt-0.5 shrink-0 text-default-500">
                {item.icon}
              </span>
              <span className="min-w-0">{t(dsk(`never.${item.id}`))}</span>
            </li>
          ))}
        </ul>
      </div>
    </figure>
  );
};

WhatSyncsDiagram.displayName = "WhatSyncsDiagram";

export default WhatSyncsDiagram;
