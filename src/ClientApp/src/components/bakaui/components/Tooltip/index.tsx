import type { TooltipProps as NextUITooltipProps } from "@heroui/react";
import { Tooltip } from "@heroui/react";
import type { ReactNode } from 'react';
import React from 'react';

interface IProps extends NextUITooltipProps{
  content: ReactNode;
  children: React.ReactNode;
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
