"use client";

import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";
import type { DestroyableProps } from "@/components/bakaui/types";

import { useState, useMemo, useCallback } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineWarning } from "react-icons/ai";

import PathMarkChip from "@/pages/path-mark-config/components/PathMarkChip";
import { Modal, Checkbox } from "@/components/bakaui";

// Child path info for delete
export interface ChildPathInfo {
  path: string;
  marks: BakabaseAbstractionsModelsDomainPathMark[];
}

interface DeleteMarksConfirmationModalProps extends DestroyableProps {
  path: string;
  marks: BakabaseAbstractionsModelsDomainPathMark[];
  /** Child paths that also have marks */
  childPaths?: ChildPathInfo[];
  onConfirm: (includeChildPaths: boolean) => void;
}

const DeleteMarksConfirmationModal = ({
  path,
  marks,
  childPaths = [],
  onDestroyed,
  onConfirm,
}: DeleteMarksConfirmationModalProps) => {
  const { t } = useTranslation();
  // Default unchecked for delete operation
  const [includeChildPaths, setIncludeChildPaths] = useState(false);

  const totalMarksCount = useMemo(() => {
    let count = marks.length;

    if (includeChildPaths) {
      count += childPaths.reduce((sum, child) => sum + child.marks.length, 0);
    }

    return count;
  }, [marks, childPaths, includeChildPaths]);

  const childMarksCount = useMemo(() => {
    return childPaths.reduce((sum, child) => sum + child.marks.length, 0);
  }, [childPaths]);

  const handleConfirm = useCallback(() => {
    onConfirm(includeChildPaths);
    onDestroyed?.();
  }, [onConfirm, includeChildPaths, onDestroyed]);

  return (
    <Modal
      defaultVisible
      classNames={{
        base: "max-w-2xl max-h-[85vh]",
        body: "min-w-0 overflow-y-auto",
      }}
      footer={{
        actions: ["cancel", "ok"],
        okProps: {
          children: t("common.action.delete"),
          color: "danger",
        },
      }}
      title={t("pathMarks.modal.confirmDeleteTitle")}
      onDestroyed={onDestroyed}
      onOk={handleConfirm}
    >
      <div className="flex min-w-0 flex-col gap-4">
        {/* Warning */}
        <div className="flex items-start gap-3 p-3 bg-danger-50 border border-danger-200 rounded-lg">
          <AiOutlineWarning className="text-danger text-xl flex-shrink-0 mt-0.5" />
          <div className="flex-1">
            <p className="text-danger-700 font-medium">
              {t("pathMarks.status.aboutToDeleteMarks", { count: totalMarksCount })}
            </p>
            <p className="text-danger-600 text-sm mt-1">{t("pathMarks.warning.cannotBeUndone")}</p>
          </div>
        </div>

        {/* Main path */}
        <div className="min-w-0 border border-default-200 rounded-lg p-3">
          <div className="font-medium text-sm mb-2">{t("pathMarks.label.mainPath")}</div>
          <div className="flex min-w-0 flex-col gap-2 text-sm">
            <div className="font-mono text-xs leading-relaxed [overflow-wrap:anywhere] text-danger">
              {path}
            </div>
            <div className="flex min-w-0 max-w-full flex-wrap items-start gap-1">
              {marks.map((mark) => (
                <PathMarkChip key={mark.id} wrapContent mark={mark} />
              ))}
            </div>
          </div>
        </div>

        {/* Child paths */}
        {childPaths.length > 0 && (
          <div className="min-w-0 border border-warning-200 rounded-lg p-3 bg-warning-50/50">
            <div className="flex items-start gap-2 mb-2">
              <Checkbox isSelected={includeChildPaths} onValueChange={setIncludeChildPaths}>
                <span className="text-sm font-medium">
                  {t("pathMarks.checkbox.alsoDeleteChildMarks", {
                    count: childMarksCount,
                    pathCount: childPaths.length,
                  })}
                </span>
              </Checkbox>
            </div>

            {includeChildPaths && (
              <div className="flex min-w-0 flex-col gap-2 max-h-48 overflow-y-auto ml-6">
                {childPaths.map((child) => (
                  <div
                    key={child.path}
                    className="flex min-w-0 flex-col gap-2 text-sm bg-default-100/50 p-2 rounded"
                  >
                    <div className="font-mono text-xs leading-relaxed [overflow-wrap:anywhere] text-danger">
                      {child.path}
                    </div>
                    <div className="flex min-w-0 max-w-full flex-wrap items-start gap-1">
                      {child.marks.map((mark) => (
                        <PathMarkChip key={mark.id} wrapContent mark={mark} />
                      ))}
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        )}
      </div>
    </Modal>
  );
};

DeleteMarksConfirmationModal.displayName = "DeleteMarksConfirmationModal";

export default DeleteMarksConfirmationModal;
