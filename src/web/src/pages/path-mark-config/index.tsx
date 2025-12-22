"use client";

import type { Entry } from "@/core/models/FileExplorer/Entry";
import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";

import { useEffect, useRef, useState, useCallback } from "react";
import { useTranslation } from "react-i18next";
import { useSearchParams } from "react-router-dom";
import { AiOutlineAim, AiOutlineCopy } from "react-icons/ai";
import { MenuItem } from "@szhsin/react-menu";

import PathMarks from "./components/PathMarks.tsx";
import MarkConfigModal from "./components/MarkConfigModal";
import PathMarkSettingsButton from "./components/PathMarkSettingsButton";
import PendingSyncButton from "./components/PendingSyncButton";
import type { PendingSyncButtonRef } from "./components/PendingSyncButton";
import usePathMarks from "./hooks/usePathMarks";
import CopyMarksSidebar from "./components/CopyMarksSidebar";
import PasteMarksButton from "./components/PasteMarksButton";

import { useFileSystemOptionsStore } from "@/stores/options";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { Chip, toast, Modal } from "@/components/bakaui";
import RootTreeEntry from "@/pages/file-processor/RootTreeEntry";
import BApi from "@/sdk/BApi";
import { PathMarkType } from "@/sdk/constants";
import { useCopyMarksStore } from "@/stores/copyMarks";

