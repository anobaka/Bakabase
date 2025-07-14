import React from 'react';
import { Input, NumberInput } from '../../bakaui';
import { FileNameModifierPosition } from '@/sdk/constants';
const PositionType = FileNameModifierPosition;

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
  </>
);

export default AddAlphabetSequenceOperationFields; 