/**
 * Mirrors ResourceMaterializedTrigger.Filter (backend).
 * Empty array = every resource that gains local files; populated narrows to resources carrying
 * an identity on one of those platforms.
 */
export interface ResourceMaterializedFilter {
  sources: number[];
}
