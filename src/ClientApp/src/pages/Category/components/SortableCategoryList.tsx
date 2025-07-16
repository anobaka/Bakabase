import React from 'react';
import { closestCorners, DndContext, DragOverlay, MouseSensor, useSensor, useSensors } from '@dnd-kit/core';
import { SortableContext, verticalListSortingStrategy } from '@dnd-kit/sortable';
import SortableCategory from '@/pages/Category/components/SortableCategory';
import BApi from '@/sdk/BApi';

type Props = {
  categories: any[];
  libraries: any[];
  loadAllMediaLibraries: () => void;
  loadAllCategories: () => void;
  allComponents: any[];
  forceUpdate: () => void;
  enhancers: any[];
  reloadCategory: (id: number) => any;
  reloadMediaLibrary: (id: number) => any;
};

export default (({ categories, libraries,
  loadAllMediaLibraries, loadAllCategories, allComponents, forceUpdate, enhancers, reloadCategory, reloadMediaLibrary }: Props) => {
  const sensors = useSensors(
    useSensor(MouseSensor, {
      activationConstraint: {
        distance: 5,
      },
    }),
  );

  async function onDragEnd(e) {
    const activeId = e.active.id;
    const overId = e.over.id;
    const oldIndex = categories.findIndex(c => c.id == activeId);
    const newIndex = categories.findIndex(c => c.id == overId);
    const oc = categories[oldIndex];
    categories.splice(oldIndex, 1);
    categories.splice(newIndex, 0, oc);
    forceUpdate();
    const newIds = categories.map((t) => t.id);
    await BApi.category.sortCategories({
      ids: newIds,
    }).then((t) => {
      if (!t.code) {
        for (let i = 0; i < categories.length; i++) {
          categories[i].order = i;
        }
        categories.sort((a, b) => a.order - b.order);
        loadAllCategories();
      }
    });
  }

  // console.log('[SortableCategoryList]rendering');

  return (
    <div className="categories">
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
          console.log('drag end', e);
          onDragEnd(e);
        }}
        onDragCancel={e => {
        }}
      >
        <SortableContext
          items={categories.map(g => g.id)!}
          strategy={verticalListSortingStrategy}
        >
          {categories.map((c, index) => {
            const mls = libraries.filter((t) => t.categoryId == c.id).sort((a, b) => a.order - b.order);
            return (
              <SortableCategory
                forceUpdate={forceUpdate}
                category={c}
                key={c.id}
                loadAllMediaLibraries={loadAllMediaLibraries}
                loadAllCategories={loadAllCategories}
                reloadCategory={reloadCategory}
                reloadMediaLibrary={reloadMediaLibrary}
                libraries={mls}
                allComponents={allComponents}
                enhancers={enhancers}
              />
            );
          })}
        </SortableContext>
      </DndContext>
    </div>
  );
});
