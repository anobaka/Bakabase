import { Popover, PopoverContent, PopoverTrigger } from '@nextui-org/react';
import type { OverlayPlacement } from '@/components/bakaui/types';

interface PopoverProps {
  placement?: OverlayPlacement;
  showArrow?: boolean;
  trigger: any;
  children: any;
}

export default ({ trigger, children, ...otherProps }: PopoverProps) => {
  return (
    <Popover {...otherProps}>
      <PopoverTrigger>
        {trigger}
      </PopoverTrigger>
      <PopoverContent>
        {children}
      </PopoverContent>
    </Popover>
  );
};
