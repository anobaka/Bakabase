import type { SubscriptionProviderUI } from "../types";

import React from "react";
import { useTranslation } from "react-i18next";

import { Input } from "@/components/bakaui";
import { ThirdPartyId } from "@/sdk/constants";

export interface VndbDeveloperTarget {
  /** A producer id (p17), or the page URL it was copied from. */
  producer: string;
}

const EMPTY: VndbDeveloperTarget = { producer: "" };

/** Anchored, so a producer id is not read out of the middle of something else. */
const producerIdOf = (value: string): string | undefined =>
  /^(?:https?:\/\/vndb\.org\/)?(p\d+)\/?$/i.exec(value.trim())?.[1]?.toLowerCase();

const Form: React.FC<{
  value: VndbDeveloperTarget;
  onChange: (v: VndbDeveloperTarget) => void;
}> = ({ value, onChange }) => {
  const { t } = useTranslation();

  return (
    <Input
      isRequired
      description={t<string>("subscription.provider.vndb.developer.producer.description")}
      label={t<string>("subscription.provider.vndb.developer.producer.label")}
      placeholder="https://vndb.org/p17"
      value={value.producer}
      onValueChange={(producer) => onChange({ ...value, producer })}
    />
  );
};

const Summary: React.FC<{ target: VndbDeveloperTarget }> = ({ target }) =>
  target?.producer ? (
    <span className="text-default-500 text-xs truncate">{target.producer}</span>
  ) : null;

export const VndbDeveloperUI: SubscriptionProviderUI<VndbDeveloperTarget> = {
  kind: "vndb.developer",
  thirdPartyId: ThirdPartyId.Vndb,
  defaultTarget: () => ({ ...EMPTY }),
  parseTarget: (json: string) => {
    if (!json) return { ...EMPTY };
    try {
      return { producer: (JSON.parse(json) as Partial<VndbDeveloperTarget>).producer ?? "" };
    } catch {
      return { ...EMPTY };
    }
  },
  isValid: (target) => !!producerIdOf(target.producer ?? ""),
  Form,
  Summary,
};
