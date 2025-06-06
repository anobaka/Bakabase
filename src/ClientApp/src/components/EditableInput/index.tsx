import React from 'react';
import { Input, Snippet } from '@/components/bakaui';
import EditableValue from '@/components/EditableValue';

type Props = {
  value?: string;
  onSubmit?: (value?: string) => any | Promise<any>;
  size?: 'sm' | 'md' | 'lg' | undefined;
  className?: string;
};

export default ({
                  value,
                  onSubmit,
                  size,
                  className,
                }: Props) => {
  return (
    <EditableValue
      Viewer={({
                 size,
                 value,
               }) => <Snippet symbol={<>&nbsp;</>} size={size}>{value}</Snippet>}
      Editor={({
                 size,
                 value,
                 onValueChange,
                 className,
               }) => (
                 <Input
                   size={size}
                   value={value}
                   onValueChange={onValueChange}
                   className={className}
                 />)}
      size={size}
      value={value}
      onSubmit={onSubmit}
      className={className}
    />
  );
};
