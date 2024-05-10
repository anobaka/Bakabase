import { useRef } from 'react';
import type { ValueEditorProps } from '../models';
import { Input } from '@/components/bakaui';

type NumberValueEditorProps = ValueEditorProps<number>;

export default ({ initValue, onChange, ...props }: NumberValueEditorProps) => {
  const valueRef = useRef(initValue);

  return (
    <Input
      defaultValue={valueRef.current?.toString()}
      onValueChange={v => {
        const nStr = v?.match(/[\d,]+(\.\d+)?/)?.[0];
        const n = Number(nStr);
        const r = Number.isNaN(n) ? n : undefined;
        valueRef.current = r;
      }}
      onBlur={() => {
        onChange?.(valueRef.current);
      }}
    />
  );
};
