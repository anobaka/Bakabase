"use client";

import type { PopoverProps as NextUIPopoverProps } from "@heroui/react";
import { forwardRef } from "react";

import { Popover, PopoverContent, PopoverTrigger } from "@heroui/react";

interface PopoverProps extends Omit<NextUIPopoverProps, "ref"> {
  trigger: any;
  children: any;
  visible?: boolean;
  onVisibleChange?: (visible: boolean) => void;
  closeMode?: ("mask" | "esc")[];
}

const PopoverComponent = forwardRef<HTMLDivElement, PopoverProps>(({
  trigger,
  children,
  visible,
  closeMode = ["esc", "mask"],
  ...otherProps
}, ref) => {
  // console.log(closeMode?.includes('esc'), closeMode?.includes('mask') == true);
  return (
    <Popover
      isOpen={visible}
      shouldCloseOnBlur={closeMode?.includes("esc")}
      shouldCloseOnInteractOutside={() => closeMode?.includes("mask") == true}
      // style={{
      //   zIndex: 0,
      // }}
      {...otherProps}
      ref={ref}
    >
      <PopoverTrigger>{trigger}</PopoverTrigger>
      <PopoverContent>
        {children}
      </PopoverContent>
    </Popover>
  );
});

PopoverComponent.displayName = "Popover";

export default PopoverComponent;
