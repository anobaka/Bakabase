import React from 'react';
import { Input } from '../../bakaui';

const AddDateTimeOperationFields: React.FC<any> = ({ operation, t, onChange }) => (
  <Input
    value={operation.dateTimeFormat || ''}
    onValueChange={e => onChange({ ...operation, dateTimeFormat: e })}
    placeholder={t('FileNameModifier.Placeholder.DateTimeFormat')}
    label={t('FileNameModifier.Label.DateTimeFormat')}
    size="sm"
    isRequired={!operation.dateTimeFormat}
    className="w-[180px]"
  />
);

export default AddDateTimeOperationFields; 