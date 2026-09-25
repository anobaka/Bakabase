"use client";

import type { DeviceId } from "../devices";
import type { ReactNode } from "react";

import { useId } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineCheck, AiOutlineCheckCircle } from "react-icons/ai";

import { deviceNameKey, deviceStyle } from "../devices";

import { DeviceMark, HERE, LaneArrow, THERE, dsk } from "./shared";

export const conflictSteps = ["rename", "ask", "decide"] as const;

type ConflictStep = (typeof conflictSteps)[number];

/** One device's side of a step: its name and colour on top, what it shows below. */
const DevicePanel = ({ id, children }: { id: DeviceId; children: ReactNode }) => {
  const { t } = useTranslation();
  const style = deviceStyle(id);

  return (
    <div
      className={`flex min-w-0 flex-col gap-1.5 rounded-md border ${style.border} bg-content1 p-2`}
      data-device={id}
    >
      <div className="flex min-w-0 items-center gap-1.5 text-[11px] font-medium text-default-600">
        <DeviceMark className="h-3.5 w-4" id={id} />
        <span className="truncate">{t(deviceNameKey(id))}</span>
      </div>
      {children}
    </div>
  );
};

/** A property name the way the pictures show one. */
const Name = ({ children, muted }: { children: ReactNode; muted?: boolean }) => (
  <span
    className={`break-words text-xs ${
      muted ? "text-default-400 line-through" : "font-semibold text-foreground"
    }`}
  >
    {children}
  </span>
);

/** The "Needs you" card both devices show, with the two names to choose from. */
const AskCard = () => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-1 rounded border border-warning/40 bg-warning/10 p-1.5">
      <span className="text-[11px] text-warning-700 dark:text-warning">
        {t(dsk("conflicts.diagram.card"))}
      </span>
      <span className="flex flex-wrap gap-1">
        {[dsk("conflicts.diagram.sample.here"), dsk("conflicts.diagram.sample.there")].map(
          (key) => (
            <span
              key={key}
              className="rounded-full border border-default-300 bg-content1 px-2 text-[11px] text-default-700"
            >
              {t(key)}
            </span>
          ),
        )}
      </span>
    </div>
  );
};

/**
 * A rename conflict in three steps: both devices rename the same property, both ask, and a
 * decision on the laptop reaches this device, whose question closes by itself. The one
 * arrow carries the decision, so it points to the device that receives it.
 */
const ConflictDiagram = () => {
  const { t } = useTranslation();
  const titleId = `${useId()}-conflict`;
  const base = t(dsk("conflicts.diagram.sample.base"));
  const here = t(dsk("conflicts.diagram.sample.here"));
  const there = t(dsk("conflicts.diagram.sample.there"));

  const panels: Record<ConflictStep, Record<"here" | "there", ReactNode>> = {
    rename: {
      here: (
        <span className="flex flex-wrap items-center gap-1">
          <Name muted>{base}</Name>
          <span aria-hidden className="text-default-400">
            →
          </span>
          <Name>{here}</Name>
        </span>
      ),
      there: (
        <span className="flex flex-wrap items-center gap-1">
          <Name muted>{base}</Name>
          <span aria-hidden className="text-default-400">
            →
          </span>
          <Name>{there}</Name>
        </span>
      ),
    },
    ask: { here: <AskCard />, there: <AskCard /> },
    decide: {
      here: (
        <>
          <Name>{there}</Name>
          <span
            data-closed
            className="flex items-center gap-1 rounded border border-default-200 bg-default-100 px-1.5 py-1 text-[11px] text-default-500"
          >
            <AiOutlineCheckCircle aria-hidden className="shrink-0" />
            {t(dsk("conflicts.diagram.resolvedOn"), { name: t(deviceNameKey(THERE)) })}
          </span>
        </>
      ),
      there: (
        <>
          <Name>{there}</Name>
          <span className="flex items-center gap-1 text-[11px] text-default-700">
            <AiOutlineCheck aria-hidden className="shrink-0" />
            {t(dsk("conflicts.diagram.decidedHere"))}
          </span>
        </>
      ),
    },
  };

  return (
    <figure
      aria-labelledby={titleId}
      className="@container flex flex-col gap-2"
      data-testid="data-sync-conflict"
    >
      <h4 className="text-sm font-medium" id={titleId}>
        {t(dsk("conflicts.title"))}
      </h4>
      <ol className="grid grid-cols-1 gap-2 @min-[46rem]:grid-cols-3">
        {conflictSteps.map((step, index) => (
          <li
            key={step}
            className="@container flex min-w-0 flex-col gap-2 rounded-lg border border-default-200 bg-default-50 p-2.5"
            data-step={step}
          >
            <div className="flex items-center gap-2 text-xs font-medium">
              <span className="flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-primary/10 font-semibold text-primary">
                {index + 1}
              </span>
              {t(dsk(`conflicts.diagram.step.${step}`))}
            </div>
            <div className="grid grid-cols-1 items-center gap-1.5 @min-[15rem]:grid-cols-[minmax(0,1fr)_auto_minmax(0,1fr)]">
              <DevicePanel id={HERE}>{panels[step].here}</DevicePanel>
              <div className="flex justify-center">
                {step === "decide" ? (
                  <>
                    <LaneArrow
                      className="@min-[15rem]:hidden"
                      direction="up"
                      from={THERE}
                      to={HERE}
                    />
                    <LaneArrow
                      className="hidden @min-[15rem]:block"
                      direction="left"
                      from={THERE}
                      to={HERE}
                    />
                  </>
                ) : (
                  <span aria-hidden className="hidden w-2 @min-[15rem]:block" />
                )}
              </div>
              <DevicePanel id={THERE}>{panels[step].there}</DevicePanel>
            </div>
          </li>
        ))}
      </ol>
    </figure>
  );
};

ConflictDiagram.displayName = "ConflictDiagram";

export default ConflictDiagram;
