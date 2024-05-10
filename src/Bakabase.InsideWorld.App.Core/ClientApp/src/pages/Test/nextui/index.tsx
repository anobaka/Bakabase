import React from 'react';
import { Button, Popover, PopoverContent, PopoverTrigger } from '@nextui-org/react';

export default () => {
  return (
    <div>
      <Popover
        placement="right"
        isKeyboardDismissDisabled
        isDismissable
        shouldCloseOnBlur
      >
        <PopoverTrigger>
          <Button>Open Popover</Button>
        </PopoverTrigger>
        <PopoverContent>
          <div className="px-1 py-2">
            <div className="text-small font-bold">Popover Content</div>
            <div className="text-tiny">This is the popover content</div>
          </div>
        </PopoverContent>
      </Popover>
    </div>
  );
};
