import { Input } from '@nextui-org/react';
import type * as react from 'react';

interface IProps extends Omit<React.ComponentPropsWithRef<'input'>, 'size' | 'value'>{
  startContent?: React.ReactNode;
  endContent?: React.ReactNode;
  placeholder?: string;
  className?: any;
  label?: react.ReactNode;
  size?: 'sm' | 'md' | 'lg';
  value?: string;
  defaultValue?: string;
}

export default (props: IProps) => {
  return (
    <Input color {...props} />
  );
};
