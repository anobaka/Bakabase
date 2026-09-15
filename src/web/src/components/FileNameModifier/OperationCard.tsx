"use client";

import type { BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation as Operation } from "@/sdk/Api";
import type { CSSProperties, HTMLAttributes } from "react";

import { forwardRef, useState } from "react";
import {
  AiOutlineDelete,
  AiOutlineCopy,
  AiOutlineHolder,
  AiOutlineDown,
  AiOutlineRight,
} from "react-icons/ai";
import { useTranslation } from "react-i18next";

import { Select, Button, Tooltip } from "../bakaui";

import InsertOperationFields from "./OperationFields/InsertOperationFields";
import ReplaceOperationFields from "./OperationFields/ReplaceOperationFields";
import AddDateTimeOperationFields from "./OperationFields/AddDateTimeOperationFields";
import DeleteOperationFields from "./OperationFields/DeleteOperationFields";
import ChangeCaseOperationFields from "./OperationFields/ChangeCaseOperationFields";
import AddAlphabetSequenceOperationFields from "./OperationFields/AddAlphabetSequenceOperationFields";

import {
  FileNameModifierOperationType as OperationType,
  fileNameModifierOperationTypes,
  FileNameModifierFileNameTarget,
  fileNameModifierFileNameTargets,
  FileNameModifierCaseType,
} from "@/sdk/constants";

export interface OperationCardProps {
  operation: Operation;
  index: number;
  errors?: string;
  onChange: (operation: Operation) => void;
  onDelete: () => void;
  onCopy?: () => void;
  style?: CSSProperties;
  dragHandleProps?: HTMLAttributes<HTMLDivElement>;
  isDisabled?: boolean;
}

