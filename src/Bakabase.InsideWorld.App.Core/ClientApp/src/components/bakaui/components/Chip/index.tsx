import type { ChipProps } from '@nextui-org/react';
import { Chip as NextUIChip } from '@nextui-org/react';
import React, { forwardRef } from 'react';


interface IProps extends ChipProps{
  children: React.ReactNode;
  size?: 'sm' | 'md' | 'lg';
  className?: string;
  onClick?: () => any;
  radius?: 'full' | 'sm' | 'md' | 'lg';
  isDisabled?: boolean;
  variant?: 'solid' | 'bordered' | 'light' | 'flat' | 'faded' | 'shadow' | 'dot';
  color?: 'default' | 'primary' | 'secondary' | 'success' | 'danger' | 'warning';
}

const Chip = forwardRef<any, IProps>((props: IProps, ref) => {
  return (
    <NextUIChip ref={ref} {...props} />
  );
});

export default Chip;
