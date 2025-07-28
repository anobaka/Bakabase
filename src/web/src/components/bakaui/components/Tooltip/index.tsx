import type { TooltipProps as NextUITooltipProps } from "@heroui/react";
import type { ReactNode } from "react";

import { Tooltip as HeroTooltip } from "@heroui/react";
import React from "react";

interface IProps extends NextUITooltipProps {
  content: ReactNode;
  children: React.ReactNode;
}
const Tooltip = (props: IProps) => {
  return (
    <HeroTooltip showArrow {...props}>
      {props.children}
    </HeroTooltip>
  );
};

Tooltip.displayName = "Tooltip";

export default Tooltip;
