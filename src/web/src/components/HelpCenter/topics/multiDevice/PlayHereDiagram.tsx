"use client";

import type { ReactNode } from "react";

import { useTranslation } from "react-i18next";
import { AiOutlinePlayCircle, AiOutlineSwap } from "react-icons/ai";

import { HOME_DEVICE, deviceNameKey, deviceStyle, mdk } from "./devices";
import { MediaIcon } from "./MockWindow";

/**
 * Sample paths, not prose: the same in every language. The NAS keeps its music under
 * a Linux path; the computer reaches the same folder as a network share.
 */
const SOURCE_ROOT = "/volume1/music";
const LOCAL_ROOT = "\\\\nas\\music";
const FILE = ["Live", "01.flac"];

/**
 * Why path mappings exist, in one line: the file lives on the NAS, the player runs on
 * the computer you sit at, and the mapping is what lets one find the other.
 */
const PlayHereDiagram = () => {
  const { t } = useTranslation();
  const source = deviceStyle("nas");
  const home = deviceStyle(HOME_DEVICE);

  const steps: { id: string; tone: string; icon: ReactNode; title: string; path: string }[] = [
    {
      id: "source",
      tone: `${source.border} ${source.tint}`,
      icon: <MediaIcon className={source.text} kind={source.media} />,
      title: t(mdk("switch.play.source"), { name: t(deviceNameKey("nas")) }),
      path: [SOURCE_ROOT, ...FILE].join("/"),
    },
    {
      id: "mapping",
      tone: "border-default-300 bg-default-100",
      icon: <AiOutlineSwap className="text-default-600" />,
      title: t(mdk("switch.play.mapping")),
      path: `${SOURCE_ROOT} → ${LOCAL_ROOT}`,
    },
    {
      id: "player",
      tone: `${home.border} ${home.tint}`,
      icon: <AiOutlinePlayCircle className={home.text} />,
      title: t(mdk("switch.play.player")),
      path: [LOCAL_ROOT, ...FILE].join("\\"),
    },
  ];

  return (
    <figure className="flex flex-col gap-2" data-testid="multi-device-play-here">
      <div className="text-sm font-medium">{t(mdk("switch.play.title"))}</div>
      <ol className="flex flex-col gap-2 lg:flex-row lg:items-stretch">
        {steps.map((step, index) => (
          <li key={step.id} className="flex min-w-0 flex-1 flex-col gap-2 lg:flex-row">
            {index > 0 && (
              <span aria-hidden className="self-center text-lg text-default-400">
                <span className="lg:hidden">↓</span>
                <span className="hidden lg:inline">→</span>
              </span>
            )}
            <div
              className={`flex min-w-0 flex-1 flex-col gap-1.5 rounded-lg border p-3 ${step.tone}`}
            >
              <div className="flex items-center gap-1.5 text-sm font-medium text-foreground">
                <span aria-hidden className="shrink-0 text-base">
                  {step.icon}
                </span>
                <span className="min-w-0">{step.title}</span>
              </div>
              <code className="break-all rounded bg-content1 px-1.5 py-1 text-[11px] text-default-700">
                {step.path}
              </code>
            </div>
          </li>
        ))}
      </ol>
      <figcaption className="text-xs text-default-500">{t(mdk("switch.play.caption"))}</figcaption>
    </figure>
  );
};

PlayHereDiagram.displayName = "PlayHereDiagram";

export default PlayHereDiagram;
