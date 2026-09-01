"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { components } from "@/sdk/BApi2";

import React, { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";

import DiffHighlight from "@/components/FileNameModifier/DiffHighlight";
import BApi from "@/sdk/BApi";
import { Button, Checkbox, Chip, Modal, Spinner, Switch, toast } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { FileRenameStatus } from "@/sdk/constants";

type EntryVm = components["schemas"]["Bakabase.Service.Models.View.FileRenameEntryViewModel"];

interface Props extends DestroyableProps {
  runId: number;
  /**
   * The one component, two faces (owner decision, capability map §9·决定 2): the confirm
   * surface and the run-detail record render this same panel over the same endpoint. The
   * read-only face drops the checkboxes and the apply/undo footer, nothing else.
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

const RenamePlanPanel = ({ runId, readOnly = false }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [entries, setEntries] = useState<EntryVm[] | null>(null);
  const [conflictsOnly, setConflictsOnly] = useState(false);
  const [busy, setBusy] = useState(false);

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

  const pendingCount = counts.get(FileRenameStatus.Pending) ?? 0;
  const appliedCount = counts.get(FileRenameStatus.Applied) ?? 0;

  const visible = useMemo(
    () =>
      (entries ?? []).filter((e) => !conflictsOnly || e.status === FileRenameStatus.Conflict),
    [entries, conflictsOnly],
  );

  const parentDir = (path: string, from: string) =>
    path.length > from.length ? path.slice(0, path.length - from.length) : "";

  const toggleExcluded = async (entry: EntryVm) => {
    setBusy(true);
    try {
      const rsp = await BApi.workflow.setFileRenameEntryExcluded(entry.id, {
        excluded: entry.status === FileRenameStatus.Pending,
      });

      if (rsp.code) throw new Error(rsp.message);
      const updated = rsp.data as EntryVm;

      setEntries((prev) => (prev ?? []).map((e) => (e.id === updated.id ? updated : e)));
    } catch (e: any) {
      toast.danger(e?.message ?? String(e));
    } finally {
      setBusy(false);
    }
  };

  const apply = () => {
    createPortal(Modal, {
      defaultVisible: true,
      title: t<string>("workflow.renamePlan.apply.confirmTitle", { count: pendingCount }),
      children: t<string>("workflow.renamePlan.apply.confirmContent"),
      onOk: async () => {
        setBusy(true);
        try {
          const rsp = await BApi.workflow.applyWorkflowRunFileRenames(runId);

          if (rsp.code) throw new Error(rsp.message);
          const rows = (rsp.data ?? []) as EntryVm[];

          setEntries(rows);
          const failed = rows.filter((e) => e.status === FileRenameStatus.Failed).length;
          const applied = rows.filter((e) => e.status === FileRenameStatus.Applied).length;

          if (failed > 0) {
            toast.warning(
              t<string>("workflow.renamePlan.apply.partial", { applied, failed }),
            );
          } else {
            toast.success(t<string>("workflow.renamePlan.apply.done", { applied }));
          }
        } catch (e: any) {
          toast.danger(e?.message ?? String(e));
        } finally {
          setBusy(false);
        }
      },
    });
  };

  const undo = () => {
    createPortal(Modal, {
      defaultVisible: true,
      title: t<string>("workflow.renamePlan.undo.confirmTitle", { count: appliedCount }),
      children: t<string>("workflow.renamePlan.undo.confirmContent"),
      onOk: async () => {
        setBusy(true);
        try {
          const rsp = await BApi.workflow.undoWorkflowRunFileRenames(runId);

          if (rsp.code) throw new Error(rsp.message);
          const rows = (rsp.data ?? []) as EntryVm[];

          setEntries(rows);
          // A row that resisted undo keeps Applied with the reason in its error field.
          const blocked = rows.filter(
            (e) => e.status === FileRenameStatus.Applied && e.error,
          ).length;
          const undone = rows.filter((e) => e.status === FileRenameStatus.Undone).length;

          if (blocked > 0) {
            toast.warning(t<string>("workflow.renamePlan.undo.partial", { undone, blocked }));
          } else {
            toast.success(t<string>("workflow.renamePlan.undo.done", { undone }));
          }
        } catch (e: any) {
          toast.danger(e?.message ?? String(e));
        } finally {
          setBusy(false);
        }
      },
    });
  };

  const footer = readOnly ? (
    { actions: ["cancel"] }
  ) : (
    <div className="flex items-center gap-2 w-full">
      {appliedCount > 0 && (
        <Button isDisabled={busy} size="sm" variant="flat" onPress={undo}>
          {t<string>("workflow.renamePlan.undo.button", { count: appliedCount })}
        </Button>
      )}
      <Button
        className="ml-auto"
        color="primary"
        isDisabled={busy || pendingCount === 0}
        isLoading={busy}
        size="sm"
        onPress={apply}
      >
        {t<string>("workflow.renamePlan.apply.button", { count: pendingCount })}
      </Button>
    </div>
  );

  return (
    <Modal
      defaultVisible
      footer={footer}
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
            {visible.map((e) => {
              const checkable =
                !readOnly &&
                (e.status === FileRenameStatus.Pending ||
                  e.status === FileRenameStatus.Excluded);

              return (
                <div key={e.id} className="py-1.5 flex flex-col gap-0.5">
                  <div className="flex items-center gap-2 min-w-0">
                    {!readOnly && (
                      <Checkbox
                        className="shrink-0"
                        isDisabled={!checkable || busy}
                        isSelected={
                          checkable
                            ? e.status === FileRenameStatus.Pending
                            : e.status === FileRenameStatus.Applied
                        }
                        size="sm"
                        onValueChange={() => toggleExcluded(e)}
                      />
                    )}
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
                  {e.error && (
                    <div className="pl-10 text-xs text-danger break-words">{e.error}</div>
                  )}
                </div>
              );
            })}
          </div>
        </div>
      )}
    </Modal>
  );
};

RenamePlanPanel.displayName = "RenamePlanPanel";

export default RenamePlanPanel;
