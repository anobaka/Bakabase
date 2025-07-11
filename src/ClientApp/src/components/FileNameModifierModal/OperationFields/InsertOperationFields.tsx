import React from 'react';
import { Input, Select, NumberInput } from '../../bakaui';
import { FileNameModifierPosition, fileNameModifierPositions } from '@/sdk/constants';
const PositionType = FileNameModifierPosition;
const PositionTypeOptions = fileNameModifierPositions.map(opt => ({ label: 'FileNameModifier.PositionType.' + FileNameModifierPosition[opt.value], value: opt.value }));

const InsertOperationFields: React.FC<any> = ({ operation, t, onChange }) => {
  const handleChangeField = (key: string, value: any) => onChange({ ...operation, [key]: value });
  return (
    <>
      <Input
        value={operation.text || ''}
        onValueChange={e => handleChangeField('text', e)}
        placeholder={t('FileNameModifier.Placeholder.Text')}
        label={t('FileNameModifier.Label.Text')}
        size="sm"
        isRequired={!operation.text && !(operation.position === PositionType.BeforeText || operation.position === PositionType.AfterText && operation.targetText)}
        className="w-[240px]"
      />
      {(operation.position === PositionType.BeforeText || operation.position === PositionType.AfterText) && (
        <Input
          value={operation.targetText || ''}
          onValueChange={e => handleChangeField('targetText', e)}
          placeholder={t('FileNameModifier.Placeholder.TargetText')}
          label={t('FileNameModifier.Label.TargetText')}
          size="sm"
          isRequired={!operation.targetText}
          className="w-[240px]"
        />
      )}
      <Select
        dataSource={PositionTypeOptions.map(opt => ({ label: t(opt.label), value: opt.value }))}
        selectedKeys={[operation.position?.toString() || '']}
        onSelectionChange={keys => {
          const key = parseInt(Array.from(keys)[0] as string);
          if (key !== operation.position) {
            handleChangeField('position', key);
          }
        }}
        label={t('FileNameModifier.Label.PositionType')}
        size="sm"
        isRequired={!operation.position}
        className="w-[160px]"
        placeholder={t('FileNameModifier.Placeholder.PositionType')}
      />
      {operation.position === PositionType.AtPosition && (
        <NumberInput
          value={operation.positionIndex}
          onValueChange={e => handleChangeField('positionIndex', e)}
          placeholder={t('FileNameModifier.Placeholder.PositionIndex')}
          label={t('FileNameModifier.Label.PositionIndex')}
          size="sm"
          isRequired={true}
          className="w-[120px]"
        />
      )}
    </>
  );
};

export default InsertOperationFields; 