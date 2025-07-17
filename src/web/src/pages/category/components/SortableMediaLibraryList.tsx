"use client";

import React from "react";
import {
  closestCorners,
  DndContext,
  MouseSensor,
  useSensor,
  useSensors,
} from "@dnd-kit/core";
import {
  SortableContext,
  verticalListSortingStrategy,
} from "@dnd-kit/sortable";

import SortableMediaLibrary from "@/pages/category/components/SortableMediaLibrary";
import BApi from "@/sdk/BApi";

export default ({
  libraries,
  loadAllMediaLibraries,
  forceUpdate,
  reloadMediaLibrary,
}) => {
  const sensors = useSensors(
    useSensor(MouseSensor, {
      activationConstraint: {
        distance: 5,
      },
    }),
  );

  // console.log('[SortableMediaLibraryList]rendering')

  function handleDragEnd(e) {
    const activeId = e.active.id;
    const overId = e.over.id;
    const oldIndex = libraries.findIndex((c) => c.id == activeId);
    const newIndex = libraries.findIndex((c) => c.id == overId);

    const ol = libraries[oldIndex];

    libraries.splice(oldIndex, 1);
    libraries.splice(newIndex, 0, ol);
    const newIds = libraries.map((t) => t.id);

    for (let i = 0; i < newIds.length; i++) {
      const id = newIds[i];
      const l = libraries.find((a) => a.id == id);

      if (l) {
        l.order = i;
      }
    }
    forceUpdate();
    BApi.mediaLibrary
      .sortMediaLibrariesInCategory({
        ids: newIds,
      })
      .then((t) => {
        if (!t.code) {
          for (let i = 0; i < libraries.length; i++) {
            libraries[i].order = i;
          }
          loadAllMediaLibraries();
        }
      });
  }

  return (
    <div className={"libraries"}>
      <DndContext
        collisionDetection={closestCorners}
        sensors={sensors}
        onDragCancel={(e) => {}}
        onDragEnd={(e) => {
          // console.log('drag end', e);
          handleDragEnd(e);
        }}
        onDragMove={(e) => {
          // console.log('drag move', e)
        }}
        onDragOver={(e) => {}}
        onDragStart={({ active }) => {}}
      >
        <SortableContext
          items={libraries.map((g) => g.id)!}
          strategy={verticalListSortingStrategy}
        >
          {libraries
            .sort((a, b) => a.order - b.order)
            .map((library, index) => (
              <SortableMediaLibrary
                key={library.id}
                library={library}
                loadAllMediaLibraries={loadAllMediaLibraries}
                reloadMediaLibrary={reloadMediaLibrary}
              />
            ))}
        </SortableContext>
      </DndContext>
    </div>
  );
};
