"use client";

import type { BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation } from "../../sdk/Api";

import React, { CSSProperties, forwardRef } from "react";
import {
  AiOutlineDelete,
  AiOutlineCopy,
  AiOutlineHolder,
} from "react-icons/ai";
import { useTranslation } from "react-i18next";

import { Select, Button, Card, Chip } from "../bakaui";

import InsertOperationFields from "./OperationFields/InsertOperationFields";
import ReplaceOperationFields from "./OperationFields/ReplaceOperationFields";
import AddDateTimeOperationFields from "./OperationFields/AddDateTimeOperationFields";
import DeleteOperationFields from "./OperationFields/DeleteOperationFields";
import ChangeCaseOperationFields from "./OperationFields/ChangeCaseOperationFields";
import AddAlphabetSequenceOperationFields from "./OperationFields/AddAlphabetSequenceOperationFields";

import {
  FileNameModifierOperationType,
  fileNameModifierOperationTypes,
  FileNameModifierFileNameTarget,
  fileNameModifierFileNameTargets,
} from "@/sdk/constants";

export interface OperationCardProps {
  operation: BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation;
  index: number;
  errors?: string;
  onChange: (
    op: BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation,
  ) => void;
  onDelete: () => void;
  onCopy?: () => void;
  style?: CSSProperties;
  dragHandleProps?: React.HTMLAttributes<HTMLDivElement>;
}

// 枚举常量
const OperationType = FileNameModifierOperationType;
const TargetTypeOptions = fileNameModifierFileNameTargets.map((opt) => ({
  label: "FileNameModifier.Target." + FileNameModifierFileNameTarget[opt.value],
  value: opt.value,
}));

const OperationCard = forwardRef<HTMLDivElement, OperationCardProps>(
  (
    {
      operation,
      index,
      onChange,
      onDelete,
      onCopy,
      errors,
      style,
      dragHandleProps,
    },
    ref
  ) => {
    const { t } = useTranslation();
    // 参数输入变更
    const handleChange = (
      key: keyof BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation,
      value: any,
    ) => {
      onChange({ ...operation, [key]: value });
    };

    return (
      <Card ref={ref} className="mb-2 p-3 operation-card" style={style}>
        {/* 顶部：序号、拖拽手柄、操作按钮 */}
        <div className="flex items-center justify-between mb-2">
          <div className="flex items-center gap-2">
            {dragHandleProps && (
              <div
                {...dragHandleProps}
                className="cursor-grab active:cursor-grabbing text-gray-400 hover:text-gray-600"
              >
                <AiOutlineHolder className="text-lg" />
              </div>
            )}
            <Chip color="primary" radius="md" size="sm" variant="flat">
              {index + 1}
            </Chip>
            <Select
              className="w-[200px]"
              dataSource={TargetTypeOptions.map((opt) => ({
                label: t<string>(opt.label),
                value: opt.value,
              }))}
              disallowEmptySelection
              isRequired={true}
              placeholder={t<string>("FileNameModifier.TargetTypePlaceholder")}
              selectedKeys={[operation.target?.toString() || ""]}
              size="sm"
              onSelectionChange={(keys) => {
                const key = parseInt(Array.from(keys)[0] as string);

                if (key !== operation.target) {
                  handleChange("target", key);
                }
              }}
            />
            <Select
              className="w-[160px]"
              dataSource={fileNameModifierOperationTypes.map((opt) => ({
                label: t<string>(`FileNameModifier.OperationType.${opt.label}`),
                value: opt.value,
              }))}
              disallowEmptySelection
              isRequired={true}
              placeholder={t<string>("FileNameModifier.OperationTypePlaceholder")}
              selectedKeys={[operation.operation?.toString() || ""]}
              size="sm"
              onSelectionChange={(keys) => {
                const key = Array.from(keys)[0] as string;
                const keyInt = parseInt(key);

                if (keyInt && keyInt !== operation.operation) {
                  handleChange("operation", keyInt);
                }
              }}
            />
          </div>
          <div className="flex items-center gap-1">
            {onCopy && (
              <Button isIconOnly size="sm" variant="light" onClick={onCopy}>
                <AiOutlineCopy className="text-lg" />
              </Button>
            )}
            <Button
              isIconOnly
              color="danger"
              size="sm"
              variant="light"
              onClick={onDelete}
            >
              <AiOutlineDelete className="text-lg" />
            </Button>
          </div>
        </div>
        {/* 错误提示 */}
        {errors && <div className="text-red-500 text-xs mb-1">{errors}</div>}
        {/* 操作参数字段 */}
        <div className="flex flex-wrap gap-2 items-center">
          {operation.operation === OperationType.Insert && (
            <InsertOperationFields
              operation={operation}
              t={t}
              onChange={onChange}
            />
          )}
          {operation.operation === OperationType.Replace && (
            <ReplaceOperationFields
              operation={operation}
              t={t}
              onChange={onChange}
            />
          )}
          {operation.operation === OperationType.AddDateTime && (
            <AddDateTimeOperationFields
              operation={operation}
              t={t}
              onChange={onChange}
            />
          )}
          {operation.operation === OperationType.Delete && (
            <DeleteOperationFields
              operation={operation}
              t={t}
              onChange={onChange}
            />
          )}
          {operation.operation === OperationType.ChangeCase && (
            <ChangeCaseOperationFields
              operation={operation}
              t={t}
              onChange={onChange}
            />
          )}
          {operation.operation === OperationType.AddAlphabetSequence && (
            <AddAlphabetSequenceOperationFields
              operation={operation}
              t={t}
              onChange={onChange}
            />
          )}
          {/* Reverse 无需参数 */}
        </div>
      </Card>
    );
  }
);

OperationCard.displayName = "OperationCard";

export default OperationCard;
