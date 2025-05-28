import React from 'react';
import { Button, Popover, PopoverContent, PopoverTrigger } from '@heroui/react';
import { Select, SelectItem } from '@heroui/react';
import { Tooltip } from '@/components/bakaui';


export const animals = [
  { key: 'cat', label: 'Cat' },
  { key: 'dog', label: 'Dog' },
  { key: 'elephant', label: 'Elephant' },
  { key: 'lion', label: 'Lion' },
  { key: 'tiger', label: 'Tiger' },
  { key: 'giraffe', label: 'Giraffe' },
  { key: 'dolphin', label: 'Dolphin' },
  { key: 'penguin', label: 'Penguin' },
  { key: 'zebra', label: 'Zebra' },
  { key: 'shark', label: 'Shark' },
  { key: 'whale', label: 'Whale' },
  { key: 'otter', label: 'Otter' },
  { key: 'crocodile', label: 'Crocodile' },
];

export default () => {
  return (
    <>
      <div className="flex w-full flex-wrap gap-4">
        <Select fullWidth={false}>
          {animals.map((animal) => (
            <SelectItem key={animal.key}>{animal.label}</SelectItem>
          ))}
        </Select>
        <Select label="Favorite Animal" placeholder="Select an animal" fullWidth={false}>
          {animals.map((animal) => (
            <SelectItem key={animal.key}>{animal.label}</SelectItem>
          ))}
        </Select>
      </div>
      <div>
        {/* <Popover */}
        {/*   placement="right" */}
        {/*   isKeyboardDismissDisabled */}
        {/*   isDismissable */}
        {/*   shouldCloseOnBlur */}
        {/* > */}
        {/*   <PopoverTrigger> */}
        {/*     <Button>Open Popover</Button> */}
        {/*   </PopoverTrigger> */}
        {/*   <PopoverContent> */}
        {/*     <div className="px-1 py-2"> */}
        {/*       <div className="text-small font-bold">Popover Content</div> */}
        {/*       <div className="text-tiny">This is the popover content</div> */}
        {/*     </div> */}
        {/*   </PopoverContent> */}
        {/* </Popover> */}

        {/* <Tooltip */}
        {/*   content={( */}
        {/*     <img src={'http://localhost:5001/resource/56653/cover'} /> */}
        {/*   )} */}
        {/* >Test image in tooltip</Tooltip> */}
      </div>
    </>
  );
};
