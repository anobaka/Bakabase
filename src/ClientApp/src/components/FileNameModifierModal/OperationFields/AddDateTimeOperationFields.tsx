import React from 'react';
import { Input, Select, NumberInput } from '../../bakaui';
import { FileNameModifierPosition, fileNameModifierPositions } from '@/sdk/constants';
const PositionType = FileNameModifierPosition;
const PositionTypeOptions = fileNameModifierPositions.map(opt => ({ label: 'FileNameModifier.PositionType.' + FileNameModifierPosition[opt.value], value: opt.value }));

const AddDateTimeOperationFields: React.FC<any> = ({ operation, t, onChange }) => (
  <>
    <Input
      value={operation.dateTimeFormat || ''}
      onValueChange={e => onChange({ ...operation, dateTimeFormat: e })}
      placeholder={t('FileNameModifier.Placeholder.DateTimeFormat')}
      label={t('FileNameModifier.Label.DateTimeFormat')}
      size="sm"
      isRequired={!operation.dateTimeFormat}
      className="w-[180px]"
    />
    <Select
      dataSource={PositionTypeOptions.map(opt => ({ label: t(opt.label), value: opt.value }))}
      selectedKeys={[operation.position?.toString() || '']}
      onSelectionChange={keys => {
        const key = parseInt(Array.from(keys)[0] as string);
        if (key !== operation.position) {
          onChange({ ...operation, position: key });
        }
      }}
      label={t('FileNameModifier.Label.PositionType')}
      size="sm"
      isRequired={true}
      className="w-[160px]"
      placeholder={t('FileNameModifier.Placeholder.PositionType')}
    />
    {operation.position === PositionType.AtPosition && (
      <NumberInput
        value={operation.positionIndex}
        onValueChange={e => onChange({ ...operation, positionIndex: e })}
        placeholder={t('FileNameModifier.Placeholder.PositionIndex')}
        label={t('FileNameModifier.Label.PositionIndex')}
        size="sm"
        isRequired={true}
        className="w-[120px]"
      />
    )}
    {(operation.position === PositionType.BeforeText || operation.position === PositionType.AfterText) && (
      <Input
        value={operation.targetText || ''}
        onValueChange={e => onChange({ ...operation, targetText: e })}
        placeholder={t('FileNameModifier.Placeholder.TargetText')}
        label={t('FileNameModifier.Label.TargetText')}
        size="sm"
        isRequired={!operation.targetText}
        className="w-[240px]"
      />
    )}
  </>
);

export default AddDateTimeOperationFields; 