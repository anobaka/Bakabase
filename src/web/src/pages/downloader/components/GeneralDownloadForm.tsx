import { forwardRef, useEffect, useImperativeHandle, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Input } from "@/components/bakaui";
import DownloadDirectoryField from "@/components/ThirdPartyConfig/base/DownloadDirectoryField";
import { useDownloaderGlobalOptionsStore } from "@/stores/options";
import BApi from "@/sdk/BApi";
import type { DownloadFormHandle, DownloadFormState } from "./downloadForm";

export type GeneralDownloadKind = "http" | "bt" | "file";

export const GeneralDownloadForm = forwardRef<
  DownloadFormHandle,
  {
    kind: GeneralDownloadKind;
    formId: string;
    onStateChange: (state: DownloadFormState) => void;
    onCreated: () => void;
  }
>(({ kind, formId, onStateChange, onCreated }, ref) => {
  const { t } = useTranslation();
  const defaultPath = useDownloaderGlobalOptionsStore((s) => s.data?.defaultDownloadPath);
  const [source, setSource] = useState("");
  const [name, setName] = useState("");
  const [fileName, setFileName] = useState("");
  const [file, setFile] = useState<File>();
  const [outputDirectory, setOutputDirectory] = useState(defaultPath ?? "");
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(false);
  const validLink =
    /^https?:\/\//i.test(source.trim()) || (kind === "bt" && /^magnet:\?/i.test(source.trim()));
  const valid = outputDirectory.trim().length > 0 && (kind === "file" ? !!file : validLink);

  useEffect(() => onStateChange({ canSubmit: valid, busy }), [valid, busy, onStateChange]);

  const submit = async () => {
    if (!valid || busyRef.current) return;
    busyRef.current = true;
    setBusy(true);
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
    } finally {
      busyRef.current = false;
      setBusy(false);
    }
  };
  useImperativeHandle(ref, () => ({ submit }));

  return (
    <form
      id={formId}
      onSubmit={(event) => {
        event.preventDefault();
        void submit().catch(() => undefined);
      }}
    >
      <fieldset disabled={busy} className="space-y-3">
        {kind === "bt" && (
          <Input
            label={t("bt.add.source")}
            placeholder="magnet:?xt=urn:btih:… / https://…/file.torrent"
            value={source}
            onValueChange={setSource}
          />
        )}
        {kind === "http" && (
          <>
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
          </>
        )}
        {kind === "file" && (
          <Input
            label={t("bt.add.file")}
            type="file"
            accept=".torrent,application/x-bittorrent"
            onChange={(event) => setFile(event.target.files?.[0])}
          />
        )}
        <Input label={t("bt.add.name")} value={name} onValueChange={setName} />
        <DownloadDirectoryField
          label={t("bt.outputDirectory")}
          value={outputDirectory}
          description={t(
            kind === "http"
              ? "downloadCenter.add.httpDirectoryDescription"
              : "bt.add.directoryDescription",
          )}
          onChange={setOutputDirectory}
        />
        {error && (
          <p className="text-sm text-danger" role="alert">
            {error}
          </p>
        )}
      </fieldset>
    </form>
  );
});
GeneralDownloadForm.displayName = "GeneralDownloadForm";
