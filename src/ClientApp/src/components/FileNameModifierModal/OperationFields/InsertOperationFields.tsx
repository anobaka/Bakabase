import React from 'react';
import { Input, Select } from '../../bakaui';
import { PositionType, PositionTypeOptions } from '../OperationCard';

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
        isRequired={!operation.text && !(operation.position === PositionType.BeforeMatch || operation.position === PositionType.AfterMatch && operation.targetText)}
        className="w-[240px]"
      />
      {(operation.position === PositionType.BeforeMatch || operation.position === PositionType.AfterMatch) && (
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
    </>
  );
};

export default InsertOperationFields; 