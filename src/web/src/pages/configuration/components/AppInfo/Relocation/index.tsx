"use client";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import {
  Button,
  Card,
  CardBody,
  Input,
  Modal,
  Snippet,
} from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { FileSystemSelectorModal } from "@/components/FileSystemSelector";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { useRelocationPendingStore } from "@/stores/relocationPending";
import { RelocationMode } from "@/sdk/constants";

interface RelocationButtonProps {
  currentDataPath: string;
}

/**
 * "Modify" affordance attached to the AppData path row in <AppInfo />. Drives a
 * three-stage flow: pick target → validate → confirm/relocate. The companion
 * <RelocationRestartGate /> reacts to the server's RelocationPending hub event.
 *
 * Validation is fast (a handful of File.Exists calls server-side, no recursive
 * scan), so we don't bother with a "analysing target" loading modal — by the
 * time the picker animation finishes, validate has typically returned.
 */
export const RelocationButton: React.FC<RelocationButtonProps> = ({ currentDataPath }) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const openPicker = () => {
    createPortal(FileSystemSelectorModal, {
      targetType: "folder",
      onSelected: (entry) => {
        if (entry?.path) {
          handleSelected(entry.path, currentDataPath);
        }
      },
    });
  };

  const handleSelected = async (target: string, current: string) => {
    let resp;
    try {
      resp = await BApi.app.validateAppDataPath({ targetPath: target });
    } catch (e) {
      showError(String(e));
      return;
    }

    const data = (resp as any).data;
    if (!data?.valid) {
      const reasonKey = `configuration.dataPath.validation.${camelize(data?.reason)}`;
      // Some reason messages interpolate runtime values (e.g. insideInstall renders the
      // detected install root). Pass the whole response in — i18next ignores unused keys.
      createPortal(Modal, {
        size: "md",
        title: t("configuration.dataPath.error.title"),
        children: (
          <div className="flex flex-col gap-2">
            <p>{t(reasonKey, { installRoot: data?.installRoot ?? "" })}</p>
            <p className="text-xs text-foreground-400">
              {t("configuration.dataPath.error.targetWas")}: <code>{target}</code>
            </p>
          </div>
        ),
        defaultVisible: true,
        footer: { actions: ["cancel"] },
      });
      return;
    }

    // Two-state target classification (matches DataPathValidator.TargetState):
    //   0 = NeedsCopy        → only one sensible action: merge current → target
    //   1 = HasBakabaseData  → user picks: adopt target's data, or merge current over it
    if (data.targetState === 1 /* HasBakabaseData */) {
      openHasBakabaseDataModal(target, current, data.targetAppVersion);
    } else {
      openNeedsCopyModal(target, current);
    }
  };

  const showError = (message: string) => {
    createPortal(Modal, {
      size: "md",
      title: t("configuration.dataPath.error.title"),
      children: <div>{String(message)}</div>,
      defaultVisible: true,
      footer: { actions: ["cancel"] },
    });
  };

  const submitRelocate = async (target: string, mode: RelocationMode) => {
    try {
      await BApi.app.relocateAppDataPath({ targetPath: target, mode });
    } catch (e) {
      showError(String(e));
    }
  };

  const openNeedsCopyModal = (target: string, current: string) => {
    createPortal(Modal, {
      size: "lg",
      title: t("configuration.dataPath.confirm.title.copy"),
      children: <PathPair current={current} target={target} note={t("configuration.dataPath.confirm.copyNote")} />,
      defaultVisible: true,
      onOk: () => submitRelocate(target, RelocationMode.MergeOverwrite),
      footer: {
        actions: ["ok", "cancel"],
        okProps: { children: t("configuration.dataPath.confirm.migrate") },
      },
    });
  };

  const openHasBakabaseDataModal = (
    target: string,
    current: string,
    targetVersion?: string,
  ) => {
    createPortal(Modal, {
      size: "lg",
      title: t("configuration.dataPath.confirm.title.useOrMerge"),
      children: (
        <UseOrMergeBody
          current={current}
          target={target}
          targetVersion={targetVersion}
          onUseTarget={() => submitRelocate(target, RelocationMode.UseTarget)}
          onMerge={() => openMergeConfirmModal(target)}
        />
      ),
      defaultVisible: true,
      footer: { actions: ["cancel"] },
    });
  };

  const openMergeConfirmModal = (target: string) => {
    createPortal(MergeConfirmModal, {
      onConfirm: () => submitRelocate(target, RelocationMode.MergeOverwrite),
    } as any);
  };

  return (
    <Button size="sm" variant="light" color="primary" onPress={openPicker}>
      {t("configuration.dataPath.modify.button")}
    </Button>
  );
};

const PathPair: React.FC<{ current: string; target: string; note?: string }> = ({
  current,
  target,
  note,
}) => {
  const { t } = useTranslation();
  return (
    <div className="flex flex-col gap-3">
      <Row label={t("configuration.dataPath.confirm.current")} value={current} />
      <Row label={t("configuration.dataPath.confirm.target")} value={target} />
      {note && <p className="text-sm text-foreground-500">{note}</p>}
    </div>
  );
};

const Row = ({ label, value }: { label: string; value: string }) => (
  <div className="flex flex-col gap-1">
    <span className="text-xs text-foreground-400">{label}</span>
    <Snippet hideSymbol size="sm" variant="bordered">{value}</Snippet>
  </div>
);

