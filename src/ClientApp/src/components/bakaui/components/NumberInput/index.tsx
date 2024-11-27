import type { InputProps as NextUIInputProps } from '@nextui-org/react';
import { Input } from '@nextui-org/react';
import { forwardRef, useState } from 'react';

type NumberInputProps = {
  value?: number;
  defaultValue?: number;
  onValueChange?: (value: number) => void;
} & Omit<NextUIInputProps, 'onValueChange' | 'value' | 'defaultValue'>;

export default forwardRef<HTMLInputElement, NumberInputProps>(({ onValueChange, value, defaultValue, ...otherProps }, ref) => {
  return (
    <Input
      ref={ref}
      onValueChange={v => {
        const n = Number(v);
        if (!Number.isNaN(n)) {
          onValueChange?.(n);
        }
      }}
      value={value?.toString()}
      defaultValue={defaultValue?.toString()}
      {...otherProps}
    />
  );
});
