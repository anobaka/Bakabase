import type { PopoverProps as NextUIPopoverProps } from "@heroui/react";
import { Popover, PopoverContent, PopoverTrigger } from "@heroui/react";

interface PopoverProps extends NextUIPopoverProps{
  trigger: any;
  children: any;
  visible?: boolean;
  onVisibleChange?: (visible: boolean) => void;
  closeMode?: ('mask' | 'esc')[];
}

export default ({ trigger, children, visible, closeMode = ['esc', 'mask'], ...otherProps }: PopoverProps) => {
  // console.log(closeMode?.includes('esc'), closeMode?.includes('mask') == true);
  return (
    <Popover
      isOpen={visible}
      shouldCloseOnBlur={closeMode?.includes('esc')}
      shouldCloseOnInteractOutside={() => closeMode?.includes('mask') == true}
      onOpenChange={o => {
        // console.log(o, 555);
        otherProps.onVisibleChange?.(o);
      }}
      // style={{
      //   zIndex: 0,
      // }}
      {...otherProps}
    >
      <PopoverTrigger>
        {trigger}
      </PopoverTrigger>
      <PopoverContent>
        {children}
      </PopoverContent>
    </Popover>
  );
};
