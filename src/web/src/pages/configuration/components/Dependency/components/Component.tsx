"use client";

import { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { usePrevious } from "react-use";
import { CheckCircleOutlined } from "@ant-design/icons";
import { MdError } from "react-icons/md";

import { Button, Icon, Spinner } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { useDependentComponentContextsStore } from "@/stores/dependentComponentContexts";
import { DependentComponentStatus } from "@/sdk/constants";
import { Chip, Modal } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
const Component = ({ id }: { id: string }) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const context = useDependentComponentContextsStore(
    (state) => state.contexts,
  ).find((a) => a.id == id);
  const [latestVersion, setLatestVersion] = useState<{
    version?: string;
    canUpdate: boolean;
    error?: string | null;
  }>();
  // const prevInstallationProgress = usePrevious(context);
  const [discovering, setDiscovering] = useState(true);
  const [findingNewVersion, setFindingNewVersion] = useState(false);

  const prevStatus = usePrevious(context?.status);

  useEffect(() => {
    if (
      context?.status == DependentComponentStatus.Installed &&
      prevStatus == DependentComponentStatus.Installing
    ) {
      init();
    }
  }, [context]);

  const init = useCallback(async () => {
    try {
      await BApi.component.discoverDependentComponent({ id });
    } finally {
      setDiscovering(false);
    }

    if (
      context?.isRequired ||
      context?.status == DependentComponentStatus.NotInstalled
    ) {
      setFindingNewVersion(true);
      try {
        const latestVersionRsp =
          await BApi.component.getDependentComponentLatestVersion({ id });

        if (!latestVersionRsp.code) {
          // @ts-ignore
          setLatestVersion(latestVersionRsp.data);
        } else {
          setLatestVersion({
            canUpdate: false,
            error: latestVersionRsp.message,
          });
        }
      } catch (e) {
        setLatestVersion({
          canUpdate: false,
          error: e.toString(),
        });
      } finally {
        setFindingNewVersion(false);
      }
    } else {
      setLatestVersion(undefined);
    }
  }, []);

  useEffect(() => {
    init();
  }, []);

  console.log(context?.name, latestVersion, discovering, context);

  const renderNewVersionInner = useCallback(() => {
    const elements: any[] = [];

    // new version
    if (latestVersion) {
      if (latestVersion.error) {
        elements.push(
          <Button
            isIconOnly
            color={"danger"}
            size={"sm"}
            variant={"light"}
            onPress={() => {
              createPortal(Modal, {
                defaultVisible: true,
                title: t<string>("configuration.dependency.failedToGetVersion"),
                children: <pre>{latestVersion.error}</pre>,
                size: "lg",
              });
            }}
          >
            <MdError className={"text-base"} />
          </Button>,
        );
      } else {
        if (latestVersion.canUpdate) {
          if (context?.status != DependentComponentStatus.Installing) {
            elements.push(
              <Button
                size={"sm"}
                color={"primary"}
                variant={"light"}
                onClick={() => {
                  BApi.component.installDependentComponent({ id });
                }}
              >
                {t<string>("configuration.dependency.clickToUpdate")}:{" "}
                {latestVersion.version}
              </Button>,
            );
          }
        } else {
          elements.push(
            <CheckCircleOutlined className={"text-base text-success"} />,
          );
        }
      }
    } else {
      if (findingNewVersion) {
        elements.push(
          <Spinner size="sm" />
        );
      }
    }

    // current status
    if (context && context.status == DependentComponentStatus.Installing) {
      elements.push(
        <>
          {t<string>("configuration.dependency.updating")}: {context.installationProgress}%
          <Spinner size="sm" />
        </>,
      );
    }
    if (context?.error) {
      elements.push(
        <Button
          isIconOnly
          color={"danger"}
          size={"sm"}
          variant={"light"}
          onPress={() => {
            createPortal(Modal, {
              defaultVisible: true,
              title: t<string>("error.title"),
              children: <pre>{context.error}</pre>,
              size: "lg",
            });
          }}
        >
          <MdError className={"text-base"} />
        </Button>,
      );
    }

    return elements;
  }, [latestVersion, context, discovering]);

  return (
    <div
      className={"third-party-component"}
      style={{
        display: "flex",
        gap: 10,
        alignItems: "center",
      }}
    >
      <div className={"installed"}>
        {discovering ? (
          <Spinner size="sm" />
        ) : (
          <Chip
            radius={"sm"}
            size={"sm"}
            title={context?.location ?? undefined}
          >
            {context?.version ?? t<string>("configuration.dependency.notInstalled")}
          </Chip>
        )}
      </div>
      {!discovering && (
        <div
          className="new-version"
          style={{
            display: "flex",
            alignItems: "center",
            gap: 5,
          }}
        >
          {renderNewVersionInner()}
        </div>
      )}
    </div>
  );
};

Component.displayName = "Component";

export default Component;
