"use client";

import type { Tag } from "@/components/Property/models";

import ReferenceListEditor from "../ChoiceList/ReferenceListEditor";
import { tagsFromText, tagText } from "../ChoiceList/helpers";

import { SortableTag } from "./components/SortableTag";

import { uuidv4 } from "@/components/utils";

interface Props {
  tags?: Tag[];
  onChange?: (tags: Tag[]) => void;
  className?: string;
  checkUsage?: (value: string) => Promise<number>;
}

export default function TagList({ tags = [], onChange, className, checkUsage }: Props) {
  return (
    <ReferenceListEditor
      className={className}
      createItem={() => ({ value: uuidv4() })}
      fromText={tagsFromText}
      itemText={tagText}
      items={tags}
      kind="tags"
      renderItem={({ item, compact, style, onChange, onRemove }) => (
        <SortableTag
          key={item.value}
          checkUsage={checkUsage}
          compact={compact}
          id={item.value}
          style={style}
          tag={item}
          onChange={onChange}
          onRemove={onRemove}
        />
      )}
      onChange={onChange}
    />
  );
}
