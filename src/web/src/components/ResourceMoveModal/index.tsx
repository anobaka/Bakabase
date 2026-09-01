"use client";

import type { Entry } from "@/core/models/FileExplorer/Entry";
import type { FileExplorerRef } from "@/components/FileExplorer";
import type { DestroyableProps } from "@/components/bakaui/types";
import type { BakabaseAbstractionsModelsViewResourceMovePreviewViewModel } from "@/sdk/Api";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineArrowRight, AiOutlineWarning } from "react-icons/ai";

import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { Button, Checkbox, Chip, Modal, Spinner, Tooltip, toast } from "@/components/bakaui";
import { FileExplorer } from "@/components/FileExplorer";
import { standardizePath } from "@/components/utils";
import { PathMarkType } from "@/sdk/constants";
import BApi from "@/sdk/BApi";

/** Segment-anchored containment: /a contains /a/b but never /abc. */
const isPathEqualOrUnder = (candidate?: string, root?: string) => {
  const c = standardizePath(candidate)?.toLowerCase();
  const r = standardizePath(root)?.toLowerCase();

  if (!c || !r) return false;
  if (c === r) return true;

  return c.startsWith(r.endsWith("/") ? r : `${r}/`);
};

/** Per-browser opt-out of the confirmation step; localStorage may be unavailable. */
const SKIP_CONFIRM_KEY = "bakabase:resourceMove:skipConfirm";
const shouldSkipConfirm = () => {
  try {
    return localStorage.getItem(SKIP_CONFIRM_KEY) === "1";
  } catch {
    return false;
  }
};
const rememberSkipConfirm = () => {
  try {
    localStorage.setItem(SKIP_CONFIRM_KEY, "1");
  } catch {
    // Ignored: the user simply gets asked again next time.
  }
};

type MovableResource = {
  id: number;
  path?: string | null;
};

type Props = {
  resources: MovableResource[];
  /** Called after the move task is created (files move in the background). */
  onMoved?: () => any;
} & DestroyableProps;

