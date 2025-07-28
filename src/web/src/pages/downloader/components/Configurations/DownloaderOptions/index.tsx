"use client";

import type { CookieValidatorTarget } from "@/sdk/constants";
import type {
  BakabaseInsideWorldBusinessComponentsDownloaderAbstractionsModelsDownloaderOptions,
  BakabaseInsideWorldBusinessComponentsDownloaderAbstractionsModelsDownloaderDefinition,
} from "@/sdk/Api";
import type { ThirdPartyId } from "@/sdk/constants";

import { useTranslation } from "react-i18next";
import { Textarea } from "@heroui/react";

import CookieValidator from "@/components/CookieValidator";
import { FileSystemSelectorButton } from "@/components/FileSystemSelector";
import { Chip, NumberInput } from "@/components/bakaui";

type Props = {
  thirdPartyId: ThirdPartyId;
  options: Partial<BakabaseInsideWorldBusinessComponentsDownloaderAbstractionsModelsDownloaderOptions>;
  namingDefinition?: BakabaseInsideWorldBusinessComponentsDownloaderAbstractionsModelsDownloaderDefinition;
  onChange: (
    patches: Partial<BakabaseInsideWorldBusinessComponentsDownloaderAbstractionsModelsDownloaderOptions>,
  ) => void;
};
const DownloaderOptions = ({
  thirdPartyId,
  options,
  namingDefinition,
  onChange,
}: Props) => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-4">
      <CookieValidator
        cookie={options?.cookie || ""}
        description={t<string>("Cookie required for authentication")}
        target={thirdPartyId as unknown as CookieValidatorTarget}
        onChange={(cookie: string) => {
          onChange({ cookie });
        }}
      />

      <div className={"flex flex-col gap-1"}>
        <Chip className={"opacity-70"} size="sm" variant="light">
          {t<string>("Default download path")}
        </Chip>
        <div>
          {options && (
            <FileSystemSelectorButton
              fileSystemSelectorProps={{
                targetType: "folder",
                onSelected: (e) =>
                  onChange({
                    defaultPath: e.path,
                  }),
                defaultSelectedPath: options?.defaultPath,
              }}
            />
          )}
        </div>
        <Chip className={"opacity-70"} size="sm" variant="light">
          {t<string>("Default path where downloads will be saved")}
        </Chip>
      </div>

      <NumberInput
        isRequired
        description={t<string>("Maximum concurrent download threads")}
        label={t<string>("Max concurrency")}
        max={100}
        min={1}
        value={options?.maxConcurrency || 1}
        onValueChange={(maxConcurrency) => {
          onChange({ maxConcurrency });
        }}
      />

      <NumberInput
        isRequired
        description={t<string>("Request interval help")}
        label={t<string>("Request interval (s)")}
        max={60}
        min={0}
        value={options?.requestInterval || 0}
        onValueChange={(requestInterval) => {
          onChange({ requestInterval });
        }}
      />

      <div>
        <Textarea
          isRequired
          description={
            namingDefinition ? (
              <div>
                <div>
                  {t<string>("Define how downloaded files should be named")}
                </div>
                {namingDefinition.namingFields &&
                  namingDefinition.namingFields.length > 0 && (
                    <div className="mt-2">
                      <div className="text-xs mb-1">
                        {t<string>("Available fields")}:
                      </div>
                      <div className="flex flex-wrap gap-1">
                        {namingDefinition.namingFields.map((x, index) => (
                          <Chip
                            key={index}
                            color="secondary"
                            size="sm"
                            variant="flat"
                            onClick={() => {
                              const currentConvention =
                                options?.namingConvention || "";
                              const newConvention =
                                currentConvention + `{${x.name || x.key}}`;

                              onChange({ namingConvention: newConvention });
                            }}
                          >
                            {x.name || x.key}
                          </Chip>
                        ))}
                      </div>
                    </div>
                  )}
              </div>
            ) : (
              t<string>("Define how downloaded files should be named")
            )
          }
          label={t<string>("Naming convention")}
          placeholder={namingDefinition?.defaultConvention}
          value={options?.namingConvention || ""}
          onValueChange={(namingConvention) => {
            onChange({ namingConvention });
          }}
        />
      </div>
    </div>
  );
};

DownloaderOptions.displayName = "DownloaderOptions";

export default DownloaderOptions;
