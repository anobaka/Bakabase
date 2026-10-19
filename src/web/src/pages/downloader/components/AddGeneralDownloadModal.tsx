import type { DestroyableProps } from "@/components/bakaui/types";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Input, Modal, Tab, Tabs } from "@/components/bakaui";
import DownloadDirectoryField from "@/components/ThirdPartyConfig/base/DownloadDirectoryField";
import { useDownloaderGlobalOptionsStore } from "@/stores/options";
import BApi from "@/sdk/BApi";

export default function AddGeneralDownloadModal({
  onDestroyed,
  onCreated,
}: DestroyableProps & { onCreated: () => void }) {
  const { t } = useTranslation();
  const defaultPath = useDownloaderGlobalOptionsStore((s) => s.data?.defaultDownloadPath);
  const [kind, setKind] = useState("bt");
  const [source, setSource] = useState("");
  const [name, setName] = useState("");
  const [fileName, setFileName] = useState("");
  const [file, setFile] = useState<File>();
  const [outputDirectory, setOutputDirectory] = useState(defaultPath ?? "");
  const [error, setError] = useState<string>();
  const validLink =
    /^https?:\/\//i.test(source.trim()) || (kind === "bt" && /^magnet:\?/i.test(source.trim()));
  const valid = outputDirectory.trim().length > 0 && (kind === "file" ? !!file : validLink);
  return (
    <Modal
      defaultVisible
      size="2xl"
      title={t("downloadCenter.add.general")}
      onDestroyed={onDestroyed}
      footer={{ actions: ["ok", "cancel"], okProps: { isDisabled: !valid } }}
      onOk={async () => {
        setError(undefined);
        try {
          const response =
            kind === "file"
              ? await BApi.downloadCenter.uploadDownloadCenterTorrent({
                  file: file!,
                  name: name.trim() || undefined,
                  outputDirectory: outputDirectory.trim(),
                })
              : await BApi.downloadCenter.createDownloadCenterTask(
                  {
                    provider: kind,
                    name: name.trim() || undefined,
                    ...(kind === "http"
                      ? { url: source.trim(), fileName: fileName.trim() || undefined }
                      : /^magnet:/i.test(source.trim())
                        ? { magnetUri: source.trim() }
                        : { torrentUrl: source.trim() }),
                  },
                  { outputDirectory: outputDirectory.trim() },
                );
          if (response.code) throw new Error(response.message || t("downloadCenter.error.action"));
          onCreated();
        } catch (e) {
          setError(e instanceof Error ? e.message : t("downloadCenter.error.action"));
          throw e;
        }
      }}
    >
      <Tabs
        selectedKey={kind}
        onSelectionChange={(key) => {
          setKind(String(key));
          setSource("");
          setError(undefined);
        }}
      >
        <Tab key="bt" title={t("downloadCenter.provider.bt")}>
          <Input
            label={t("bt.add.source")}
            placeholder="magnet:?xt=urn:btih:… / https://…/file.torrent"
            value={source}
            onValueChange={setSource}
          />
        </Tab>
        <Tab key="http" title={t("downloadCenter.provider.http")}>
          <div className="space-y-3">
            <Input
              label={t("downloadCenter.add.httpUrl")}
              placeholder="https://…/file.zip"
              value={source}
              onValueChange={setSource}
            />
            <Input
              label={t("downloadCenter.add.fileName")}
              description={t("downloadCenter.add.fileNameDescription")}
              value={fileName}
              onValueChange={setFileName}
            />
          </div>
        </Tab>
        <Tab key="file" title={t("bt.add.file")}>
          <label className="flex flex-col gap-2 text-sm">
            {t("bt.add.file")}
            <input
              type="file"
              accept=".torrent,application/x-bittorrent"
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
