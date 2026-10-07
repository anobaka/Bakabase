"use client";

import { useTranslation } from "react-i18next";
import { AiOutlineClockCircle, AiOutlineSwap } from "react-icons/ai";

import { TopicCallout, TopicCards, TopicHeadline, TopicSteps } from "../../components/TopicBlocks";

const k = (key: string) => `helpCenter.collectionMemo.${key}`;
const steps = ["target", "range", "review"].map((id) => ({
  id,
  titleKey: k(`step.${id}.title`),
  descKey: k(`step.${id}.desc`),
}));
const settings = [
  {
    id: "start",
    icon: <AiOutlineClockCircle className="text-lg" />,
    titleKey: k("settings.start.title"),
    descKey: k("settings.start.desc"),
  },
  {
    id: "direction",
    icon: <AiOutlineSwap className="text-lg" />,
    titleKey: k("settings.direction.title"),
    descKey: k("settings.direction.desc"),
  },
];

const CollectionMemoTopic = () => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-4">
      <TopicHeadline introKey={k("intro")} titleKey={k("headline")} />
      <TopicSteps steps={steps} titleKey={k("steps.title")} />
      <TopicCards cards={settings} titleKey={k("settings.title")} />
      <section className="flex flex-col gap-2">
        <h4 className="text-sm font-medium">{t(k("timeline.title"))}</h4>
        <ul className="list-disc space-y-2 pl-5 text-sm text-default-600">
          {["inspect", "drag", "keyboard", "fill", "retry"].map((id) => (
            <li key={id}>{t(k(`timeline.${id}`))}</li>
          ))}
        </ul>
      </section>
      <TopicCallout textKey={k("dates")} />
      <TopicCallout textKey={k("browsingIntegration")} />
    </div>
  );
};

CollectionMemoTopic.displayName = "CollectionMemoTopic";

export default CollectionMemoTopic;
