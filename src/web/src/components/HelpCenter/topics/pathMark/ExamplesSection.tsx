"use client";

import type { TreeMarkType } from "./DirectoryTree";

import { useTranslation } from "react-i18next";

import DirectoryTree from "./DirectoryTree";
import { pathMarkExamples } from "./examples";

import { Chip } from "@/components/bakaui";

const markTypeColors: Record<TreeMarkType, string> = {
  resource: "bg-success",
  property: "bg-primary",
  mediaLibrary: "bg-secondary",
};

const ExamplesSection = () => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-3">
      <p className="text-sm text-default-500">{t("helpCenter.pathMark.examples.intro")}</p>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-3">
        {pathMarkExamples.map((example) => {
          const base = `helpCenter.pathMark.examples.${example.id}`;

          return (
            <div
              key={example.id}
              className="flex flex-col gap-2 rounded-lg border border-default-200 p-3"
            >
              {/* Title + ability tags */}
              <div className="flex items-start justify-between gap-2">
                <h4 className="text-sm font-semibold text-default-800">{t(`${base}.title`)}</h4>
                <div className="flex flex-wrap justify-end gap-1 shrink-0">
                  {example.abilities.map((ability) => (
                    <Chip key={ability} color="warning" size="sm" variant="flat">
                      {t(`helpCenter.pathMark.ability.${ability}`)}
                    </Chip>
                  ))}
                </div>
              </div>

              <p className="text-xs text-default-500">{t(`${base}.desc`)}</p>

              <DirectoryTree lines={example.tree} />

              {/* Marks to configure */}
              <div className="flex flex-col gap-1">
                {example.markTypes.map((markType, index) => (
                  <div key={index} className="flex items-start gap-2 text-xs text-default-600">
                    <span
                      className={`mt-1 w-2 h-2 rounded-full shrink-0 ${markTypeColors[markType]}`}
                    />
                    <span>{t(`${base}.mark${index + 1}`)}</span>
                  </div>
                ))}
              </div>

              {/* Result */}
              <div className="text-xs text-default-700 bg-default-100 rounded px-2 py-1.5 mt-auto">
                {t(`${base}.result`)}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
};

ExamplesSection.displayName = "ExamplesSection";

export default ExamplesSection;
