"use client";

import type { MigrationTarget } from "@/pages/migration/components/Target";

import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";

import { Divider, Spacer, Spinner } from "@/components/bakaui";
import Target from "@/pages/migration/components/Target";
import BApi from "@/sdk/BApi";
import { ErrorLabel } from "@/components/Error";

export default () => {
  const { t } = useTranslation();

  const [allMigrationsAreDone, setAllMigrationsAreDone] = useState(false);

  const [targets, setTargets] = useState<MigrationTarget[]>([]);
  const [loadingTargets, setLoadingTargets] = useState(false);
  const [error, setError] = useState<boolean>();

  const loadTargets = async () => {
    setLoadingTargets(true);
    try {
      const rsp = await BApi.migration.getMigrationTargets();

      if (rsp.code) {
        setError(true);
      } else {
        const ts = rsp.data || [];

        // @ts-ignore
        setTargets(ts);
        if (ts.length == 0) {
          setAllMigrationsAreDone(true);
        }
      }
    } catch (e) {
      setError(true);
    } finally {
      setLoadingTargets(false);
    }
  };

  useEffect(() => {
    loadTargets();
  }, []);

  const renderInner = () => {
    if (loadingTargets) {
      return (
        <div className={"min-h-full flex items-center justify-center text-2xl"}>
          <Spinner />
          <Spacer x={5} />
          {t<string>(
            "We are checking all the data, this may take up to 1 minute, please wait.",
          )}
        </div>
      );
    }
    if (error) {
      return (
        <div className={"min-h-full flex items-center justify-center text-2xl"}>
          <ErrorLabel />
        </div>
      );
    }
    if (allMigrationsAreDone) {
      return (
        <div className={"min-h-full flex items-center justify-center text-2xl"}>
          {t<string>("Congratulations, all data has been migrated")}
        </div>
      );
    }

    return (
      <>
        <div className={"opacity-70"}>
          {t<string>(
            "Because the new feature is not compatible with some historical data, please complete the data migration before using it.",
          )}
        </div>
        <div>
          {targets.map((t) => (
            <>
              <Target
                isLeaf={!t.subTargets}
                target={t}
                onMigrated={loadTargets}
              />
              <Divider />
            </>
          ))}
          {/* </div> */}
        </div>
      </>
    );
  };

  return (
    <div className={"h-full min-h-full max-h-full overflow-auto flex flex-col"}>
      {renderInner()}
    </div>
  );
};
