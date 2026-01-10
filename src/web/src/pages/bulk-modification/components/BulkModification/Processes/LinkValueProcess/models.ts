import type { BulkModificationProcessValue } from "@/pages/bulk-modification/components/BulkModification/models";
import type { StringProcessOptions } from "../StringValueProcess/models";
import type { BulkModificationStringProcessOperation } from "@/sdk/constants";

export type LinkProcessOptions = {
  value?: BulkModificationProcessValue;
  text?: BulkModificationProcessValue;
  url?: BulkModificationProcessValue;
  stringOperation?: BulkModificationStringProcessOperation;
  stringOptions?: StringProcessOptions;
};
