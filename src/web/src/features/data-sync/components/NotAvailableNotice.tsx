import { useTranslation } from "react-i18next";
import { AiOutlineSync } from "react-icons/ai";

/**
 * What the page says where it cannot be used: a browser on another device of a server that
 * does not let such browsers manage it (outside Unrestricted mode). Nothing on the page asks the
 * server anything there — it would only be refused.
 */
export default function NotAvailableNotice() {
  const { t } = useTranslation();

  return (
    <div
      className="mx-auto flex max-w-2xl flex-col gap-3 p-6"
      data-testid="data-sync-not-available"
    >
      <h1 className="flex items-center gap-2 text-xl font-semibold">
        <AiOutlineSync aria-hidden />
        {t("dataSync.title")}
      </h1>
      <p>{t("dataSync.notAvailable")}</p>
    </div>
  );
}
