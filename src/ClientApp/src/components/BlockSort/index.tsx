import React, { useState } from 'react';
import {
  DndContext,
  closestCenter,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors,
} from '@dnd-kit/core';
import {
  arrayMove, horizontalListSortingStrategy,
  SortableContext,
  sortableKeyboardCoordinates,
  verticalListSortingStrategy,
} from '@dnd-kit/sortable';
import { useUpdate } from 'react-use';
import SortableBlock from '@/components/BlockSort/components/SortableBlock';

type Block = {
  id: any;
  name: any;
};

type Props = {
  blocks: Block[];
  onSorted: (ids: any[]) => any;
};

export default ({ blocks, onSorted }: Props) => {
  const [items, setItems] = useState(blocks.map((block) => block.id));
  const sensors = useSensors(
    useSensor(PointerSensor),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
    }),
  );

  // console.log(items);

  return (
    <div className={'flex flex-wrap gap-y-2 gap-x-4'}>
      <DndContext
        sensors={sensors}
        collisionDetection={closestCenter}
        onDragEnd={handleDragEnd}
      >
        <SortableContext
          items={items}
        >
          {items.map(id => (<SortableBlock
            key={id}
            id={id}
            name={blocks.find(b => b.id == id)!.name}
          />))}
        </SortableContext>
      </DndContext>
    </div>
  );

  function handleDragEnd(event) {
    const { active, over } = event;

    // console.log(active, over);

    if (active.id !== over.id) {
      setItems((items) => {
        const oldIndex = items.indexOf(active.id);
        const newIndex = items.indexOf(over.id);

        const newBlocks = arrayMove(items, oldIndex, newIndex);

        onSorted?.(newBlocks);
        return newBlocks;
      });
    }
  }
};
