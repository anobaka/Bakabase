"use client";

import { useSortable } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { Chip } from "@heroui/react";

import { Card, CardBody } from "@/components/bakaui";

type Props = {
  id: any;
  name: any;
  idx: number;
};
const SortableBlock = ({ id, name, idx }: Props) => {
  const { attributes, listeners, setNodeRef, transform, transition } =
    useSortable({ id: id });

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
  };

  return (
    <div ref={setNodeRef} style={style} {...attributes} {...listeners}>
      <Card>
        <CardBody>
          <div className={"flex items-center gap-1"}>
            <Chip size={"sm"}>{idx + 1}</Chip>
            {name}
          </div>
        </CardBody>
      </Card>
    </div>
  );
};

SortableBlock.displayName = "SortableBlock";

export default SortableBlock;
