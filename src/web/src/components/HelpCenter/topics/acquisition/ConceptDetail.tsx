"use client";

import { AiOutlineBulb } from "react-icons/ai";

import { TopicCallout, TopicFlow, TopicHeadline, TopicSteps } from "../../components/TopicBlocks";

import { acquisitionConcepts } from "./concepts";

const k = (key: string) => `helpCenter.acquisition.${key}`;

const directoryFlow = ["inbox", "working", "library"].map((id) => ({
  id,
  titleKey: k(`directory.${id}.title`),
  descKey: k(`directory.${id}.desc`),
}));

const conceptSteps: Record<string, string[]> = {
  inbox: ["configure", "transfer", "claim"],
  workflow: ["choose", "save", "run"],
  lead: ["record", "check", "acquire"],
  task: ["start", "wait", "finish"],
};

const AcquisitionConceptDetail = ({ conceptId }: { conceptId: string }) => {
  if (!acquisitionConcepts.some((concept) => concept.id === conceptId)) return null;

  const base = `concept.${conceptId}`;
  const steps = conceptSteps[conceptId].map((id) => ({
    id,
    titleKey: k(`${base}.${id}.title`),
    descKey: k(`${base}.${id}.desc`),
  }));

  return (
    <div className="flex flex-col gap-4">
      <TopicHeadline introKey={k(`${base}.intro`)} titleKey={k(`${base}.name`)} />
      {conceptId === "inbox" && <TopicFlow steps={directoryFlow} titleKey={k("directory.title")} />}
      <TopicSteps steps={steps} />
      <TopicCallout icon={<AiOutlineBulb />} textKey={k(`${base}.note`)} tone="primary" />
    </div>
  );
};

AcquisitionConceptDetail.displayName = "AcquisitionConceptDetail";

export default AcquisitionConceptDetail;
