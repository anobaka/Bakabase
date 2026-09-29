import { useTranslation } from "react-i18next";

import { focusHeadingClass } from "../components/common";

import { useDevicesPage } from "./context";

/**
 * The shown tab's heading. A link to the tab (the nav, the help, the map) moves focus
 * here; it takes focus without a ring unless the keyboard put it there, and is brought into
 * view below the page's sticky feedback.
 */
export default function TabHeading({ introKey }: { introKey?: string }) {
  const { t } = useTranslation();
  const { tab, tabs, headingRef } = useDevicesPage();
  const entry = tabs.find((candidate) => candidate.id === tab);

  return (
    <div>
      <h2
        ref={headingRef}
        className={`text-lg font-semibold ${focusHeadingClass}`}
        id="devices-panel-title"
        tabIndex={-1}
      >
        {entry ? t(entry.labelKey) : null}
      </h2>
      {introKey && <p className="mt-1 max-w-3xl text-sm text-default-500">{t(introKey)}</p>}
    </div>
  );
}