interface UseOrMergeProps {
  target: string;
  current: string;
  targetVersion?: string;
  onUseTarget: () => void;
  onMerge: () => void;
}

const UseOrMergeBody: React.FC<UseOrMergeProps> = ({
  target,
  current,
  targetVersion,
  onUseTarget,
  onMerge,
}) => {
  const { t } = useTranslation();
  return (
    <div className="flex flex-col gap-4">
      <p>
        {t("configuration.dataPath.confirm.useOrMergeHeader", {
          version: targetVersion ?? t("configuration.dataPath.confirm.unknownVersion"),
        })}
      </p>
      <PathPair current={current} target={target} />

      <div className="grid grid-cols-1 md:grid-cols-2 gap-3 mt-2">
        <ChoiceCard
          color="primary"
          title={t("configuration.dataPath.confirm.option.useTarget.title")}
          description={t("configuration.dataPath.confirm.option.useTarget.description")}
          onPress={onUseTarget}
        />
        <ChoiceCard
          color="warning"
          title={t("configuration.dataPath.confirm.option.merge.title")}
          description={t("configuration.dataPath.confirm.option.merge.description")}
          onPress={onMerge}
        />
      </div>
    </div>
  );
};

const ChoiceCard: React.FC<{
  color: "primary" | "warning";
  title: string;
  description: string;
  onPress: () => void;
}> = ({ color, title, description, onPress }) => (
  <Card
    isPressable
    isHoverable
    onPress={onPress}
    className={
      color === "warning"
        ? "border-1 border-warning-200 hover:border-warning"
        : "border-1 border-primary-200 hover:border-primary"
    }
  >
    <CardBody className="flex flex-col gap-2">
      <span className={`font-semibold text-${color}`}>{title}</span>
      <span className="text-sm text-foreground-500">{description}</span>
    </CardBody>
  </Card>
);

interface MergeConfirmProps {
  onConfirm: () => void;
  onDestroyed?: () => void;
}

/**
 * Phrase-input speed-bump that gates the merge action when target already has Bakabase data.
 * The current/target paths are intentionally NOT re-rendered here — the previous modal in the
 * flow already showed them and the user just clicked "merge" knowing what they were doing.
 */
const MergeConfirmModal: React.FC<MergeConfirmProps> = ({ onConfirm, onDestroyed }) => {
  const { t } = useTranslation();
  const expectedPhrase = t("configuration.dataPath.confirm.merge.phrase");
  const [visible, setVisible] = useState(true);
  const [phrase, setPhrase] = useState("");

  const ok = phrase.normalize("NFC").trim() === expectedPhrase;

  return (
    <Modal
      size="md"
      title={t("configuration.dataPath.confirm.title.merge")}
      visible={visible}
      onClose={() => setVisible(false)}
      onDestroyed={onDestroyed}
      footer={{
        actions: ["ok", "cancel"],
        okProps: {
          isDisabled: !ok,
          color: "danger",
          children: t("configuration.dataPath.confirm.merge.confirmButton"),
        },
      }}
      onOk={() => ok && onConfirm()}
    >
      <div className="flex flex-col gap-3">
        <p className="text-warning">
          {t("configuration.dataPath.confirm.merge.warning")}
        </p>
        <div className="flex flex-col gap-1">
          <span className="text-xs text-foreground-400">
            {t("configuration.dataPath.confirm.merge.typeBelow")}
          </span>
          <Snippet hideSymbol size="sm" variant="bordered">
            {expectedPhrase}
          </Snippet>
          <Input
            size="sm"
            value={phrase}
            onChange={(e) => setPhrase(e.target.value)}
            placeholder={t("configuration.dataPath.confirm.merge.placeholder")}
          />
        </div>
      </div>
    </Modal>
  );
};

/**
 * Subscribes to <code>RelocationPending</code> hub events and renders a non-dismissable
 * "restart now" modal. Call once near the AppInfo / settings page root.
 */
export const RelocationRestartGate: React.FC = () => {
  const { t } = useTranslation();
  const pending = useRelocationPendingStore((s) => s.pending);
  const [open, setOpen] = useState(false);
  const [restarting, setRestarting] = useState(false);

  useEffect(() => {
    if (pending) setOpen(true);
  }, [pending]);

  if (!pending) return null;

  const triggerRestart = async () => {
    if (restarting) return;
    setRestarting(true);
    try {
      await BApi.app.restartApp();
    } catch {
      // restart endpoint best-effort — even if the server lost the response, the actual
      // process spawn happens server-side. Reload anyway.
    }
    // Give the server a moment to spawn the replacement before we reload.
    setTimeout(() => window.location.reload(), 800);
  };

  return (
    <Modal
      size="md"
      title={t("configuration.dataPath.restart.title")}
      visible={open}
      onClose={() => {}}
      hideCloseButton
      isDismissable={false}
      isKeyboardDismissDisabled
      footer={{ actions: [] }}
    >
      <div className="flex flex-col gap-4">
        <p>{t("configuration.dataPath.restart.body")}</p>
        <Snippet hideSymbol size="sm" variant="bordered">{pending.target}</Snippet>
        <Button
          color="primary"
          isLoading={restarting}
          isDisabled={restarting}
          onPress={triggerRestart}
        >
          {t("configuration.dataPath.restart.button")}
        </Button>
      </div>
    </Modal>
  );
};

function camelize(reason: any): string {
  if (typeof reason !== "string") return "unknown";
  return reason.charAt(0).toLowerCase() + reason.slice(1);
}
