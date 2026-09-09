"use client";

import type { CollectionModel } from "@/stores/collections";

import React from "react";
import { useTranslation } from "react-i18next";

import { Tooltip } from "@/components/bakaui";

type Props = {
  collection: CollectionModel;
  /** Show the numbers under the bar. Off on a crowded card. */
  showLegend?: boolean;
};

/**
 * How much of a collection you have, as one bar.
 *
 * Three states share the width — had, on its way, still missing — so the answer to "how far
 * along is this" is a shape rather than a number to read. Ignored members are not on the bar
 * at all: they left the count on purpose, and drawing them would put them back in it.
 */
const CompositionBar: React.FC<Props> = ({ collection, showLegend = true }) => {
  const { t } = useTranslation();
  const p = collection.progress;
  const total = p?.total ?? 0;
  const owned = p?.owned ?? 0;
  const acquiring = p?.acquiring ?? 0;
  const missing = Math.max(0, total - owned - acquiring);

  const share = (n: number) => (total === 0 ? 0 : (n / total) * 100);

  return (
    <div className="flex flex-col gap-1">
      <div className="flex h-2 w-full overflow-hidden rounded-full bg-default-200">
        {total === 0 ? null : (
          <>
            <Tooltip content={t<string>("collection.progress.owned", { count: owned })}>
              <div className="bg-success h-full" style={{ width: `${share(owned)}%` }} />
            </Tooltip>
            <Tooltip content={t<string>("collection.progress.acquiring", { count: acquiring })}>
              <div className="bg-primary h-full" style={{ width: `${share(acquiring)}%` }} />
            </Tooltip>
            <Tooltip content={t<string>("collection.progress.missing", { count: missing })}>
              <div className="bg-default-300 h-full" style={{ width: `${share(missing)}%` }} />
            </Tooltip>
          </>
        )}
      </div>
      {showLegend && (
        <div className="flex items-center gap-3 text-xs text-default-500">
          <span>{t<string>("collection.progress.owned", { count: owned })}</span>
          {acquiring > 0 && (
            <span className="text-primary">
              {t<string>("collection.progress.acquiring", { count: acquiring })}
            </span>
          )}
          {missing > 0 && (
            <span>{t<string>("collection.progress.missing", { count: missing })}</span>
          )}
          {(p?.ignored ?? 0) > 0 && (
            <span className="text-default-400">
              {t<string>("collection.progress.ignored", { count: p!.ignored })}
            </span>
          )}
        </div>
      )}
    </div>
  );
};

CompositionBar.displayName = "CompositionBar";

export default CompositionBar;
