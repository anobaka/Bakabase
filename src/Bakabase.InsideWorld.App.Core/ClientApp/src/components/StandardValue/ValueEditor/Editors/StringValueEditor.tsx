import { useEffect, useRef, useState } from 'react';
import { useUpdateEffect } from 'react-use';
import type { ValueEditorProps } from '../models';
import { Input } from '@/components/bakaui';
type StringValueEditorProps = ValueEditorProps<string>;

export default ({ initValue, onChange, ...props }: StringValueEditorProps) => {
  useEffect(() => {
    console.log('initialized');
  }, []);

  const valueRef = useRef<string | undefined>(initValue);

  return (
    <Input
      size={'sm'}
      isClearable
      defaultValue={initValue}
      onValueChange={v => valueRef.current = v}
      onBlur={() => {
        onChange?.(valueRef.current);
      }}
      onClear={() => {
        valueRef.current = undefined;
        onChange?.(valueRef.current);
      }}
    />
  );
};
