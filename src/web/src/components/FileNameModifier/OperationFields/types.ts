import type { TFunction } from "i18next";
import type { BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation as Operation } from "@/sdk/Api";

export type { Operation };

export interface OperationFieldsProps {
  operation: Operation;
  t: TFunction;
  onChange: (operation: Operation) => void;
}
