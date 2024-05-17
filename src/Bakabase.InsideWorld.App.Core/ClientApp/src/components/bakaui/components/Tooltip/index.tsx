import type { TooltipProps as NextUITooltipProps } from '@nextui-org/react';
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

type Color = 'default' |
  'foreground' |
  'primary' |
  'secondary' |
  'success' |
  'warning' |
  'danger';

interface IProps extends NextUITooltipProps{
  content: ReactNode;
  children: React.ReactNode;
  placement?: TooltipPlacement;
  color?: Color;
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
