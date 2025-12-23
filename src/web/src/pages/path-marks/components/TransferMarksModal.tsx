"use client";

import type { Entry } from "@/core/models/FileExplorer/Entry";
import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";
import type { DestroyableProps } from "@/components/bakaui/types";

import { useState, useCallback, useMemo } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineWarning, AiOutlineArrowRight, AiOutlineImport } from "react-icons/ai";

import PathMarkChip from "@/pages/path-mark-config/components/PathMarkChip";

import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { Modal, Button, toast, Spinner, Checkbox, Tooltip } from "@/components/bakaui";
import { FileExplorer } from "@/components/FileExplorer";
import BApi from "@/sdk/BApi";

// Child path info for transfer
export interface ChildPathInfo {
  path: string;
  marks: BakabaseAbstractionsModelsDomainPathMark[];
}

interface TransferMarksModalProps extends DestroyableProps {
  fromPath: string;
  marks: BakabaseAbstractionsModelsDomainPathMark[];
  /** Invalid child paths that also have marks */
  invalidChildPaths?: ChildPathInfo[];
  onTransferComplete?: () => void;
}

const TransferMarksModal = ({
  fromPath,
  marks,
  invalidChildPaths = [],
  onDestroyed,
  onTransferComplete,
}: TransferMarksModalProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [selectedDestination, setSelectedDestination] = useState<string | null>(null);
  const [transferring, setTransferring] = useState(false);

  const canTransfer = selectedDestination != null && selectedDestination !== fromPath;

  // Calculate new paths for child paths based on selected destination
  const childPathMappings = useMemo(() => {
    if (!selectedDestination || invalidChildPaths.length === 0) return [];

    const normalizedFromPath = fromPath.replace(/\\/g, "/");
    const normalizedDestPath = selectedDestination.replace(/\\/g, "/");

    return invalidChildPaths.map((child) => {
      const normalizedChildPath = child.path.replace(/\\/g, "/");
      const relativePath = normalizedChildPath.slice(normalizedFromPath.length);
      const newPath = normalizedDestPath + relativePath;
      return {
        ...child,
        newPath,
      };
    });
  }, [fromPath, selectedDestination, invalidChildPaths]);

  // Handle clicking "Transfer here" button
  const handleSelectDestination = useCallback((entry: Entry) => {
    if (entry.path === fromPath) {
      toast.warning(t("Cannot transfer to the same path"));
      return;
    }
    setSelectedDestination(entry.path);
  }, [fromPath, t]);

  // Handle confirm button - show confirmation dialog
  const handleConfirm = useCallback(() => {
    if (!canTransfer) return;

    // Show confirmation dialog
    const modal = createPortal(TransferConfirmationModal, {
      fromPath,
      toPath: selectedDestination!,
      marks,
      childPathMappings: invalidChildPaths.length > 0 ? childPathMappings : [],
      onConfirm: async (includeChildPaths: boolean) => {
        setTransferring(true);
        try {
          // Transfer main path marks
          for (const mark of marks) {
            const { id: _id, ...markWithoutId } = mark;
            await BApi.pathMark.addPathMark({
              ...markWithoutId,
              path: selectedDestination!,
            } as BakabaseAbstractionsModelsDomainPathMark);

            if (mark.id) {
              await BApi.pathMark.softDeletePathMark(mark.id);
            }
          }

          // Transfer child path marks if selected
          if (includeChildPaths && childPathMappings.length > 0) {
            for (const childMapping of childPathMappings) {
              for (const mark of childMapping.marks) {
                const { id: _id, ...markWithoutId } = mark;
                await BApi.pathMark.addPathMark({
                  ...markWithoutId,
                  path: childMapping.newPath,
                } as BakabaseAbstractionsModelsDomainPathMark);

                if (mark.id) {
                  await BApi.pathMark.softDeletePathMark(mark.id);
                }
              }
            }
          }

          const totalCount = marks.length + (includeChildPaths ? invalidChildPaths.reduce((sum, c) => sum + c.marks.length, 0) : 0);
          toast.success(t("Transferred {{count}} mark(s) successfully", { count: totalCount }));
          onTransferComplete?.();
          onDestroyed?.();
        } catch (error) {
          console.error("Failed to transfer marks", error);
          toast.danger(t("Failed to transfer marks"));
        } finally {
          setTransferring(false);
        }
      },
    });
  }, [canTransfer, createPortal, fromPath, selectedDestination, marks, invalidChildPaths, childPathMappings, t, onTransferComplete, onDestroyed]);

  // Render "Transfer here" button after entry name
  const renderAfterName = useCallback(
    (entry: Entry) => {
      const isSelected = entry.path === selectedDestination;
      const isSamePath = entry.path === fromPath;

      return (
        <div className="flex items-center gap-1 ml-2 opacity-0 group-hover:opacity-100 transition-opacity">
          {!isSamePath && (
            <Tooltip content={t("Transfer marks to this path")}>
              <Button
                className={`min-w-0 px-2 h-6 ${isSelected ? "bg-primary text-white" : ""}`}
                color={isSelected ? "primary" : "default"}
                size="sm"
                startContent={<AiOutlineImport className="text-sm" />}
                variant={isSelected ? "solid" : "flat"}
                onPress={() => handleSelectDestination(entry)}
              >
                {isSelected ? t("Selected") : t("Transfer here")}
              </Button>
            </Tooltip>
          )}
        </div>
      );
    },
    [selectedDestination, fromPath, t, handleSelectDestination],
  );

  return (
    <Modal
      classNames={{
        base: "max-w-4xl w-[90vw] h-[80vh]",
        body: "p-0 overflow-hidden",
      }}
      defaultVisible
      footer={{
        actions: ["cancel", "ok"],
        okProps: {
          isDisabled: !canTransfer || transferring,
          children: transferring ? <Spinner size="sm" /> : t("Transfer"),
          color: "primary",
        },
      }}
      title={t("Transfer Marks")}
      onDestroyed={onDestroyed}
      onOk={handleConfirm}
    >
      <div className="flex flex-col h-full">
        {/* Header info */}
        <div className="p-4 border-b border-default-200 bg-default-50">
          <div className="flex items-start gap-3">
            <AiOutlineWarning className="text-warning text-xl flex-shrink-0 mt-0.5" />
            <div className="flex-1">
              <p className="font-medium">
                {t("Transferring marks from:")}
              </p>
              <code className="text-sm text-danger line-through">{fromPath}</code>
              <div className="flex flex-wrap gap-1 mt-2">
                {marks.map((mark) => (
                  <PathMarkChip key={mark.id} mark={mark} />
                ))}
              </div>
              {invalidChildPaths.length > 0 && (
                <p className="text-sm text-warning-600 mt-2">
                  {t("+ {{count}} child path(s) with marks will also be available for transfer", { count: invalidChildPaths.length })}
                </p>
              )}
            </div>
          </div>

          {/* Selected destination preview */}
          {selectedDestination && (
            <div className="mt-3 pt-3 border-t border-default-200">
              <p className="text-sm font-medium">{t("Transfer to:")}</p>
              <code className="text-sm text-success">{selectedDestination}</code>
            </div>
          )}

          <p className="text-sm text-default-500 mt-3">
            {t("Browse and click \"Transfer here\" to select destination path")}
          </p>
        </div>

        {/* RootTreeEntry for browsing */}
        <div className="flex-1 min-h-0 overflow-hidden">
          <FileExplorer
            expandable
            capabilities={["select"]}
            renderAfterName={renderAfterName}
            selectable="disabled"
          />
        </div>
      </div>
    </Modal>
  );
};

