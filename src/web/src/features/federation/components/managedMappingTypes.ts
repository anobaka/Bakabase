export interface ManagedMappingTreeNode {
  serverPath: string;
  name: string;
  resourceCount: number;
  hasChildren: boolean;
}
export interface ManagedMappingTreePage {
  libraryEpoch: string;
  nodes: ManagedMappingTreeNode[];
  nextCursor?: string;
}
