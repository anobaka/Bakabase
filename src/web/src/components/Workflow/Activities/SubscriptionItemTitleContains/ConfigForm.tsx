import type { SubscriptionItemTitleContainsConfig } from "./types";

import React from "react";
import { useTranslation } from "react-i18next";

import { Switch, Textarea } from "@/components/bakaui";

interface Props {
  value: SubscriptionItemTitleContainsConfig;
  onChange: (v: SubscriptionItemTitleContainsConfig) => void;
}

const ConfigForm: React.FC<Props> = ({ value, onChange }) => {
  const { t } = useTranslation();

  // Comma-or-newline separated; the backend just sees a string[]. Whitespace is trimmed
  // and empties dropped — keeps the textarea forgiving without UI noise.
  const text = value.keywords.join("\n");

  return (
    <div className="flex flex-col gap-3">
      <Textarea
        description={t<string>(
          "workflow.activity.subscriptionItemTitleContains.keywords.description",
        )}
        label={t<string>("workflow.activity.subscriptionItemTitleContains.keywords.label")}
        minRows={2}
        value={text}
        onValueChange={(s) => {
          const keywords = s
            .split(/[\n,]/)
            .map((k) => k.trim())
            .filter((k) => k.length > 0);
          onChange({ ...value, keywords });
        }}
      />
      <Switch
        isSelected={value.exclude}
        onValueChange={(b) => onChange({ ...value, exclude: b })}
      >
        {t<string>("workflow.activity.subscriptionItemTitleContains.exclude.label")}
      </Switch>
    </div>
  );
};

export default ConfigForm;
