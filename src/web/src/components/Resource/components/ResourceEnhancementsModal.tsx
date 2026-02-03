"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { Enhancement } from "@/components/Enhancer/models";
import type { EnhancerDescriptor } from "@/components/EnhancerSelectorV2/models";
import type { BakabaseServiceModelsViewResourceProfileViewModel } from "@/sdk/Api";

import React, { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";
import {
  ApiOutlined,
  ExclamationCircleOutlined,
  QuestionCircleOutlined,
  SyncOutlined,
} from "@ant-design/icons";
import { Dayjs } from "dayjs";

import BApi from "@/sdk/BApi";
import { createPortalOfComponent } from "@/components/utils";
import {
  Accordion,
  AccordionItem,
  Button,
  Chip,
  Listbox,
  ListboxItem,
  Modal,
  Popover,
  Snippet,
  Tab,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  Tabs,
  toast,
  Tooltip,
} from "@/components/bakaui";
import {
  EnhancementAdditionalItem,
  EnhancementRecordStatus,
  PropertyPool,
  ReservedProperty,
  ResourceProperty,
} from "@/sdk/constants";
import EnhancerOptionsModal from "@/components/EnhancerSelectorV2/components/EnhancerOptionsModal";
import PropertyValueRenderer from "@/components/Property/components/PropertyValueRenderer";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { convertFromApiValue, serializeStandardValue } from "@/components/StandardValue/helpers";
import { EnhancerDescription } from "@/components/Chips/Terms";

type ResourceProfile = BakabaseServiceModelsViewResourceProfileViewModel;

interface Props extends DestroyableProps {
  resourceId: number;
}

type EnhancementLog = {
  timestamp: string;
  level: string;
  event: string;
  message: string;
  data?: any;
};

type ResourceEnhancements = {
  enhancer: EnhancerDescriptor;
  contextCreatedAt?: string;
  contextAppliedAt?: string;
  status: EnhancementRecordStatus;
  targets: {
    target: number;
    targetName: string;
    enhancement: Enhancement;
  }[];
  dynamicTargets: {
    target: number;
    targetName: string;
    enhancements: Enhancement[];
  }[];
  logs?: EnhancementLog[];
  optionsSnapshot?: any;
  errorMessage?: string;
};

type ConfigureEnhancerButtonProps = {
  enhancer: EnhancerDescriptor;
  onConfigure: (profile: ResourceProfile, enhancer: EnhancerDescriptor) => void;
  getProfiles: (enhancer: EnhancerDescriptor) => Promise<ResourceProfile[]>;
};

const ConfigureEnhancerButton = ({
  enhancer,
  onConfigure,
  getProfiles,
}: ConfigureEnhancerButtonProps) => {
  const { t } = useTranslation();
  const [profiles, setProfiles] = useState<ResourceProfile[]>([]);
  const [loading, setLoading] = useState(false);
  const [isOpen, setIsOpen] = useState(false);

  const handleClick = async () => {
    setLoading(true);
    try {
      const profilesWithEnhancer = await getProfiles(enhancer);

      if (profilesWithEnhancer.length === 0) {
        toast.default(t<string>("enhancer.records.noProfilesFound.warning"));
        return;
      }

      if (profilesWithEnhancer.length === 1) {
        // 只有一个 profile，直接打开配置
        onConfigure(profilesWithEnhancer[0], enhancer);
        return;
      }

      // 有多个 profiles，显示下拉列表
      setProfiles(profilesWithEnhancer);
      setIsOpen(true);
    } finally {
      setLoading(false);
    }
  };

  if (profiles.length > 1 && isOpen) {
    return (
      <Popover
        isOpen={isOpen}
        onOpenChange={setIsOpen}
        placement="bottom"
        trigger={
          <Button
            color="primary"
            size="sm"
            variant="light"
            isLoading={loading}
            onClick={handleClick}
          >
            {t<string>("enhancer.records.configure.action")}
          </Button>
        }
      >
        <Listbox
          aria-label={t<string>("enhancer.records.selectProfile.label")}
          onAction={(key) => {
            const profile = profiles.find((p) => p.id === Number(key));
            if (profile) {
              onConfigure(profile, enhancer);
              setIsOpen(false);
            }
          }}
        >
          {profiles.map((profile) => (
            <ListboxItem key={profile.id!}>
              {profile.name}
            </ListboxItem>
          ))}
        </Listbox>
      </Popover>
    );
  }

  return (
    <Button
      color="primary"
      size="sm"
      variant="light"
      isLoading={loading}
      onClick={handleClick}
    >
      {t<string>("enhancer.records.configure.action")}
    </Button>
  );
};

type EmptyStateProps = {
  type: "no-profile" | "no-enhancements";
  enhancerName: string;
  onConfigureClick?: () => void;
  onNavigateToProfiles?: () => void;
};

const EmptyState = ({ type, enhancerName, onConfigureClick, onNavigateToProfiles }: EmptyStateProps) => {
  const { t } = useTranslation();

  if (type === "no-profile") {
    return (
      <div className={"flex flex-col items-center justify-center py-12 gap-4"}>
        <div className={"text-center text-default-500"}>
          <div className={"text-lg font-semibold mb-2"}>
            {t<string>("enhancer.records.noProfiles.title")}
          </div>
          <div className={"text-sm"}>
            {t<string>(
              "enhancer.records.noProfiles.description",
              { enhancerName }
            )}
          </div>
        </div>
        <Button
          color={"primary"}
          variant={"flat"}
          onClick={onNavigateToProfiles}
        >
          {t<string>("enhancer.records.goToProfiles.action")}
        </Button>
      </div>
    );
  }

  if (type === "no-enhancements") {
    return (
      <div className={"flex flex-col items-center justify-center py-12 gap-4"}>
        <div className={"text-center text-default-500"}>
          <div className={"text-lg font-semibold mb-2"}>
            {t<string>("enhancer.records.noData.title")}
          </div>
          <div className={"text-sm"}>
            {t<string>(
              "enhancer.records.noData.description"
            )}
          </div>
        </div>
        {onConfigureClick && (
          <Button
            color={"primary"}
            variant={"flat"}
            onClick={onConfigureClick}
          >
            {t<string>("enhancer.records.enhanceNow.action")}
          </Button>
        )}
      </div>
    );
  }

  return null;
};

type EnhancerTabData = {
  enhancer: EnhancerDescriptor;
  profiles: ResourceProfile[];
  enhancement?: ResourceEnhancements;
};

function ResourceEnhancementsModal({ resourceId, ...props }: Props) {
  const [enhancerTabs, setEnhancerTabs] = useState<EnhancerTabData[]>([]);
  const [resource, setResource] = useState<{
    path: string;
    categoryId: number;
  }>({
    path: "",
    categoryId: 0,
  });
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { createPortal } = useBakabaseContext();
  const [enhancing, setEnhancing] = useState(false);
  const [applyingContext, setApplyingContext] = useState(false);

  useEffect(() => {
    loadEnhancements();

    BApi.resource.getResourcesByKeys({ ids: [resourceId] }).then((r) => {
      const data = r.data || [];

      setResource({
        path: data[0]?.path || "",
        categoryId: data[0]?.categoryId ?? 0,
      });
    });
  }, []);

  const loadEnhancements = useCallback(async () => {
    // 1. Get enhancement data (historical records)
    const enhancementsResponse = await BApi.resource.getResourceEnhancements(resourceId, {
      additionalItem: EnhancementAdditionalItem.GeneratedPropertyValue,
    });
    const enhancementsData = enhancementsResponse.data || [];

    // Process enhancement data and collect enhancers
    const enhancerMap = new Map<number, EnhancerDescriptor>();
    for (const d of enhancementsData) {
      d.dynamicTargets?.forEach((dt) => {
        dt.enhancements?.forEach((e) => {
          e.value = convertFromApiValue(e.value, e.valueType!);
          const v = e.customPropertyValue;
          const p = e.property;

          if (p && v) {
            v.value = convertFromApiValue(v.value, p.dbValueType!);
            v.bizValue = convertFromApiValue(v.bizValue, p.bizValueType!);
          }
        });
      });

      // Collect enhancer info from enhancements
      enhancerMap.set(d.enhancer.id, d.enhancer);
    }

    // 2. Get all matching resource profiles
    const profilesResponse = await BApi.resourceProfile.getMatchingProfilesForResource(resourceId);
    const allProfiles = (profilesResponse.data || []) as ResourceProfile[];

    // Extract enhancers from profiles and group profiles by enhancer
    const profilesByEnhancerId = new Map<number, ResourceProfile[]>();
    for (const profile of allProfiles) {
      const enhancers = profile.enhancerOptions?.enhancers || [];
      for (const enhancerOption of enhancers) {
        const enhancerId = enhancerOption.enhancerId;
        if (!enhancerId) continue;

        // Store profile for this enhancer
        if (!profilesByEnhancerId.has(enhancerId)) {
          profilesByEnhancerId.set(enhancerId, []);
        }
        profilesByEnhancerId.get(enhancerId)!.push(profile);
      }
    }

    // Note: Enhancers from profiles without enhancement data cannot be shown
    // because we need enhancer descriptor which is only available from enhancement data
    // This is a limitation of the current API

    // 3. Create tab data combining all enhancers
    const tabsData: EnhancerTabData[] = Array.from(enhancerMap.values()).map((enhancer) => ({
      enhancer,
      profiles: profilesByEnhancerId.get(enhancer.id) || [],
      // @ts-ignore - API type is compatible but TS doesn't recognize it
      enhancement: enhancementsData.find((e) => e.enhancer.id === enhancer.id),
    }));

    setEnhancerTabs(tabsData);
  }, []);

  const openEnhancerOptionsForProfile = (
    profile: ResourceProfile,
    enhancer: EnhancerDescriptor,
  ) => {
    // 从 profile 中获取该 enhancer 的配置
    const enhancerOption = profile.enhancerOptions?.enhancers?.find(
      (opt) => opt.enhancerId === enhancer.id,
    );

    // 打开 EnhancerOptionsModal
    createPortal(EnhancerOptionsModal, {
      enhancer,
      options: enhancerOption
        ? {
            targetOptions: enhancerOption.targetOptions as any,
            expressions: enhancerOption.expressions ?? undefined,
            requirements: enhancerOption.requirements ?? undefined,
            keywordProperty: enhancerOption.keywordProperty as any,
            pretreatKeyword: enhancerOption.pretreatKeyword ?? undefined,
          }
        : undefined,
      onSubmit: async (options) => {
        // 更新 profile 的 enhancer 配置
        const updatedEnhancers = (profile.enhancerOptions?.enhancers || []).map((opt) =>
          opt.enhancerId === enhancer.id
            ? {
                enhancerId: enhancer.id,
                targetOptions: options.targetOptions as any,
                expressions: options.expressions,
                requirements: options.requirements,
                keywordProperty: options.keywordProperty as any,
                pretreatKeyword: options.pretreatKeyword,
              }
            : opt,
        );

        // 构建更新的 profile 数据
        const updatedProfile = {
          ...profile,
          enhancerOptions: { enhancers: updatedEnhancers },
        };

        // 转换为 API 输入格式
        const inputModel = {
          name: updatedProfile.name ?? "",
          search: updatedProfile.search,
          nameTemplate: updatedProfile.nameTemplate,
          enhancerOptions: updatedProfile.enhancerOptions,
          playableFileOptions: updatedProfile.playableFileOptions,
          playerOptions: updatedProfile.playerOptions,
          propertyOptions: updatedProfile.propertyOptions,
          priority: updatedProfile.priority ?? 0,
        };

        await BApi.resourceProfile.updateResourceProfile(profile.id!, inputModel as any);

        toast.success(t<string>("enhancer.records.configUpdated.label"));

        // 重新加载 enhancements 以显示更新后的配置
        loadEnhancements();
      },
    });
  };

  const getProfilesForEnhancer = async (
    enhancer: EnhancerDescriptor
  ): Promise<ResourceProfile[]> => {
    // 1. 获取匹配的 resource profiles
    const profilesResponse = await BApi.resourceProfile.getMatchingProfilesForResource(resourceId);
    const profiles = (profilesResponse.data || []) as ResourceProfile[];

    // 2. 过滤出包含该 enhancer 的 profiles
    const profilesWithEnhancer = profiles.filter((profile) =>
      profile.enhancerOptions?.enhancers?.some((opt) => opt.enhancerId === enhancer.id)
    );

    return profilesWithEnhancer;
  };

  console.log(enhancerTabs);

  const renderConvertedValue = (e: Enhancement | undefined) => {
    if (!e || e.propertyPool == undefined || e.propertyId == undefined || e.property == undefined) {
      return;
    }

    const { property } = e;

    switch (e.propertyPool) {
      case PropertyPool.Reserved: {
        const rv = e.reservedPropertyValue;

        if (!rv) {
          return;
        }
        let bizRv: any;
        let dbRv: any;

        switch (e.propertyId as ResourceProperty) {
          case ResourceProperty.Introduction:
            bizRv = rv.introduction;
            dbRv = rv.introduction;
            break;
          case ResourceProperty.Rating:
            bizRv = rv.rating;
            dbRv = rv.rating;
            break;
          case ResourceProperty.Cover:
            bizRv = rv.coverPaths;
            dbRv = rv.coverPaths;
            break;
          case ResourceProperty.PlayedAt:
            bizRv = new Dayjs(rv.playedAt);
            dbRv = new Dayjs(rv.playedAt);
            break;
          default:
            return t<string>("enhancer.records.unsupportedReservedType.error", {
              type: e.propertyId,
            });
        }

        return (
          <PropertyValueRenderer
            bizValue={serializeStandardValue(bizRv, property.bizValueType)}
            dbValue={serializeStandardValue(dbRv, property.dbValueType)}
            property={property}
            variant={"light"}
          />
        );
      }
      case PropertyPool.Custom: {
        const pv = e.customPropertyValue;

        if (!pv) {
          return;
        }

        return (
          <PropertyValueRenderer
            bizValue={serializeStandardValue(pv.bizValue, property!.bizValueType)}
            dbValue={serializeStandardValue(pv.value, property!.dbValueType)}
            property={property}
            variant={"light"}
          />
        );
      }
      case PropertyPool.Internal:
      case PropertyPool.All:
        return t<string>("enhancer.records.unsupportedPropertyType.error", {
          type: e.propertyPool,
        });
    }
  };

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["cancel"],
        cancelProps: {
          children: t<string>("common.action.close"),
        }
      }}
      size={"7xl"}
      title={t<string>("enhancer.records.title")}
      onDestroyed={props.onDestroyed}
    >
      <div className={"flex items-center gap-2"}>
        <div>{t<string>("enhancer.records.resourcePath.label")}</div>
        <Snippet size={"sm"} symbol={""}>
          {resource.path}
        </Snippet>
      </div>
      {enhancerTabs.length === 0 ? (
        <div className={"flex flex-col items-center justify-center py-16 gap-6"}>
          <div className={"bg-default-100 dark:bg-default-50 p-4 rounded-lg max-w-2xl"}>
            <EnhancerDescription />
          </div>
          <div className={"text-center text-default-500"}>
            <div className={"text-lg font-semibold mb-2"}>
              {t<string>("enhancer.records.noEnhancers.title")}
            </div>
            <div className={"text-sm"}>
              {t<string>(
                "enhancer.records.noEnhancers.description"
              )}
            </div>
          </div>
          <Button
            color={"primary"}
            variant={"flat"}
            onClick={() => navigate("/resource-profile")}
          >
            {t<string>("enhancer.records.goToProfiles.action")}
          </Button>
        </div>
      ) : (
      <Tabs
        isVertical
        aria-label="Enhancers"
        classNames={{
          panel: "grow min-w-0",
        }}
        variant={"bordered"}
      >
        {enhancerTabs.map((tabData) => {
          const { enhancer, profiles, enhancement } = tabData;
          const hasProfiles = profiles.length > 0;
          const targets = enhancement?.targets || [];
          const hasEnhancementData =
            enhancement?.contextCreatedAt ||
            targets.some((x) => x.enhancement) ||
            (enhancement?.dynamicTargets && enhancement.dynamicTargets.some((dt) => dt.enhancements && dt.enhancements.length > 0));

          return (
            <Tab key={enhancer.id} title={enhancer.name}>
              {!hasEnhancementData ? (
                <EmptyState
                  type="no-enhancements"
                  enhancerName={enhancer.name}
                  onConfigureClick={() => {
                    setEnhancing(true);
                    BApi.resource
                      .enhanceResourceByEnhancer(resourceId, enhancer.id)
                      .then(() => {
                        loadEnhancements();
                        setEnhancing(false);
                      });
                  }}
                  onNavigateToProfiles={() => navigate("/resource-profiles")}
                />
              ) : (
                <>
              {!hasProfiles && (
                <div className={"mb-4 p-4 bg-warning-50 dark:bg-warning-900/20 rounded-lg border border-warning-200 dark:border-warning-800"}>
                  <div className={"flex items-start gap-2"}>
                    <ExclamationCircleOutlined className={"text-warning-600 dark:text-warning-400 text-lg mt-0.5"} />
                    <div className={"flex-1"}>
                      <div className={"text-warning-800 dark:text-warning-300 font-semibold mb-1"}>
                        {t<string>("enhancer.records.notConfigured.title")}
                      </div>
                      <div className={"text-warning-700 dark:text-warning-400 text-sm"}>
                        {t<string>(
                          "enhancer.records.notConfigured.description"
                        )}
                      </div>
                      <Button
                        className={"mt-2"}
                        color={"warning"}
                        size={"sm"}
                        variant={"flat"}
                        onClick={() => navigate("/resource-profiles")}
                      >
                        {t<string>("enhancer.records.goToProfiles.action")}
                      </Button>
                    </div>
                  </div>
                </div>
              )}
              <div className={"flex items-center justify-between"}>
                <div className={"flex items-center gap-2"}>
                  <div className={"flex items-center gap-2"}>
                    <Chip
                      // size={'sm'}
                      color={"secondary"}
                      radius={"sm"}
                      variant={"light"}
                    >
                      <div className={"flex items-center gap-1"}>
                        {t<string>("enhancer.records.dataCreatedAt.label")}
                        <Tooltip
                          className={"max-w-[500px]"}
                          color={"secondary"}
                          content={t<string>(
                            "enhancer.records.dataCreatedAt.tip",
                          )}
                          placement={"top"}
                        >
                          <QuestionCircleOutlined className={"text-base"} />
                        </Tooltip>
                      </div>
                    </Chip>
                    {enhancement?.contextCreatedAt ?? t<string>("common.label.none")}
                  </div>
                  <div className={"flex items-center gap-2"}>
                    <Chip
                      // size={'sm'}
                      color={"secondary"}
                      radius={"sm"}
                      variant={"light"}
                    >
                      <div className={"flex items-center gap-1"}>
                        {t<string>("enhancer.records.dataAppliedAt.label")}
                        <Tooltip
                          className={"max-w-[500px]"}
                          color={"secondary"}
                          content={t<string>(
                            "enhancer.records.dataAppliedAt.tip",
                          )}
                          placement={"top"}
                        >
                          <QuestionCircleOutlined className={"text-base"} />
                        </Tooltip>
                      </div>
                    </Chip>
                    {enhancement?.contextAppliedAt ?? t<string>("common.label.none")}
                  </div>
                  <Tooltip
                    className={"max-w-[500px]"}
                    color={"secondary"}
                    content={t<string>(
                      "enhancer.records.enhanceProcess.tip",
                    )}
                  >
                    <Button
                      color={
                        enhancement?.status == EnhancementRecordStatus.ContextApplied ||
                        enhancement?.status == EnhancementRecordStatus.ContextCreated
                          ? "warning"
                          : "primary"
                      }
                      isLoading={enhancing}
                      size={"sm"}
                      variant={"light"}
                      onClick={() => {
                        setEnhancing(true);
                        BApi.resource
                          .enhanceResourceByEnhancer(resourceId, enhancer.id)
                          .then(() => {
                            loadEnhancements();
                            setEnhancing(false);
                          });
                      }}
                    >
                      <SyncOutlined className={"text-base"} />
                      {t<string>(
                        enhancement?.status == EnhancementRecordStatus.ContextApplied
                          ? "enhancer.records.reEnhance.action"
                          : "enhancer.records.enhanceNow.action",
                      )}
                    </Button>
                  </Tooltip>
                  {(enhancement?.status == EnhancementRecordStatus.ContextApplied ||
                    enhancement?.status == EnhancementRecordStatus.ContextCreated) && (
                    <Tooltip
                      color={"secondary"}
                      content={t<string>("enhancer.records.applyData.tip")}
                    >
                      <Button
                        color={"primary"}
                        isLoading={applyingContext}
                        size={"sm"}
                        variant={"light"}
                        onClick={() => {
                          setApplyingContext(true);
                          BApi.resource
                            .applyEnhancementContextDataForResourceByEnhancer(
                              resourceId,
                              enhancer.id,
                            )
                            .then(() => {
                              loadEnhancements();
                              setApplyingContext(false);
                            });
                        }}
                      >
                        <ApiOutlined className={"text-base"} />
                        {t<string>("enhancer.records.applyData.action")}
                      </Button>
                    </Tooltip>
                  )}
                </div>
                {hasProfiles && (
                  <div className={"flex items-center gap-2"}>
                    <ConfigureEnhancerButton
                      enhancer={enhancer}
                      onConfigure={openEnhancerOptionsForProfile}
                      getProfiles={getProfilesForEnhancer}
                    />
                  </div>
                )}
              </div>
              {/* Error Message Display */}
              {enhancement?.errorMessage && (
                <div className={"mb-4 p-4 bg-danger-50 dark:bg-danger-900/20 rounded-lg border border-danger-200 dark:border-danger-800"}>
                  <div className={"flex items-start gap-2"}>
                    <ExclamationCircleOutlined className={"text-danger-600 dark:text-danger-400 text-lg mt-0.5"} />
                    <div className={"flex-1"}>
                      <div className={"text-danger-800 dark:text-danger-300 font-semibold mb-1"}>
                        {t<string>("enhancer.records.error.title")}
                      </div>
                      <div className={"text-danger-700 dark:text-danger-400 text-sm whitespace-pre-wrap"}>
                        {enhancement.errorMessage}
                      </div>
                    </div>
                  </div>
                </div>
              )}

              {/* Enhancement Logs Section */}
              {enhancement?.logs && enhancement.logs.length > 0 && (
                <Accordion className={"mb-4"} variant={"bordered"}>
                  <AccordionItem
                    key="logs"
                    aria-label={t<string>("enhancer.logs.title")}
                    title={
                      <div className={"flex items-center gap-2"}>
                        <span>{t<string>("enhancer.logs.title")}</span>
                        <Chip size={"sm"} variant={"flat"}>{enhancement.logs.length}</Chip>
                      </div>
                    }
                  >
                    <div className={"max-h-[400px] overflow-y-auto"}>
                      <Table isStriped aria-label={t<string>("enhancer.logs.title")}>
                        <TableHeader>
                          <TableColumn width={160}>{t<string>("enhancer.logs.timestamp.label")}</TableColumn>
                          <TableColumn width={80}>{t<string>("enhancer.logs.level.label")}</TableColumn>
                          <TableColumn width={120}>{t<string>("enhancer.logs.event.label")}</TableColumn>
                          <TableColumn>{t<string>("common.label.message")}</TableColumn>
                          <TableColumn width={200}>{t<string>("enhancer.logs.data.label")}</TableColumn>
                        </TableHeader>
                        <TableBody>
                          {enhancement.logs.map((log, index) => (
                            <TableRow key={index}>
                              <TableCell>
                                <span className={"text-xs text-default-500"}>
                                  {new Date(log.timestamp).toLocaleString()}
                                </span>
                              </TableCell>
                              <TableCell>
                                <Chip
                                  size={"sm"}
                                  color={
                                    log.level === "Error" ? "danger" :
                                    log.level === "Warning" ? "warning" : "primary"
                                  }
                                  variant={"flat"}
                                >
                                  {log.level}
                                </Chip>
                              </TableCell>
                              <TableCell>
                                <Chip size={"sm"} variant={"light"}>
                                  {log.event}
                                </Chip>
                              </TableCell>
                              <TableCell>
                                <span className={"text-sm"}>{log.message}</span>
                              </TableCell>
                              <TableCell>
                                {log.data && (
                                  <Popover
                                    placement={"left"}
                                    trigger={
                                      <span className={"text-xs text-primary cursor-pointer underline"}>
                                        {t<string>("enhancer.logs.viewData.action")}
                                      </span>
                                    }
                                  >
                                    <div className={"p-2"}>
                                      <div className={"flex justify-end mb-2"}>
                                        <Button
                                          size={"sm"}
                                          variant={"light"}
                                          onClick={() => {
                                            navigator.clipboard.writeText(JSON.stringify(log.data, null, 2));
                                            toast.success(t<string>("enhancer.logs.copiedToClipboard.label"));
                                          }}
                                        >
                                          {t<string>("common.action.copy")}
                                        </Button>
                                      </div>
                                      <pre className={"text-xs max-w-[400px] max-h-[300px] overflow-auto whitespace-pre-wrap bg-default-100 p-2 rounded"}>
                                        {JSON.stringify(log.data, null, 2)}
                                      </pre>
                                    </div>
                                  </Popover>
                                )}
                              </TableCell>
                            </TableRow>
                          ))}
                        </TableBody>
                      </Table>
                    </div>
                  </AccordionItem>
                </Accordion>
              )}

              <div className={"flex flex-col gap-y-2 min-w-0"}>
                <div>
                  {targets.every((x) => !x.enhancement) && (
                    <Chip className={"opacity-60"} radius={"sm"} size={"sm"} variant={"light"}>
                      <ExclamationCircleOutlined className={"text-sm mr-1"} />
                      {t<string>(
                        "enhancer.records.noFixedData.warning",
                      )}
                    </Chip>
                  )}
                  <Table isStriped className={"break-all"}>
                    <TableHeader>
                      <TableColumn>{t<string>("enhancer.target.label")}</TableColumn>
                      <TableColumn>{t<string>("enhancer.records.rawData.label")}</TableColumn>
                      <TableColumn>{t<string>("enhancer.records.generatedValue.label")}</TableColumn>
                    </TableHeader>
                    <TableBody>
                      {targets.map((e) => {
                        return (
                          <TableRow>
                            <TableCell>{e.targetName}</TableCell>
                            <TableCell>{JSON.stringify(e.enhancement?.value)}</TableCell>
                            <TableCell>{renderConvertedValue(e.enhancement)}</TableCell>
                          </TableRow>
                        );
                      })}
                    </TableBody>
                  </Table>
                </div>
                {enhancement?.dynamicTargets?.map((dt) => {
                  return (
                    <div>
                      {(!dt.enhancements || dt.enhancements.length == 0) && (
                        <Chip className={"opacity-60"} radius={"sm"} size={"sm"} variant={"light"}>
                          <ExclamationCircleOutlined className={"text-sm mr-1"} />
                          {t<string>(
                            "enhancer.records.noDynamicData.warning",
                          )}
                        </Chip>
                      )}
                      <Table isStriped className={"break-all"}>
                        <TableHeader>
                          <TableColumn>
                            {dt.targetName}({dt.enhancements?.length ?? 0})
                          </TableColumn>
                          <TableColumn>{t<string>("enhancer.records.rawData.label")}</TableColumn>
                          <TableColumn>{t<string>("enhancer.records.generatedValue.label")}</TableColumn>
                        </TableHeader>
                        <TableBody>
                          {dt.enhancements?.map((e) => {
                            return (
                              <TableRow>
                                <TableCell>{e.dynamicTarget}</TableCell>
                                <TableCell>{JSON.stringify(e.value)}</TableCell>
                                <TableCell>{renderConvertedValue(e)}</TableCell>
                              </TableRow>
                            );
                          })}
                        </TableBody>
                      </Table>
                    </div>
                  );
                })}
              </div>
              </>
              )}
            </Tab>
          );
        })}
      </Tabs>
      )}
    </Modal>
  );
}

export default ResourceEnhancementsModal;
