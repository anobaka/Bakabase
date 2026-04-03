"use client";

import type { Entry } from "@/core/models/FileExplorer/Entry";
import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";

import React, { useCallback } from "react";
import { useTranslation } from "react-i18next";
import { MenuItem } from "@szhsin/react-menu";
import { AiOutlineAim, AiOutlineCopy } from "react-icons/ai";

import PathMarks from "./PathMarks";
import MarkConfigModal from "./MarkConfigModal";
import PasteMarksButton from "./PasteMarksButton";
import CopyMarksSidebar from "./CopyMarksSidebar";
import usePathMarks from "../hooks/usePathMarks";

import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { toast, Modal, Checkbox } from "@/components/bakaui";
import { FileExplorer } from "@/components/FileExplorer";
import BApi from "@/sdk/BApi";
import { PathMarkType } from "@/sdk/constants";
import { useCopyMarksStore } from "@/stores/copyMarks";

interface PathMarkTreeViewProps {
  rootPath?: string;
  onMarksChanged?: () => void;
  onInitialized?: (path: string | undefined) => void;
}

const PathMarkTreeView = ({
  rootPath,
  onMarksChanged,
  onInitialized,
}: PathMarkTreeViewProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const { loadAllMarks, getMarksForPath } = usePathMarks();

  // Copy marks store
  const { enterCopyMode, selectAllMarks } = useCopyMarksStore();

  const notifyMarksChanged = useCallback(() => {
    loadAllMarks();
    onMarksChanged?.();
  }, [loadAllMarks, onMarksChanged]);

  const handleSaveMark = useCallback(
    async (
      entry: Entry,
      newMark: BakabaseAbstractionsModelsDomainPathMark,
      oldMark?: BakabaseAbstractionsModelsDomainPathMark,
    ) => {
      try {
        const oldMarkId = oldMark?.id;

        if (oldMarkId) {
          await BApi.pathMark.updatePathMark(oldMarkId, {
            ...newMark,
            path: entry.path,
          });
        } else {
          await BApi.pathMark.addPathMark({
            ...newMark,
            path: entry.path,
          });
        }

        toast.success(t(oldMark ? "pathMarkConfig.success.markUpdated" : "pathMarkConfig.success.markAdded"));
        notifyMarksChanged();
      } catch (error) {
        console.error("Failed to save mark", error);
        toast.danger(t("pathMarkConfig.error.saveMark"));
      }
    },
    [t, notifyMarksChanged],
  );

  const handleDeleteMark = useCallback(
    async (entry: Entry, mark: BakabaseAbstractionsModelsDomainPathMark) => {
      const markId = mark?.id;

      if (!markId) {
        console.error("Mark has no id, cannot delete");
        return;
      }

      let removeEffects = true;

      const confirmed = await new Promise<boolean>((resolve) => {
        const ModalContent = () => {
          const [checked, setChecked] = React.useState(true);
          return (
            <div>
              <p>{t("pathMarkConfig.confirm.deleteMarkQuestion")}</p>
              <p className="text-sm text-default-500 mt-2">
                {t("common.label.path")}: {entry.path}
              </p>
              <p className="text-sm text-default-500">
                {t("common.label.priority")}: {mark.priority}
              </p>
              <div className="mt-4">
                <Checkbox
                  isSelected={checked}
                  onValueChange={(v) => {
                    setChecked(v);
                    removeEffects = v;
                  }}
                >
                  {t("pathMarkConfig.confirm.removeEffects")}
                </Checkbox>
                <p className="text-xs text-default-400 mt-1 ml-7">
                  {checked
                    ? t("pathMarkConfig.confirm.removeEffects.checked")
                    : t("pathMarkConfig.confirm.removeEffects.unchecked")}
                </p>
              </div>
            </div>
          );
        };

        const modal = createPortal(Modal, {
          defaultVisible: true,
          title: t("pathMarkConfig.confirm.deleteMarkTitle"),
          children: <ModalContent />,
          footer: {
            actions: ["cancel", "ok"],
            okProps: {
              color: "danger",
              children: t("common.action.delete"),
            },
          },
          onOk: () => {
            resolve(true);
            modal.destroy();
          },
          onDestroyed: () => {
            resolve(false);
          },
        });
      });

      if (confirmed) {
        try {
          await BApi.pathMark.softDeletePathMark(markId, { removeEffects });
          toast.success(t("pathMarkConfig.success.markDeleted"));
          notifyMarksChanged();
        } catch (error) {
          console.error("Failed to delete mark", error);
          toast.danger(t("pathMarkConfig.error.deleteMark"));
        }
      }
    },
    [createPortal, t, notifyMarksChanged],
  );

  // Render function to display path marks after entry name
  const renderAfterName = useCallback(
    (entry: Entry) => {
      const marks = getMarksForPath(entry.path);

      return (
        <PathMarks
          entry={entry}
          marks={marks}
          onDeleteMark={handleDeleteMark}
          onSaveMark={handleSaveMark}
          onTaskComplete={notifyMarksChanged}
        />
      );
    },
    [getMarksForPath, handleSaveMark, handleDeleteMark, notifyMarksChanged],
  );

  // Handle adding marks from context menu
  const handleAddMarksFromContextMenu = useCallback(
    async (entries: Entry[], newMark: BakabaseAbstractionsModelsDomainPathMark) => {
      try {
        for (const entry of entries) {
          await BApi.pathMark.addPathMark({
            ...newMark,
            path: entry.path,
          });
        }

        toast.success(t("pathMarkConfig.success.markAddedToCount", { count: entries.length }));
        notifyMarksChanged();
      } catch (error) {
        console.error("Failed to add marks", error);
        toast.danger(t("pathMarkConfig.error.addMarks"));
      }
    },
    [t, notifyMarksChanged],
  );

  // Handle pasting marks from copied group
  const handlePasteMarks = useCallback(
    async (targetPath: string, marks: BakabaseAbstractionsModelsDomainPathMark[]) => {
      try {
        for (const mark of marks) {
          await BApi.pathMark.addPathMark({
            path: targetPath,
            type: mark.type,
            configJson: mark.configJson,
            priority: mark.priority,
            expiresInSeconds: mark.expiresInSeconds,
          } as BakabaseAbstractionsModelsDomainPathMark);
        }

        toast.success(t("pathMarkConfig.success.pastedCount", { count: marks.length }));
        notifyMarksChanged();
      } catch (error) {
        console.error("Failed to paste marks", error);
        toast.danger(t("pathMarkConfig.error.pasteMarks"));
      }
    },
    [t, notifyMarksChanged],
  );

  // Render paste button before right operations
  const renderBeforeRightOperations = useCallback(
    (entry: Entry) => {
      const marks = getMarksForPath(entry.path);

      return (
        <PasteMarksButton
          targetPath={entry.path}
          existingMarks={marks}
          onPaste={(marksToPaste) => handlePasteMarks(entry.path, marksToPaste)}
        />
      );
    },
    [getMarksForPath, handlePasteMarks],
  );

  // Handle entering copy mode from context menu
  const handleEnterCopyModeFromContextMenu = useCallback(
    (entry: Entry) => {
      const marks = getMarksForPath(entry.path);
      if (marks.length > 0) {
        enterCopyMode(entry.path);
        const markIds = marks.filter((m) => m.id !== undefined).map((m) => m.id!);
        selectAllMarks(markIds);
      }
    },
    [enterCopyMode, selectAllMarks, getMarksForPath],
  );

  // Render extra context menu items for adding marks
  const renderExtraContextMenuItems = useCallback(
    (entries: Entry[]) => {
      if (entries.length === 0) return null;

      // Check if single entry has marks (for copy option)
      const singleEntry = entries.length === 1 ? entries[0] : null;
      const singleEntryMarks = singleEntry ? getMarksForPath(singleEntry.path) : [];
      const canCopyMarks = singleEntry && singleEntryMarks.length > 0;

      return (
        <>
          <MenuItem
            onClick={() => {
              createPortal(MarkConfigModal, {
                markType: PathMarkType.Resource,
                rootPaths: entries.map((e) => e.path),
                onSave: async (mark) => {
                  handleAddMarksFromContextMenu(entries, mark);
                },
              });
            }}
          >
            <div className="flex items-center gap-2 text-success">
              <AiOutlineAim className="text-base" />
              {t("pathMarkConfig.action.addResourceMark", { count: entries.length })}
            </div>
          </MenuItem>
          <MenuItem
            onClick={() => {
              createPortal(MarkConfigModal, {
                markType: PathMarkType.Property,
                rootPaths: entries.map((e) => e.path),
                onSave: async (mark) => {
                  handleAddMarksFromContextMenu(entries, mark);
                },
              });
            }}
          >
            <div className="flex items-center gap-2 text-primary">
              <AiOutlineAim className="text-base" />
              {t("pathMarkConfig.action.addPropertyMark", { count: entries.length })}
            </div>
          </MenuItem>
          <MenuItem
            onClick={() => {
              createPortal(MarkConfigModal, {
                markType: PathMarkType.MediaLibrary,
                rootPaths: entries.map((e) => e.path),
                onSave: async (mark) => {
                  handleAddMarksFromContextMenu(entries, mark);
                },
              });
            }}
          >
            <div className="flex items-center gap-2 text-secondary">
              <AiOutlineAim className="text-base" />
              {t("pathMarkConfig.action.addMediaLibraryMark", { count: entries.length })}
            </div>
          </MenuItem>
          {canCopyMarks && (
            <MenuItem
              onClick={() => {
                handleEnterCopyModeFromContextMenu(singleEntry);
              }}
            >
              <div className="flex items-center gap-2">
                <AiOutlineCopy className="text-base text-default-500" />
                {t("pathMarkConfig.action.copyMarks")}
              </div>
            </MenuItem>
          )}
        </>
      );
    },
    [createPortal, t, handleAddMarksFromContextMenu, getMarksForPath, handleEnterCopyModeFromContextMenu],
  );

  return (
    <div className="flex h-full">
      {/* Tree container */}
      <div className="flex-1 min-w-0 overflow-hidden h-full flex flex-col">
        <FileExplorer
          expandable
          capabilities={["select", "multi-select", "range-select"]}
          renderAfterName={renderAfterName}
          renderBeforeRightOperations={renderBeforeRightOperations}
          renderExtraContextMenuItems={renderExtraContextMenuItems}
          rootPath={rootPath}
          selectable="multiple"
          onInitialized={onInitialized}
        />
      </div>

      {/* Copy Marks Sidebar */}
      <CopyMarksSidebar />
    </div>
  );
};

PathMarkTreeView.displayName = "PathMarkTreeView";

export default PathMarkTreeView;