const PathRuleConfigPage = () => {
  const { t } = useTranslation();
  const [searchParams] = useSearchParams();

  const [selectedEntries, setSelectedEntries] = useState<Entry[]>([]);
  const { allMarks, loading: pathMarksLoading, loadAllMarks, getMarksForPath } = usePathMarks();

  const optionsStore = useFileSystemOptionsStore((state) => state);
  const [rootPath, setRootPath] = useState<string>();
  const [rootPathInitialized, setRootPathInitialized] = useState(false);

  const fpOptionsRef = useRef(optionsStore.data?.fileProcessor);
  const pendingSyncButtonRef = useRef<PendingSyncButtonRef>(null);

  const { createPortal } = useBakabaseContext();

  // Copy marks store
  const { enterCopyMode, selectAllMarks } = useCopyMarksStore();

  // Refresh pending sync count
  const refreshPendingSyncCount = useCallback(() => {
    pendingSyncButtonRef.current?.refresh();
  }, []);

  useEffect(() => {
    if (!rootPathInitialized && optionsStore.initialized) {
      // Check URL params first
      const pathFromUrl = searchParams.get("path");

      if (pathFromUrl) {
        setRootPath(decodeURIComponent(pathFromUrl));
      } else {
        const p = optionsStore.data.fileProcessor?.workingDirectory;

        if (p) {
          setRootPath(p);
        }
      }
      setRootPathInitialized(true);
    }
  }, [rootPathInitialized, optionsStore.initialized, searchParams]);

  useEffect(() => {
    fpOptionsRef.current = optionsStore.data?.fileProcessor;
  }, [optionsStore]);

  const handleSaveMark = useCallback(
    async (
      entry: Entry,
      newMark: BakabaseAbstractionsModelsDomainPathMark,
      oldMark?: BakabaseAbstractionsModelsDomainPathMark,
    ) => {
      try {
        const oldMarkId = oldMark?.id;

        if (oldMarkId) {
          // Update existing mark
          await BApi.pathMark.updatePathMark(oldMarkId, {
            ...newMark,
            path: entry.path,
          });
        } else {
          // Create new mark for this path
          await BApi.pathMark.addPathMark({
            ...newMark,
            path: entry.path,
          });
        }

        toast.success(t(oldMark ? "Mark updated successfully" : "Mark added successfully"));
        loadAllMarks();
        refreshPendingSyncCount();
      } catch (error) {
        console.error("Failed to save mark", error);
        toast.danger(t("Failed to save mark"));
      }
    },
    [t, loadAllMarks, refreshPendingSyncCount],
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
          // Soft delete the mark (will be synced later)
          await BApi.pathMark.softDeletePathMark(markId);

          toast.success(t("Mark deleted successfully"));
          loadAllMarks();
          refreshPendingSyncCount();
        } catch (error) {
          console.error("Failed to delete mark", error);
          toast.danger(t("Failed to delete mark"));
        }
      }
    },
    [createPortal, t, loadAllMarks, refreshPendingSyncCount],
  );

  // Render function to display path rule marks after entry name
  const renderAfterName = useCallback(
    (entry: Entry) => {
      const marks = getMarksForPath(entry.path);

      return (
        <PathMarks
          entry={entry}
          marks={marks}
          onDeleteMark={handleDeleteMark}
          onSaveMark={handleSaveMark}
          onTaskComplete={() => {
            loadAllMarks();
            refreshPendingSyncCount();
          }}
        />
      );
    },
    [getMarksForPath, handleSaveMark, handleDeleteMark, loadAllMarks, refreshPendingSyncCount],
  );

  // Handle adding marks from context menu (for multiple paths)
  const handleAddMarksFromContextMenu = useCallback(
    async (entries: Entry[], newMark: BakabaseAbstractionsModelsDomainPathMark) => {
      try {
        // Create marks for each selected entry
        for (const entry of entries) {
          await BApi.pathMark.addPathMark({
            ...newMark,
            path: entry.path,
          });
        }

        toast.success(t("Mark added successfully to {{count}} paths", { count: entries.length }));
        loadAllMarks();
        refreshPendingSyncCount();
      } catch (error) {
        console.error("Failed to add marks", error);
        toast.danger(t("Failed to add marks"));
      }
    },
    [t, loadAllMarks, refreshPendingSyncCount],
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
        loadAllMarks();
        refreshPendingSyncCount();
      } catch (error) {
        console.error("Failed to paste marks", error);
        toast.danger(t("Failed to paste marks"));
      }
    },
    [t, loadAllMarks, refreshPendingSyncCount],
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
    <div className="path-mark-config-page h-full flex flex-col">
      <div className="flex flex-col gap-4 p-4 flex-1 min-h-0">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <h2 className="text-xl font-semibold">{t("Configure Your Media Library")}</h2>
            <Chip color="primary" size="sm" variant="flat">
              {t("Beta")}
            </Chip>
          </div>

          {/* Sync options */}
          <div className="flex items-center gap-2">
            {/* Pending sync button */}
            <PendingSyncButton ref={pendingSyncButtonRef} onSyncComplete={loadAllMarks} />

            {/* Settings button */}
            <PathMarkSettingsButton />
          </div>
        </div>

        <div className="text-sm text-default-500">{t("PathRuleConfig.Description")}</div>

        <div className="overflow-hidden flex-1 min-h-0 flex">
          {/* Tree container - must have flex-1 and min-w-0 to allow sidebar to appear */}
          <div className="flex-1 min-w-0 overflow-hidden h-full flex flex-col">
            {rootPathInitialized && (
              <RootTreeEntry
                expandable
                capabilities={["select", "multi-select", "range-select"]}
                renderAfterName={renderAfterName}
                renderBeforeRightOperations={renderBeforeRightOperations}
                renderExtraContextMenuItems={renderExtraContextMenuItems}
                rootPath={rootPath}
                selectable="multiple"
                onInitialized={(v) => {
                  if (v != undefined) {
                    BApi.options.patchFileSystemOptions({
                      fileProcessor: {
                        ...(fpOptionsRef.current ?? { showOperationsAfterPlayingFirstFile: false }),
                        workingDirectory: v,
                      },
                    });
                  }
                }}
                onSelected={(entries) => {
                  setSelectedEntries(entries);
                }}
              />
            )}
          </div>

          {/* Copy Marks Sidebar */}
          <CopyMarksSidebar />
        </div>
      </div>
    </div>
  );
};

PathRuleConfigPage.displayName = "PathRuleConfigPage";

export default PathRuleConfigPage;
