import { useRef } from 'react';
import type { ValueEditorProps } from '../models';
import { Checkbox } from '@/components/bakaui';

type BooleanValueEditorProps = ValueEditorProps<boolean>;

export default ({ initValue, onChange }: BooleanValueEditorProps) => {
  const valueRef = useRef(initValue);
  return (
    <Checkbox
      size={'sm'}
      isSelected={valueRef.current}
      onValueChange={v => {
        valueRef.current = v;
      }}
      onBlur={() => {
        onChange?.(valueRef.current);
      }}
    />
  );
};
