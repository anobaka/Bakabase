import { Input } from '@nextui-org/react';
import type * as react from 'react';
import type { Color } from '../../types';

interface IProps extends Omit<React.ComponentPropsWithRef<'input'>, 'size' | 'value'>{
  startContent?: React.ReactNode;
  endContent?: React.ReactNode;
  placeholder?: string;
  className?: any;
  label?: react.ReactNode;
  size?: 'sm' | 'md' | 'lg';
  value?: string;
  defaultValue?: string;
  color?: Color;
  onValueChange?: (value: string) => any;
}

export default (props: IProps) => {
  return (
    <Input {...props} />
  );
};
