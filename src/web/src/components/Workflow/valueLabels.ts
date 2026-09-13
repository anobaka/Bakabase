import type { TFunction } from "i18next";

import {
  AcquisitionStatus,
  AcquisitionStatusLabel,
  CollectionMembershipOrigin,
  CollectionMembershipOriginLabel,
} from "@/sdk/constants";

const ACQUISITION_STATUS_KEYS: Record<AcquisitionStatus, string> = {
  [AcquisitionStatus.Pending]: "status.pending",
  [AcquisitionStatus.Running]: "status.running",
  [AcquisitionStatus.Waiting]: "status.waiting",
  [AcquisitionStatus.Completed]: "status.completed",
  [AcquisitionStatus.Failed]: "status.failed",
  [AcquisitionStatus.Cancelled]: "status.cancelled",
};

const COLLECTION_MEMBERSHIP_ORIGIN_KEYS: Record<CollectionMembershipOrigin, string> = {
  [CollectionMembershipOrigin.Manual]: "common.label.manual",
  [CollectionMembershipOrigin.Subscription]: "workflow.group.subscription",
};

export function acquisitionStatusLabel(t: TFunction, status: AcquisitionStatus): string {
  const key = ACQUISITION_STATUS_KEYS[status];
  const fallback = AcquisitionStatusLabel[status] ?? String(status);

  return key ? t<string>(key, { defaultValue: fallback }) : fallback;
}

export function collectionMembershipOriginLabel(
  t: TFunction,
  origin: CollectionMembershipOrigin,
): string {
  const key = COLLECTION_MEMBERSHIP_ORIGIN_KEYS[origin];
  const fallback = CollectionMembershipOriginLabel[origin] ?? String(origin);

  return key ? t<string>(key, { defaultValue: fallback }) : fallback;
}
