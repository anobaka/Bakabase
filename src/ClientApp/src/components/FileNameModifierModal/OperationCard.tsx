import React, { useRef } from 'react';
import type { BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation } from '../../sdk/Api';
import { Select, Input, NumberInput, Checkbox, Button, Tooltip, Card, Chip } from '../bakaui';
import { AiOutlineDelete, AiOutlineDown, AiOutlineUp, AiOutlineCopy } from 'react-icons/ai';
import { useTranslation } from 'react-i18next';
import InsertOperationFields from './OperationFields/InsertOperationFields';
import ReplaceOperationFields from './OperationFields/ReplaceOperationFields';
import AddDateTimeOperationFields from './OperationFields/AddDateTimeOperationFields';
import DeleteOperationFields from './OperationFields/DeleteOperationFields';
import ChangeCaseOperationFields from './OperationFields/ChangeCaseOperationFields';
import AddAlphabetSequenceOperationFields from './OperationFields/AddAlphabetSequenceOperationFields';
import {
  FileNameModifierOperationType,
  fileNameModifierOperationTypes,
  FileNameModifierPosition,
  fileNameModifierPositions,
  FileNameModifierCaseType,
  fileNameModifierCaseTypes,
  FileNameModifierFileNameTarget,
  fileNameModifierFileNameTargets,
} from '@/sdk/constants';

interface OperationCardProps {
  operation: BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation;
  index: number;
  errors?: string;
  onChange: (op: BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation) => void;
  onDelete: () => void;
  onMoveUp?: () => void;
  onMoveDown?: () => void;
  onCopy?: () => void;
}

// 枚举常量
const OperationType = FileNameModifierOperationType;
const PositionType = FileNameModifierPosition;
const CaseTypeOptions = fileNameModifierCaseTypes.map(opt => ({ label: 'FileNameModifier.CaseType.' + FileNameModifierCaseType[opt.value], value: opt.value }));
const PositionTypeOptions = fileNameModifierPositions.map(opt => ({ label: 'FileNameModifier.PositionType.' + FileNameModifierPosition[opt.value], value: opt.value }));
const TargetTypeOptions = fileNameModifierFileNameTargets.map(opt => ({ label: 'FileNameModifier.Target.' + FileNameModifierFileNameTarget[opt.value], value: opt.value }));

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
  const handleChange = (key: keyof BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation, value: any) => {
    onChange({ ...operation, [key]: value });
  };

  return (
    <Card className="mb-2 p-3 operation-card">
      <div className="flex items-center gap-2 mb-2">
        <Select
          dataSource={TargetTypeOptions.map(opt => ({ label: t(opt.label), value: opt.value }))}
          selectedKeys={[operation.target?.toString() || '']}
          onSelectionChange={keys => {
            const key = parseInt(Array.from(keys)[0] as string);
            if (key !== operation.target) {
              handleChange('target', key);
            }
          }}
          isRequired={true}
          className="w-[240px]"
          label={t('FileNameModifier.TargetType')}
          placeholder={t('FileNameModifier.TargetTypePlaceholder')}
        />
        <Select
          dataSource={fileNameModifierOperationTypes.map(opt => ({ label: t(opt.label), value: opt.value }))}
          selectedKeys={[operation.operation?.toString() || '']}
          onSelectionChange={keys => {
            const key = Array.from(keys)[0] as string;
            const keyInt = parseInt(key);
            if (keyInt && keyInt !== operation.operation) {
              handleChange('operation', keyInt);
            }
          }}
          isRequired={true}
          className="w-[180px]"
          label={t('FileNameModifier.OperationType')}
          placeholder={t('FileNameModifier.OperationTypePlaceholder')}
        />
      </div>
      {/* 错误提示 */}
      {errors && <div className="text-red-500 text-xs mb-1">{errors}</div>}
      <div className="flex flex-wrap gap-2 items-center">
        {operation.operation === OperationType.Insert && (
          <InsertOperationFields operation={operation} t={t} onChange={onChange} />
        )}
        {operation.operation === OperationType.Replace && (
          <ReplaceOperationFields operation={operation} t={t} onChange={onChange} />
        )}
        {operation.operation === OperationType.AddDateTime && (
          <AddDateTimeOperationFields operation={operation} t={t} onChange={onChange} />
        )}
        {operation.operation === OperationType.Delete && (
          <DeleteOperationFields operation={operation} t={t} onChange={onChange} />
        )}
        {operation.operation === OperationType.ChangeCase && (
          <ChangeCaseOperationFields operation={operation} t={t} onChange={onChange} />
        )}
        {operation.operation === OperationType.AddAlphabetSequence && (
          <AddAlphabetSequenceOperationFields operation={operation} t={t} onChange={onChange} />
        )}
        {/* Reverse 无需参数 */}
        <Chip size='sm' color="primary" radius="md" variant="flat">{index + 1}</Chip>
        {/* 拖拽/移动/复制/删除按钮 */}
        {onMoveUp && <Button size='sm' onClick={() => onMoveUp()} variant="light" isIconOnly><AiOutlineUp className='text-lg' /></Button>}
        {onMoveDown && <Button size='sm' onClick={() => onMoveDown()} variant="light" isIconOnly><AiOutlineDown className='text-lg' /></Button>}
        {onCopy && <Button size='sm' onClick={onCopy} variant="light" isIconOnly><AiOutlineCopy className='text-lg' /></Button>}
        <Button size='sm' onClick={onDelete} color="danger" variant="light" isIconOnly>
          <AiOutlineDelete className='text-lg' />
        </Button>
      </div>
    </Card>
  );
};

export default OperationCard; 