"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { components } from "@/sdk/BApi2";

import React, { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";

import DiffHighlight from "@/components/FileNameModifier/DiffHighlight";
import BApi from "@/sdk/BApi";
import { Chip, Modal, Spinner, Switch } from "@/components/bakaui";
import { FileRenameStatus } from "@/sdk/constants";

type EntryVm = components["schemas"]["Bakabase.Service.Models.View.FileRenameEntryViewModel"];

interface Props extends DestroyableProps {
  runId: number;
  /**
   * The one component, two faces (owner decision, capability map §9·决定 2): the confirm
   * surface and the run-detail record render this same panel over the same endpoint. The
   * preview batch only ships the read-only face; apply/exclude arrive with the two-phase batch
   * behind readOnly=false.
   */
  readOnly?: boolean;
}

const StatusColor: Record<number, "default" | "primary" | "success" | "danger" | "warning"> = {
  [FileRenameStatus.Pending]: "primary",
  [FileRenameStatus.Conflict]: "danger",
  [FileRenameStatus.Excluded]: "default",
  [FileRenameStatus.Applied]: "success",
  [FileRenameStatus.Failed]: "danger",
  [FileRenameStatus.Undone]: "warning",
};

const RenamePlanPanel = ({ runId }: Props) => {
  const { t } = useTranslation();
  const [entries, setEntries] = useState<EntryVm[] | null>(null);
  const [conflictsOnly, setConflictsOnly] = useState(false);

  useEffect(() => {
    void BApi.workflow
      .getWorkflowRunFileRenameEntries(runId)
      .then((r) => setEntries((r.data ?? []) as EntryVm[]));
  }, [runId]);

  const counts = useMemo(() => {
    const byStatus = new Map<number, number>();

    for (const e of entries ?? []) {
      byStatus.set(e.status, (byStatus.get(e.status) ?? 0) + 1);
    }

    return byStatus;
  }, [entries]);

  const visible = useMemo(
    () =>
      (entries ?? []).filter((e) => !conflictsOnly || e.status === FileRenameStatus.Conflict),
    [entries, conflictsOnly],
  );

  const parentDir = (path: string, from: string) =>
    path.length > from.length ? path.slice(0, path.length - from.length) : "";

  return (
    <Modal
      defaultVisible
      footer={{ actions: ["cancel"] }}
      size="4xl"
      title={t<string>("workflow.renamePlan.title", { runId })}
    >
      {entries == null ? (
        <div className="flex justify-center py-10">
          <Spinner size="lg" />
        </div>
      ) : entries.length === 0 ? (
        <div className="text-center text-default-500 py-10">
          {t<string>("workflow.renamePlan.empty")}
        </div>
      ) : (
        <div className="flex flex-col gap-2">
          <div className="flex items-center gap-2 flex-wrap">
            {Array.from(counts.entries()).map(([status, count]) => (
              <Chip
                key={status}
                color={StatusColor[status] ?? "default"}
                radius="sm"
                size="sm"
                variant="flat"
              >
                {t<string>(`workflow.renamePlan.status.${FileRenameStatus[status]}`)} · {count}
              </Chip>
            ))}
            <div className="ml-auto flex items-center gap-1 text-xs">
              <Switch isSelected={conflictsOnly} size="sm" onValueChange={setConflictsOnly} />
              <span>{t<string>("workflow.renamePlan.conflictsOnly")}</span>
            </div>
          </div>

          <div className="flex flex-col divide-y divide-default-100 max-h-[60vh] overflow-auto">
            {visible.map((e) => (
              <div key={e.id} className="py-1.5 flex flex-col gap-0.5">
                <div className="flex items-center gap-2 min-w-0">
                  <span className="text-xs text-default-400 w-8 text-right shrink-0">
                    {e.seq}
                  </span>
                  <Chip
                    className="shrink-0"
                    color={StatusColor[e.status] ?? "default"}
                    radius="sm"
                    size="sm"
                    variant="flat"
                  >
                    {t<string>(`workflow.renamePlan.status.${FileRenameStatus[e.status]}`)}
                  </Chip>
                  <span className="text-sm break-all">
                    <DiffHighlight modified={e.to} original={e.from} />
                  </span>
                </div>
                <div className="pl-10 text-xs text-default-400 break-all">
                  {parentDir(e.path, e.from)}
                </div>
                {e.error && <div className="pl-10 text-xs text-danger break-words">{e.error}</div>}
              </div>
            ))}
          </div>
        </div>
      )}
    </Modal>
  );
};

RenamePlanPanel.displayName = "RenamePlanPanel";

export default RenamePlanPanel;
