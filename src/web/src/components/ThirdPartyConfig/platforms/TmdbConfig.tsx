"use client";

import { useEffect, useState, type FC } from "react";
import { useTranslation } from "react-i18next";
import { Button, Input } from "@heroui/react";

import { toast } from "@/components/bakaui";
import { useTmdbOptionsStore } from "@/stores/options";

import ConfigurableThirdPartyPanel, { type ConfigFieldTab } from "../base/ConfigurableThirdPartyPanel";
import ThirdPartyConfigModal from "../base/ThirdPartyConfigModal";

export enum TmdbConfigField {
  Settings = "settings",
}

export interface TmdbConfigPanelProps {
  fields?: TmdbConfigField[] | "all";
  showFooter?: boolean;
  onCancel?: () => void;
}

export const TmdbConfigPanel: FC<TmdbConfigPanelProps> = ({
  fields = "all",
  showFooter = true,
  onCancel,
}) => {
  const { t } = useTranslation();
  const tmdbOptions = useTmdbOptionsStore((s) => s.data);
  const patch = useTmdbOptionsStore((s) => s.patch);

  const [tmp, setTmp] = useState<any>(tmdbOptions || {});

  useEffect(() => {
    setTmp(JSON.parse(JSON.stringify(tmdbOptions || {})));
  }, [tmdbOptions]);

  const save = async () => {
    await patch({
      maxConcurrency: tmp.maxConcurrency,
      requestInterval: tmp.requestInterval,
      apiKey: tmp.apiKey,
      userAgent: tmp.userAgent,
      referer: tmp.referer,
      headers: tmp.headers,
    });
    toast.success(t<string>("thirdPartyConfig.success.saved"));
  };

  const formContent = (
    <div className="space-y-3">
      <h3 className="text-small font-semibold text-default-700">{t<string>("thirdPartyConfig.group.dataFetch")}</h3>
      <div className="grid grid-cols-2 gap-4">
        <Input
          label={t<string>("thirdPartyConfig.label.maxConcurrency")}
          size="sm"
          type="number"
          value={String(tmp.maxConcurrency || 1)}
          onValueChange={(v) => setTmp({ ...tmp, maxConcurrency: Number(v) || 1 })}
        />
        <Input
          label={t<string>("thirdPartyConfig.label.requestInterval")}
          size="sm"
          type="number"
          value={String(tmp.requestInterval || 1000)}
          onValueChange={(v) => setTmp({ ...tmp, requestInterval: Number(v) || 1000 })}
        />
      </div>
      <Input
        label={t<string>("thirdPartyConfig.label.apiKey")}
        size="sm"
        value={tmp.apiKey || ""}
        onValueChange={(v) => setTmp({ ...tmp, apiKey: v })}
      />
      <div className="grid grid-cols-2 gap-4">
        <Input
          label={t<string>("thirdPartyConfig.label.userAgent")}
          size="sm"
          value={tmp.userAgent || ""}
          onValueChange={(v) => setTmp({ ...tmp, userAgent: v })}
        />
        <Input
          label={t<string>("thirdPartyConfig.label.referer")}
          size="sm"
          value={tmp.referer || ""}
          onValueChange={(v) => setTmp({ ...tmp, referer: v })}
        />
      </div>
    </div>
  );

  const tabs: ConfigFieldTab<TmdbConfigField>[] = [
    {
      field: TmdbConfigField.Settings,
      key: "settings",
      title: t<string>("thirdPartyConfig.group.dataFetch"),
      content: formContent,
    },
  ];

  return (
    <>
      <ConfigurableThirdPartyPanel fields={fields} tabs={tabs} />
      {showFooter && (
        <div className="mt-4 flex justify-end gap-2 border-t border-default-200 pt-4">
          {onCancel ? (
            <Button variant="light" onPress={onCancel}>
              {t("common.action.cancel")}
            </Button>
          ) : null}
          <Button color="primary" onPress={save}>
            {t<string>("thirdPartyConfig.action.save")}
          </Button>
        </div>
      )}
    </>
  );
};

export interface TmdbConfigModalProps {
  onDestroyed?: () => void;
  onClose?: () => void;
  isOpen?: boolean;
  fields?: TmdbConfigField[] | "all";
}

export const TmdbConfigModal: FC<TmdbConfigModalProps> = ({ onDestroyed, onClose, isOpen, fields }) => {
  const { t } = useTranslation();
  const handleClose = onClose ?? onDestroyed;
  return (
    <ThirdPartyConfigModal title="TMDB" size="2xl" isOpen={isOpen} onClose={handleClose}>
      <TmdbConfigPanel fields={fields} onCancel={handleClose} />
    </ThirdPartyConfigModal>
  );
};

const TmdbConfig = TmdbConfigModal;
export default TmdbConfig;
