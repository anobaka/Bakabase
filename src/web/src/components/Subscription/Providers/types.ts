import type React from "react";

import type { ThirdPartyId } from "@/sdk/constants";

/**
 * Per-provider UI bundle. Keyed by the backend's `kind` string (e.g. "exhentai.search").
 *
 * Form: rendered inside the create/edit modal. Receives the typed target and reports
 * changes back. The modal owns Save; the Form just maintains its local validity.
 *
 * Summary: rendered in the subscription list to describe the target compactly.
 */
export interface SubscriptionProviderUI<T = unknown> {
  kind: string;
  /** The ThirdParty this kind belongs to — drives the first-level picker. */
  thirdPartyId: ThirdPartyId;
  /** Returns an empty target — used when opening the create modal. */
  defaultTarget: () => T;
  /** Parse the backend's opaque TargetJson into the typed shape this provider expects. */
  parseTarget: (targetJson: string) => T;
  /** Local form-level validity (separate from the backend's ValidateTarget). */
  isValid: (target: T) => boolean;
  Form: React.FC<SubscriptionProviderFormProps<T>>;
  Summary: React.FC<SubscriptionProviderSummaryProps<T>>;
}

export interface SubscriptionProviderFormProps<T> {
  value: T;
  onChange: (value: T) => void;
}

export interface SubscriptionProviderSummaryProps<T> {
  target: T;
}
