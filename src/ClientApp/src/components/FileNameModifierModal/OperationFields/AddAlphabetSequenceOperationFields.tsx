import React from 'react';
import { Input, NumberInput } from '../../bakaui';

const AddAlphabetSequenceOperationFields: React.FC<any> = ({ operation, t, onChange }) => (
  <>
    <Input
      value={operation.alphabetStartChar}
      onValueChange={e => onChange({ ...operation, alphabetStartChar: e })}
      placeholder={t('FileNameModifier.Placeholder.StartChar')}
      label={t('FileNameModifier.Label.StartChar')}
      size="sm"
      isRequired={!operation.alphabetStartChar}
      maxLength={1}
      className="w-[100px]"
    />
    <NumberInput
      value={operation.alphabetCount}
      onValueChange={e => onChange({ ...operation, alphabetCount: e })}
      placeholder={t('FileNameModifier.Placeholder.Count')}
      label={t('FileNameModifier.Label.Count')}
      size="sm"
      isRequired={operation.alphabetCount == null}
      className="w-[100px]"
    />
  </>
);

export default AddAlphabetSequenceOperationFields; 