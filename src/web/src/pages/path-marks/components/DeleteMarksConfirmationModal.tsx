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
      classNames={{
        base: "max-w-2xl max-h-[85vh]",
        body: "overflow-y-auto",
      }}
      defaultVisible
      footer={{
        actions: ["cancel", "ok"],
        okProps: {
          children: t("Delete"),
          color: "danger",
        },
      }}
      title={t("Confirm Delete Marks")}
      onDestroyed={onDestroyed}
      onOk={handleConfirm}
    >
      <div className="flex flex-col gap-4">
        {/* Warning */}
        <div className="flex items-start gap-3 p-3 bg-danger-50 border border-danger-200 rounded-lg">
          <AiOutlineWarning className="text-danger text-xl flex-shrink-0 mt-0.5" />
          <div className="flex-1">
            <p className="text-danger-700 font-medium">
              {t("You are about to delete {{count}} mark(s)", { count: totalMarksCount })}
            </p>
            <p className="text-danger-600 text-sm mt-1">
              {t("This action cannot be undone.")}
            </p>
          </div>
        </div>

        {/* Main path */}
        <div className="border border-default-200 rounded-lg p-3">
          <div className="font-medium text-sm mb-2">{t("Main Path")}</div>
          <div className="flex items-start gap-2 text-sm">
            <div className="flex-1 min-w-0">
              <div className="font-mono text-xs break-all text-danger">{path}</div>
            </div>
            <div className="flex flex-wrap gap-1 flex-shrink-0">
              {marks.map((mark) => (
                <PathMarkChip key={mark.id} mark={mark} />
              ))}
            </div>
          </div>
        </div>

        {/* Child paths */}
        {childPaths.length > 0 && (
          <div className="border border-warning-200 rounded-lg p-3 bg-warning-50/50">
            <div className="flex items-start gap-2 mb-2">
              <Checkbox
                isSelected={includeChildPaths}
                onValueChange={setIncludeChildPaths}
              >
                <span className="text-sm font-medium">
                  {t("Also delete {{count}} mark(s) from {{pathCount}} child path(s)", {
                    count: childMarksCount,
                    pathCount: childPaths.length
                  })}
                </span>
              </Checkbox>
            </div>

            {includeChildPaths && (
              <div className="flex flex-col gap-2 max-h-48 overflow-y-auto ml-6">
                {childPaths.map((child) => (
                  <div key={child.path} className="flex items-start gap-2 text-sm bg-white/50 p-2 rounded">
                    <div className="flex-1 min-w-0">
                      <div className="font-mono text-xs truncate text-danger" title={child.path}>
                        {child.path}
                      </div>
                    </div>
                    <div className="flex flex-wrap gap-1 flex-shrink-0">
                      {child.marks.map((mark) => (
                        <PathMarkChip key={mark.id} mark={mark} />
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
