export interface AiTransformConfig {
  /** Pinned target item type. Empty = inherit from the next activity's accepted type. */
  targetItemType: string;
  /** Extra guidance appended to the LLM system prompt. */
  extraInstructions: string;
}
