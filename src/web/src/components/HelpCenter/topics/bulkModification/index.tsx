"use client";

import { useTranslation } from "react-i18next";
import { AiOutlineBulb, AiOutlineDelete, AiOutlineEdit, AiOutlineFunction } from "react-icons/ai";

import { TopicCallout, TopicCards, TopicHeadline, TopicSteps } from "../../components/TopicBlocks";

const k = (key: string) => `helpCenter.bulkModification.${key}`;

const steps = [
  { id: "filter", titleKey: k("step.filter.title"), descKey: k("step.filter.desc") },
  { id: "variables", titleKey: k("step.variables.title"), descKey: k("step.variables.desc") },
  { id: "process", titleKey: k("step.process.title"), descKey: k("step.process.desc") },
  { id: "preview", titleKey: k("step.preview.title"), descKey: k("step.preview.desc") },
];

const valueSources = [
  {
    id: "fixed",
    icon: <AiOutlineEdit className="text-lg" />,
    titleKey: k("valueSource.fixed.title"),
    descKey: k("valueSource.fixed.desc"),
    tone: "bg-primary/10 text-primary",
  },
  {
    id: "variable",
    icon: <AiOutlineFunction className="text-lg" />,
    titleKey: k("valueSource.variable.title"),
    descKey: k("valueSource.variable.desc"),
    tone: "bg-secondary/10 text-secondary",
  },
  {
    id: "delete",
    icon: <AiOutlineDelete className="text-lg" />,
    titleKey: k("valueSource.delete.title"),
    descKey: k("valueSource.delete.desc"),
    tone: "bg-danger/10 text-danger",
  },
];

const BulkModificationTopic = () => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-4">
      <TopicHeadline introKey={k("intro")} titleKey={k("headline")} />

      <TopicSteps steps={steps} titleKey={k("step.title")} />

      <TopicCards
        cards={valueSources}
        columns={3}
        subtitleKey={k("valueSource.subtitle")}
        titleKey={k("valueSource.title")}
      />

      {/* The chained-pipeline idea is the part users most often miss. */}
      <div className="flex flex-col gap-2">
        <div className="text-sm font-medium">{t(k("pipeline.title"))}</div>
        <p className="text-xs text-default-500">{t(k("pipeline.explanation"))}</p>
        <div className="flex flex-col gap-1.5 rounded-lg border border-default-200 bg-default-50 p-3">
          {["example1", "example2", "example3"].map((id) => (
            <div key={id} className="text-xs text-default-600 font-mono">
              {t(k(`pipeline.${id}`))}
            </div>
          ))}
        </div>
      </div>

      <div className="flex flex-col gap-2">
        <div className="text-sm font-medium">{t(k("saveReuse.title"))}</div>
        <ul className="flex flex-col gap-1 list-disc pl-5">
          {["point1", "point2", "point3"].map((id) => (
            <li key={id} className="text-xs text-default-600">
              {t(k(`saveReuse.${id}`))}
            </li>
          ))}
        </ul>
      </div>

      <TopicCallout
        icon={<AiOutlineBulb className="text-sm" />}
        textKey={k("tip")}
        tone="primary"
      />
    </div>
  );
};

BulkModificationTopic.displayName = "BulkModificationTopic";

export default BulkModificationTopic;
