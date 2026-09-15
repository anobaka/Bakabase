"use client";

import { useTranslation } from "react-i18next";

import { subscriptionConcepts } from "./concepts";

const ConceptDetail = ({ conceptId }: { conceptId: string }) => {
  const { t } = useTranslation();
  const concept = subscriptionConcepts.find((item) => item.id === conceptId);

  if (!concept) return null;

  const base = `helpCenter.subscription.concept.${concept.id}`;

  return (
    <div className="flex flex-col gap-3">
      <div>
        <h3 className="text-lg font-semibold">{t(concept.labelKey)}</h3>
        <p className="text-sm text-default-500">{t(`${base}.short`)}</p>
      </div>
      <p className="whitespace-pre-line text-sm text-default-700">{t(`${base}.long`)}</p>
      <div className="whitespace-pre-line rounded-lg bg-default-100 px-3 py-2 text-sm text-default-600">
        {t(`${base}.example`)}
      </div>
    </div>
  );
};

export default ConceptDetail;
