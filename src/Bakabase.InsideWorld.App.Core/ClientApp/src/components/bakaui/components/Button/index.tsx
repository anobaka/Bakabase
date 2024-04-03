import { Button } from '@nextui-org/react';
import type { ReactNode } from 'react';
import type * as react from 'react';

export interface ButtonProps extends React.ComponentPropsWithoutRef<'button'>{
  size?: 'sm' | 'md' | 'lg';
  color: 'default' | 'primary' | 'secondary' | 'success' | 'danger' | 'warning';
  variant?: 'solid' | 'faded' | 'bordered' | 'light' | 'flat' | 'shadow';
  children: ReactNode;
  isIconOnly?: boolean;
  startContent?: any;
  endContent?: any;
}

export default (props: ButtonProps) => {
  return (
    <Button
      {...props}
    />
  );
};
