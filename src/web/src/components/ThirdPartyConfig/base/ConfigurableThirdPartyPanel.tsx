"use client";

import { useMemo } from "react";
import { Divider } from "@heroui/react";

export type ConfigFieldTab<T extends string = string> = {
  field: T;
  key: string;
  title: string;
  content: React.ReactNode;
};

export interface ConfigurableThirdPartyPanelProps<T extends string> {
  /** When `"all"`, every section is shown; otherwise only matching `field` values. */
  fields: T[] | "all";
  tabs: ConfigFieldTab<T>[];
}

/**
 * Renders field-based third-party configuration as flat vertical sections.
 * Each section has a title and content, separated by dividers.
 */
export default function ConfigurableThirdPartyPanel<T extends string>({
  fields,
  tabs: allTabs,
}: ConfigurableThirdPartyPanelProps<T>) {
  const visible = useMemo(() => {
    if (fields === "all") return allTabs;
    return allTabs.filter((tab) => fields.includes(tab.field));
  }, [fields, allTabs]);

  if (visible.length === 0) return null;

  if (visible.length === 1) {
    return <div className="min-h-0">{visible[0].content}</div>;
  }

  return (
    <div className="space-y-5">
      {visible.map((section, index) => (
        <div key={section.key}>
          {index > 0 && <Divider className="mb-5" />}
          <div className="text-sm font-semibold mb-3">{section.title}</div>
          <div>{section.content}</div>
        </div>
      ))}
    </div>
  );
}
