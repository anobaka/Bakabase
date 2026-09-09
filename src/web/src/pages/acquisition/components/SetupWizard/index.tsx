"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { AcquisitionDriveKind } from "@/sdk/constants";

import React from "react";
import { useTranslation } from "react-i18next";

import BApi from "@/sdk/BApi";
import { Button, Chip, Input, Modal, NumberInput, Select, toast } from "@/components/bakaui";
import { AcquisitionDriveKindLabel, acquisitionDriveKinds } from "@/sdk/constants";

interface Props extends DestroyableProps {
  onDone?: () => void;
}

const TEMPLATES = ["{Title}", "{LeadKind}/{Title}", "{Date} {Title}"];

/**
 * Four questions, all with defaults. The point is that someone who installed this an hour ago can
 * have their first game filed automatically, and the only thing they have to understand is where
 * their downloads go and where their games live — the path mark that makes the second one mean
 * something is created for them.
 */
const SetupWizard = ({ onDone, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const [step, setStep] = React.useState(0);
  const [inbox, setInbox] = React.useState("");
  const [library, setLibrary] = React.useState("");
  const [template, setTemplate] = React.useState(TEMPLATES[0]);
  const [drives, setDrives] = React.useState<AcquisitionDriveKind[]>([]);
  const [limit, setLimit] = React.useState(0);
  const [saving, setSaving] = React.useState(false);

  React.useEffect(() => {
    void BApi.acquisition.getAcquisitionOptions().then((r) => {
      const o = r.data;

      if (!o) return;
      setInbox(o.inboxDirectory ?? "");
      setLibrary(o.libraryRootDirectory ?? "");
      setTemplate(o.directoryTemplate ?? TEMPLATES[0]);
      setDrives(o.preferredDriveKinds ?? []);
      setLimit(o.autoPurchaseLimit ?? 0);
    });
  }, []);

  const finish = async () => {
    setSaving(true);
    try {
      const rsp = await BApi.acquisition.setUpAcquisition({
        inboxDirectory: inbox.trim() || undefined,
        libraryRootDirectory: library.trim() || undefined,
        directoryTemplate: template.trim() || undefined,
        preferredDriveKinds: drives,
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
      <Input
        description={t<string>("acquisition.setup.inbox.description")}
        label={t<string>("acquisition.setup.inbox.label")}
        value={inbox}
        onValueChange={setInbox}
      />
    </div>,
    <div key="library" className="flex flex-col gap-2">
      <Input
        description={t<string>("acquisition.setup.library.description")}
        label={t<string>("acquisition.setup.library.label")}
        value={library}
        onValueChange={setLibrary}
      />
      <div className="text-xs text-default-400">{t<string>("acquisition.setup.library.mark")}</div>
    </div>,
    <div key="template" className="flex flex-col gap-2">
      <Input
        description={t<string>("acquisition.setup.template.description")}
        label={t<string>("acquisition.setup.template.label")}
        value={template}
        onValueChange={setTemplate}
      />
      <div className="flex flex-wrap gap-1">
        {TEMPLATES.map((x) => (
          <Chip
            key={x}
            className="cursor-pointer"
            size="sm"
            variant="flat"
            onClick={() => setTemplate(x)}
          >
            {x}
          </Chip>
        ))}
      </div>
      <div className="text-xs text-default-500">
        {t<string>("acquisition.setup.template.preview", {
          preview: template
            .replace("{Title}", "A Great Work")
            .replace("{LeadKind}", "SharedPage")
            .replace("{Date}", new Date().toISOString().slice(0, 10)),
        })}
      </div>
    </div>,
    <div key="drives" className="flex flex-col gap-3">
      <Select
        dataSource={acquisitionDriveKinds.map(({ value }) => ({
          value: String(value),
          label: AcquisitionDriveKindLabel[value],
          textValue: AcquisitionDriveKindLabel[value],
        }))}
        description={t<string>("acquisition.setup.drives.description")}
        label={t<string>("acquisition.setup.drives.label")}
        selectedKeys={drives.map(String)}
        selectionMode="multiple"
        onSelectionChange={(keys) =>
          setDrives(
            Array.from(keys)
              .map((k) => Number(k))
              .filter((n) => !isNaN(n)) as AcquisitionDriveKind[],
          )
        }
      />
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
