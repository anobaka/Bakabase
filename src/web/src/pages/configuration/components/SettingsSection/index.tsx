"use client";

import React, { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import { AiOutlineQuestionCircle } from "react-icons/ai";

import { Tooltip } from "@/components/bakaui";

export interface SettingItem {
  /** Stable within its section; used as the React key. */
  id: string;
  /** Already translated — sections do no translation of their own. */
  label: React.ReactNode;
  /** Already translated. Rendered as a tooltip on a hint icon next to the label. */
  tip?: string;
  /**
   * Extra plain-text terms this row should match on. Use for things the visible
   * label doesn't say — synonyms, the English name of a translated label, the
   * underlying option key.
   */
  keywords?: string[];
  render: () => React.ReactNode;
}

/** Lowercased text a row can be matched against. */
const searchableText = (item: SettingItem): string =>
  [typeof item.label === "string" ? item.label : "", item.tip ?? "", ...(item.keywords ?? [])]
    .join(" ")
    .toLowerCase();

export const normalizeQuery = (q: string) => q.trim().toLowerCase();

/**
 * Decides what a section shows for a query.
 *
 * A section whose own title matches keeps all of its rows — searching "network"
 * should reveal that whole section rather than only the rows that happen to
 * repeat the word.
 */
export const filterItems = (
  title: string,
  items: SettingItem[],
  query: string,
  keywords?: string[],
): SettingItem[] => {
  if (!query) return items;

  const sectionText = [title, ...(keywords ?? [])].join(" ").toLowerCase();

  if (sectionText.includes(query)) return items;

  return items.filter((i) => searchableText(i).includes(query));
};

/**
 * Lets sections tell the page whether they matched, so the page can show an
 * empty state without needing to know what any section contains.
 */
const MatchReportContext = createContext<((id: string, matched: boolean) => void) | null>(null);

export const SettingsSearchResults: React.FC<{
  children: (anyMatched: boolean) => React.ReactNode;
}> = ({ children }) => {
  const [matches, setMatches] = useState<Record<string, boolean>>({});

  const report = useCallback((id: string, matched: boolean) => {
    setMatches((prev) => (prev[id] === matched ? prev : { ...prev, [id]: matched }));
  }, []);

  const anyMatched = useMemo(() => Object.values(matches).some(Boolean), [matches]);

  return (
    <MatchReportContext.Provider value={report}>{children(anyMatched)}</MatchReportContext.Provider>
  );
};

interface Props {
  /** Already translated. */
  title: string;
  items: SettingItem[];
  /** Normalized (lowercased, trimmed) search query; empty shows everything. */
  query?: string;
  /** Extra terms that should match the whole section. */
  keywords?: string[];
  /** Rendered above the rows, inside the section, regardless of the query. */
  header?: React.ReactNode;
}

const SettingsSection: React.FC<Props> = ({ title, items, query = "", keywords, header }) => {
  const visible = filterItems(title, items, query, keywords);
  const report = useContext(MatchReportContext);
  const matched = visible.length > 0;

  useEffect(() => {
    report?.(title, matched);
  }, [report, title, matched]);

  if (!matched) {
    return null;
  }

  return (
    <section className="rounded-large border border-default-200 dark:border-default-100 overflow-hidden">
      <h2 className="px-4 py-2 text-sm font-semibold bg-default-100/60 dark:bg-default-50/40">
        {title}
      </h2>
      {header && <div className="px-4 pt-3">{header}</div>}
      <div className="divide-y divide-default-200/60 dark:divide-default-100/60">
        {visible.map((item) => (
          <div
            key={item.id}
            className="grid gap-x-4 gap-y-1 px-4 py-2.5 items-center hover:bg-[var(--bakaui-overlap-background)] grid-cols-1 sm:grid-cols-[minmax(140px,220px)_1fr]"
          >
            <div className="flex items-center gap-1 text-sm text-foreground-600">
              {item.label}
              {item.tip && (
                <Tooltip
                  className="max-w-[300px]"
                  color="secondary"
                  content={item.tip}
                  placement="top"
                >
                  <span className="inline-flex">
                    <AiOutlineQuestionCircle className="text-base" />
                  </span>
                </Tooltip>
              )}
            </div>
            <div className="min-w-0">{item.render()}</div>
          </div>
        ))}
      </div>
    </section>
  );
};

SettingsSection.displayName = "SettingsSection";

export default SettingsSection;
