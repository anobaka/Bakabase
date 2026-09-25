"use client";

import { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { usePrevious } from "react-use";
import { CheckCircleOutlined } from "@ant-design/icons";
import { MdError } from "react-icons/md";

import { Button, Spinner } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { useDependentComponentContextsStore } from "@/stores/dependentComponentContexts";
import { DependentComponentStatus } from "@/sdk/constants";
import { Chip, Modal } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

/**
 * The latest version the server reports when there is nothing it could offer on this platform
 * (e.g. no ffmpeg build is published for this runtime). It is not a version to compare against,
 * so it reads as "could not check", never as "up to date".
 */
const NotAvailableVersion = "N/A";

const Component = ({ id }: { id: string }) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const context = useDependentComponentContextsStore((state) => state.contexts).find(
    (a) => a.id == id,
  );
  const [latestVersion, setLatestVersion] = useState<{
    version?: string | null;
    description?: string | null;
    canUpdate: boolean;
    installedVersionRecognized?: boolean;
    error?: string | null;
  }>();
  const [discovering, setDiscovering] = useState(true);
  const [findingNewVersion, setFindingNewVersion] = useState(false);

  const prevStatus = usePrevious(context?.status);

  // Reads the store when it runs, not a render's snapshot of it: the effects below call it after
  // the context has changed, and a captured context would describe the component as it was.
  const init = useCallback(async () => {
    const findContext = () =>
      useDependentComponentContextsStore.getState().contexts.find((a) => a.id == id);

    if (findContext()?.isAvailableOnCurrentPlatform === false) {
      setDiscovering(false);

      return;
    }

    try {
      await BApi.component.discoverDependentComponent(id);
    } finally {
      setDiscovering(false);
    }

    if (findContext()?.isAvailableOnCurrentPlatform === false) {
      return;
    }

    // Installed components are checked too: the server caches the lookup and decides whether the
    // latest version is newer than the one installed.
    setFindingNewVersion(true);
    try {
      const latestVersionRsp = await BApi.component.getDependentComponentLatestVersion(id);

      if (!latestVersionRsp.code) {
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
        error: String(e),
      });
    } finally {
      setFindingNewVersion(false);
    }
  }, [id]);

  useEffect(() => {
    if (
      context?.status == DependentComponentStatus.Installed &&
      prevStatus == DependentComponentStatus.Installing
    ) {
      init();
    }
  }, [context?.status, prevStatus, init]);

  useEffect(() => {
    init();
  }, [init]);

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
                color={"primary"}
                size={"sm"}
                variant={"light"}
                onClick={() => {
                  BApi.component.installDependentComponent(id);
                }}
              >
                {t<string>("configuration.dependency.clickToUpdate")}: {latestVersion.version}
              </Button>,
            );
          }
        } else if (!latestVersion.version || latestVersion.version === NotAvailableVersion) {
          // The lookup failed or has nothing for this platform: that is not "up to date".
          elements.push(
            <Chip
              color={"default"}
              radius={"sm"}
              size={"sm"}
              title={latestVersion.description ?? undefined}
              variant={"flat"}
            >
              {t<string>("configuration.dependency.couldNotCheckForUpdates")}
            </Chip>,
          );
        } else if (latestVersion.installedVersionRecognized === false) {
          // The installed version (a git-date ffmpeg build, Locale Emulator's "unknown") could not
          // be compared with the latest one: nothing says it is up to date.
          elements.push(
            <Chip color={"default"} radius={"sm"} size={"sm"} variant={"flat"}>
              {t<string>("configuration.dependency.installedVersionNotRecognized")}
            </Chip>,
          );
        } else if (context?.status === DependentComponentStatus.Installed) {
          elements.push(
            <CheckCircleOutlined
              aria-label={t<string>("configuration.dependency.upToDate")}
              className={"text-base text-success"}
            />,
          );
        }
      }
    } else {
      if (findingNewVersion) {
        elements.push(<Spinner size="sm" />);
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
  }, [latestVersion, context, discovering, findingNewVersion]);

  if (context && !context.isAvailableOnCurrentPlatform) {
    return (
      <div
        className={"third-party-component"}
        style={{
          display: "flex",
          gap: 10,
          alignItems: "center",
        }}
      >
        <Chip color={"default"} radius={"sm"} size={"sm"}>
          {t<string>("configuration.dependency.notAvailableOnCurrentPlatform")}
        </Chip>
      </div>
    );
  }

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
          <Chip radius={"sm"} size={"sm"} title={context?.location ?? undefined}>
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
