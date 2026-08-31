"use client";

import { useTranslation } from "react-i18next";
import { AiOutlineCheck, AiOutlineClose, AiOutlineMinus } from "react-icons/ai";

type SupportLevel = "yes" | "partial" | "no";

interface ComparisonRow {
  id: string;
  /** [scraper-style manager, manual tagging tool, Bakabase path marks] */
  levels: [SupportLevel, SupportLevel, SupportLevel];
}

const rows: ComparisonRow[] = [
  { id: "noRestructure", levels: ["no", "yes", "yes"] },
  { id: "anyLevel", levels: ["no", "partial", "yes"] },
  { id: "regexMatch", levels: ["no", "no", "yes"] },
  { id: "dynamicProperty", levels: ["partial", "no", "yes"] },
  { id: "dynamicLibrary", levels: ["no", "no", "yes"] },
  { id: "stackedRules", levels: ["no", "partial", "yes"] },
  { id: "preview", levels: ["no", "no", "yes"] },
  { id: "anyFileType", levels: ["partial", "partial", "yes"] },
  { id: "keepDataOnMove", levels: ["partial", "no", "yes"] },
  { id: "portableRules", levels: ["no", "no", "yes"] },
];

const LevelIcon = ({ level }: { level: SupportLevel }) => {
  switch (level) {
    case "yes":
      return <AiOutlineCheck className="text-success text-base mx-auto" />;
    case "partial":
      return <AiOutlineMinus className="text-warning text-base mx-auto" />;
    case "no":
      return <AiOutlineClose className="text-default-300 text-base mx-auto" />;
  }
};

const k = (key: string) => `helpCenter.pathMark.comparison.${key}`;

const ComparisonSection = () => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-3">
      <p className="text-sm text-default-500">{t(k("intro"))}</p>

      <div className="overflow-x-auto">
        <table className="w-full text-sm border-collapse">
          <thead>
            <tr className="border-b border-default-200">
              <th className="text-left font-medium text-default-500 py-2 pr-2">
                {t(k("column.capability"))}
              </th>
              <th className="font-medium text-default-500 py-2 px-2 whitespace-nowrap">
                {t(k("column.scraper"))}
              </th>
              <th className="font-medium text-default-500 py-2 px-2 whitespace-nowrap">
                {t(k("column.manual"))}
              </th>
              <th className="font-medium text-primary py-2 px-2 whitespace-nowrap bg-primary/5 rounded-t">
                {t(k("column.bakabase"))}
              </th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.id} className="border-b border-default-100">
                <td className="py-2 pr-2 text-default-700">{t(k(`row.${row.id}`))}</td>
                <td className="py-2 px-2 text-center">
                  <LevelIcon level={row.levels[0]} />
                </td>
                <td className="py-2 px-2 text-center">
                  <LevelIcon level={row.levels[1]} />
                </td>
                <td className="py-2 px-2 text-center bg-primary/5">
                  <LevelIcon level={row.levels[2]} />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="flex items-center gap-4 text-xs text-default-400">
        <span className="flex items-center gap-1">
          <AiOutlineCheck className="text-success" />
          {t(k("legend.yes"))}
        </span>
        <span className="flex items-center gap-1">
          <AiOutlineMinus className="text-warning" />
          {t(k("legend.partial"))}
        </span>
        <span className="flex items-center gap-1">
          <AiOutlineClose className="text-default-300" />
          {t(k("legend.no"))}
        </span>
      </div>

      <p className="text-xs text-default-400">{t(k("note"))}</p>
    </div>
  );
};

ComparisonSection.displayName = "ComparisonSection";

export default ComparisonSection;
