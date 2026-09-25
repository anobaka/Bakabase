"use client";

import type { LaneState } from "./shared";
import type { ReactNode } from "react";

import { useTranslation } from "react-i18next";
import {
  AiOutlineArrowRight,
  AiOutlineCheckCircle,
  AiOutlineCloudServer,
  AiOutlineDelete,
  AiOutlineMergeCells,
  AiOutlinePauseCircle,
  AiOutlineRollback,
  AiOutlineSwap,
  AiOutlineUndo,
} from "react-icons/ai";

import { TopicCallout, TopicHeadline } from "../../../components/TopicBlocks";
import OpenPageButton, { MAP_ROUTE, useDevicePagesReach } from "../OpenPageButton";
import { mdk } from "../devices";

import ConflictDiagram from "./ConflictDiagram";
import HowItWorksDiagram from "./HowItWorksDiagram";
import { DevicePair, dsk } from "./shared";
import WhatSyncsDiagram from "./WhatSyncsDiagram";

import { Button } from "@/components/bakaui";
import { DATA_SYNC_ROUTE } from "@/features/data-sync/routes";
import { useCanAdministerShownServer } from "@/stores/remoteAccess";

/** The three ways to sync with a device, drawn from this device's side (it sits on the left). */
export const syncModes: {
  id: "follow" | "twoWay" | "copyOnce";
  toHere: LaneState;
  toThere: LaneState;
  mark?: string;
}[] = [
  { id: "follow", toHere: "active", toThere: "none" },
  { id: "twoWay", toHere: "active", toThere: "active" },
  // A sign, not prose: the same in every language.
  { id: "copyOnce", toHere: "active", toThere: "none", mark: "1×" },
];

const conflictLines = ["fields", "same", "hub", "types", "case"];
const deletionLines = ["options", "definitions", "edited", "filters"];
const safetyLines = ["noValues", "noMerge", "noRemote", "stop", "grant"];

const recoverCards = [
  { id: "undo", icon: <AiOutlineUndo /> },
  { id: "pause", icon: <AiOutlinePauseCircle /> },
  { id: "restore", icon: <AiOutlineRollback /> },
];

/** A heading and a list of short statements, each with the same mark. */
const Statements = ({
  titleKey,
  keys,
  icon,
  testId,
}: {
  titleKey: string;
  keys: string[];
  icon: ReactNode;
  testId: string;
}) => {
  const { t } = useTranslation();

  return (
    <section className="flex flex-col gap-2" data-testid={testId}>
      <h4 className="text-sm font-medium">{t(titleKey)}</h4>
      <ul className="flex flex-col gap-1.5">
        {keys.map((key) => (
          <li key={key} className="flex items-start gap-2 text-xs text-default-600">
            <span aria-hidden className="mt-0.5 shrink-0 text-default-400">
              {icon}
            </span>
            <span className="min-w-0">{t(key)}</span>
          </li>
        ))}
      </ul>
    </section>
  );
};

/**
 * The help center's 「数据同步 / Data sync」 section: what data sync keeps in step, how a link
 * works from first review to undo, what happens on conflicts and deletions, and where to
 * find it. The linking words and the Device map line follow whether this window has the
 * map (this device's own window, or the desktop app showing a device it manages, which can
 * switch back to it); a browser on another device has none.
 */
