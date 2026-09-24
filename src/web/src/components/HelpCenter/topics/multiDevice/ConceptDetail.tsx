"use client";

import { useTranslation } from "react-i18next";

import { multiDeviceConcepts } from "./concepts";
import { mdk } from "./devices";
import PlayHereDiagram from "./PlayHereDiagram";

/**
 * What each remote-access setting lets other devices do. Row labels are the Devices
 * page's own status names, so the table reads the way the page does.
 */
const remoteAccessModes = [
  { id: "off", unpaired: "none", paired: "none" },
  { id: "paired", unpaired: "none", paired: "full" },
  { id: "open", unpaired: "browse", paired: "full" },
  { id: "unrestricted", unpaired: "full", paired: "full" },
] as const;

const valueTone: Record<string, string> = {
  none: "text-default-400",
  browse: "text-default-700",
  full: "font-medium text-warning-700 dark:text-warning",
};

const RemoteAccessModes = () => {
  const { t } = useTranslation();
  const base = mdk("concept.remoteAccess.table");

  return (
    <div className="overflow-x-auto rounded-lg border border-default-200">
      <table className="w-full min-w-[360px] text-left text-xs" data-testid="remote-access-modes">
        <thead className="bg-default-100">
          <tr>
            {["mode", "unpaired", "paired"].map((column) => (
              <th key={column} className="p-2.5 font-medium" scope="col">
                {t(`${base}.${column}`)}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {remoteAccessModes.map((mode) => (
            <tr key={mode.id} className="border-t border-default-200">
              <th className="p-2.5 font-medium text-default-700" scope="row">
                {t(`federation.management.status.${mode.id}`)}
              </th>
              {[mode.unpaired, mode.paired].map((value, index) => (
                <td key={index} className={`p-2.5 ${valueTone[value]}`}>
                  {t(`${base}.value.${value}`)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
};

const MultiDeviceConceptDetail = ({ conceptId }: { conceptId: string }) => {
  const { t } = useTranslation();
  const concept = multiDeviceConcepts.find((item) => item.id === conceptId);

  if (!concept) return null;

  const base = mdk(`concept.${concept.id}`);

  return (
    <div className="flex flex-col gap-3">
      <div>
        <h3 className="text-lg font-semibold">{t(concept.labelKey)}</h3>
        <p className="text-sm text-default-500">{t(`${base}.short`)}</p>
      </div>
      <p className="whitespace-pre-line text-sm text-default-700">{t(`${base}.long`)}</p>
      {concept.id === "pathMapping" && <PlayHereDiagram />}
      {concept.id === "remoteAccess" && <RemoteAccessModes />}
      <div className="whitespace-pre-line rounded-lg bg-default-100 px-3 py-2 text-sm text-default-600">
        {t(`${base}.example`)}
      </div>
    </div>
  );
};

MultiDeviceConceptDetail.displayName = "MultiDeviceConceptDetail";

export default MultiDeviceConceptDetail;
