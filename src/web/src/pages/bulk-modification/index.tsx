"use client";

import type { BulkModification as BulkModificationModel } from "@/pages/bulk-modification/components/BulkModification";

import { useTranslation } from "react-i18next";
import {
  DeleteOutlined,
  EditOutlined,
  QuestionCircleOutlined,
  PlusOutlined,
} from "@ant-design/icons";
import { useCallback, useEffect, useRef, useState } from "react";
import { useUpdate } from "react-use";
import toast from "react-hot-toast";
import { AiOutlineInbox } from "react-icons/ai";

import BulkModification from "./components/BulkModification";
import {
  BulkModificationGuideModal,
  useBulkModificationGuide,
} from "./components/BulkModificationGuide";

import {
  Accordion,
  AccordionItem,
  Button,
  Chip,
  Input,
  Modal,
  Spinner,
  Tooltip,
} from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BetaChip from "@/components/Chips/BetaChip";

const BulkModification2Page = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const forceUpdate = useUpdate();
  const { showGuide, completeGuide, resetGuide } = useBulkModificationGuide();

  const [expandedKeys, setExpandedKeys] = useState<string[]>([]);
  const [bulkModifications, setBulkModifications] = useState<BulkModificationModel[]>();
  const isFirstLoad = useRef(true);

  const loadAllBulkModifications = useCallback(async () => {
    const r = await BApi.bulkModification.getAllBulkModifications();
    const bms = r.data || [];

    setBulkModifications(bms);
    if (isFirstLoad.current) {
      setExpandedKeys(bms.map((bm) => bm.id.toString()));
      isFirstLoad.current = false;
    }
  }, []);

  useEffect(() => {
    loadAllBulkModifications();
  }, []);

  const handleAdd = () => {
    BApi.bulkModification.addBulkModification().then(() => {
      loadAllBulkModifications();
    });
  };

  const renderEmptyState = () => (
    <div className="flex flex-col items-center justify-center min-h-[400px] gap-4">
      <AiOutlineInbox className="text-6xl text-default-300" />
      <div className="text-default-500 text-center">
        <p className="text-lg mb-2">{t<string>("bulkModification.empty.noData")}</p>
        <p className="text-sm text-default-400">
          {t<string>("bulkModificationGuide.welcome.description")}
        </p>
      </div>
      <Button color="primary" startContent={<PlusOutlined />} onPress={handleAdd}>
        {t<string>("bulkModification.action.add")}
      </Button>
    </div>
  );

  const renderAccordionTitle = (bm: BulkModificationModel, isExpanded: boolean) => (
    <div className="flex items-center justify-between w-full">
      {/* Left section: Name & Stats */}
      <div className="flex items-center gap-2">
        <div className="flex items-center gap-1">
          <span className="font-medium">{bm.name}</span>
          {isExpanded && (
            <Button
              isIconOnly
              size="sm"
              variant="light"
              onPress={() => {
                let newName = bm.name;

                createPortal(Modal, {
                  defaultVisible: true,
                  size: "lg",
                  title: t<string>("bulkModification.action.editName"),
                  children: (
                    <Input
                      isRequired
                      defaultValue={bm.name}
                      onValueChange={(v) => (newName = v.trim())}
                    />
                  ),
                  onOk: async () => {
                    if (newName.length === 0) {
                      toast.error(t<string>("bulkModification.error.nameEmpty"));
                      throw new Error("Name cannot be empty");
                    }
                    BApi.bulkModification
                      .patchBulkModification(bm.id, { name: newName })
                      .then((r) => {
                        if (!r.code) {
                          bm.name = newName;
                          forceUpdate();
                        }
                      });
                  },
                });
              }}
            >
              <EditOutlined className="text-base" />
            </Button>
          )}
        </div>
        <Chip size="sm" variant="flat">
          {t<string>("bulkModification.label.resourceCount", {
            count: bm.filteredResourceIds?.length || 0,
          })}
        </Chip>
        {bm.resourceDiffCount > 0 && (
          <Chip color="warning" size="sm" variant="flat">
            {bm.resourceDiffCount} {t<string>("bulkModification.label.diffs")}
          </Chip>
        )}
      </div>

      {/* Right section: Actions */}
      <div className="flex items-center gap-1">
        <Chip className="text-default-400" size="sm" variant="light">
          {bm.createdAt}
        </Chip>
        <Tooltip
          content={t<string>(
            bm.isActive
              ? "bulkModification.action.clickToDisable"
              : "bulkModification.action.clickToEnable",
          )}
        >
          <Chip
            className="cursor-pointer"
            color={bm.isActive ? "success" : "default"}
            size="sm"
            variant={bm.isActive ? "flat" : "bordered"}
            onClick={() => {
              BApi.bulkModification
                .patchBulkModification(bm.id, { isActive: !bm.isActive })
                .then(() => loadAllBulkModifications());
            }}
          >
            {t<string>(
              bm.isActive ? "bulkModification.status.enabled" : "bulkModification.status.disabled",
            )}
          </Chip>
        </Tooltip>
        <Button
          size="sm"
          variant="light"
          onPress={() => {
            BApi.bulkModification.duplicateBulkModification(bm.id).then(() => {
              loadAllBulkModifications();
            });
          }}
        >
          {t<string>("bulkModification.action.duplicate")}
        </Button>
        <Button
          isIconOnly
          color="danger"
          size="sm"
          variant="light"
          onPress={() => {
            createPortal(Modal, {
              defaultVisible: true,
              title: t<string>("bulkModification.action.delete"),
              children: t<string>("bulkModification.confirm.delete"),
              onOk: async () => {
                await BApi.bulkModification.deleteBulkModification(bm.id);
                loadAllBulkModifications();
              },
            });
          }}
        >
          <DeleteOutlined className="text-base" />
        </Button>
      </div>
    </div>
  );

  return (
    <div className="flex flex-col gap-2">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <Button color="primary" size="sm" startContent={<PlusOutlined />} onPress={handleAdd}>
            {t<string>("bulkModification.action.add")}
          </Button>
          <BetaChip />
          {bulkModifications && bulkModifications.length > 0 && (
            <span className="text-sm text-default-400">
              {t<string>("bulkModification.label.count")}: {bulkModifications.length}
            </span>
          )}
        </div>
        <Button
          color="primary"
          size="sm"
          startContent={<QuestionCircleOutlined className="text-base" />}
          variant="light"
          onPress={resetGuide}
        >
          {t<string>("bulkModificationGuide.action.showGuide")}
        </Button>
      </div>

      <BulkModificationGuideModal visible={showGuide} onComplete={completeGuide} />

      {/* Content */}
      {bulkModifications ? (
        bulkModifications.length === 0 ? (
          renderEmptyState()
        ) : (
          <Accordion
            className="p-0"
            selectedKeys={expandedKeys}
            selectionMode="multiple"
            variant="splitted"
            onSelectionChange={(keys) => {
              if (!keys) {
                setExpandedKeys([]);
              }
              setExpandedKeys(Array.from(keys).map((x) => x as string));
            }}
          >
            {bulkModifications.map((bm) => {
              const isExpanded = expandedKeys.includes(bm.id.toString());

              return (
                <AccordionItem key={bm.id.toString()} title={renderAccordionTitle(bm, isExpanded)}>
                  <BulkModification
                    bm={bm}
                    onChange={(newBm) =>
                      setBulkModifications(
                        bulkModifications.map((b) => (b.id === newBm.id ? newBm : b)),
                      )
                    }
                  />
                </AccordionItem>
              );
            })}
          </Accordion>
        )
      ) : (
        <div className="flex items-center justify-center min-h-[400px]">
          <Spinner size="lg" />
        </div>
      )}
    </div>
  );
};

BulkModification2Page.displayName = "BulkModification2Page";

export default BulkModification2Page;
