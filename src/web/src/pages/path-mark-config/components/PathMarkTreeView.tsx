"use client";

import type { Entry } from "@/core/models/FileExplorer/Entry";
import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";

import { useCallback } from "react";
import { useTranslation } from "react-i18next";
import { MenuItem } from "@szhsin/react-menu";
import { AiOutlineAim, AiOutlineCopy } from "react-icons/ai";

import PathMarks from "./PathMarks";
import MarkConfigModal from "./MarkConfigModal";
import PasteMarksButton from "./PasteMarksButton";
import CopyMarksSidebar from "./CopyMarksSidebar";
import usePathMarks from "../hooks/usePathMarks";

import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { toast, Modal } from "@/components/bakaui";
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

        toast.success(t(oldMark ? "Mark updated successfully" : "Mark added successfully"));
        notifyMarksChanged();
      } catch (error) {
        console.error("Failed to save mark", error);
        toast.danger(t("Failed to save mark"));
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

      const confirmed = await new Promise<boolean>((resolve) => {
        const modal = createPortal(Modal, {
          defaultVisible: true,
          title: t("Confirm Delete Mark"),
          children: (
            <div>
              <p>{t("Are you sure you want to delete this mark?")}</p>
              <p className="text-sm text-default-500 mt-2">
                {t("Path")}: {entry.path}
              </p>
              <p className="text-sm text-default-500">
                {t("Priority")}: {mark.priority}
              </p>
            </div>
          ),
          footer: {
            actions: ["cancel", "ok"],
            okProps: {
              color: "danger",
              children: t("Delete"),
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
          await BApi.pathMark.softDeletePathMark(markId);
          toast.success(t("Mark deleted successfully"));
          notifyMarksChanged();
        } catch (error) {
          console.error("Failed to delete mark", error);
          toast.danger(t("Failed to delete mark"));
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

        toast.success(t("Mark added successfully to {{count}} paths", { count: entries.length }));
        notifyMarksChanged();
      } catch (error) {
        console.error("Failed to add marks", error);
        toast.danger(t("Failed to add marks"));
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

        toast.success(t("Pasted {{count}} marks successfully", { count: marks.length }));
        notifyMarksChanged();
      } catch (error) {
        console.error("Failed to paste marks", error);
        toast.danger(t("Failed to paste marks"));
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
            <div className="flex items-center gap-2">
              <AiOutlineAim className="text-base text-success" />
              {t("Add Resource Mark ({{count}} paths)", { count: entries.length })}
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
            <div className="flex items-center gap-2">
              <AiOutlineAim className="text-base text-primary" />
              {t("Add Property Mark ({{count}} paths)", { count: entries.length })}
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
            <div className="flex items-center gap-2">
              <AiOutlineAim className="text-base text-warning" />
              {t("Add MediaLibrary Mark ({{count}} paths)", { count: entries.length })}
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
                {t("Copy marks from this path")}
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
