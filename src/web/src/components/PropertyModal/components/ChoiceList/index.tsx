"use client";

import type { IChoice } from "@/components/Property/models";

import ReferenceListEditor from "./ReferenceListEditor";
import { choicesFromText } from "./helpers";
import { SortableChoice } from "./components/SortableChoice";

import { uuidv4 } from "@/components/utils";

interface Props {
  choices?: IChoice[];
  onChange?: (choices: IChoice[]) => void;
  className?: string;
  checkUsage?: (value: string) => Promise<number>;
}

export default function ChoiceList({ choices = [], onChange, className, checkUsage }: Props) {
  return (
    <ReferenceListEditor
      className={className}
      createItem={() => ({ value: uuidv4(), label: "" })}
      fromText={choicesFromText}
      itemText={(choice) => choice.label ?? ""}
      items={choices}
      kind="choices"
      renderItem={({ item, compact, style, onAdd, onChange, onRemove }) => (
        <SortableChoice
          key={item.value}
          checkUsage={checkUsage}
          choice={item}
          compact={compact}
          id={item.value}
          style={style}
          onChange={onChange}
          onEnterKeyDown={onAdd}
          onRemove={onRemove}
        />
      )}
      onChange={onChange}
    />
  );
}
