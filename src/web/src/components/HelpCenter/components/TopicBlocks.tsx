"use client";

import type { ReactNode } from "react";

import { useTranslation } from "react-i18next";

/**
 * Presentational building blocks shared by the simpler help topics.
 *
 * The pathMark and workflow topics each hand-roll their layout because they carry
 * bespoke diagrams. Topics migrated from the old guide modals are plain prose plus
 * lists, so they share these instead of triplicating the same markup.
 */

export const TopicHeadline = ({ titleKey, introKey }: { titleKey: string; introKey: string }) => {
  const { t } = useTranslation();

  return (
    <div>
      <h3 className="text-lg font-semibold mb-1">{t(titleKey)}</h3>
      <p className="text-sm text-default-600">{t(introKey)}</p>
    </div>
  );
};

export interface TopicStep {
  id: string;
  titleKey: string;
  descKey: string;
}

/** Numbered steps, used for the "how you actually use this" sections. */
export const TopicSteps = ({ titleKey, steps }: { titleKey?: string; steps: TopicStep[] }) => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-2">
      {titleKey && <div className="text-sm font-medium">{t(titleKey)}</div>}
      <ol className="flex flex-col gap-2">
        {steps.map((step, index) => (
          <li
            key={step.id}
            className="flex items-start gap-3 rounded-lg border border-default-200 bg-default-50 p-3"
          >
            <span className="shrink-0 w-6 h-6 rounded-full bg-primary/10 text-primary text-xs font-semibold flex items-center justify-center">
              {index + 1}
            </span>
            <div className="min-w-0">
              <div className="text-sm font-medium">{t(step.titleKey)}</div>
              <div className="text-xs text-default-500 mt-0.5">{t(step.descKey)}</div>
            </div>
          </li>
        ))}
      </ol>
    </div>
  );
};

export interface TopicCard {
  id: string;
  icon: ReactNode;
  titleKey: string;
  descKey: string;
  /** Tailwind classes for the icon chip; defaults to the primary tint. */
  tone?: string;
}

/** A responsive grid of icon + title + description cards. */
export const TopicCards = ({
  titleKey,
  subtitleKey,
  cards,
  columns = 2,
}: {
  titleKey?: string;
  subtitleKey?: string;
  cards: TopicCard[];
  columns?: 1 | 2 | 3;
}) => {
  const { t } = useTranslation();
  const gridClass =
    columns === 1 ? "grid-cols-1" : columns === 3 ? "sm:grid-cols-3" : "sm:grid-cols-2";

  return (
    <div className="flex flex-col gap-2">
      {titleKey && <div className="text-sm font-medium">{t(titleKey)}</div>}
      {subtitleKey && <div className="text-xs text-default-500 -mt-1">{t(subtitleKey)}</div>}
      <div className={`grid grid-cols-1 ${gridClass} gap-2`}>
        {cards.map((card) => (
          <div
            key={card.id}
            className="flex items-start gap-2.5 rounded-lg border border-default-200 bg-default-50 p-3"
          >
            <span
              className={`shrink-0 w-8 h-8 rounded-lg flex items-center justify-center ${
                card.tone ?? "bg-primary/10 text-primary"
              }`}
            >
              {card.icon}
            </span>
            <div className="min-w-0">
              <div className="text-sm font-medium">{t(card.titleKey)}</div>
              <div className="text-xs text-default-500 mt-0.5">{t(card.descKey)}</div>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
};

/** A short aside — the "tip" / "note" line the old guide modals ended their steps with. */
export const TopicCallout = ({
  icon,
  textKey,
  tone = "default",
}: {
  icon?: ReactNode;
  textKey: string;
  tone?: "default" | "primary";
}) => {
  const { t } = useTranslation();
  const toneClass =
    tone === "primary"
      ? "border-primary/20 bg-primary/5 text-primary"
      : "border-default-200 bg-default-100 text-default-600";

  return (
    <div className={`flex items-start gap-2 rounded-lg border p-3 text-xs ${toneClass}`}>
      {icon && <span className="shrink-0 mt-0.5">{icon}</span>}
      <span>{t(textKey)}</span>
    </div>
  );
};
