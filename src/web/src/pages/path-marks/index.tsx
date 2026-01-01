"use client";

import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";

import { useCallback, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineWarning } from "react-icons/ai";

import PathTree from "./components/PathTree";
import PathConfigModal from "./components/PathConfigModal";
import type { ChildPathInfo } from "./components/TransferMarksModal";
import DeleteMarksConfirmationModal from "./components/DeleteMarksConfirmationModal";

import usePathMarks from "@/pages/path-mark-config/hooks/usePathMarks";
import PathMarkSettingsButton from "@/pages/path-mark-config/components/PathMarkSettingsButton";
import PendingSyncButton from "@/pages/path-mark-config/components/PendingSyncButton";
import type { PendingSyncButtonRef } from "@/pages/path-mark-config/components/PendingSyncButton";
import CopyMarksSidebar from "@/pages/path-mark-config/components/CopyMarksSidebar";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { Chip, toast, Modal, Spinner, Switch } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

const PathMarksPage = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [showOnlyInvalid, setShowOnlyInvalid] = useState(false);

  const { loading, checkingPaths, loadAllMarks, getGroupedMarksFiltered, getGroupedMarks, getInvalidPathsCount } = usePathMarks();
  const pendingSyncButtonRef = useRef<PendingSyncButtonRef>(null);


  const allGroups = useMemo(() => getGroupedMarks(), [getGroupedMarks]);
  const groups = useMemo(() => getGroupedMarksFiltered(showOnlyInvalid), [getGroupedMarksFiltered, showOnlyInvalid]);
  const invalidPathsCount = useMemo(() => getInvalidPathsCount(), [getInvalidPathsCount]);

  // Refresh pending sync count
  const refreshPendingSyncCount = useCallback(() => {
    pendingSyncButtonRef.current?.refresh();
  }, []);

  const handleSaveMark = useCallback(
    async (
      path: string,
      newMark: BakabaseAbstractionsModelsDomainPathMark,
      oldMark?: BakabaseAbstractionsModelsDomainPathMark,
    ) => {
      try {
        const oldMarkId = oldMark?.id;

        if (oldMarkId) {
          await BApi.pathMark.updatePathMark(oldMarkId, {
            ...newMark,
            path,
          });
        } else {
          await BApi.pathMark.addPathMark({
            ...newMark,
            path,
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
    async (path: string, mark: BakabaseAbstractionsModelsDomainPathMark) => {
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
                {t("Path")}: {path}
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

  // Handle configure sub-path marks (open config modal for valid paths)
  const handleConfigureSubPathMarks = useCallback(
    (path: string) => {
      createPortal(PathConfigModal, {
        path,
        onMarksChanged: () => {
          loadAllMarks();
          refreshPendingSyncCount();
        },
      });
    },
    [createPortal, loadAllMarks, refreshPendingSyncCount],
  );

  // Handle delete all marks on a path
  const handleDeletePathMarks = useCallback(
    (path: string) => {
      // Find marks for this path
      const group = allGroups.find((g) => g.path === path);
      const marks = group?.marks || [];

      if (marks.length === 0) {
        toast.warning(t("No marks to delete"));
        return;
      }

      // Find child paths with marks
      const normalizedPath = path.replace(/\\/g, "/").toLowerCase();
      const childPaths: ChildPathInfo[] = allGroups
        .filter((g) => {
          if (g.path === path) return false; // Exclude the main path itself
          const normalizedChildPath = g.path.replace(/\\/g, "/").toLowerCase();
          // Check if it's a child path
          return normalizedChildPath.startsWith(normalizedPath + "/");
        })
        .map((g) => ({
          path: g.path,
          marks: g.marks,
        }));

      createPortal(DeleteMarksConfirmationModal, {
        path,
        marks,
        childPaths,
        onConfirm: async (includeChildPaths: boolean) => {
          try {
            // Delete main path marks
            for (const mark of marks) {
              if (mark.id) {
                await BApi.pathMark.softDeletePathMark(mark.id);
              }
            }

            // Delete child path marks if selected
            if (includeChildPaths && childPaths.length > 0) {
              for (const child of childPaths) {
                for (const mark of child.marks) {
                  if (mark.id) {
                    await BApi.pathMark.softDeletePathMark(mark.id);
                  }
                }
              }
            }

            const totalCount = marks.length + (includeChildPaths ? childPaths.reduce((sum, c) => sum + c.marks.length, 0) : 0);
            toast.success(t("Deleted {{count}} mark(s) successfully", { count: totalCount }));
            loadAllMarks();
            refreshPendingSyncCount();
          } catch (error) {
            console.error("Failed to delete marks", error);
            toast.danger(t("Failed to delete marks"));
          }
        },
      });
    },
    [createPortal, allGroups, t, loadAllMarks, refreshPendingSyncCount],
  );

  // Handle pasting marks to a path
  const handlePasteMarks = useCallback(
    async (path: string, marks: BakabaseAbstractionsModelsDomainPathMark[]) => {
      try {
        for (const mark of marks) {
          await BApi.pathMark.addPathMark({
            path,
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

  return (
    <div className="path-marks-page h-full flex flex-col">
      <div className="flex flex-col gap-4 p-4 flex-1 min-h-0">
        {/* Header */}
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <h2 className="text-xl font-semibold">{t("Path Marks")}</h2>
            <Chip color="primary" size="sm" variant="flat">
              {t("Beta")}
            </Chip>
          </div>

          {/* Actions */}
          <div className="flex items-center gap-2">
            {/* Pending sync button */}
            <PendingSyncButton ref={pendingSyncButtonRef} onSyncComplete={loadAllMarks} />

            {/* Settings button */}
            <PathMarkSettingsButton />
          </div>
        </div>

        {/* Description */}
        <div className="text-sm text-default-500">
          {t("View and manage all path marks across your media libraries. Click on a path to edit marks in the config page.")}
        </div>

        {/* Invalid paths warning */}
        {!checkingPaths && invalidPathsCount > 0 && (
          <div className="flex items-center gap-3 p-3 bg-warning-50 border border-warning-200 rounded-lg">
            <AiOutlineWarning className="text-warning text-xl flex-shrink-0" />
            <div className="flex-1">
              <span className="text-warning-700">
                {t("{{count}} path(s) no longer exist on the file system", { count: invalidPathsCount })}
              </span>
            </div>
            <div className="flex items-center gap-2">
              <span className="text-sm text-default-600">{t("Show only invalid")}</span>
              <Switch
                isSelected={showOnlyInvalid}
                size="sm"
                onValueChange={setShowOnlyInvalid}
              />
            </div>
          </div>
        )}

        {/* Content */}
        <div className="overflow-hidden flex-1 min-h-0 flex">
          {/* Tree container */}
          <div className="flex-1 min-w-0 overflow-auto border border-default-200 rounded-lg p-2">
            {loading ? (
              <div className="flex items-center justify-center h-full">
                <Spinner size="lg" />
              </div>
            ) : (
              <PathTree
                groups={groups}
                onConfigureSubPathMarks={handleConfigureSubPathMarks}
                onDeleteMark={handleDeleteMark}
                onDeletePathMarks={handleDeletePathMarks}
                onPasteMarks={handlePasteMarks}
                onSaveMark={handleSaveMark}
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

PathMarksPage.displayName = "PathMarksPage";

export default PathMarksPage;
