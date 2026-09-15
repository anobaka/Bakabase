import type { IChoice, Tag } from "@/components/Property/models";

import { arrayMove } from "@dnd-kit/sortable";

import { uuidv4 } from "@/components/utils";

export function moveReferenceValue<T extends { value: string }>(
  items: T[],
  activeId: string | number,
  overId?: string | number,
): T[] {
  const from = items.findIndex((item) => item.value === activeId);
  const to = items.findIndex((item) => item.value === overId);

  return from < 0 || to < 0 || from === to ? items : arrayMove(items, from, to);
}

export const tagText = (tag: Tag) =>
  tag.group ? `${tag.group}:${tag.name ?? ""}` : (tag.name ?? "");

export const sortReferenceValues = <T>(items: T[], itemText: (item: T) => string): T[] =>
  [...items].sort((a, b) => itemText(a).localeCompare(itemText(b)));

// Reuse saved objects so bulk editing preserves IDs, colors and hidden flags.
// A repeated line must not create repeated IDs in the sortable list.
export function choicesFromText(choices: IChoice[], text: string): IChoice[] {
  return [
    ...new Set(
      text
        .split("\n")
        .map((line) => line.trim())
        .filter(Boolean),
    ),
  ].map((label) => choices.find((choice) => choice.label === label) ?? { label, value: uuidv4() });
}

export function tagsFromText(tags: Tag[], text: string): Tag[] {
  return [
    ...new Set(
      text
        .split("\n")
        .map((line) => line.trim())
        .filter(Boolean),
    ),
  ].map((line) => {
    const saved = tags.find((tag) => tagText(tag) === line);

    if (saved) return saved;
    const separator = line.indexOf(":");

    return {
      value: uuidv4(),
      group: separator < 0 ? undefined : line.slice(0, separator),
      name: separator < 0 ? line : line.slice(separator + 1),
    };
  });
}
