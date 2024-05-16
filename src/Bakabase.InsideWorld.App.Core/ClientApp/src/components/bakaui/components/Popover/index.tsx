import { Popover, PopoverContent, PopoverTrigger } from '@nextui-org/react';
import type { OverlayPlacement } from '@/components/bakaui/types';

interface PopoverProps {
  placement?: OverlayPlacement;
  showArrow?: boolean;
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
      style={{
        zIndex: 0,
      }}
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
