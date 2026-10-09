import type { Entry } from "@/core/models/FileExplorer/Entry";

import { IwFsType } from "@/sdk/constants";

/** Drive roots and passive directory headers are navigation targets, not delete targets. */
export const canDeleteEntry = (entry: Entry): boolean =>
  !!entry.path && !entry.isDrive && !entry.passive && entry.type !== IwFsType.Invalid;
