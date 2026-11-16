export type TaskTagKind = "attentionTags" | "facts";

export function taskTag(kind: TaskTagKind, id: string) {
  const isFact = kind === "facts";
  return {
    kind,
    id,
    label: isFact ? `acquisition.filters.fact.${id}` : `acquisition.attention.tag.${id}`,
    hint: isFact ? `acquisition.filters.hint.${id}` : `acquisition.attention.hint.${id}`,
  };
}
