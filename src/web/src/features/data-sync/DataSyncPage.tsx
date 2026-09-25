import { useTranslation } from "react-i18next";
import { AiOutlineSync } from "react-icons/ai";

/**
 * The data sync page, under System. A placeholder until the feature is built: the route and
 * its menu entry exist from the start so the page's tests can run, and until then it shows
 * its title and nothing that describes data sync.
 *
 * Not `localNodeOnly`: the finished page works in a relay window and in an Unrestricted
 * browser too.
 */
export default function DataSyncPage() {
  const { t } = useTranslation();

  return (
    <div
      className="mx-auto flex max-w-[1500px] flex-col gap-4 p-4 sm:p-6"
      data-testid="data-sync-page"
    >
      <header>
        <h1 className="flex items-center gap-2 text-2xl font-semibold">
          <AiOutlineSync aria-hidden />
          {t("menu.dataSync")}
        </h1>
      </header>
    </div>
  );
}
