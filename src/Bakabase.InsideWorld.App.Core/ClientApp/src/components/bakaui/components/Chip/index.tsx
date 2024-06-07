import type { ChipProps as NextUIChipProps } from '@nextui-org/react';
import { Chip as NextUIChip } from '@nextui-org/react';
import React, { forwardRef } from 'react';


export interface ChipProps extends NextUIChipProps{
  children: React.ReactNode;
  size?: 'sm' | 'md' | 'lg';
  className?: string;
  radius?: 'full' | 'sm' | 'md' | 'lg';
  isDisabled?: boolean;
  variant?: 'solid' | 'bordered' | 'light' | 'flat' | 'faded' | 'shadow' | 'dot';
  color?: 'default' | 'primary' | 'secondary' | 'success' | 'danger' | 'warning';
}

const Chip = forwardRef<any, ChipProps>((props: ChipProps, ref) => {
  return (
    <NextUIChip ref={ref} {...props} />
  );
});

export default Chip;
