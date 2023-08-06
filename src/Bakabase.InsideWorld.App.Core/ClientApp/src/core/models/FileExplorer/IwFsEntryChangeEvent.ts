import type { IwFsEntryChangeType } from '@/sdk/constants';
import type { IwFsEntryTask } from '@/core/models/FileExplorer/Entry';

export default class {
  type: IwFsEntryChangeType;
  path: string;
  prevPath?: string;
  changedAt: string;
  task?: IwFsEntryTask;
}
