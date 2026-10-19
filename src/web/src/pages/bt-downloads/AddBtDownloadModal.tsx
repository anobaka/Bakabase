import type { DestroyableProps } from "@/components/bakaui/types";

import { useState } from "react";
import { useTranslation } from "react-i18next";

import { Input, Modal, Tab, Tabs } from "@/components/bakaui";
import DownloadDirectoryField from "@/components/ThirdPartyConfig/base/DownloadDirectoryField";
import { useDownloaderGlobalOptionsStore } from "@/stores/options";
import BApi from "@/sdk/BApi";

export default function AddBtDownloadModal({
  onDestroyed,
  onCreated,
}: DestroyableProps & { onCreated: () => void }) {
  const { t } = useTranslation();
  const defaultPath = useDownloaderGlobalOptionsStore((s) => s.data?.defaultDownloadPath);
  const [kind, setKind] = useState("link");
  const [source, setSource] = useState("");
  const [name, setName] = useState("");
  const [file, setFile] = useState<File>();
  const [outputDirectory, setOutputDirectory] = useState(defaultPath ?? "");
  const [error, setError] = useState<string>();
  const validLink = /^magnet:\?/i.test(source.trim()) || /^https?:\/\//i.test(source.trim());
  const valid = outputDirectory.trim().length > 0 && (kind === "file" ? !!file : validLink);

  return (
    <Modal
      defaultVisible
      size="2xl"
      title={t("bt.add.title")}
      onDestroyed={onDestroyed}
      footer={{ actions: ["ok", "cancel"], okProps: { isDisabled: !valid } }}
      onOk={async () => {
        setError(undefined);
        try {
          const result =
            kind === "file"
              ? await BApi.btDownloads.uploadBtDownload({
                  file: file!,
                  name: name.trim() || undefined,
                  outputDirectory: outputDirectory.trim(),
                })
              : await BApi.btDownloads.createBtDownload(
                  {
                    name: name.trim() || undefined,
                    ...(/^magnet:/i.test(source.trim())
                      ? { magnetUri: source.trim() }
                      : { torrentUrl: source.trim() }),
                  },
                  { outputDirectory: outputDirectory.trim() },
                );
          if (result.code) throw new Error(result.message || t("bt.error.action"));
          onCreated();
        } catch (e) {
          setError(e instanceof Error ? e.message : t("bt.error.action"));
          throw e;
        }
      }}
    >
      <Tabs selectedKey={kind} onSelectionChange={(key) => setKind(String(key))}>
        <Tab key="link" title={t("bt.add.link")}>
          <Input
            label={t("bt.add.source")}
            placeholder="magnet:?xt=urn:btih:… / https://…/file.torrent"
            value={source}
            onValueChange={setSource}
          />
        </Tab>
        <Tab key="file" title={t("bt.add.file")}>
          <label className="flex flex-col gap-2 text-sm">
            {t("bt.add.file")}
            <input
              accept=".torrent,application/x-bittorrent"
              type="file"
              onChange={(event) => setFile(event.target.files?.[0])}
            />
          </label>
        </Tab>
      </Tabs>
      <Input label={t("bt.add.name")} value={name} onValueChange={setName} />
      <DownloadDirectoryField
        label={t("bt.outputDirectory")}
        value={outputDirectory}
        description={t("bt.add.directoryDescription")}
        onChange={setOutputDirectory}
      />
      {error && (
        <p className="text-sm text-danger" role="alert">
          {error}
        </p>
      )}
    </Modal>
  );
}
