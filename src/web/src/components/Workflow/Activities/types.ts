import type React from "react";

import type { WorkflowActivityCategory } from "@/sdk/constants";

/**
 * Per-activity UI bundle, mirroring the SubscriptionProvider registry pattern.
 * The backend registry decides what's *compatible* (per trigger); this frontend
 * registry decides what's *renderable*.
 */
export interface WorkflowActivityUI<TConfig = unknown> {
  kind: string;
  /**
   * i18n key for the activity's display name. The server ships an English fallback in
   * its descriptor; the editor / picker / runs-drawer prefer this key so non-English
   * locales render correctly. `displayName` (below) is kept only as a hard override.
   */
  displayNameKey?: string;
  /** Display name used by the picker / card header. Falls back to the server's value. */
  displayName?: string;
  category: WorkflowActivityCategory;
  defaultConfig: () => TConfig;
  parseConfig: (json: string) => TConfig;
  serializeConfig: (config: TConfig) => string;
  isValid: (config: TConfig) => boolean;
  /**
   * For AdaptToNext activities only — mirrors the backend's
   * IWorkflowActivity.ResolveAdaptedOutputType. Default behavior (returning
   * `nextActivityRequiredType`) is built into chainWalk, so most activities can omit this.
   */
  resolveAdaptedOutputType?: (configJson: string, nextActivityRequiredType: string | null) => string | null;
  ConfigForm: React.FC<{ value: TConfig; onChange: (v: TConfig) => void }>;
  Summary: React.FC<{ config: TConfig }>;
}
