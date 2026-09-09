import type { SubscriptionProviderFormProps } from "../types";
import type { BangumiSubjectRelationsTarget } from "./types";

import React from "react";
import { useTranslation } from "react-i18next";

import { BANGUMI_RELATIONS } from "./types";

import { Input, Select, Switch } from "@/components/bakaui";

const BangumiSubjectRelationsForm: React.FC<
  SubscriptionProviderFormProps<BangumiSubjectRelationsTarget>
> = ({ value, onChange }) => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-3">
      <Input
        isRequired
        description={t<string>(
          "subscription.provider.bangumi.subjectRelations.subject.description",
        )}
        label={t<string>("subscription.provider.bangumi.subjectRelations.subject.label")}
        placeholder="https://bgm.tv/subject/389156"
        value={value.subject}
        onValueChange={(subject) => onChange({ ...value, subject })}
      />
      <Select
        dataSource={BANGUMI_RELATIONS.map((r) => ({ value: r, label: r, textValue: r }))}
        description={t<string>(
          "subscription.provider.bangumi.subjectRelations.relations.description",
        )}
        label={t<string>("subscription.provider.bangumi.subjectRelations.relations.label")}
        selectedKeys={value.relations}
        selectionMode="multiple"
        onSelectionChange={(keys) =>
          onChange({ ...value, relations: Array.from(keys).map(String) })
        }
      />
      <Switch
        isSelected={value.includeSelf}
        size="sm"
        onValueChange={(includeSelf) => onChange({ ...value, includeSelf })}
      >
        <span className="text-sm">
          {t<string>("subscription.provider.bangumi.subjectRelations.includeSelf")}
        </span>
      </Switch>
    </div>
  );
};

export default BangumiSubjectRelationsForm;
