import { useEffect, useState } from 'react';
import { useUpdateEffect } from 'react-use';
import type { ValueEditorProps } from '../models';
import { Input } from '@/components/bakaui';
type StringValueEditorProps = ValueEditorProps<string>;

export default ({ initValue, onChange, ...props }: StringValueEditorProps) => {
  const [value, setValue] = useState(initValue);

  useEffect(() => {
    console.log('initialized');
  }, []);

  useUpdateEffect(() => {
    setValue(initValue);
  }, [initValue]);

  return (
    <Input
      size={'sm'}
      value={initValue}
      onValueChange={v => setValue(v)}
      onBlur={() => {
        onChange?.(value);
      }}
    />
  );
};
