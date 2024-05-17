import type { ButtonProps as NextUIButtonProps } from '@nextui-org/react';
import { Button as NextUiButton, ButtonGroup } from '@nextui-org/react';
import type { ReactNode } from 'react';
import { forwardRef } from 'react';
import type * as react from 'react';

interface ButtonProps extends React.ComponentPropsWithoutRef<'button'>{
  size?: 'sm' | 'md' | 'lg' | 'small' | 'medium' | 'large';
  color?: 'default' | 'primary' | 'secondary' | 'success' | 'danger' | 'warning';
  variant?: 'solid' | 'faded' | 'bordered' | 'light' | 'flat' | 'shadow';
  children?: ReactNode;
  isIconOnly?: boolean;
  startContent?: any;
  endContent?: any;
  isLoading?: boolean;
}

const Button = forwardRef((props: ButtonProps, ref: react.Ref<HTMLButtonElement>) => {
  let nSize: NextUIButtonProps['size'];

  if (props.size) {
    switch (props.size) {
      case 'sm':
      case 'md':
      case 'lg':
        nSize = props.size;
        break;
      case 'small':
        nSize = 'sm';
        break;
      case 'medium':
        nSize = 'md';
        break;
      case 'large':
        nSize = 'lg';
        break;
    }
  }

  return (
    <NextUiButton
      ref={ref}
      isDisabled={props.disabled}
      {...props}
      size={nSize}
    />
  );
});

export {
  Button,
  ButtonGroup,
  ButtonProps,
};
