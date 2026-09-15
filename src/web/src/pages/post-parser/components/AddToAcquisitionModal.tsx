"use client";

import type { PostParserTask } from "@/core/models/PostParserTask";
import type { DestroyableProps } from "@/components/bakaui/types";

import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";
import { AiOutlineCloudDownload, AiOutlineCheckCircle } from "react-icons/ai";

import { getDownloadInfo } from "../results";

import DownloadInfoResultRenderer from "./DownloadInfoResultRenderer";

import { Button, Checkbox, Input, Modal } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

interface Props extends DestroyableProps {
  task: PostParserTask;
}

const AddToAcquisitionModal = ({ task, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const data = getDownloadInfo(task);
  const resources = (data?.resources ?? [])
    .map((resource, index) => ({ resource, index }))
    .filter(({ resource }) => !!resource.link?.trim());
  const [visible, setVisible] = useState(true);
  const [title, setTitle] = useState(data?.title || task.title || "");
  const [selected, setSelected] = useState<number[]>(
    resources.length > 0 ? [resources[0].index] : [],
  );
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string>();
  const [resourceId, setResourceId] = useState<number>();

  const save = async () => {
    if (saving || selected.length === 0) return;
    setSaving(true);
    setError(undefined);
    try {
      const response = await BApi.postParser.importPostParserTaskToAcquisition(task.id, {
        title: title.trim() || undefined,
        resourceIndices: selected,
        revision: task.revision ?? 0,
      });

      if (response.code || !response.data)
        throw new Error(response.message || t<string>("postParser.result.failed"));
      setResourceId(response.data.resourceId);
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : t<string>("postParser.result.failed"));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      footer={
        <div className="flex w-full justify-end gap-2">
          <Button isDisabled={saving} variant="light" onPress={() => setVisible(false)}>
            {t<string>("postParser.action.close")}
          </Button>
          {resourceId ? (
            <Button
              color="primary"
              onPress={() => {
                setVisible(false);
                navigate("/acquisitions");
              }}
            >
              {t<string>("postParser.action.openAcquisitions")}
            </Button>
          ) : (
            <Button
              color="primary"
              isDisabled={selected.length === 0 || saving}
              isLoading={saving}
              startContent={<AiOutlineCloudDownload aria-hidden />}
              onPress={save}
            >
              {t<string>("postParser.action.addToAcquisition")}
            </Button>
          )}
        </div>
      }
      isDismissable={!saving}
      size="lg"
      title={t<string>("postParser.action.addToAcquisition")}
      visible={visible}
      onClose={() => setVisible(false)}
      onDestroyed={onDestroyed}
    >
      {resourceId ? (
        <div className="flex items-start gap-3 rounded-xl bg-success/10 p-4" role="status">
          <AiOutlineCheckCircle aria-hidden className="mt-0.5 shrink-0 text-xl text-success" />
          <div>
            <p className="font-medium">
              {t<string>("postParser.acquisition.added", { id: resourceId })}
            </p>
            <p className="mt-1 text-sm text-default-600">
              {t<string>("postParser.acquisition.nextStep")}
            </p>
          </div>
        </div>
      ) : (
        <div className="flex flex-col gap-4">
          <p className="text-sm leading-relaxed text-default-600">
            {t<string>("postParser.acquisition.description")}
          </p>
          <Input
            isDisabled={saving}
            label={t<string>("postParser.acquisition.title")}
            value={title}
            onValueChange={setTitle}
          />
          <div
            aria-label={t<string>("postParser.acquisition.links")}
            className="flex flex-col gap-2"
            role="group"
          >
            {resources.map(({ resource, index }) => (
              <div
                key={index}
                className="flex min-w-0 items-start gap-2 rounded-lg bg-default-50 p-3"
              >
                <Checkbox
                  aria-label={t<string>("postParser.acquisition.selectLink", { number: index + 1 })}
                  className="mt-1"
                  isDisabled={saving}
                  isSelected={selected.includes(index)}
                  onValueChange={(checked) =>
                    setSelected((previous) =>
                      checked
                        ? [...previous, index].sort((a, b) => a - b)
                        : previous.filter((value) => value !== index),
                    )
                  }
                />
                <div className="min-w-0 flex-1">
                  <DownloadInfoResultRenderer data={{ resources: [resource] }} />
                </div>
              </div>
            ))}
          </div>
          <p className="text-xs leading-relaxed text-default-500">
            {t<string>("postParser.acquisition.noDownload")}
          </p>
          {error && (
            <p className="text-sm text-danger" role="alert">
              {error}
            </p>
          )}
        </div>
      )}
    </Modal>
  );
};

export default AddToAcquisitionModal;
