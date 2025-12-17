"use client";

import type { Entry } from "@/core/models/FileExplorer/Entry";
import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";
import type { DestroyableProps } from "@/components/bakaui/types";

import { useCallback, useEffect, useRef } from "react";
import { useTranslation } from "react-i18next";
import { MenuItem } from "@szhsin/react-menu";
import { AiOutlineAim } from "react-icons/ai";

import PathMarks from "@/pages/path-mark-config/components/PathMarks";
import MarkConfigModal from "@/pages/path-mark-config/components/MarkConfigModal";
import usePathMarks from "@/pages/path-mark-config/hooks/usePathMarks";

import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { Modal, toast } from "@/components/bakaui";
import RootTreeEntry from "@/pages/file-processor/RootTreeEntry";
import BApi from "@/sdk/BApi";
import { PathMarkType } from "@/sdk/constants";

interface PathConfigModalProps extends DestroyableProps {
  path: string;
  onMarksChanged?: () => void;
}

const PathConfigModal = ({
  path,
  onDestroyed,
  onMarksChanged,
}: PathConfigModalProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const { loadAllMarks, getMarksForPath } = usePathMarks();
  const onMarksChangedRef = useRef(onMarksChanged);

  useEffect(() => {
    onMarksChangedRef.current = onMarksChanged;
  }, [onMarksChanged]);

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
        loadAllMarks();
        onMarksChangedRef.current?.();
      } catch (error) {
        console.error("Failed to save mark", error);
        toast.danger(t("Failed to save mark"));
      }
    },
    [t, loadAllMarks],
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
          loadAllMarks();
          onMarksChangedRef.current?.();
        } catch (error) {
          console.error("Failed to delete mark", error);
          toast.danger(t("Failed to delete mark"));
        }
      }
    },
    [createPortal, t, loadAllMarks],
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
          onTaskComplete={() => {
            loadAllMarks();
            onMarksChangedRef.current?.();
          }}
        />
      );
    },
    [getMarksForPath, handleSaveMark, handleDeleteMark, loadAllMarks],
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
        loadAllMarks();
        onMarksChangedRef.current?.();
      } catch (error) {
        console.error("Failed to add marks", error);
        toast.danger(t("Failed to add marks"));
      }
    },
    [t, loadAllMarks],
  );

  // Render extra context menu items for adding marks
  const renderExtraContextMenuItems = useCallback(
    (entries: Entry[]) => {
      if (entries.length === 0) return null;

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
        </>
      );
    },
    [createPortal, t, handleAddMarksFromContextMenu],
  );

  return (
    <Modal
      classNames={{
        base: "max-w-4xl w-[90vw] h-[80vh]",
        body: "p-0 overflow-hidden",
      }}
      defaultVisible
      footer={false}
      title={t("Configure Path Marks")}
      onDestroyed={onDestroyed}
    >
      <div className="flex flex-col h-full p-4">
        <div className="text-sm text-default-500 mb-4">
          {t("Click on folders to navigate. Use the Add Mark button or right-click for mark options.")}
        </div>
        <div className="flex-1 min-h-0 overflow-hidden border border-default-200 rounded-lg">
          <RootTreeEntry
            expandable
            capabilities={["select", "multi-select", "range-select"]}
            renderAfterName={renderAfterName}
            renderExtraContextMenuItems={renderExtraContextMenuItems}
            rootPath={path}
            selectable="multiple"
          />
        </div>
      </div>
    </Modal>
  );
};

PathConfigModal.displayName = "PathConfigModal";

export default PathConfigModal;