const OperationCard = forwardRef<HTMLDivElement, OperationCardProps>(
  (
    { operation, index, onChange, onDelete, onCopy, errors, style, dragHandleProps, isDisabled },
    ref,
  ) => {
    const { t } = useTranslation();
    const [expanded, setExpanded] = useState(true);
    const change = (next: Operation) => {
      if (!isDisabled) onChange(next);
    };
    const operationLabel = t<string>(
      `FileNameModifier.OperationType.${OperationType[operation.operation]}`,
    );
    const targetLabel = t<string>(
      `FileNameModifier.Target.${FileNameModifierFileNameTarget[operation.target]}`,
    );
    const summary =
      operation.operation === OperationType.Replace
        ? `${operation.replaceEntire ? targetLabel : operation.targetText || "…"} → ${operation.text || t<string>("fileNameModifier.rules.emptyText")}`
        : operation.operation === OperationType.ChangeCase
          ? t<string>(`FileNameModifier.CaseType.${FileNameModifierCaseType[operation.caseType]}`)
          : operation.operation === OperationType.AddDateTime
            ? operation.dateTimeFormat
            : operation.operation === OperationType.Insert
              ? operation.text
              : operation.operation === OperationType.AddAlphabetSequence
                ? `${operation.alphabetStartChar} · ${operation.alphabetCount}`
                : operation.operation === OperationType.Delete
                  ? t<string>("fileNameModifier.rules.deleteSummary", {
                      count: operation.deleteCount,
                    })
                  : undefined;

    return (
      <div
        ref={ref}
        className={`operation-card rounded-xl p-3 ${errors ? "bg-danger/5" : "bg-default-50"}`}
        style={style}
      >
        <div className="flex min-w-0 items-start gap-1">
          {dragHandleProps && (
            <div
              {...dragHandleProps}
              aria-disabled={isDisabled}
              aria-label={t<string>("fileNameModifier.rules.reorder", { number: index + 1 })}
              className="mt-1.5 shrink-0 cursor-grab touch-none rounded p-1 text-default-400 outline-none focus-visible:ring-2 focus-visible:ring-primary active:cursor-grabbing"
              tabIndex={isDisabled ? -1 : dragHandleProps.tabIndex}
            >
              <AiOutlineHolder aria-hidden className="text-base" />
            </div>
          )}
          <Button
            aria-expanded={expanded}
            aria-label={t<string>(
              expanded ? "fileNameModifier.rules.collapse" : "fileNameModifier.rules.expand",
              { number: index + 1 },
            )}
            className="h-auto min-w-0 flex-1 justify-start px-1 py-1.5 text-left"
            isDisabled={isDisabled}
            variant="light"
            onPress={() => setExpanded((previous) => !previous)}
          >
            <span className="flex min-w-0 flex-1 items-start gap-2">
              <span className="mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center rounded-md bg-default-200/60 text-xs tabular-nums text-default-600">
                {index + 1}
              </span>
              <span className="flex min-w-0 flex-1 flex-col gap-0.5">
                <span className="truncate text-sm font-medium">{operationLabel}</span>
                <span className="truncate text-xs font-normal text-default-500">{targetLabel}</span>
                {summary && !expanded && (
                  <span className="truncate text-xs font-normal text-default-500" title={summary}>
                    {summary}
                  </span>
                )}
              </span>
              {expanded ? (
                <AiOutlineDown aria-hidden className="mt-1 shrink-0 text-xs text-default-400" />
              ) : (
                <AiOutlineRight aria-hidden className="mt-1 shrink-0 text-xs text-default-400" />
              )}
            </span>
          </Button>
          <div className="flex shrink-0 items-center">
            {onCopy && (
              <Tooltip content={t<string>("fileNameModifier.rules.copy")}>
                <Button
                  isIconOnly
                  aria-label={t<string>("fileNameModifier.rules.copy")}
                  className="h-7 min-w-7 w-7"
                  isDisabled={isDisabled}
                  size="sm"
                  variant="light"
                  onPress={onCopy}
                >
                  <AiOutlineCopy aria-hidden />
                </Button>
              </Tooltip>
            )}
            <Tooltip content={t<string>("fileNameModifier.rules.delete")}>
              <Button
                isIconOnly
                aria-label={t<string>("fileNameModifier.rules.delete")}
                className="h-7 min-w-7 w-7"
                color="danger"
                isDisabled={isDisabled}
                size="sm"
                variant="light"
                onPress={onDelete}
              >
                <AiOutlineDelete aria-hidden />
              </Button>
            </Tooltip>
          </div>
        </div>
        {errors && (
          <p className="mt-2 text-xs leading-relaxed text-danger" role="alert">
            {errors}
          </p>
        )}
        {expanded && (
          <fieldset className="mt-3 min-w-0 space-y-3" disabled={isDisabled}>
            <div className="grid min-w-0 grid-cols-2 gap-2">
              <Select
                disallowEmptySelection
                className="min-w-0"
                dataSource={fileNameModifierFileNameTargets.map((item) => ({
                  label: t<string>(`FileNameModifier.Target.${item.label}`),
                  value: String(item.value),
                }))}
                isDisabled={isDisabled}
                label={t<string>("fileNameModifier.rules.target")}
                selectedKeys={[String(operation.target)]}
                size="sm"
                onSelectionChange={(keys) => {
                  const target = Number(Array.from(keys)[0]) as Operation["target"];

                  if (target) change({ ...operation, target });
                }}
              />
              <Select
                disallowEmptySelection
                className="min-w-0"
                dataSource={fileNameModifierOperationTypes.map((item) => ({
                  label: t<string>(`FileNameModifier.OperationType.${item.label}`),
                  value: String(item.value),
                }))}
                isDisabled={isDisabled}
                label={t<string>("fileNameModifier.rules.operation")}
                selectedKeys={[String(operation.operation)]}
                size="sm"
                onSelectionChange={(keys) => {
                  const next = Number(Array.from(keys)[0]) as Operation["operation"];

                  if (next) change({ ...operation, operation: next });
                }}
              />
            </div>
            <div className="grid min-w-0 grid-cols-2 items-start gap-2 [&>*]:min-w-0 [&>*]:max-w-full">
              {operation.operation === OperationType.Insert && (
                <InsertOperationFields operation={operation} t={t} onChange={change} />
              )}
              {operation.operation === OperationType.Replace && (
                <ReplaceOperationFields operation={operation} t={t} onChange={change} />
              )}
              {operation.operation === OperationType.AddDateTime && (
                <AddDateTimeOperationFields operation={operation} t={t} onChange={change} />
              )}
              {operation.operation === OperationType.Delete && (
                <DeleteOperationFields operation={operation} t={t} onChange={change} />
              )}
              {operation.operation === OperationType.ChangeCase && (
                <ChangeCaseOperationFields operation={operation} t={t} onChange={change} />
              )}
              {operation.operation === OperationType.AddAlphabetSequence && (
                <AddAlphabetSequenceOperationFields operation={operation} t={t} onChange={change} />
              )}
              {operation.operation === OperationType.Reverse && (
                <p className="col-span-2 text-xs text-default-500">
                  {t<string>("fileNameModifier.rules.noParameters")}
                </p>
              )}
            </div>
          </fieldset>
        )}
      </div>
    );
  },
);

OperationCard.displayName = "OperationCard";
export default OperationCard;