// Confirmation dialog component
interface TransferConfirmationModalProps extends DestroyableProps {
  fromPath: string;
  toPath: string;
  marks: BakabaseAbstractionsModelsDomainPathMark[];
  childPathMappings: Array<ChildPathInfo & { newPath: string }>;
  onConfirm: (includeChildPaths: boolean) => void;
}

const TransferConfirmationModal = ({
  fromPath,
  toPath,
  marks,
  childPathMappings,
  onDestroyed,
  onConfirm,
}: TransferConfirmationModalProps) => {
  const { t } = useTranslation();
  const [includeChildPaths, setIncludeChildPaths] = useState(true);

  const totalMarksCount = useMemo(() => {
    let count = marks.length;
    if (includeChildPaths) {
      count += childPathMappings.reduce((sum, child) => sum + child.marks.length, 0);
    }
    return count;
  }, [marks, childPathMappings, includeChildPaths]);

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
          children: t("Confirm Transfer"),
          color: "primary",
        },
      }}
      title={t("Confirm Transfer")}
      onDestroyed={onDestroyed}
      onOk={handleConfirm}
    >
      <div className="flex flex-col gap-4">
        {/* Summary */}
        <div className="flex items-start gap-3 p-3 bg-primary-50 border border-primary-200 rounded-lg">
          <div className="flex-1">
            <p className="text-primary-700 font-medium">
              {t("You are about to transfer {{count}} mark(s)", { count: totalMarksCount })}
            </p>
          </div>
        </div>

        {/* Main path transfer */}
        <div className="border border-default-200 rounded-lg p-3">
          <div className="font-medium text-sm mb-2">{t("Main Path")}</div>
          <div className="flex items-start gap-2 text-sm">
            <div className="flex-1 min-w-0">
              <div className="text-danger line-through font-mono text-xs break-all">{fromPath}</div>
              <div className="flex items-center gap-1 mt-1">
                <AiOutlineArrowRight className="text-default-400 flex-shrink-0" />
                <div className="text-success font-mono text-xs break-all">{toPath}</div>
              </div>
            </div>
            <div className="flex flex-wrap gap-1 flex-shrink-0">
              {marks.map((mark) => (
                <PathMarkChip key={mark.id} mark={mark} />
              ))}
            </div>
          </div>
        </div>

        {/* Child paths */}
        {childPathMappings.length > 0 && (
          <div className="border border-warning-200 rounded-lg p-3 bg-warning-50/50">
            <div className="flex items-start gap-2 mb-2">
              <Checkbox
                isSelected={includeChildPaths}
                onValueChange={setIncludeChildPaths}
              >
                <span className="text-sm font-medium">
                  {t("Include {{count}} child path(s) with marks", { count: childPathMappings.length })}
                </span>
              </Checkbox>
            </div>

            {includeChildPaths && (
              <div className="flex flex-col gap-2 max-h-48 overflow-y-auto ml-6">
                {childPathMappings.map((child) => (
                  <div key={child.path} className="flex items-start gap-2 text-sm bg-white/50 p-2 rounded">
                    <div className="flex-1 min-w-0">
                      <div className="text-danger line-through font-mono text-xs truncate" title={child.path}>
                        {child.path}
                      </div>
                      <div className="flex items-center gap-1 mt-1">
                        <AiOutlineArrowRight className="text-default-400 flex-shrink-0" />
                        <div className="text-success font-mono text-xs truncate" title={child.newPath}>
                          {child.newPath}
                        </div>
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

TransferMarksModal.displayName = "TransferMarksModal";

export default TransferMarksModal;
