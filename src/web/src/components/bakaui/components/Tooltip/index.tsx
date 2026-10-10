import type { TooltipProps as NextUITooltipProps } from "@heroui/react";
import type { ReactNode } from "react";

import { Tooltip as HeroTooltip } from "@heroui/react";
import React, { useState } from "react";

interface IProps extends NextUITooltipProps {
  content: ReactNode;
  children: React.ReactNode;
}
const Tooltip = (props: IProps) => {
  const [observedOpen, setObservedOpen] = useState(!!props.defaultOpen);
  const isOpen = !props.isDisabled && (props.isOpen ?? observedOpen);
  const trigger =
    React.isValidElement(props.children) && props.children.type !== React.Fragment
      ? React.cloneElement(props.children as React.ReactElement<Record<string, unknown>>, {
          // Hover-only controls must keep their anchor visible while the pointer
          // moves into a tooltip portaled outside their CSS hover group.
          "data-bakabase-tooltip-open": isOpen ? "true" : undefined,
        })
      : props.children;

  return (
    <HeroTooltip
      showArrow
      // Exit animations retain overlays after their anchor is hidden/unmounted,
      // which can briefly relocate them to the viewport origin.
      disableAnimation
      {...props}
      onOpenChange={(open) => {
        setObservedOpen(open);
        props.onOpenChange?.(open);
      }}
    >
      {trigger}
    </HeroTooltip>
  );
};

Tooltip.displayName = "Tooltip";

export default Tooltip;
