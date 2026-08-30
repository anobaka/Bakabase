"use client";

import { useEffect, useRef } from "react";
import { useTranslation } from "react-i18next";

import { pathMarkConcepts } from "./concepts";

import { Accordion, AccordionItem } from "@/components/bakaui";

const ConceptsSection = ({ concept }: { concept?: string }) => {
  const { t } = useTranslation();
  const containerRef = useRef<HTMLDivElement>(null);

  // Scroll the targeted concept into view when the section opens.
  useEffect(() => {
    if (concept && containerRef.current) {
      const target = containerRef.current.querySelector(`[data-concept="${concept}"]`);

      target?.scrollIntoView({ behavior: "smooth", block: "start" });
    }
  }, [concept]);

  return (
    <div ref={containerRef} className="flex flex-col gap-2">
      <p className="text-sm text-default-500">{t("helpCenter.pathMark.concepts.intro")}</p>
      <Accordion
        defaultExpandedKeys={concept ? [concept] : []}
        selectionMode="multiple"
        variant="bordered"
      >
        {pathMarkConcepts.map((item) => {
          const base = `helpCenter.pathMark.concept.${item.id}`;

          return (
            <AccordionItem
              key={item.id}
              aria-label={t(`${base}.name`)}
              data-concept={item.id}
              subtitle={t(`${base}.short`)}
              title={<span className="text-sm font-medium">{t(`${base}.name`)}</span>}
            >
              <div className="flex flex-col gap-2 pb-2 text-sm text-default-600">
                <p>{t(`${base}.long`)}</p>
                {item.hasExample && (
                  <p className="text-xs text-default-500 bg-default-100 rounded px-2 py-1.5">
                    {t(`${base}.example`)}
                  </p>
                )}
              </div>
            </AccordionItem>
          );
        })}
      </Accordion>
    </div>
  );
};

ConceptsSection.displayName = "ConceptsSection";

export default ConceptsSection;
