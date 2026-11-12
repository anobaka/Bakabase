"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { AcquisitionDriveKind } from "@/sdk/constants";

import React from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineFolderOpen } from "react-icons/ai";

import { driveOptions, normalizePreferredDrives } from "./driveOptions";

import BApi from "@/sdk/BApi";
import { HelpCenterButton } from "@/components/HelpCenter";
import { Button, Input, Modal, NumberInput, toast } from "@/components/bakaui";
import { FileSystemSelectorModal } from "@/components/FileSystemSelector";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

interface Props extends DestroyableProps {
  onDone?: () => void;
}

/** Configure manual collection and purchase preferences without changing delivery paths. */
const SetupWizard = ({ onDone, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [step, setStep] = React.useState(0);
  const [inbox, setInbox] = React.useState("");
  const [drives, setDrives] = React.useState<AcquisitionDriveKind[]>([]);
  const [limit, setLimit] = React.useState(0);
  const [saving, setSaving] = React.useState(false);

  React.useEffect(() => {
    void BApi.acquisition.getAcquisitionOptions().then((r) => {
      const o = r.data;

      if (!o) return;
      setInbox(o.inboxDirectory ?? "");
      setDrives(normalizePreferredDrives(o.preferredDriveKinds ?? []));
      setLimit(o.autoPurchaseLimit ?? 0);
    });
  }, []);

  const chooseDirectory = (path: string, onSelected: (value: string) => void) => {
    createPortal(FileSystemSelectorModal, {
      targetType: "folder",
      multiple: false,
      startPath: path.trim() || undefined,
      defaultSelectedPath: path.trim() || undefined,
      onSelected: (entry) => {
        if (entry.path) onSelected(entry.path);
      },
    });
  };

  const finish = async () => {
    setSaving(true);
    try {
      const rsp = await BApi.acquisition.setUpAcquisition({
        inboxDirectory: inbox.trim() || undefined,
        preferredDriveKinds: normalizePreferredDrives(drives),
        autoPurchaseLimit: limit,
      });

      if (!rsp.code) {
        toast.success(t<string>("acquisition.setup.done"));
        onDone?.();
        onDestroyed?.();
      }
    } finally {
      setSaving(false);
    }
  };

  const steps = [
    <div key="inbox" className="flex flex-col gap-2">
      <p className="rounded-lg bg-primary/5 p-3 text-sm text-default-600">
        {t<string>("acquisition.setup.inbox.explanation")}
      </p>
      <Input
        description={t<string>("acquisition.setup.inbox.description")}
        endContent={
          <Button
            isIconOnly
            aria-label={t<string>("acquisition.setup.inbox.browse")}
            size="sm"
            variant="light"
            onPress={() => chooseDirectory(inbox, setInbox)}
          >
            <AiOutlineFolderOpen className="text-lg" />
          </Button>
        }
        label={t<string>("acquisition.setup.inbox.label")}
        value={inbox}
        onValueChange={setInbox}
      />
      <p className="text-xs text-default-500">{t<string>("acquisition.setup.inbox.example")}</p>
      <HelpCenterButton
        className="self-start"
        concept="inbox"
        label={t<string>("acquisition.setup.inbox.help")}
        topic="acquisition"
      />
    </div>,
    <div key="drives" className="flex flex-col gap-3">
      <div
        aria-label={t<string>("acquisition.setup.drives.label")}
        className="flex flex-col gap-2"
        role="group"
      >
        <div className="text-sm font-medium">{t<string>("acquisition.setup.drives.label")}</div>
        <div className="flex flex-col gap-1 rounded-lg bg-default-100 p-3">
          <p className="text-sm text-default-600">
            {t<string>("acquisition.setup.drives.description")}
          </p>
          <p className="text-xs text-default-500">{t<string>("acquisition.setup.drives.scope")}</p>
        </div>
        <div className="flex flex-wrap gap-2">
          {driveOptions.map(({ value, labelKey, icon: Icon }) => {
            const priority = drives.indexOf(value);
            const selected = priority >= 0;

            return (
              <Button
                key={value}
                aria-pressed={selected}
                color={selected ? "primary" : "default"}
                endContent={
                  selected ? (
                    <span className="text-xs tabular-nums">{priority + 1}</span>
                  ) : undefined
                }
                size="sm"
                startContent={<Icon aria-hidden className="text-base" />}
                variant="flat"
                onPress={() =>
                  setDrives((current) =>
                    selected ? current.filter((drive) => drive !== value) : [...current, value],
                  )
                }
              >
                {t<string>(labelKey)}
              </Button>
            );
          })}
        </div>
        <p className="text-xs text-default-500">{t<string>("acquisition.setup.drives.hint")}</p>
      </div>
      <NumberInput
        description={t<string>("acquisition.setup.limit.description")}
        label={t<string>("acquisition.setup.limit.label")}
        minValue={0}
        value={limit}
        onValueChange={setLimit}
      />
    </div>,
  ];

  const isLast = step === steps.length - 1;

  return (
    <Modal
      defaultVisible
      footer={
        <div className="flex w-full items-center gap-2">
          <span className="text-xs text-default-400">
            {t<string>("acquisition.setup.step", { current: step + 1, total: steps.length })}
          </span>
          <div className="ml-auto flex gap-2">
            {step > 0 && (
              <Button size="sm" variant="flat" onPress={() => setStep(step - 1)}>
                {t<string>("acquisition.setup.back")}
              </Button>
            )}
            <Button
              color="primary"
              isDisabled={saving}
              size="sm"
              onPress={() => (isLast ? finish() : setStep(step + 1))}
            >
              {t<string>(isLast ? "acquisition.setup.finish" : "acquisition.setup.next")}
            </Button>
          </div>
        </div>
      }
      size="lg"
      title={t<string>("acquisition.setup.title")}
      onDestroyed={onDestroyed}
    >
      {steps[step]}
    </Modal>
  );
};

export default SetupWizard;