const ResourceMoveModal = ({ resources, onMoved, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [visible, setVisible] = useState(true);
  const [markRoots, setMarkRoots] = useState<string[]>();
  const [selectedDest, setSelectedDest] = useState<string>();
  const [previewing, setPreviewing] = useState(false);

  // The preview call can outlive the modal; a confirm dialog must not pop up over a page the
  // user has already dismissed this flow from.
  const visibleRef = useRef(true);
  const close = useCallback(() => {
    visibleRef.current = false;
    setVisible(false);
  }, []);

  useEffect(
    () => () => {
      visibleRef.current = false;
    },
    [],
  );

  useEffect(() => {
    BApi.pathMark.getAllPathMarkPaths().then((r) => {
      // Only directories can host a moved resource; a mark on a file still pins down its
      // directory as a browsable starting point.
      const roots = [...new Set((r.data ?? []).map((p) => standardizePath(p)!))];

      setMarkRoots(roots);
    });
  }, []);

  const resourceIds = useMemo(() => resources.map((r) => r.id), [resources]);
  const sourcePaths = useMemo(
    () =>
      resources.map((r) => standardizePath(r.path ?? undefined)).filter((p): p is string => !!p),
    [resources],
  );

  const validateDest = useCallback(
    (path: string): boolean => {
      if (sourcePaths.some((sp) => isPathEqualOrUnder(path, sp))) {
        toast.warning(t<string>("resourceMove.warning.destInsideSource"));

        return false;
      }

      // The backend rejects a whole batch when any resource would "move" onto its own path.
      if (
        sourcePaths.some(
          (sp) => sp.slice(0, sp.lastIndexOf("/")).toLowerCase() === path.toLowerCase(),
        )
      ) {
        toast.warning(t<string>("resourceMove.warning.alreadyAtDestination"));

        return false;
      }

      return true;
    },
    [sourcePaths, t],
  );

  const finishAfterTaskCreated = useCallback(() => {
    close();
    onMoved?.();
    onDestroyed?.();
  }, [close, onMoved, onDestroyed]);

  const openConfirmModal = useCallback(async () => {
    if (!selectedDest || previewing) return;
    setPreviewing(true);
    try {
      // The user opted out of the confirmation step — start the move right away.
      if (shouldSkipConfirm()) {
        const moveRsp = await BApi.resourceMove.moveResources({
          resourceIds,
          destDir: selectedDest,
        });

        if (moveRsp.code) {
          toast.danger(moveRsp.message ?? "Failed to create the move task");

          return;
        }
        toast.success(t<string>("resourceMove.status.taskCreated"));
        finishAfterTaskCreated();

        return;
      }

      const rsp = await BApi.resourceMove.previewResourceMove({
        resourceIds,
        destDir: selectedDest,
      });

      if (rsp.code || !rsp.data) {
        toast.danger(rsp.message ?? "Failed to preview");

        return;
      }

      if (!visibleRef.current) return;

      createPortal(ResourceMoveConfirmModal, {
        destDir: selectedDest,
        resourceIds,
        preview: rsp.data,
        onMoved: finishAfterTaskCreated,
      });
    } finally {
      setPreviewing(false);
    }
  }, [selectedDest, previewing, resourceIds, createPortal, finishAfterTaskCreated, t]);

  // A stable filter identity matters: FileExplorer invalidates every node's memoized
  // filtered children whenever the filter object changes.
  const explorerFilter = useMemo(() => ({ custom: (e: Entry) => e.isDirectoryOrDrive }), []);

  const explorerRef = useRef<FileExplorerRef | null>(null);

  // Selecting a row IS picking the destination (group headers included — they stand for
  // real directories). An invalid pick is rejected with a toast and deselected, which
  // re-enters here with an empty selection and clears the destination.
  const onExplorerSelected = useCallback(
    (entries: Entry[]) => {
      const entry = entries[0];

      if (!entry) {
        setSelectedDest(undefined);

        return;
      }

      const path = standardizePath(entry.path)!;

      if (!validateDest(path)) {
        explorerRef.current?.clearSelection();

        return;
      }

      setSelectedDest(path);
    },
    [validateDest],
  );

  return (
    <Modal
      classNames={{
        base: "max-w-5xl w-[92vw] h-[85vh]",
        body: "p-0 overflow-hidden",
      }}
      footer={false}
      title={
        resourceIds.length > 1
          ? t<string>("resourceMove.title.forCount", { count: resourceIds.length })
          : t<string>("resourceMove.title")
      }
      visible={visible}
      onClose={close}
      onDestroyed={onDestroyed}
    >
      <div className="flex flex-col h-full">
        {/* Path-mark roots as a browsable multi-root tree. Must be a flex column: the
            FileExplorer's own root uses `grow`, which is inert inside a block container and
            would collapse the tree area to the toolbar's height. */}
        <div className="flex-1 min-h-0 overflow-hidden flex flex-col">
          {markRoots == undefined ? (
            <div className="flex items-center justify-center h-full">
              <Spinner />
            </div>
          ) : markRoots.length === 0 ? (
            <div className="flex items-center justify-center h-full text-sm text-default-500">
              {t<string>("resourceMove.tip.noMarkRoots")}
            </div>
          ) : (
            <FileExplorer
              expandable
              ref={explorerRef}
              capabilities={["select"]}
              filter={explorerFilter}
              keyboard={false}
              rootPaths={markRoots}
              selectable="single"
              onSelected={onExplorerSelected}
            />
          )}
        </div>

        {/* Own footer: keep this modal open while the confirmation step runs. */}
        <div className="flex items-center justify-between gap-3 p-3 border-t border-default-200">
          <div className="min-w-0 flex-1 text-sm flex items-center">
            {selectedDest && (
              <>
                <span className="font-medium flex-shrink-0">
                  {t<string>("resourceMove.label.moveTo")}
                </span>
                <code className="text-success ml-1 truncate" title={selectedDest}>
                  {selectedDest}
                </code>
              </>
            )}
          </div>
          <div className="flex items-center gap-2 flex-shrink-0">
            <Button variant="light" onPress={close}>
              {t<string>("common.action.cancel")}
            </Button>
            <Button
              color="primary"
              isDisabled={!selectedDest || previewing}
              onPress={openConfirmModal}
            >
              {previewing ? (
                <Spinner size="sm" />
              ) : selectedDest ? (
                t<string>("resourceMove.action.moveFiles")
              ) : (
                t<string>("resourceMove.action.pickDestination")
              )}
            </Button>
          </div>
        </div>
      </div>
    </Modal>
  );
};

type ConfirmProps = {
  destDir: string;
  resourceIds: number[];
  preview: BakabaseAbstractionsModelsViewResourceMovePreviewViewModel;
  onMoved: () => any;
} & DestroyableProps;

const ResourceMoveConfirmModal = ({
  destDir,
  resourceIds,
  preview,
  onMoved,
  onDestroyed,
}: ConfirmProps) => {
  const { t } = useTranslation();
  const items = preview.items ?? [];
  const [dontRemindAgain, setDontRemindAgain] = useState(false);

  return (
    <Modal
      defaultVisible
      classNames={{
        base: "max-w-2xl max-h-[85vh]",
        body: "overflow-y-auto",
      }}
      footer={{
        actions: ["cancel", "ok"],
        okProps: {
          color: "danger",
          children: t<string>("resourceMove.action.moveFiles"),
        },
      }}
      title={t<string>("resourceMove.confirm.title")}
      onDestroyed={onDestroyed}
      onOk={async () => {
        const rsp = await BApi.resourceMove.moveResources({ resourceIds, destDir });

        if (rsp.code) {
          throw new Error(rsp.message ?? "Failed to create the move task");
        }
        if (dontRemindAgain) {
          rememberSkipConfirm();
        }
        toast.success(t<string>("resourceMove.status.taskCreated"));
        onMoved();
      }}
    >
      <div className="flex flex-col gap-3">
        <div className="flex items-start gap-2 p-3 bg-danger-50 border border-danger-200 rounded-lg text-sm">
          <AiOutlineWarning className="text-danger text-xl flex-shrink-0 mt-0.5" />
          <div>
            <p className="text-danger-700 font-medium">
              {t<string>("resourceMove.warning.irreversible")}
            </p>
            <p className="text-default-600 mt-0.5">
              {t<string>("resourceMove.tip.physicalMove")}
              &nbsp;
              {t<string>("resourceMove.tip.mayTakeLong")}
            </p>
            <p className="text-default-600 mt-0.5">
              {t<string>("resourceMove.warning.lockedDuringMove")}
            </p>
          </div>
        </div>

        <div className="text-sm font-medium">
          {t<string>("resourceMove.confirm.summary", { count: items.length })}
          <code className="text-success ml-1 break-all">{destDir}</code>
        </div>

        <div className="flex flex-col gap-2 max-h-[45vh] overflow-y-auto">
          {items.map((item) => (
            <div key={item.resourceId} className="border border-default-200 rounded-lg p-2 text-sm">
              <div
                className="text-danger line-through font-mono text-xs truncate"
                title={item.sourcePath}
              >
                {item.sourcePath}
              </div>
              <div className="flex items-center gap-1 mt-1 min-w-0">
                <AiOutlineArrowRight className="text-default-400 flex-shrink-0" />
                <div className="text-success font-mono text-xs truncate" title={item.destPath}>
                  {item.destPath}
                </div>
              </div>
              {(item.coveredResources ?? []).length > 0 && (
                <div className="mt-1.5 text-xs border-t border-default-100 pt-1.5">
                  <div className="text-warning-600">
                    {t<string>("resourceMove.confirm.coveredResources", {
                      count: item.coveredResources!.length,
                    })}
                  </div>
                  <div className="max-h-28 overflow-y-auto mt-0.5 flex flex-col gap-0.5">
                    {item.coveredResources!.map((c) => (
                      <div
                        key={c.resourceId}
                        className="font-mono text-default-600 truncate"
                        title={c.path}
                      >
                        {c.path.startsWith(`${item.sourcePath}/`)
                          ? c.path.slice(item.sourcePath!.length + 1)
                          : c.path}
                        {c.wasSelected && (
                          <span className="ml-1 font-sans text-default-400">
                            ({t<string>("resourceMove.confirm.coveredSelected")})
                          </span>
                        )}
                      </div>
                    ))}
                  </div>
                </div>
              )}
              {(item.destConflict || item.destInsideSource || (item.effects ?? []).length > 0) && (
                <div className="flex flex-wrap gap-1 mt-1.5">
                  {item.destConflict && (
                    <Chip color="danger" size="sm" variant="flat">
                      {t<string>("resourceMove.confirm.destConflict")}
                    </Chip>
                  )}
                  {item.destInsideSource && (
                    <Chip color="danger" size="sm" variant="flat">
                      {t<string>("resourceMove.warning.destInsideSource")}
                    </Chip>
                  )}
                  {(item.effects ?? []).map((effect) => {
                    const label =
                      effect.type === PathMarkType.MediaLibrary
                        ? effect.isDynamic
                          ? t<string>("resourceMove.confirm.effect.dynamicMediaLibrary")
                          : t<string>("resourceMove.confirm.effect.mediaLibrary", {
                              name: effect.mediaLibraryName ?? "?",
                            })
                        : t<string>("resourceMove.confirm.effect.property", {
                            name: effect.propertyName ?? "?",
                          }) + (effect.fixedValue ? ` = ${effect.fixedValue}` : "");

                    const chip = (
                      <Chip
                        key={effect.markId}
                        className={effect.willApply ? "" : "opacity-50"}
                        color={effect.type === PathMarkType.MediaLibrary ? "primary" : "secondary"}
                        size="sm"
                        variant="flat"
                      >
                        {label}
                      </Chip>
                    );

                    return effect.willApply ? (
                      chip
                    ) : (
                      <Tooltip
                        key={effect.markId}
                        content={t<string>("resourceMove.confirm.effect.mayNotApply")}
                      >
                        {chip}
                      </Tooltip>
                    );
                  })}
                </div>
              )}
            </div>
          ))}
        </div>

        <p className="text-xs text-default-500">{t<string>("resourceMove.confirm.effectsHint")}</p>

        <Checkbox isSelected={dontRemindAgain} size="sm" onValueChange={setDontRemindAgain}>
          {t<string>("resourceMove.confirm.dontRemindAgain")}
        </Checkbox>
      </div>
    </Modal>
  );
};

ResourceMoveModal.displayName = "ResourceMoveModal";

export default ResourceMoveModal;
