import type { SubscriptionProviderUI } from "../types";
import type { BangumiSubjectRelationsTarget } from "./types";

import Form from "./Form";
import Summary from "./Summary";

import { ThirdPartyId } from "@/sdk/constants";

const EMPTY: BangumiSubjectRelationsTarget = { subject: "", relations: [], includeSelf: true };

/** A subject id, however the user gave it — the number, or the page they copied. */
const subjectIdOf = (subject: string): string | undefined => {
  const trimmed = subject.trim();

  if (/^\d+$/.test(trimmed)) return trimmed;

  return /subject\/(\d+)/i.exec(trimmed)?.[1];
};

export const BangumiSubjectRelationsUI: SubscriptionProviderUI<BangumiSubjectRelationsTarget> = {
  kind: "bangumi.subjectRelations",
  thirdPartyId: ThirdPartyId.Bangumi,
  defaultTarget: () => ({ ...EMPTY }),
  parseTarget: (json: string) => {
    if (!json) return { ...EMPTY };
    try {
      const parsed = JSON.parse(json) as Partial<BangumiSubjectRelationsTarget>;

      return {
        subject: parsed.subject ?? "",
        relations: parsed.relations ?? [],
        includeSelf: parsed.includeSelf ?? true,
      };
    } catch {
      return { ...EMPTY };
    }
  },
  isValid: (target) => !!subjectIdOf(target.subject ?? ""),
  Form,
  Summary,
};
