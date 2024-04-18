import { Chip as NextUIChip } from '@nextui-org/react';
import React, { forwardRef } from 'react';


interface IProps {
  children: React.ReactNode;
  size?: 'sm' | 'md' | 'lg';
  className?: string;
  onClick?: () => any;
  radius?: 'full' | 'sm' | 'md' | 'lg';
  isDisabled?: boolean;
  variant?: 'solid' | 'bordered' | 'light' | 'flat' | 'faded' | 'shadow' | 'dot';
}

const Chip = forwardRef<any, IProps>((props: IProps, ref) => {
  return (
    <NextUIChip ref={ref} {...props} />
  );
});

export default Chip;
