import { useState } from 'react';
import { useUpdateEffect } from 'react-use';
import type { InputProps } from '@/components/bakaui';
import { Chip, Input } from '@/components/bakaui';

type Props = {
  value?: string[];
  onValueChange?: (value: string[]) => any;
  inputProps?: Omit<InputProps, 'value' | 'onValueChange'>;
};

export default (props: Props) => {
  const {
    value: propsValue,
    onValueChange,
    inputProps,
  } = props;

  const [value, setValue] = useState(propsValue ?? []);

  useUpdateEffect(() => {
    setValue(propsValue ?? []);
  }, [propsValue]);

  return (
    <div>
      <Input
        {...inputProps}
        value={value?.join(' ')}
        onValueChange={v => {
          const values = v.split(' ').map(x => x.trim()).filter(x => x);
          setValue(values);
          onValueChange?.(values);
        }}
      />
      {value && value.length > 0 && (
        <div className={'flex items-center gap-1 flex-wrap'}>
          {value.map(v => {
            return (
              <Chip size={'sm'} radius={'sm'}>
                {v}
              </Chip>
            );
          })}
        </div>
      )}
    </div>
  );
};
