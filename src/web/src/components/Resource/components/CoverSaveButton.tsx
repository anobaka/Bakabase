"use client";

import { useTranslation } from "react-i18next";
import toast from "react-hot-toast";

import { useResourceOptionsStore } from "@/stores/options";
import { CoverSaveMode, coverSaveModes } from "@/sdk/constants";
import { Button, Tooltip } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

type Props = {
  resourceId: number;
  disabledReason?: string;
  getDataURL: () => string;
};

const modeTips: Record<CoverSaveMode, string> = {
  [CoverSaveMode.Prepend]: "As the first cover",
  [CoverSaveMode.Replace]: "Current covers will be discard",
};
const CoverSaveButton = (props: Props) => {
  const { t } = useTranslation();
  const { disabledReason, getDataURL, resourceId } = props;
  const resourceOptions = useResourceOptionsStore((state) => state.data);

  const { saveMode } = resourceOptions.coverOptions ?? {};
  const externalDisabled = !!disabledReason;

  return (
    <Tooltip
      content={
        <div className={"flex flex-col gap-2 p-2"}>
          <div>{t<string>("Please select mode")}</div>
          {coverSaveModes.map((c) => {
            return (
              <div className={"flex items-center gap-2"}>
                <Button
                  color={saveMode == c.value ? "primary" : "default"}
                  size={"sm"}
                  onClick={() => {
                    BApi.options.patchResourceOptions({
                      ...resourceOptions,
                      coverOptions: {
                        ...resourceOptions.coverOptions,
                        saveMode: c.value,
                      },
                    });
                  }}
                >
                  {t<string>(c.label)}
                </Button>
                <div>{t<string>(modeTips[c.value])}</div>
              </div>
            );
          })}
        </div>
      }
    >
      <Button
        isDisabled={externalDisabled}
        size={"sm"}
        onClick={() => {
          if (saveMode != undefined) {
            BApi.resource
              .saveCover(resourceId, {
                base64String: getDataURL(),
                saveMode,
              })
              .then((r) => {
                toast.success(t<string>("Saved"));
              });
          }
        }}
      >
        {externalDisabled
          ? disabledReason
          : saveMode == undefined
            ? t<string>("Please select cover save mode first")
            : saveMode == CoverSaveMode.Prepend
              ? t<string>("Prepend to covers")
              : t<string>("Replace cover")}
      </Button>
    </Tooltip>
  );
};

CoverSaveButton.displayName = "CoverSaveButton";

export default CoverSaveButton;
