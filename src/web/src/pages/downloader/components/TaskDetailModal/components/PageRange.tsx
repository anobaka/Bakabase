"use client";

import { useTranslation } from "react-i18next";

import { NumberInput } from "@/components/bakaui";

type Props = {
  start?: number;
  end?: number;
  onChange?: (start?: number, end?: number) => void;
};

const PageRange = ({ start, end, onChange }: Props) => {
  const { t } = useTranslation();

  return (
    <>
      <div className={"flex items-start gap-2"}>
        <NumberInput
          description={
            <div>
              <div>
                {t<string>(
                  "Set a page range if you don't want to download them all.",
                )}
              </div>
              <div>{t<string>("The minimal page number is 1.")}</div>
            </div>
          }
          label={t<string>("Start page")}
          min={1}
          size={"sm"}
          step={1}
          value={start}
          onValueChange={(v) => {
            onChange?.(v, end);
          }}
        />
        <NumberInput
          description={t<string>(
            "Set a page range if you don't want to download them all.",
          )}
          label={t<string>("End page")}
          min={end ?? 1}
          size={"sm"}
          step={1}
          value={end}
          onValueChange={(v) => {
            onChange?.(start, v);
          }}
        />
      </div>
    </>
  );
};

export default PageRange;
