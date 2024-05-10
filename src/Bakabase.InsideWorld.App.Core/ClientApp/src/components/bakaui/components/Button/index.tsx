import { Button as NextUiButton } from '@nextui-org/react';
import type { ReactNode } from 'react';
import { forwardRef } from 'react';
import type * as react from 'react';

export interface ButtonProps extends React.ComponentPropsWithoutRef<'button'>{
  size?: 'sm' | 'md' | 'lg';
  color?: 'default' | 'primary' | 'secondary' | 'success' | 'danger' | 'warning';
  variant?: 'solid' | 'faded' | 'bordered' | 'light' | 'flat' | 'shadow';
  children?: ReactNode;
  isIconOnly?: boolean;
  startContent?: any;
  endContent?: any;
  isLoading?: boolean;
}

const Button = forwardRef((props: ButtonProps, ref: react.Ref<HTMLButtonElement>) => {
  return (
    <NextUiButton
      ref={ref}
      isDisabled={props.disabled}
      {...props}
    />
  );
});

export default Button;
