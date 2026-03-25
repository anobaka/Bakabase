"use client";

import { useMemo } from "react";
import { useTranslation } from "react-i18next";
import {
  Button,
  Chip,
  Divider,
  Modal,
  ModalBody,
  ModalContent,
  ModalHeader,
  Switch,
  Tab,
  Tabs,
} from "@heroui/react";
import { AiOutlineDelete, AiOutlinePlus } from "react-icons/ai";

import { toast } from "@/components/bakaui";
import { useDLsiteOptionsStore } from "@/stores/options";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { FileSystemSelectorModal } from "@/components/FileSystemSelector";
import { CookieValidatorTarget, ResourceSource } from "@/sdk/constants";
import AccountsPanel, { type AccountField } from "./AccountsPanel";
import MetadataMappingPanel from "./MetadataMappingPanel";
import { LeStatusIndicator } from "@/pages/dlsite-works/components/LeStatusIndicator";

interface DLsiteConfigProps {
  onDestroyed?: () => void;
}

export default function DLsiteConfig({ onDestroyed }: DLsiteConfigProps) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const options = useDLsiteOptionsStore((s) => s.data);
  const patch = useDLsiteOptionsStore((s) => s.patch);

  const downloadDir = options?.defaultPath;
  const scanFolders = options?.scanFolders || [];

  const fields: AccountField[] = useMemo(
    () => [
      {
        key: "cookie",
        label: t("resourceSource.accounts.cookie"),
        placeholder: t("resourceSource.accounts.cookiePlaceholder"),
        type: "textarea" as const,
        cookieValidatorTarget: CookieValidatorTarget.DLsite,
      },
    ],
    [t],
  );

  const handleSave = async (accounts: any[]) => {
    await patch({ accounts });
    toast.success(t("thirdPartyConfig.success.saved"));
  };

  const handleSelectDownloadDir = () => {
    createPortal(FileSystemSelectorModal, {
      targetType: "folder",
      defaultSelectedPath: downloadDir,
      startPath: downloadDir,
      onSelected: async (e: any) => {
        await patch({ defaultPath: e.path });
      },
    });
  };

  const handleAddScanFolder = () => {
    createPortal(FileSystemSelectorModal, {
      targetType: "folder",
      onSelected: async (e: any) => {
        const updated = [...scanFolders, e.path];
        await patch({ scanFolders: updated });
      },
    });
  };

  const handleRemoveScanFolder = async (index: number) => {
    const updated = scanFolders.filter((_: string, i: number) => i !== index);
    await patch({ scanFolders: updated });
  };

  return (
    <Modal defaultOpen scrollBehavior="inside" size="5xl" onClose={onDestroyed}>
      <ModalContent>
        <ModalHeader>{t("resourceSource.dlsite.title")}</ModalHeader>
        <ModalBody className="pb-6">
          <Tabs isVertical classNames={{ panel: "flex-1 w-0" }}>
            <Tab key="accounts" title={t("resourceSource.config.tab.accounts")}>
              <AccountsPanel
                accounts={options?.accounts || []}
                fields={fields}
                onSave={handleSave}
              />
            </Tab>
            <Tab key="localFiles" title={t("resourceSource.config.tab.localFiles")}>
              <div className="space-y-4">
                <div>
                  <div className="flex items-center justify-between mb-2">
                    <span className="text-sm font-medium">
                      {t("resourceSource.dlsite.config.downloadDir")}
                    </span>
                  </div>
                  <Button
                    className="w-full justify-start"
                    size="sm"
                    variant="flat"
                    onPress={handleSelectDownloadDir}
                  >
                    {downloadDir || t("resourceSource.dlsite.config.downloadDirPlaceholder")}
                  </Button>
                </div>

                <Divider />

                <Switch
                  isSelected={options?.deleteArchiveAfterExtraction ?? false}
                  size="sm"
                  onValueChange={(v) => patch({ deleteArchiveAfterExtraction: v })}
                >
                  <span className="text-sm font-medium">
                    {t("resourceSource.dlsite.config.deleteArchiveAfterExtraction")}
                  </span>
                </Switch>

                <Divider />

                <div>
                  <div className="flex items-center justify-between mb-2">
                    <span className="text-sm font-medium">
                      {t("resourceSource.dlsite.config.scanFolders")}
                    </span>
                    <Button
                      size="sm"
                      startContent={<AiOutlinePlus />}
                      variant="flat"
                      onPress={handleAddScanFolder}
                    >
                      {t("resourceSource.dlsite.config.addScanFolder")}
                    </Button>
                  </div>
                  <p className="text-xs text-default-400 mb-2">
                    {t("resourceSource.dlsite.config.scanFoldersTip1")}
                  </p>
                  <p className="text-xs text-default-400 mb-3">
                    {t("resourceSource.dlsite.config.scanFoldersTip2")}
                  </p>
                  {scanFolders.length === 0 ? (
                    <div className="text-center py-3 text-default-400 text-sm">
                      {t("resourceSource.dlsite.config.noScanFolders")}
                    </div>
                  ) : (
                    <div className="space-y-2">
                      {scanFolders.map((folder: string, index: number) => (
                        <div
                          key={index}
                          className="flex items-center gap-2 border-small border-default-200 rounded-lg px-3 py-2"
                        >
                          <Chip className="flex-1 max-w-full" size="sm" variant="flat">
                            {folder}
                          </Chip>
                          <Button
                            color="danger"
                            isIconOnly
                            size="sm"
                            variant="light"
                            onPress={() => handleRemoveScanFolder(index)}
                          >
                            <AiOutlineDelete className="text-lg" />
                          </Button>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              </div>
            </Tab>
            <Tab key="metadata" title={t("resourceSource.config.tab.metadataSync")}>
              <MetadataMappingPanel source={ResourceSource.DLsite} />
            </Tab>
            <Tab key="le" title={t("resourceSource.config.tab.localeEmulator")}>
              <div className="py-4">
                <LeStatusIndicator />
              </div>
            </Tab>
          </Tabs>
        </ModalBody>
      </ModalContent>
    </Modal>
  );
}