const DataSyncSection = ({ onNavigate }: { onNavigate?: (path: string) => void }) => {
  const { t } = useTranslation();
  const hasMap = useDevicePagesReach() !== "none";
  const canOpen = useCanAdministerShownServer() && !!onNavigate;

  return (
    <div className="flex flex-col gap-5" data-testid="data-sync-help">
      <TopicHeadline introKey={dsk("intro")} titleKey={dsk("headline")} />

      <HowItWorksDiagram hasMap={hasMap} />

      <section className="@container flex flex-col gap-2" data-testid="data-sync-modes">
        <h4 className="text-sm font-medium">{t(dsk("modes.title"))}</h4>
        <ul className="grid grid-cols-1 gap-2 @min-[40rem]:grid-cols-3">
          {syncModes.map((mode) => (
            <li
              key={mode.id}
              className="flex flex-col gap-2 rounded-lg border border-default-200 bg-default-50 p-3"
              data-mode={mode.id}
            >
              <DevicePair mark={mode.mark} toHere={mode.toHere} toThere={mode.toThere} />
              <div className="min-w-0">
                <div className="text-sm font-medium">{t(dsk(`modes.${mode.id}.title`))}</div>
                <p className="mt-0.5 text-xs text-default-600">{t(dsk(`modes.${mode.id}.desc`))}</p>
              </div>
            </li>
          ))}
        </ul>
        <p className="text-xs text-default-500">{t(dsk("modes.loops"))}</p>
      </section>

      <WhatSyncsDiagram />

      <div className="flex flex-col gap-3">
        <ConflictDiagram />
        <ul className="flex flex-col gap-1.5" data-testid="data-sync-conflicts">
          {conflictLines.map((id) => (
            <li key={id} className="flex items-start gap-2 text-xs text-default-600">
              <AiOutlineMergeCells aria-hidden className="mt-0.5 shrink-0 text-default-400" />
              <span className="min-w-0">{t(dsk(`conflicts.${id}`))}</span>
            </li>
          ))}
        </ul>
      </div>

      <Statements
        icon={<AiOutlineDelete />}
        keys={deletionLines.map((id) => dsk(`deletes.${id}`))}
        testId="data-sync-deletes"
        titleKey={dsk("deletes.title")}
      />

      <section className="@container flex flex-col gap-2" data-testid="data-sync-recover">
        <h4 className="text-sm font-medium">{t(dsk("recover.title"))}</h4>
        <div className="grid grid-cols-1 gap-2 @min-[48rem]:grid-cols-3">
          {recoverCards.map((card) => (
            <div
              key={card.id}
              className="flex items-start gap-2.5 rounded-lg border border-default-200 bg-default-50 p-3"
              data-recover={card.id}
            >
              <span
                aria-hidden
                className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-lg text-primary"
              >
                {card.icon}
              </span>
              <div className="min-w-0">
                <div className="text-sm font-medium">{t(dsk(`${card.id}.title`))}</div>
                <p className="mt-0.5 text-xs text-default-600">{t(dsk(`${card.id}.desc`))}</p>
              </div>
            </div>
          ))}
        </div>
      </section>

      <Statements
        icon={<AiOutlineCheckCircle />}
        keys={safetyLines.map((id) => dsk(`safety.${id}`))}
        testId="data-sync-safety"
        titleKey={dsk("safety.title")}
      />

      <section className="flex flex-col gap-2" data-testid="data-sync-where">
        <h4 className="text-sm font-medium">{t(dsk("where.title"))}</h4>
        {hasMap && (
          <div className="flex flex-col items-start gap-1.5" data-where="map">
            <p className="text-xs text-default-600">{t(dsk("where.map"))}</p>
            <OpenPageButton labelKey={mdk("open.map")} route={MAP_ROUTE} onNavigate={onNavigate} />
          </div>
        )}
        <div className="flex flex-col items-start gap-1.5" data-where="page">
          <p className="text-xs text-default-600">{t(dsk("where.page"))}</p>
          {canOpen && (
            <Button
              color="primary"
              endContent={<AiOutlineArrowRight className="text-sm" />}
              size="sm"
              variant="flat"
              onPress={() => onNavigate?.(DATA_SYNC_ROUTE)}
            >
              {t(dsk("open"))}
            </Button>
          )}
        </div>
        {/* Not a TopicCallout: the environment variable is one long word, which has to
            break for the note to fit a narrow dialog. */}
        <div
          className="flex items-start gap-2 rounded-lg border border-default-200 bg-default-100 p-3 text-xs text-default-600"
          data-where="nas"
        >
          <AiOutlineCloudServer aria-hidden className="mt-0.5 shrink-0" />
          <span className="min-w-0 break-words">{t(dsk("where.nas"))}</span>
        </div>
      </section>

      <TopicCallout icon={<AiOutlineSwap />} textKey={dsk("vsOthers")} tone="primary" />
    </div>
  );
};

DataSyncSection.displayName = "DataSyncSection";

export default DataSyncSection;
