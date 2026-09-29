import type { DevicesData } from "./context";
import type { DevicesTab } from "./sections";
import type { DevicesTabId } from "../switching";

import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";

import { devicesRoute } from "../switching";

/**
 * The page's sections as links, not a tablist: each is an address of its own, so Back,
 * copying the link and opening it in another window all work. A vertical list beside the
 * content in a wide container, pills above it in a narrow one.
 *
 * A number says how many requests wait in a section, a dot that something there needs a
 * look. Neither is the only way to learn it — the device tab lists the same items — and
 * both are spoken as words after the section's name.
 */
export default function DevicesNav({
  tabs,
  active,
  data,
}: {
  tabs: DevicesTab[];
  active: DevicesTabId;
  data: DevicesData;
}) {
  const { t } = useTranslation();

  return (
    <nav
      aria-label={t("federation.devices.nav.label")}
      className="@3xl:sticky @3xl:top-4 @3xl:self-start"
      data-testid="devices-nav"
    >
      <ul className="flex flex-wrap gap-2 @3xl:flex-col @3xl:gap-1">
        {tabs.map((tab) => {
          const current = tab.id === active;
          const badge = tab.badge?.(data);
          const Icon = tab.icon;

          return (
            <li key={tab.id}>
              <Link
                aria-current={current ? "page" : undefined}
                className={`flex items-center gap-2 rounded-full px-3 py-1.5 text-sm transition @3xl:rounded-lg ${
                  current
                    ? "bg-primary/10 font-medium text-primary"
                    : "border border-default-200 hover:bg-default-100 @3xl:border-transparent"
                }`}
                data-tab={tab.id}
                to={devicesRoute(tab.id)}
              >
                <Icon aria-hidden className="shrink-0" />
                <span>{t(tab.labelKey)}</span>
                {badge && badge.count > 0 && (
                  <span
                    aria-hidden
                    className="ml-auto min-w-5 rounded-full bg-primary px-1.5 text-center text-xs text-primary-foreground"
                  >
                    {badge.count}
                  </span>
                )}
                {badge?.attention && (
                  <span
                    aria-hidden
                    className={`${badge.count > 0 ? "" : "ml-auto"} h-2 w-2 shrink-0 rounded-full bg-warning`}
                  />
                )}
                {badge && (
                  <span className="sr-only">
                    {badge.count > 0 && badge.countKey
                      ? t(badge.countKey, { count: badge.count })
                      : ""}
                    {badge.attention ? t("federation.devices.nav.attention") : ""}
                  </span>
                )}
              </Link>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}
