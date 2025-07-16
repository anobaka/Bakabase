import type { IwFsEntryChangeType } from "@/sdk/constants";

export default class {
  type: IwFsEntryChangeType;
  path: string;
  prevPath?: string;
  changedAt: string;
}
