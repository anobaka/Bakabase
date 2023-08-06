import React from 'react';
import SortableMediaLibrary from '@/pages/Category/components/SortableMediaLibrary';
import { closestCorners, DndContext, DragOverlay, MouseSensor, useSensor, useSensors } from "@dnd-kit/core";
import { SortableContext, verticalListSortingStrategy } from "@dnd-kit/sortable";
import SortableTagGroup from "@/pages/Tag/components/SortableTagGroup";
import SortableTag from "@/pages/Tag/components/SortableTag";
import { SortMediaLibrariesInCategory } from "@/sdk/apis";

export default (({ libraries, loadAllMediaLibraries, forceUpdate }) => {

  const sensors = useSensors(
    useSensor(MouseSensor, {
      activationConstraint: {
        distance: 5,
      },
    }),
  );

  console.log('[SortableMediaLibraryList]rendering')

  function handleDragEnd(e) {
    const activeId = e.active.id;
    const overId = e.over.id;
    const oldIndex = libraries.findIndex(c => c.id == activeId);
    const newIndex = libraries.findIndex(c => c.id == overId);

    const ol = libraries[oldIndex];
    libraries.splice(oldIndex, 1);
    libraries.splice(newIndex, 0, ol);
    const newIds = libraries.map((t) => t.id);
    for(let i=0; i<newIds.length; i++) {
      const id = newIds[i];
      const l = libraries.find(a => a.id == id);
      if (l) {
        l.order = i;
      }
    }
    forceUpdate();
    SortMediaLibrariesInCategory({
      model: {
        ids: newIds,
      },
    })
      .invoke((t) => {
        if (!t.code) {
          for (let i = 0; i < libraries.length; i++) {
            libraries[i].order = i;
          }
          loadAllMediaLibraries();
        }
      });
  }

  return (
    <div className={'libraries'}>
      <DndContext
        onDragStart={({ active }) => {
        }}
        sensors={sensors}
        collisionDetection={closestCorners}
        onDragMove={e => {
          // console.log('drag move', e)
        }}
        onDragOver={e => {

        }}
        onDragEnd={(e) => {
          // console.log('drag end', e);
          handleDragEnd(e);
        }}
        onDragCancel={e => {
        }}
      >
        <SortableContext
          items={libraries.map(g => g.id)!}
          strategy={verticalListSortingStrategy}
        >
          {libraries.sort((a, b) => a.order - b.order).map((library, index) => (
            <SortableMediaLibrary
              key={library.id}
              library={library}
              loadAllMediaLibraries={loadAllMediaLibraries}
            />
          ))}
        </SortableContext>
      </DndContext>
    </div>
  );
});
