import type { WorkflowTriggerUI } from "../types";

import { useTranslation } from "react-i18next";

import { Input, Tab, Tabs, Textarea } from "@/components/bakaui";
import { WorkflowItemTypes } from "@/components/Workflow/itemTypes";

const Explanation = () => {
  const { t } = useTranslation();

  return (
    <p className="text-sm leading-relaxed text-default-500">
      {t("workflow.trigger.postParserManual.description")}
    </p>
  );
};

const ManualRunForm: NonNullable<WorkflowTriggerUI["ManualRunForm"]> = ({ value, onChange }) => {
  const { t } = useTranslation();
  let payload: { link?: string; text?: string; title?: string } = {};

  try {
    const parsed = JSON.parse(value);

    if (parsed && typeof parsed === "object" && !Array.isArray(parsed)) payload = parsed;
  } catch {
    /* Keep invalid drafts editable. */
  }
  const isText = "text" in payload;

  return (
    <div className="space-y-4">
      <Explanation />
      <Tabs
        aria-label={t("workflow.postParser.inputMode")}
        selectedKey={isText ? "text" : "link"}
        onSelectionChange={(key) =>
          onChange(
            JSON.stringify(
              key === "text"
                ? { text: "", title: payload.title }
                : { link: "", title: payload.title },
            ),
          )
        }
      >
        <Tab key="link" title={t("workflow.postParser.link")}>
          <Input
            label={t("workflow.postParser.link")}
            placeholder="https://…"
            value={payload.link ?? ""}
            onValueChange={(link) => onChange(JSON.stringify({ ...payload, link }))}
          />
        </Tab>
        <Tab key="text" title={t("workflow.postParser.text")}>
          <Textarea
            label={t("workflow.postParser.text")}
            minRows={6}
            value={payload.text ?? ""}
            onValueChange={(text) => onChange(JSON.stringify({ ...payload, text }))}
          />
        </Tab>
      </Tabs>
      <Input
        label={t("workflow.postParser.title")}
        value={payload.title ?? ""}
        onValueChange={(title) => onChange(JSON.stringify({ ...payload, title }))}
      />
    </div>
  );
};

export const PostParserManualTriggerUI: WorkflowTriggerUI<Record<string, never>> = {
  kind: "postParser.manual",
  displayNameKey: "workflow.trigger.postParserManual.displayName",
  defaultFilter: () => ({}),
  parseFilter: () => ({}),
  serializeFilter: () => null,
  isValid: () => true,
  resolveOutputItemType: () => WorkflowItemTypes.PostParserInput,
  FilterForm: Explanation,
  FilterSummary: Explanation,
  ManualRunForm,
  defaultManualPayload: () => JSON.stringify({ link: "" }),
  isManualPayloadValid: (json) => {
    try {
      const value = JSON.parse(json);

      if (typeof value.text === "string" && value.text.trim()) return !value.link;
      if (typeof value.link !== "string" || value.text) return false;

      return ["http:", "https:"].includes(new URL(value.link).protocol);
    } catch {
      return false;
    }
  },
};
