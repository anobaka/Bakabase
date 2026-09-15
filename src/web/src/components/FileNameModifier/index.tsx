"use client";

import type { DragEndEvent } from "@dnd-kit/core";
import type { OperationWithId } from "./useFileNameModifier";

import { useCallback, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";
import {
  AiOutlineEdit,
  AiOutlineFolderAdd,
  AiOutlinePartition,
  AiOutlinePlus,
  AiOutlineReload,
  AiOutlineUndo,
  AiOutlineCheckCircle,
} from "react-icons/ai";
import {
  DndContext,
  closestCenter,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors,
} from "@dnd-kit/core";
import {
  arrayMove,
  SortableContext,
  sortableKeyboardCoordinates,
  verticalListSortingStrategy,
} from "@dnd-kit/sortable";

import { Button, Textarea, Modal, Checkbox, Chip } from "../bakaui";

import SortableOperationCard from "./SortableOperationCard";
import PreviewList from "./PreviewList";
import { normalizeFilePaths, useFileNameModifier } from "./useFileNameModifier";
import { detectCommonPrefix } from "./utils";

import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { FileSystemSelectorModal } from "@/components/FileSystemSelector";
import { EDITOR_SEED_STORAGE_KEY } from "@/components/Workflow/CanvasEditor/templates";
import {
  FileNameModifierOperationType,
  FileNameModifierFileNameTarget,
  FileNameModifierPosition,
  FileNameModifierCaseType,
} from "@/sdk/constants";

export interface FileNameModificationResult {
  originalPath: string;
  modifiedPath: string;
  originalFileName: string;
  modifiedFileName: string;
  commonPrefix: string;
  originalRelative: string;
  modifiedRelative: string;
}

let operationIdCounter = 0;
const generateOperationId = () => `op-${Date.now()}-${operationIdCounter++}`;
const createDefaultOperation = (): OperationWithId => ({
  id: generateOperationId(),
  target: FileNameModifierFileNameTarget.FileNameWithoutExtension,
  operation: FileNameModifierOperationType.Insert,
  position: FileNameModifierPosition.Start,
  positionIndex: 0,
  targetText: "",
  text: "",
  deleteCount: 0,
  deleteStartPosition: 0,
  caseType: FileNameModifierCaseType.TitleCase,
  dateTimeFormat: "",
  alphabetStartChar: "A",
  alphabetCount: 1,
  replaceEntire: false,
  regex: false,
});

interface Props {
  initialFilePaths?: string[];
  onClose?: () => void;
}
const k = (key: string) => `fileNameModifier.${key}`;

const FileNameModifier = ({ initialFilePaths = [] }: Props) => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { createPortal } = useBakabaseContext();
  const {
    operations,
    setOperations,
    filePaths,
    setFilePaths,
    lastFilePaths,
    setLastFilePaths,
    previewResults,
    isPreviewLoading,
    hasInvalidOperations,
    validationErrors,
    changedCount,
    canExecute,
    modifying,
    error,
    execute,
    refreshPreview,
  } = useFileNameModifier(initialFilePaths);
  const [pathDraft, setPathDraft] = useState<string>();
  const [showFullPaths, setShowFullPaths] = useState(false);
  const [onlyChanges, setOnlyChanges] = useState(false);
  const editingPaths = pathDraft != null;
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 6 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  );

  const handleDragEnd = useCallback(
    ({ active, over }: DragEndEvent) => {
      if (modifying || !over || active.id === over.id) return;
      setOperations((previous) => {
        const from = previous.findIndex((item) => item.id === active.id);
        const to = previous.findIndex((item) => item.id === over.id);

        return from < 0 || to < 0 ? previous : arrayMove(previous, from, to);
      });
    },
    [modifying, setOperations],
  );
  const addOperation = () => setOperations((previous) => [...previous, createDefaultOperation()]);
  const copyOperation = (id: string) =>
    setOperations((previous) => {
      const index = previous.findIndex((item) => item.id === id);

      return index < 0
        ? previous
        : [
            ...previous.slice(0, index + 1),
            { ...previous[index], id: generateOperationId() },
            ...previous.slice(index + 1),
          ];
    });
  const addPaths = () =>
    createPortal(FileSystemSelectorModal, {
      multiple: true,
      onMultipleSelected: (entries) => {
        const incoming = normalizeFilePaths(
          entries.map((entry) => entry.path).filter((path): path is string => !!path),
        );

        if (editingPaths)
          setPathDraft((previous) =>
            [...new Set([...normalizeFilePaths((previous ?? "").split("\n")), ...incoming])].join(
              "\n",
            ),
          );
        else setFilePaths((previous) => [...new Set([...previous, ...incoming])]);
      },
    });
  const commonPrefix = useMemo(() => detectCommonPrefix(filePaths), [filePaths]);
  const visibleResults = onlyChanges
    ? previewResults.filter((item) => item.originalPath !== item.modifiedPath)
    : previewResults;
  const previewState = modifying
    ? "executing"
    : editingPaths
      ? "editingPaths"
      : hasInvalidOperations
        ? "invalidRules"
        : isPreviewLoading
          ? "updating"
          : filePaths.length === 0
            ? "needsFiles"
            : operations.length === 0
              ? "needsRules"
              : changedCount > 0
                ? "ready"
                : "unchanged";

  const executeModification = async () => {
    if (editingPaths) return;
    const result = await execute();

    if (!result) return;
    const failures = result.filter((item) => !item.success);
    const changed = result.filter((item) => item.success && item.oldPath !== item.newPath).length;

    createPortal(Modal, {
      defaultVisible: true,
      title: t<string>("FileNameModifier.ModificationResult"),
      size: "lg",
      footer: { actions: ["cancel"] },
      children: (
        <div className="space-y-4">
          <div className="flex flex-wrap gap-2">
            <Chip color="success" variant="flat">
              {t<string>("FileNameModifier.ModificationSuccessCount", { count: changed })}
            </Chip>
            <Chip color={failures.length > 0 ? "danger" : "default"} variant="flat">
              {t<string>("FileNameModifier.ModificationFailCount", { count: failures.length })}
            </Chip>
          </div>
          {failures.length > 0 && (
            <div className="max-h-72 space-y-2 overflow-y-auto">
              {failures.map((item) => (
                <div key={item.oldPath} className="rounded-lg bg-danger/5 p-3 text-sm">
                  <p className="break-all">{item.oldPath}</p>
                  <p className="mt-1 break-words text-danger">{item.error}</p>
                </div>
              ))}
            </div>
          )}
          <p className="text-xs leading-relaxed text-default-500">
            {t<string>(k("result.nextStep"))}
          </p>
        </div>
      ),
    });
  };

  return (
    <div className="flex h-full min-h-[34rem] min-w-0 flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl bg-default-50 px-4 py-3">
        <div className="min-w-0">
          <p className="text-sm font-medium">
            {t<string>(k("files.count"), { count: filePaths.length })}
          </p>
          <p className="mt-0.5 text-xs leading-relaxed text-default-500">
            {t<string>(k("files.description"))}
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button
            color="primary"
            isDisabled={modifying}
            size="sm"
            startContent={<AiOutlineFolderAdd aria-hidden className="text-base" />}
            variant="flat"
            onPress={addPaths}
          >
            {t<string>("FileNameModifier.AddFromFileSystem")}
          </Button>
          <Button
            isDisabled={modifying || editingPaths}
            size="sm"
            startContent={<AiOutlineEdit aria-hidden />}
            variant="light"
            onPress={() => setPathDraft(filePaths.join("\n"))}
          >
            {t<string>("FileNameModifier.EditFileList")}
          </Button>
        </div>
      </div>
      {editingPaths && (
        <section
          aria-label={t<string>("FileNameModifier.EditFileList")}
          className="rounded-xl bg-default-50 p-4"
        >
          <Textarea
            aria-label={t<string>("FileNameModifier.FilePathsTextarea")}
            description={t<string>(k("files.draftHint"))}
            isDisabled={modifying}
            maxRows={8}
            minRows={4}
            placeholder={t<string>("FileNameModifier.FilePathsPlaceholder")}
            value={pathDraft}
            onValueChange={setPathDraft}
          />
          <div className="mt-3 flex flex-wrap items-center gap-2">
            <Button
              color="primary"
              size="sm"
              onPress={() => {
                setFilePaths(normalizeFilePaths(pathDraft.split("\n")));
                setPathDraft(undefined);
              }}
            >
              {t<string>("FileNameModifier.ConfirmPaths")}
            </Button>
            <Button
              size="sm"
              variant="flat"
              onPress={() =>
                setPathDraft((value) =>
                  [...new Set(normalizeFilePaths((value ?? "").split("\n")))].join("\n"),
                )
              }
            >
              {t<string>("FileNameModifier.Deduplicate")}
            </Button>
            <Button size="sm" variant="light" onPress={() => setPathDraft(undefined)}>
              {t<string>("FileNameModifier.Cancel")}
            </Button>
          </div>
        </section>
      )}
      <div className="grid min-h-0 flex-1 grid-cols-1 gap-5 lg:grid-cols-[minmax(20rem,23rem)_minmax(0,1fr)]">
        <section
          aria-label={t<string>("FileNameModifier.OperationsList")}
          className="flex min-h-0 min-w-0 flex-col"
        >
          <div className="flex items-center justify-between gap-2 pb-3">
            <div>
              <h2 className="text-sm font-semibold">
                {t<string>("FileNameModifier.OperationsList")}
              </h2>
              <p className="mt-1 text-xs text-default-500">{t<string>(k("rules.orderHint"))}</p>
            </div>
            <Button
              isIconOnly
              aria-label={t<string>("FileNameModifier.AddOperation")}
              isDisabled={modifying}
              size="sm"
              variant="flat"
              onPress={addOperation}
            >
              <AiOutlinePlus aria-hidden />
            </Button>
          </div>
          <div className="min-h-0 flex-1 space-y-2 overflow-y-auto pr-1">
            {operations.length === 0 ? (
              <div className="flex min-h-48 flex-col items-center justify-center gap-3 rounded-xl bg-default-50 p-4 text-center">
                <AiOutlinePlus aria-hidden className="text-2xl text-default-400" />
                <p className="text-sm text-default-500">
                  {t<string>("FileNameModifier.EmptyOperationsHint")}
                </p>
                <Button color="primary" size="sm" variant="flat" onPress={addOperation}>
                  {t<string>("FileNameModifier.AddFirstOperation")}
                </Button>
              </div>
            ) : (
              <DndContext
                collisionDetection={closestCenter}
                sensors={sensors}
                onDragEnd={handleDragEnd}
              >
                <SortableContext
                  items={operations.map((item) => item.id)}
                  strategy={verticalListSortingStrategy}
                >
                  {operations.map((operation, index) => (
                    <SortableOperationCard
                      key={operation.id}
                      errors={validationErrors[index] ? t<string>(validationErrors[index]) : ""}
                      id={operation.id}
                      index={index}
                      isDisabled={modifying}
                      operation={operation}
                      onChange={(next) =>
                        setOperations((previous) =>
                          previous.map((item) =>
                            item.id === operation.id ? { ...next, id: item.id } : item,
                          ),
                        )
                      }
                      onCopy={() => copyOperation(operation.id)}
                      onDelete={() =>
                        setOperations((previous) =>
                          previous.filter((item) => item.id !== operation.id),
                        )
                      }
                    />
                  ))}
                </SortableContext>
              </DndContext>
            )}
            {operations.length > 0 && (
              <Button
                className="w-full"
                isDisabled={modifying}
                size="sm"
                startContent={<AiOutlinePlus aria-hidden />}
                variant="light"
                onPress={addOperation}
              >
                {t<string>("FileNameModifier.AddOperation")}
              </Button>
            )}
          </div>
          <Button
            className="mt-2 shrink-0 justify-start"
            isDisabled={modifying || operations.length === 0 || hasInvalidOperations}
            size="sm"
            startContent={<AiOutlinePartition aria-hidden />}
            variant="light"
            onPress={() => {
              sessionStorage.setItem(
                EDITOR_SEED_STORAGE_KEY,
                JSON.stringify({
                  nameKey: "workflow.template.fileCleaning.name",
                  triggerKind: "fs.manualScan",
                  activities: [
                    {
                      kind: "transform.fs.fileNameOp",
                      configJson: JSON.stringify({
                        operations: operations.map(({ id: _id, ...operation }) => operation),
                      }),
                    },
                    { kind: "transform.text.trim" },
                    { kind: "action.fs.saveName" },
                  ],
                }),
              );
              navigate("/workflows/editor?seed=1");
            }}
          >
            {t<string>("FileNameModifier.UpgradeToWorkflow")}
          </Button>
        </section>
        <section
          aria-label={t<string>("FileNameModifier.PreviewResults")}
          className="flex min-h-[20rem] min-w-0 flex-col overflow-hidden rounded-xl bg-default-50/50"
        >
          <div className="flex flex-wrap items-center justify-between gap-3 px-3 py-3">
            <div>
              <h2 className="text-sm font-semibold">
                {t<string>("FileNameModifier.PreviewResults")}
              </h2>
              <p className="mt-1 text-xs text-default-500">
                {t<string>(k("preview.count"), { changed: changedCount, total: filePaths.length })}
              </p>
            </div>
            <div className="flex flex-wrap items-center gap-3">
              <Checkbox isSelected={onlyChanges} size="sm" onValueChange={setOnlyChanges}>
                {t<string>(k("preview.onlyChanges"))}
              </Checkbox>
              <Checkbox isSelected={showFullPaths} size="sm" onValueChange={setShowFullPaths}>
                {t<string>(k("preview.fullPaths"))}
              </Checkbox>
              <Button
                isIconOnly
                aria-label={t<string>(k("preview.refresh"))}
                isDisabled={
                  modifying ||
                  hasInvalidOperations ||
                  filePaths.length === 0 ||
                  operations.length === 0
                }
                size="sm"
                variant="light"
                onPress={refreshPreview}
              >
                <AiOutlineReload aria-hidden className="text-base" />
              </Button>
            </div>
          </div>
          <div className="min-h-0 flex-1">
            <PreviewList
              commonPrefix={commonPrefix}
              isLoading={isPreviewLoading}
              results={visibleResults}
              showFullPaths={showFullPaths}
            />
          </div>
        </section>
      </div>
      <footer className="flex shrink-0 flex-wrap items-center justify-between gap-3 border-t border-default-100 pt-3">
        <div className="min-w-0 flex-1">
          <p
            className={`text-sm ${hasInvalidOperations || error ? "text-danger" : "text-default-600"}`}
            role={error ? "alert" : "status"}
          >
            {error || t<string>(k(`state.${previewState}`))}
          </p>
          <p className="mt-1 text-xs text-default-400">{t<string>(k("execution.hint"))}</p>
        </div>
        <div className="flex flex-wrap gap-2">
          {lastFilePaths && (
            <Button
              isDisabled={modifying || editingPaths}
              size="sm"
              startContent={<AiOutlineUndo aria-hidden />}
              title={t<string>(k("files.restoreHint"))}
              variant="light"
              onPress={() => {
                setFilePaths(lastFilePaths);
                setLastFilePaths(null);
              }}
            >
              {t<string>("FileNameModifier.RestoreOriginalPaths")}
            </Button>
          )}
          <Button
            color="primary"
            isDisabled={!canExecute || editingPaths}
            isLoading={modifying}
            startContent={<AiOutlineCheckCircle aria-hidden />}
            onPress={executeModification}
          >
            {t<string>(k("execution.apply"), { count: changedCount })}
          </Button>
        </div>
      </footer>
    </div>
  );
};

export default FileNameModifier;
