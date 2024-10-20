import type { ButtonProps as NextUIButtonProps } from '@nextui-org/react';
import { Button as NextUiButton, ButtonGroup } from '@nextui-org/react';
import { forwardRef } from 'react';
import type * as react from 'react';
import type { ReactRef } from '@nextui-org/react-utils';

interface ButtonProps extends Omit<NextUIButtonProps, 'size' | 'ref'>{
  size?: 'sm' | 'md' | 'lg' | 'small' | 'medium' | 'large';
}

const Button = forwardRef<HTMLButtonElement, ButtonProps>((props, ref: ReactRef<HTMLButtonElement | null>) => {
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
