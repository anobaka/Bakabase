import { useCallback, useEffect, useId, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import type { DestroyableProps } from "@/components/bakaui/types";
import type { BakabaseModulesAcquisitionModelsDomainAcquisitionOptions as AcquisitionOptions } from "@/sdk/Api";
import {
  Accordion,
  AccordionItem,
  Button,
  Checkbox,
  Modal,
  NumberInput,
  Spinner,
  Tab,
  Tabs,
} from "@/components/bakaui";
import { FileSystemSelectorButton } from "@/components/FileSystemSelector";
import SourcePriorityConfiguration from "@/components/SourceAvailability/SourcePriorityConfiguration";
import BApi from "@/sdk/BApi";
import StorageCapacitySummary from "@/components/FileSystemSelector/components/StorageCapacitySummary";
import StoragePolicyFields, {
  defaultStoragePolicy,
  storagePolicyIsInvalid,
  storagePoliciesEqual,
  type StoragePolicy,
} from "@/components/Acquire/StoragePolicyFields";
import DeliverySettingsFields, {
  defaultDeliverySettings,
  deliverySettingsAreInvalid,
  type DeliverySettings,
} from "@/components/Acquire/DeliverySettingsFields";
import {
  defaultDeliveryContentLayout,
  deliveryContentLayoutsEqual,
} from "@/components/FileArtifacts/DeliveryContentLayoutFields";

interface Props extends DestroyableProps {
  onDone?: () => void;
  initialTab?: "directories" | "sources";
}
const gib = 1024 ** 3;
const defaultMinimumFreeSpaceBytes = 100 * gib;
const defaultConcurrency = 20;

/** The setup endpoint updates only these fields and preserves unrelated acquisition defaults. */
export default function AcquisitionSettingsModal({
  onDone,
  onDestroyed,
  initialTab = "directories",
}: Props) {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(true);
  const [selectedTab, setSelectedTab] = useState<string>(initialTab);
  const [options, setOptions] = useState<AcquisitionOptions>();
  const [inbox, setInbox] = useState("");
  const [workspace, setWorkspace] = useState("");
  const [downloadRoot, setDownloadRoot] = useState("");
  const [storage, setStorage] = useState<StoragePolicy>(defaultStoragePolicy);
  const [delivery, setDelivery] = useState<DeliverySettings>(defaultDeliverySettings);
  const [keepSupersededDeliveryFiles, setKeepSupersededDeliveryFiles] = useState(false);
  const retentionHintId = useId();
  const [limit, setLimit] = useState<number>();
  const [minimumFreeGiB, setMinimumFreeGiB] = useState(100);
  const [concurrency, setConcurrency] = useState(defaultConcurrency);
  const [loading, setLoading] = useState(true);
  const [loadFailed, setLoadFailed] = useState(false);
  const [saveFailed, setSaveFailed] = useState(false);
  const [saving, setSaving] = useState(false);
  const [prioritySaving, setPrioritySaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const requestId = useRef(0);
  const savingRef = useRef(false);
  const load = useCallback(async () => {
    const request = ++requestId.current;
    setLoading(true);
    setLoadFailed(false);
    try {
      const result = await BApi.acquisition.getAcquisitionOptions({ showErrorToast: false });
      if (request !== requestId.current) return;
      if (result.code || !result.data) throw new Error("ACQUISITION_OPTIONS_UNAVAILABLE");
      const value = result.data;
      setOptions(value);
      setConcurrency(value.concurrency ?? defaultConcurrency);
      setKeepSupersededDeliveryFiles(value.keepSupersededDeliveryFiles ?? false);
      setDownloadRoot(value.downloadRootDirectory ?? "");
      setInbox(value.inboxDirectory ?? "");
      setWorkspace(
        value.storagePolicy?.processingWorkspaceDirectory ??
          value.processingWorkspaceDirectory ??
          "",
      );
      setStorage(
        value.storagePolicy ?? {
          ...defaultStoragePolicy,
          processingWorkspaceDirectory: value.processingWorkspaceDirectory ?? "",
        },
      );
      setLimit(value.autoPurchaseLimit);
      setMinimumFreeGiB(
        (value.minimumFreeSpaceBytes ?? defaultMinimumFreeSpaceBytes) / gib,
      );
      setDelivery({
        rootDirectory: value.deliveryRootDirectory ?? "",
        directoryTemplate:
          value.deliveryDirectoryTemplate ?? defaultDeliverySettings.directoryTemplate,
        partDirectoryTemplate:
          value.deliveryPartDirectoryTemplate ?? defaultDeliverySettings.partDirectoryTemplate,
        contentLayout: value.deliveryContentLayout ?? defaultDeliveryContentLayout(),
      });
    } catch {
      if (request === requestId.current) setLoadFailed(true);
    } finally {
      if (request === requestId.current) setLoading(false);
    }
  }, []);
  useEffect(() => {
    void load();
    return () => {
      requestId.current++;
    };
  }, [load]);
  const changed = () => {
    setSaved(false);
    setSaveFailed(false);
  };
  const invalidDelivery = deliverySettingsAreInvalid(delivery);
  const invalidStorage = storagePolicyIsInvalid(storage);
  const retentionChanged =
    !!options && keepSupersededDeliveryFiles !== (options.keepSupersededDeliveryFiles ?? false);
  const contentLayoutChanged =
    !!options &&
    !deliveryContentLayoutsEqual(
      delivery.contentLayout,
      options.deliveryContentLayout ?? defaultDeliveryContentLayout(),
    );
  const storageChanged =
    !!options &&
    !storagePoliciesEqual(
      storage,
      options.storagePolicy ?? {
        ...defaultStoragePolicy,
        processingWorkspaceDirectory: options.processingWorkspaceDirectory ?? "",
      },
    );
  const invalidLimit = limit == null || !Number.isFinite(limit) || limit < 0;
  const invalidConcurrency = !Number.isInteger(concurrency) || concurrency < 1 || concurrency > 100;
  const minimumFreeSpaceBytes = Math.round(minimumFreeGiB * gib);
  const invalidReserve =
    !Number.isFinite(minimumFreeGiB) || minimumFreeGiB < 0 ||
    !Number.isSafeInteger(minimumFreeSpaceBytes);
  const dirty =
    !!options &&
    (concurrency !== (options.concurrency ?? defaultConcurrency) ||
      storageChanged ||
      contentLayoutChanged ||
      retentionChanged ||
      downloadRoot !== (options.downloadRootDirectory ?? "") ||
      inbox !== (options.inboxDirectory ?? "") ||
      workspace !==
        (options.storagePolicy?.processingWorkspaceDirectory ??
          options.processingWorkspaceDirectory ??
          "") ||
      limit !== options.autoPurchaseLimit ||
      minimumFreeSpaceBytes !== (options.minimumFreeSpaceBytes ?? defaultMinimumFreeSpaceBytes) ||
      (delivery.rootDirectory ?? "") !== (options.deliveryRootDirectory ?? "") ||
      delivery.directoryTemplate !==
        (options.deliveryDirectoryTemplate ?? defaultDeliverySettings.directoryTemplate) ||
      delivery.partDirectoryTemplate !==
        (options.deliveryPartDirectoryTemplate ?? defaultDeliverySettings.partDirectoryTemplate));
  const save = async () => {
    if (
      !options ||
      loading ||
      loadFailed ||
      savingRef.current ||
      prioritySaving ||
      invalidDelivery ||
      invalidStorage ||
      invalidLimit ||
      invalidReserve ||
      invalidConcurrency ||
      !dirty
    )
      return;
    savingRef.current = true;
    setSaving(true);
    setSaved(false);
    setSaveFailed(false);
    const nextValues = {
      concurrency,
      downloadRootDirectory: downloadRoot.trim(),
      inboxDirectory: inbox.trim() || undefined,
      autoPurchaseLimit: limit!,
      minimumFreeSpaceBytes,
      processingWorkspaceDirectory: workspace.trim(),
      deliveryRootDirectory: delivery.rootDirectory?.trim() ?? "",
      deliveryDirectoryTemplate: delivery.directoryTemplate.trim().replace(/\\/g, "/"),
      deliveryPartDirectoryTemplate: delivery.partDirectoryTemplate.trim().replace(/\\/g, "/"),
    };
    const previousValues = {
      concurrency: options.concurrency ?? defaultConcurrency,
      downloadRootDirectory: options.downloadRootDirectory?.trim() ?? "",
      inboxDirectory: options.inboxDirectory?.trim() || undefined,
      autoPurchaseLimit: options.autoPurchaseLimit,
      minimumFreeSpaceBytes: options.minimumFreeSpaceBytes ?? defaultMinimumFreeSpaceBytes,
      processingWorkspaceDirectory: (options.storagePolicy?.processingWorkspaceDirectory ?? options.processingWorkspaceDirectory ?? "").trim(),
      deliveryRootDirectory: options.deliveryRootDirectory?.trim() ?? "",
      deliveryDirectoryTemplate: (options.deliveryDirectoryTemplate ?? defaultDeliverySettings.directoryTemplate).trim().replace(/\\/g, "/"),
      deliveryPartDirectoryTemplate: (options.deliveryPartDirectoryTemplate ?? defaultDeliverySettings.partDirectoryTemplate).trim().replace(/\\/g, "/"),
    };
    // Send only edited fields, including when older settings use inherited directory defaults.
    const payload: Parameters<typeof BApi.acquisition.setUpAcquisition>[0] = Object.fromEntries(
      Object.entries(nextValues).filter(([key, value]) => value !== previousValues[key as keyof typeof previousValues]),
    );
    if (storageChanged) payload.storagePolicy = storage;
    if (retentionChanged) payload.keepSupersededDeliveryFiles = keepSupersededDeliveryFiles;
    if (contentLayoutChanged && delivery.contentLayout)
      payload.deliveryContentLayout = delivery.contentLayout;
    try {
      const result = await BApi.acquisition.setUpAcquisition(payload, { showErrorToast: false });
      if (result.code) throw new Error("ACQUISITION_SETTINGS_SAVE_FAILED");
      setOptions({
        ...options,
        ...nextValues,
        keepSupersededDeliveryFiles,
        ...(contentLayoutChanged && delivery.contentLayout
          ? { deliveryContentLayout: delivery.contentLayout } : {}),
        ...(storageChanged ? { storagePolicy: storage } : {}),
      });
      setInbox(nextValues.inboxDirectory ?? "");
      setWorkspace(nextValues.processingWorkspaceDirectory);
      setDownloadRoot(nextValues.downloadRootDirectory);
      setDelivery({
        rootDirectory: nextValues.deliveryRootDirectory,
        directoryTemplate: nextValues.deliveryDirectoryTemplate,
        partDirectoryTemplate: nextValues.deliveryPartDirectoryTemplate,
        contentLayout: delivery.contentLayout,
      });
      setSaved(true);
      onDone?.();
    } catch {
      setSaveFailed(true);
    } finally {
      savingRef.current = false;
      setSaving(false);
    }
  };

  return (
    <Modal
      visible={visible}
      onClose={() => setVisible(false)}
      onDestroyed={onDestroyed}
      title={t("acquisition.settings.title")}
      size="3xl"
      scrollBehavior="inside"
      footer={
        <Button variant="light" onPress={() => setVisible(false)}>
          {t("common.action.close")}
        </Button>
      }
    >
      <p className="text-xs text-default-500">{t("acquisition.settings.separateSaveHint")}</p>
      <Tabs
        aria-label={t("acquisition.settings.title")}
        selectedKey={selectedTab}
        onSelectionChange={(key) => {
          if (!savingRef.current && !prioritySaving) setSelectedTab(String(key));
        }}
        isDisabled={saving || prioritySaving}
        destroyInactiveTabPanel={false}
        classNames={{ panel: "px-0 pt-3", tabList: "max-w-full" }}
      >
        <Tab key="directories" title={t("acquisition.settings.tabs.directories")}>
          {loading ? (
            <div className="flex justify-center py-12">
              <Spinner aria-label={t("acquisition.settings.loading")} />
            </div>
          ) : loadFailed ? (
            <div className="space-y-3 py-6">
              <p role="alert" className="text-sm text-danger">
                {t("acquisition.settings.loadFailed")}
              </p>
              <Button size="sm" variant="flat" onPress={() => void load()}>
                {t("common.action.retry")}
              </Button>
            </div>
          ) : (
            options && (
              <div className="space-y-5">
                <NumberInput
                  label={t("acquisition.settings.concurrency.label")}
                  description={t("acquisition.settings.concurrency.description")}
                  minValue={1}
                  maxValue={100}
                  step={1}
                  value={concurrency}
                  isDisabled={saving}
                  isInvalid={invalidConcurrency}
                  errorMessage={t("acquisition.settings.concurrency.invalid")}
                  onValueChange={(value) => { setConcurrency(value); changed(); }}
                />
                <section className="space-y-2" aria-label={t("acquisition.storage.downloadRoot")}>
                  <h3 className="text-sm font-medium">{t("acquisition.storage.downloadRoot")}</h3>
                  <div className="flex flex-wrap items-center gap-2">
                    <FileSystemSelectorButton
                      key={downloadRoot || "inherited-download-root"}
                      aria-label={t("acquisition.storage.chooseDownloadRoot")}
                      className="h-auto min-h-8 max-w-full whitespace-normal break-all text-left"
                      isDisabled={saving}
                      fileSystemSelectorProps={{
                        targetType: "folder",
                        multiple: false,
                        startPath: downloadRoot || undefined,
                        defaultSelectedPath: downloadRoot || undefined,
                        onSelected: (entry) => {
                          if (entry.path?.trim()) {
                            setDownloadRoot(entry.path);
                            changed();
                          }
                        },
                      }}
                    >
                      {t("acquisition.storage.chooseDownloadRoot")}
                    </FileSystemSelectorButton>
                    {!!downloadRoot && (
                      <Button
                        size="sm"
                        variant="light"
                        isDisabled={saving}
                        onPress={() => {
                          setDownloadRoot("");
                          changed();
                        }}
                      >
                        {t("acquisition.storage.inheritDownloadRoot")}
                      </Button>
                    )}
                  </div>
                  <StorageCapacitySummary path={downloadRoot} emptyHint={t("acquisition.storage.capacity.dynamicDownload")} />
                  <p className="text-xs text-default-500">
                    {t("acquisition.storage.downloadRootHint")}
                  </p>
                </section>
                <section className="space-y-3" aria-label={t("acquisition.storage.title")}>
                  <h3 className="text-sm font-medium">{t("acquisition.storage.title")}</h3>
                  <StoragePolicyFields
                    value={storage}
                    disabled={saving}
                    workspaceAside={<StorageCapacitySummary path={storage.processingWorkspaceDirectory || downloadRoot} emptyHint={t("acquisition.storage.capacity.inputStorage")} />}
                    onChange={(value) => {
                      setStorage(value);
                      setWorkspace(value.processingWorkspaceDirectory);
                      changed();
                    }}
                  />
                  <p className="text-xs text-default-500">{t("acquisition.storage.globalScope")}</p>
                </section>
                <DeliverySettingsFields
                  value={delivery}
                  downloadRoot={downloadRoot}
                  rootAside={<StorageCapacitySummary path={delivery.rootDirectory || (downloadRoot ? `${downloadRoot.replace(/[\\/]+$/, "")}/Acquired` : undefined)} emptyHint={t("acquisition.storage.capacity.dynamicDownload")} />}
                  disabled={saving}
                  onChange={(value) => {
                    setDelivery(value);
                    changed();
                  }}
                />
                <div>
                  <Checkbox
                    aria-describedby={retentionHintId}
                    isSelected={keepSupersededDeliveryFiles}
                    isDisabled={saving}
                    onValueChange={(value) => {
                      setKeepSupersededDeliveryFiles(value);
                      changed();
                    }}
                  >
                    {t("acquisition.delivery.keepSuperseded.label")}
                  </Checkbox>
                  <p id={retentionHintId} className="pl-7 text-xs text-default-500">
                    {t("acquisition.delivery.keepSuperseded.description")}
                  </p>
                </div>
                <section className="space-y-2">
                  <NumberInput
                    label={t("acquisition.storage.minimumFreeGiB.label")}
                    description={t("acquisition.storage.minimumFreeGiB.description")}
                    minValue={0}
                    maxValue={Math.floor(Number.MAX_SAFE_INTEGER / gib)}
                    step={1}
                    formatOptions={{ maximumFractionDigits: 3 }}
                    value={minimumFreeGiB}
                    isDisabled={saving}
                    isInvalid={invalidReserve}
                    errorMessage={t("acquisition.storage.minimumFreeGiB.invalid")}
                    onValueChange={(value) => {
                      setMinimumFreeGiB(value);
                      changed();
                    }}
                  />
                  <p className="text-xs text-default-500">
                    {t("acquisition.storage.minimumFreeGiB.scope")}
                  </p>
                </section>
                <Accordion variant="bordered">
                  <AccordionItem
                    key="otherDirectories"
                    title={t("acquisition.delivery.otherSettings")}
                  >
                    <section className="space-y-2" aria-label={t("acquisition.setup.inbox.label")}>
                      <FileSystemSelectorButton
                        key={inbox}
                        className="max-w-full whitespace-normal break-all h-auto min-h-8 text-left"
                        aria-label={t("acquisition.setup.inbox.browse")}
                        isDisabled={saving}
                        fileSystemSelectorProps={{
                          targetType: "folder", multiple: false,
                          startPath: inbox || undefined, defaultSelectedPath: inbox || undefined,
                          onSelected: (entry) => {
                            if (entry.path?.trim()) { setInbox(entry.path); changed(); }
                          },
                        }}
                      >{t("acquisition.setup.inbox.browse")}</FileSystemSelectorButton>
                      <p className="text-xs leading-relaxed text-default-500">{t("acquisition.setup.inbox.explanation")}</p>
                    </section>
                  </AccordionItem>
                </Accordion>
                <div className="space-y-2">
                  <NumberInput
                    label={t("acquisition.setup.limit.label")}
                    description={t("acquisition.setup.limit.description")}
                    minValue={0}
                    value={limit}
                    isDisabled={saving}
                    isInvalid={invalidLimit}
                    onValueChange={(value) => {
                      setLimit(value);
                      changed();
                    }}
                  />
                  <p className="text-xs text-default-500">
                    {t("acquisition.settings.purchaseScope")}
                  </p>
                </div>
              </div>
            )
          )}
          <div className="mt-4 flex flex-wrap items-center gap-2">
            <Button
              color="primary"
              size="sm"
              isLoading={saving}
              isDisabled={
                !options ||
                loading ||
                loadFailed ||
                prioritySaving ||
                !dirty ||
                invalidConcurrency ||
                invalidDelivery ||
                invalidStorage ||
                invalidLimit ||
                invalidReserve
              }
              onPress={() => void save()}
            >
              {t("acquisition.settings.saveDirectories")}
            </Button>
            {saved && (
              <span role="status" className="text-xs text-success">
                {t("acquisition.settings.saved")}
              </span>
            )}
            {saveFailed && (
              <span role="alert" className="text-xs text-danger">
                {t("acquisition.settings.saveFailed")}
              </span>
            )}
          </div>
        </Tab>
        <Tab key="sources" title={t("acquisition.settings.tabs.sources")}>
          <SourcePriorityConfiguration onSavingChange={setPrioritySaving} />
        </Tab>
      </Tabs>
    </Modal>
  );
}
