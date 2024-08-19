import { useRef } from 'react';
import type { ValueEditorProps } from '../models';
import { Input } from '@/components/bakaui';

type NumberValueEditorProps = ValueEditorProps<number | undefined> & {
  label?: string;
  placeholder?: string;
};

export default ({ value, onValueChange, label, placeholder, ...props }: NumberValueEditorProps) => {
  const valueRef = useRef(value);

  return (
    <Input
      defaultValue={valueRef.current?.toString()}
      onValueChange={v => {
        const nStr = v?.match(/[\d,]+(\.\d+)?/)?.[0];
        const n = Number(nStr);
        valueRef.current = Number.isNaN(n) ? undefined : n;
        // console.log('NumberValueEditor', r, v, nStr, v?.match(/[\d,]+(\.\d+)?/));
      }}
      onBlur={() => {
        onValueChange?.(valueRef.current, valueRef.current);
      }}
      onKeyDown={(e) => {
        if (e.key === 'Enter') {
          onValueChange?.(valueRef.current, valueRef.current);
        }
      }}
      autoFocus
      label={label}
      placeholder={placeholder}
    />
  );
};
