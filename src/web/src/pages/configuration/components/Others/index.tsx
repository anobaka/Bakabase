"use client";

import type { Key } from "@react-types/shared";
import type { BakabaseInsideWorldBusinessComponentsConfigurationsModelsInputNetworkOptionsPatchInputModel } from "@/sdk/Api";

import { useTranslation } from "react-i18next";
import toast from "react-hot-toast";
import { AiOutlineQuestionCircle } from "react-icons/ai";

import BApi from "@/sdk/BApi";
import {
  Button,
  Input,
  Modal,
  Select,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  Tooltip,
  Switch,
} from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { useNetworkOptionsStore, useAppOptionsStore } from "@/stores/options";
import { OnboardingModal, useOnboarding } from "@/components/Onboarding";

enum ProxyMode {
  DoNotUse = 0,
  UseSystem = 1,
  UseCustom = 2,
}

interface OthersProps {
  applyPatches: <T>(api: (patches: T) => Promise<{ code?: number }>, patches: T, success?: (rsp: unknown) => void) => void;
}

const Others: React.FC<OthersProps> = ({ applyPatches }) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const { showOnboarding, resetOnboarding, completeOnboarding } = useOnboarding();

  const appOptions = useAppOptionsStore((state) => state.data);
  const networkOptions = useNetworkOptionsStore((state) => state.data);

  const proxies = [
    {
      label: t("configuration.others.proxy.doNotUse"),
      value: ProxyMode.DoNotUse.toString(),
    },
    {
      label: t("configuration.others.proxy.useSystem"),
      value: ProxyMode.UseSystem.toString(),
    },
    ...(networkOptions.customProxies?.map((c) => ({
      label: c.address!,
      value: c.id!,
    })) ?? []),
  ];

  let selectedProxy: Key | undefined;

  if (networkOptions?.proxy) {
    const p = networkOptions.proxy;

    if (p.mode === ProxyMode.UseCustom) {
      selectedProxy = p.customProxyId!;
    } else {
      selectedProxy = p.mode?.toString();
    }
  }

  selectedProxy ??= ProxyMode.DoNotUse.toString();

  const otherSettings = [
    {
      label: "configuration.others.proxy",
      tip: "configuration.others.proxy.tip",
      renderValue: () => {
        return (
          <div className="flex items-center gap-2">
            <div style={{ width: 300 }}>
              <Select
                dataSource={proxies}
                multiple={false}
                selectedKeys={
                  selectedProxy === undefined ? undefined : [selectedProxy]
                }
                size="sm"
                onSelectionChange={(keys) => {
                  const key = Array.from(keys)[0] as string;
                  const patches: BakabaseInsideWorldBusinessComponentsConfigurationsModelsInputNetworkOptionsPatchInputModel =
                    {};

                  if (key === ProxyMode.DoNotUse.toString()) {
                    patches.proxy = {
                      mode: ProxyMode.DoNotUse,
                      customProxyId: undefined,
                    };
                  } else {
                    if (key === ProxyMode.UseSystem.toString()) {
                      patches.proxy = {
                        mode: ProxyMode.UseSystem,
                        customProxyId: undefined,
                      };
                    } else {
                      patches.proxy = {
                        mode: ProxyMode.UseCustom,
                        customProxyId: key,
                      };
                    }
                  }
                  BApi.options.patchNetworkOptions(patches).then((x) => {
                    if (!x.code) {
                      toast.success(t("common.success.saved"));
                    }
                  });
                }}
              />
            </div>

            <Button
              color="primary"
              size="sm"
              onClick={() => {
                let p: string;

                createPortal(Modal, {
                  defaultVisible: true,
                  size: "lg",
                  title: t("configuration.others.proxy.addModal.title"),
                  children: (
                    <Input
                      placeholder={t("configuration.others.proxy.tip")}
                      onValueChange={(v) => (p = v)}
                    />
                  ),
                  onOk: async () => {
                    if (p === undefined || p.length === 0) {
                      toast.error(t("common.error.invalidData"));
                      throw new Error("Invalid data");
                    }
                    await BApi.options.patchNetworkOptions({
                      customProxies: [
                        ...(networkOptions.customProxies ?? []),
                        { address: p },
                      ],
                    });
                  },
                });
              }}
            >
              {t("common.action.add")}
            </Button>
          </div>
        );
      },
    },
    {
      label: "configuration.others.enablePreRelease",
      tip: "configuration.others.enablePreRelease.tip",
      renderValue: () => {
        return (
          <Switch
            isSelected={appOptions.enablePreReleaseChannel}
            size="sm"
            onValueChange={(checked) => {
              applyPatches(
                BApi.options.patchAppOptions,
                { enablePreReleaseChannel: checked },
              );
            }}
          />
        );
      },
    },
    {
      label: "configuration.others.enableTracking",
      tip: "configuration.others.enableTracking.tip",
      renderValue: () => {
        return (
          <Switch
            isSelected={appOptions.enableAnonymousDataTracking}
            size="sm"
            onValueChange={(checked) => {
              applyPatches(
                BApi.options.patchAppOptions,
                { enableAnonymousDataTracking: checked },
              );
            }}
          />
        );
      },
    },
    {
      label: "configuration.others.maxParallelism",
      tip: "configuration.others.maxParallelism.tip",
      renderValue: () => {
        return (
          <Input
            type="number"
            size="sm"
            min={1}
            placeholder={String(appOptions.effectiveMaxParallelism)}
            value={appOptions.maxParallelism !== undefined && appOptions.maxParallelism !== null ? String(appOptions.maxParallelism) : ""}
            className="w-24"
            onValueChange={(v) => {
              const value = v === "" ? undefined : parseInt(v, 10);
              if (value === undefined || (value >= 1 && !isNaN(value))) {
                applyPatches(
                  BApi.options.patchAppOptions,
                  { maxParallelism: value },
                );
              }
            }}
          />
        );
      },
    },
    {
      label: "onboarding.viewAgain",
      renderValue: () => {
        return (
          <Button
            color="primary"
            size="sm"
            variant="flat"
            onPress={resetOnboarding}
          >
            {t("onboarding.viewAgain")}
          </Button>
        );
      },
    },
  ];

  return (
    <div className="group">
      <OnboardingModal visible={showOnboarding} onComplete={completeOnboarding} />
      <div className="settings">
        <Table removeWrapper>
          <TableHeader>
            <TableColumn width={200}>
              {t("configuration.others.title")}
            </TableColumn>
            <TableColumn>&nbsp;</TableColumn>
          </TableHeader>
          <TableBody>
            {otherSettings.map((c, i) => {
              return (
                <TableRow
                  key={i}
                  className="hover:bg-[var(--bakaui-overlap-background)]"
                >
                  <TableCell>
                    <div className="flex items-center">
                      {c.tip ? (
                        <Tooltip
                          className="max-w-[300px]"
                          color="secondary"
                          content={t(c.tip)}
                          placement="top"
                        >
                          <div className="flex items-center gap-1">
                            {t(c.label)}
                            <AiOutlineQuestionCircle className="text-base" />
                          </div>
                        </Tooltip>
                      ) : (
                        t(c.label)
                      )}
                    </div>
                  </TableCell>
                  <TableCell>{c.renderValue()}</TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </div>
    </div>
  );
};

Others.displayName = "Others";

export default Others;
