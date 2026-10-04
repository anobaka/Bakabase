import { useTranslation } from "react-i18next";

export default function DownloadOrderPrompt({ message }: { message?: string }) {
  const { t } = useTranslation();
  // Older servers returned an untranslated localization key for this routine choice.
  const details = message?.trim() === "FailedToStart" ? undefined : message;

  return (
    <div className="flex flex-col gap-4">
      <p>{t<string>("downloader.downloadOrder.description")}</p>
      {details && <p className="whitespace-pre-wrap text-sm text-default-500">{details}</p>}
      <div className="flex flex-col gap-3 rounded-lg bg-default-100 p-3 text-sm">
        <div>
          <div className="font-medium">{t<string>("downloader.action.addToQueue")}</div>
          <p className="text-default-500">{t<string>("downloader.downloadOrder.queueHint")}</p>
        </div>
        <div>
          <div className="font-medium">{t<string>("downloader.action.downloadSelectedFirst")}</div>
          <p className="text-default-500">{t<string>("downloader.downloadOrder.priorityHint")}</p>
        </div>
      </div>
      <p className="text-xs text-default-500">{t<string>("downloader.downloadOrder.closeHint")}</p>
    </div>
  );
}
