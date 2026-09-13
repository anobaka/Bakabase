"use client";

import { useTranslation } from "react-i18next";

import { collectionConcepts } from "./concepts";

const ConceptDetail = ({ conceptId }: { conceptId: string }) => {
  const { t } = useTranslation();
  const concept = collectionConcepts.find((item) => item.id === conceptId);

  if (!concept) return null;

  const base = `helpCenter.collection.concept.${concept.id}`;

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
