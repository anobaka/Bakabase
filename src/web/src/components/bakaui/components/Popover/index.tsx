"use client";

import type { PopoverProps as NextUIPopoverProps } from "@heroui/react";

import { forwardRef } from "react";
import { Popover as HeroPopover, PopoverContent as HeroPopoverContent, PopoverTrigger as HeroPopoverTrigger } from "@heroui/react";

interface PopoverProps extends Omit<NextUIPopoverProps, "ref"> {
  trigger: any;
  children: any;
  visible?: boolean;
  onVisibleChange?: (visible: boolean) => void;
  closeMode?: ("mask" | "esc")[];
}

const PopoverComponent = forwardRef<HTMLDivElement, PopoverProps>(
  (
    { trigger, children, visible, closeMode = ["esc", "mask"], ...otherProps },
    ref,
  ) => {
    // console.log(closeMode?.includes('esc'), closeMode?.includes('mask') == true);
    return (
      <HeroPopover
        isOpen={visible}
        shouldCloseOnBlur={closeMode?.includes("esc")}
        shouldCloseOnInteractOutside={() => closeMode?.includes("mask") == true}
        // style={{
        //   zIndex: 0,
        // }}
        {...otherProps}
        ref={ref}
      >
        <HeroPopoverTrigger>{trigger}</HeroPopoverTrigger>
        <HeroPopoverContent>{children}</HeroPopoverContent>
      </HeroPopover>
    );
  },
);

PopoverComponent.displayName = "Popover";

export default PopoverComponent;
