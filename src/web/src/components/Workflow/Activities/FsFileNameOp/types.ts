import type { BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation } from "@/sdk/Api";

export type FileNameModifierOperationModel =
  BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation;

/**
 * The config is the file-name-modifier page's own operation model, verbatim — the two UIs share
 * the OperationCard editor, so the rule engine has exactly one config shape everywhere.
 */
export interface FsFileNameOpConfig {
  operations: FileNameModifierOperationModel[];
}
