import { Tooltip } from '@nextui-org/react';
import type { ReactNode } from 'react';
import React from 'react';

type TooltipPlacement =
  | 'top'
  | 'bottom'
  | 'right'
  | 'left'
  | 'top-start'
  | 'top-end'
  | 'bottom-start'
  | 'bottom-end'
  | 'left-start'
  | 'left-end'
  | 'right-start'
  | 'right-end';

interface IProps {
  content: ReactNode;
  children: React.ReactNode;
  placement?: TooltipPlacement;
}

export default (props: IProps) => {
  return (
    <Tooltip
      showArrow
      {...props}
    >
      {props.children}
    </Tooltip>
  );
};
