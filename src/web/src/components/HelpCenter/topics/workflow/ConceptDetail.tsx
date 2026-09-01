"use client";

import { useTranslation } from "react-i18next";

import { workflowConcepts } from "./concepts";

/** Detail page of one workflow concept, selected from the left navigation. */
const ConceptDetail = ({ conceptId }: { conceptId: string }) => {
  const { t } = useTranslation();
  const concept = workflowConcepts.find((item) => item.id === conceptId);

  if (!concept) {
    return null;
  }

  const base = `helpCenter.workflow.concept.${concept.id}`;

  return (
    <div className="flex flex-col gap-3">
      <div>
        <h3 className="text-lg font-semibold">{t(`${base}.name`)}</h3>
        <p className="text-sm text-default-500">{t(`${base}.short`)}</p>
      </div>

      <p className="text-sm text-default-700 whitespace-pre-line">{t(`${base}.long`)}</p>

      {concept.hasExample && (
        <div className="text-sm text-default-600 bg-default-100 rounded-lg px-3 py-2 whitespace-pre-line">
          {t(`${base}.example`)}
        </div>
      )}
    </div>
  );
};

ConceptDetail.displayName = "ConceptDetail";

export default ConceptDetail;
