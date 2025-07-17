"use client";

import type { BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation } from "../../sdk/Api";

import React from "react";
import {
  AiOutlineDelete,
  AiOutlineDown,
  AiOutlineUp,
  AiOutlineCopy,
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
  FileNameModifierPosition,
  fileNameModifierPositions,
  FileNameModifierFileNameTarget,
  fileNameModifierFileNameTargets,
} from "@/sdk/constants";

interface OperationCardProps {
  operation: BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation;
  index: number;
  errors?: string;
  onChange: (
    op: BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation,
  ) => void;
  onDelete: () => void;
  onMoveUp?: () => void;
  onMoveDown?: () => void;
  onCopy?: () => void;
}

// 枚举常量
const OperationType = FileNameModifierOperationType;
const PositionType = FileNameModifierPosition;
const PositionTypeOptions = fileNameModifierPositions.map((opt) => ({
  label: "FileNameModifier.PositionType." + FileNameModifierPosition[opt.value],
  value: opt.value,
}));
const TargetTypeOptions = fileNameModifierFileNameTargets.map((opt) => ({
  label: "FileNameModifier.Target." + FileNameModifierFileNameTarget[opt.value],
  value: opt.value,
}));

const OperationCard: React.FC<OperationCardProps> = ({
  operation,
  index,
  onChange,
  onDelete,
  onMoveUp,
  onMoveDown,
  onCopy,
  errors,
}) => {
  const { t } = useTranslation();
  // 参数输入变更
  const handleChange = (
    key: keyof BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation,
    value: any,
  ) => {
    onChange({ ...operation, [key]: value });
  };

  return (
    <Card className="mb-2 p-3 operation-card">
      <div className="flex items-center gap-2 mb-2">
        <Select
          className="w-[240px]"
          dataSource={TargetTypeOptions.map((opt) => ({
            label: t<string>(opt.label),
            value: opt.value,
          }))}
          isRequired={true}
          label={t<string>("FileNameModifier.TargetType")}
          placeholder={t<string>("FileNameModifier.TargetTypePlaceholder")}
          selectedKeys={[operation.target?.toString() || ""]}
          onSelectionChange={(keys) => {
            const key = parseInt(Array.from(keys)[0] as string);

            if (key !== operation.target) {
              handleChange("target", key);
            }
          }}
        />
        <Select
          className="w-[180px]"
          dataSource={fileNameModifierOperationTypes.map((opt) => ({
            label: t<string>(`FileNameModifier.OperationType.${opt.label}`),
            value: opt.value,
          }))}
          isRequired={true}
          label={t<string>("FileNameModifier.OperationType")}
          placeholder={t<string>("FileNameModifier.OperationTypePlaceholder")}
          selectedKeys={[operation.operation?.toString() || ""]}
          onSelectionChange={(keys) => {
            const key = Array.from(keys)[0] as string;
            const keyInt = parseInt(key);

            if (keyInt && keyInt !== operation.operation) {
              handleChange("operation", keyInt);
            }
          }}
        />
      </div>
      {/* 错误提示 */}
      {errors && <div className="text-red-500 text-xs mb-1">{errors}</div>}
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
        <Chip color="primary" radius="md" size="sm" variant="flat">
          {index + 1}
        </Chip>
        {/* 拖拽/移动/复制/删除按钮 */}
        {onMoveUp && (
          <Button
            isIconOnly
            size="sm"
            variant="light"
            onClick={() => onMoveUp()}
          >
            <AiOutlineUp className="text-lg" />
          </Button>
        )}
        {onMoveDown && (
          <Button
            isIconOnly
            size="sm"
            variant="light"
            onClick={() => onMoveDown()}
          >
            <AiOutlineDown className="text-lg" />
          </Button>
        )}
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
    </Card>
  );
};

export default OperationCard;
