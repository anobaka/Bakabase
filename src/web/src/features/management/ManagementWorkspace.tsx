import { useState } from "react";
import { useTranslation } from "react-i18next";

import { type ManagementSource, sourceKey } from "./api";
import { DefinitionGroup } from "./Definitions";
import { ResourceGroup } from "./ResourceGroup";

import { Button, Input } from "@/components/bakaui";

type Section = "resources" | "properties" | "libraries";
export default function ManagementWorkspace({
  sources,
  error,
  refresh,
}: {
  sources: ManagementSource[];
  error?: Error;
  refresh: () => void;
}) {
  const { t } = useTranslation();
  const [section, setSection] = useState<Section>("resources");
  const [excluded, setExcluded] = useState<string[]>([]);
  const [keyword, setKeyword] = useState("");
  const [submitted, setSubmitted] = useState("");
  const visible = sources.filter((source) => !excluded.includes(source.serverId));

  return (
    <div className="flex h-full min-h-0 flex-col gap-3">
      <div className="flex flex-wrap items-center gap-2">
        {(["resources", "properties", "libraries"] as Section[]).map((key) => (
          <Button
            key={key}
            color={section === key ? "primary" : "default"}
            size="sm"
            onPress={() => setSection(key)}
          >
            {t(`management.${key}`)}
          </Button>
        ))}
        <form
          className="ml-auto flex min-w-0 flex-1 items-center gap-2 sm:max-w-md"
          onSubmit={(e) => {
            e.preventDefault();
            setSubmitted(keyword.trim());
          }}
        >
          <Input
            aria-label={t("management.search")}
            placeholder={t("management.search")}
            size="sm"
            value={keyword}
            onValueChange={setKeyword}
          />
          <Button size="sm" type="submit">
            {t("management.search")}
          </Button>
        </form>
        <Button size="sm" variant="light" onPress={refresh}>
          {t("management.refreshDevices")}
        </Button>
      </div>
      <div
        aria-label={t("management.sources")}
        className="flex flex-wrap gap-x-4 gap-y-2 rounded-lg bg-default-100 px-3 py-2 text-sm"
      >
        {sources.map((source) => (
          <label key={source.serverId} className="flex items-center gap-2">
            <input
              checked={!excluded.includes(source.serverId)}
              type="checkbox"
              onChange={(e) =>
                setExcluded((current) =>
                  e.target.checked
                    ? current.filter((id) => id !== source.serverId)
                    : [...current, source.serverId],
                )
              }
            />
            {source.name}
            {!source.available && (
              <span className="text-xs text-warning">{t("management.sourceUnavailable")}</span>
            )}
          </label>
        ))}
      </div>
      <p className="text-xs text-default-500">{t("management.coverageHelp")}</p>
      {error && (
        <p className="text-sm text-warning" role="alert">
          {t("management.sourcesError")} {error.message}
        </p>
      )}
      <div className="flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto pb-4">
        {!visible.length && (
          <p className="p-8 text-center text-sm text-default-500">
            {t("management.selectSources")}
          </p>
        )}
        {visible.map((source) =>
          section === "resources" ? (
            <ResourceGroup
              key={`${sourceKey(source)}:${source.available}`}
              keyword={submitted}
              source={source}
            />
          ) : (
            <DefinitionGroup
              key={`${sourceKey(source)}:${source.available}:${section}`}
              keyword={submitted}
              kind={section}
              source={source}
            />
          ),
        )}
      </div>
    </div>
  );
}
