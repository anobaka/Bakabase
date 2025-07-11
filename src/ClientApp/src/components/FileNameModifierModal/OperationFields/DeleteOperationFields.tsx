import React from 'react';
import { Input, NumberInput, Select } from '../../bakaui';
import { PositionType, PositionTypeOptions } from '../OperationCard';

const DeleteOperationFields: React.FC<any> = ({ operation, t, onChange }) => {
  const handleChangeField = (key: string, value: any) => onChange({ ...operation, [key]: value });
  return (
    <>
      {[['deleteCount', 'DeleteCount', 120], ['deleteStartPosition', 'DeleteStartPosition', 120]].map(([key, label, w]) => (
        <NumberInput
          key={key as string}
          value={operation[key as keyof typeof operation]}
          onValueChange={e => handleChangeField(key as string, e)}
          placeholder={t(`FileNameModifier.Placeholder.${label}`)}
          label={t(`FileNameModifier.Label.${label}`)}
          size="sm"
          isRequired={operation[key as keyof typeof operation] == null}
          className={`w-[${w}px]`}
        />
      ))}
      <Input
        value={operation.targetText || ''}
        onValueChange={e => handleChangeField('targetText', e)}
        placeholder={t('FileNameModifier.Placeholder.MatchText')}
        label={t('FileNameModifier.Label.MatchText')}
        size="sm"
        isRequired={false}
        className="w-[180px]"
      />
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
        className="w-[140px]"
        placeholder={t('FileNameModifier.Placeholder.PositionType')}
      />
    </>
  );
};

export default DeleteOperationFields; 