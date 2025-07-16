import type { BulkModificationProcessValue } from "@/pages/bulk-modification2/components/BulkModification/models";

export type StringProcessOptions = {
  value?: BulkModificationProcessValue;
  index?: number;
  isOperationDirectionReversed?: boolean;
  isPositioningDirectionReversed?: boolean;
  count?: number;
  find?: string;
};
