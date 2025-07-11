import React from 'react';
import { Input, Checkbox } from '../../bakaui';

const ReplaceOperationFields: React.FC<any> = ({ operation, t, onChange }) => {
  const handleChangeField = (key: string, value: any) => onChange({ ...operation, [key]: value });
  return (
    <>
      {[['text', 'Text'], ['targetText', 'TargetText']].map(([key, label]) => (
        <Input
          key={key as string}
          value={operation[key as keyof typeof operation] || ''}
          onValueChange={e => handleChangeField(key as string, e)}
          placeholder={t(`FileNameModifier.Placeholder.${label}`)}
          label={t(`FileNameModifier.Label.${label}`)}
          size="sm"
          isRequired={!operation.text && !operation.targetText}
          className="w-[180px]"
        />
      ))}
      <div className="flex items-center gap-1">
        <Checkbox
          checked={operation.replaceEntire}
          onChange={e => handleChangeField('replaceEntire', e.target.checked)}
        />
        <span className="text-xs text-gray-500">{t('FileNameModifier.ReplaceEntire')}</span>
      </div>
    </>
  );
};

export default ReplaceOperationFields; 