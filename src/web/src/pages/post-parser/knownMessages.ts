import type { TFunction } from "i18next";

const knownMessages: Record<string, string> = {
  "Some content remains unpurchased; it was confirmed unnecessary for this acquisition scope.":
    "postParser.warning.CONTENT_UNLOCK_NOT_REQUIRED",
  "No supported evidence resolves this restricted block.":
    "postParser.lockedContent.reason.insufficientEvidence",
  "The source-specific evidence does not establish that this block is optional.":
    "postParser.lockedContent.reason.optionalityUnproven",
  POST_AVAILABILITY_EVIDENCE_INSUFFICIENT: "postParser.message.availabilityEvidenceInsufficient",
  POST_CONTENT_REVIEW_CHANGED: "postParser.contentPurchaseReview.wait.reviewChanged",
  POST_CONTENT_REVIEW_EVIDENCE_CHANGED: "postParser.contentPurchaseReview.wait.evidenceChanged",
  POST_CONTENT_REVIEW_LIMITS_CHANGED: "postParser.contentPurchaseReview.wait.limitsChanged",
  POST_CONTENT_REVIEW_PURCHASE_UNCERTAIN: "postParser.contentPurchaseReview.wait.purchaseUncertain",
};

/** Translate only known machine codes; preserve original site and analysis evidence verbatim. */
export function translateKnownMessage(t: TFunction, message: string): string {
  return knownMessages[message] ? t<string>(knownMessages[message]) : message;
}
