import type { Entry } from '@/core/models/FileExplorer/Entry';

export type FileSystemSelectorProps = {
  startPath?: string;
  targetType?: 'file' | 'folder';
  onSelected?: (entry: Entry) => any;
  onCancel?: () => any;
  filter?: (entry: Entry) => boolean;
  defaultSelectedPath?: string;
};
