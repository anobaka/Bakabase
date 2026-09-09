"use client";

import type { Resource } from "@/core/models/Resource";
import type { components } from "@/sdk/BApi2";

import React, { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineDelete, AiOutlineFolderOpen, AiOutlineLink, AiOutlinePlus } from "react-icons/ai";

import { Button, Chip, Input, Modal, Spinner, toast } from "@/components/bakaui";
import { FileSystemSelectorModal } from "@/components/FileSystemSelector";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BApi from "@/sdk/BApi";
import {
  AcquisitionLeadKind,
  AcquisitionLeadKindLabel,
  AcquisitionLeadOrigin,
} from "@/sdk/constants";

type AcquisitionLead =
  components["schemas"]["Bakabase.Modules.Acquisition.Abstractions.Models.Domain.AcquisitionLead"];

interface Props {
  resource: Resource;
  /** Called after the resource gains or loses local files, so the host can reload it. */
  onChanged?: () => void;
}

/**
 * Everything you can do about a resource you do not have yet: see where it can be obtained from,
 * add a link someone shared, or — when the files turn out to be on disk already — point the
 * resource straight at them.
 *
 * Leads of kind PlatformHolding are derived from the resource's identities rather than stored, so
 * they are listed but cannot be edited or removed here; they follow the source link.
 */
const AcquisitionPanel: React.FC<Props> = ({ resource, onChanged }) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [leads, setLeads] = useState<AcquisitionLead[]>([]);
  const [loading, setLoading] = useState(true);
  const [newUrl, setNewUrl] = useState("");
  const [adding, setAdding] = useState(false);
  const [acquiring, setAcquiring] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const rsp = await BApi.resource.getResourceAcquisitionLeads(resource.id);

      setLeads(rsp.data ?? []);
    } finally {
      setLoading(false);
    }
  }, [resource.id]);

  useEffect(() => {
    load();
  }, [load]);

  const addLead = async () => {
    const value = newUrl.trim();

    if (!value) return;
    setAdding(true);
    try {
      const rsp = await BApi.resource.addResourceAcquisitionLead(resource.id, {
        // A link pasted here is a page that shares the resource; a direct file URL or a magnet
        // gets its own kind when the acquisition pipeline can tell them apart.
        kind: AcquisitionLeadKind.SharedPage,
        value,
        origin: AcquisitionLeadOrigin.User,
      });

      if (rsp.code) {
        // The backend refuses a link that already describes another resource, and says which.
        toast.danger(rsp.message ?? t<string>("acquisition.error.addLeadFailed"));

        return;
      }
      setNewUrl("");
      await load();
    } finally {
      setAdding(false);
    }
  };

  /**
   * Starts getting it. Which recipe runs is decided from the lead's kind, so there is nothing to
   * choose here — the acquisitions page is where a different one gets picked.
   */
  const acquire = async (lead: AcquisitionLead) => {
    setAcquiring(true);
    try {
      const rsp = await BApi.acquisition.createAcquisition({
        resourceId: resource.id,
        acquisitionLeadId: lead.isDerived ? undefined : lead.id,
        leadKind: lead.kind,
        leadValue: lead.value,
      });

      if (!rsp.code) {
        toast.success(t<string>("acquisition.action.acquireStarted"));
      }
    } finally {
      setAcquiring(false);
    }
  };

  const deleteLead = async (lead: AcquisitionLead) => {
    await BApi.resource.deleteResourceAcquisitionLead(resource.id, lead.id);
    await load();
  };

  const materialize = (path: string, mergeIfOccupied: boolean) =>
    BApi.resource.materializeResource(resource.id, { path, mergeIfOccupied });

  const linkLocalFolder = () => {
    createPortal(FileSystemSelectorModal, {
      targetType: "folder",
      onSelected: async (e: any) => {
        const path = e.path as string;
        const rsp = await materialize(path, false);

        if (rsp.code) {
          toast.danger(rsp.message ?? t<string>("acquisition.error.materializeFailed"));

          return;
        }

        // The folder already belongs to another resource. Merging deletes that one, so it is put
        // to the user in words rather than done quietly.
        if (rsp.data && !rsp.data.materialized) {
          createPortal(Modal, {
            defaultVisible: true,
            title: t<string>("acquisition.materialize.conflict.title"),
            children: t<string>("acquisition.materialize.conflict.message", {
              name: rsp.data.occupiedByResourceName ?? `#${rsp.data.occupiedByResourceId}`,
            }),
            okProps: { color: "danger" },
            onOk: async () => {
              const merged = await materialize(path, true);

              if (merged.code) {
                toast.danger(merged.message ?? t<string>("acquisition.error.materializeFailed"));

                return;
              }
              toast.success(t<string>("acquisition.materialize.success"));
              onChanged?.();
            },
          });

          return;
        }

        toast.success(t<string>("acquisition.materialize.success"));
        onChanged?.();
      },
    });
  };

  const renderLead = (lead: AcquisitionLead) => (
    <div
      key={lead.isDerived ? `derived-${lead.value}` : `lead-${lead.id}`}
      className="flex items-center gap-2 rounded-md border border-default-200 px-2 py-1"
    >
      <Chip radius="sm" size="sm" variant="flat">
        {lead.isDerived
          ? (lead.sourceName ?? t<string>("acquisition.lead.derived"))
          : AcquisitionLeadKindLabel[lead.kind as AcquisitionLeadKind]}
      </Chip>
      <div className="min-w-0 grow break-all text-xs">{lead.value}</div>
      <Button
        isDisabled={acquiring}
        size="sm"
        startContent={<AiOutlineLink className="text-sm" />}
        variant="light"
        onPress={() => acquire(lead)}
      >
        {t<string>("acquisition.action.acquire")}
      </Button>
      {!lead.isDerived && (
        <Button isIconOnly size="sm" variant="light" onPress={() => deleteLead(lead)}>
          <AiOutlineDelete className="text-sm" />
        </Button>
      )}
    </div>
  );

  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-center justify-between">
        <div className="text-sm font-medium">{t<string>("acquisition.leads.title")}</div>
        <Button
          size="sm"
          startContent={<AiOutlineFolderOpen className="text-sm" />}
          variant="light"
          onPress={linkLocalFolder}
        >
          {t<string>("acquisition.action.linkLocalFolder")}
        </Button>
      </div>

      {loading ? (
        <Spinner size="sm" />
      ) : leads.length === 0 ? (
        <div className="text-xs text-default-500">{t<string>("acquisition.leads.empty")}</div>
      ) : (
        <div className="flex flex-col gap-1">{leads.map(renderLead)}</div>
      )}

      <div className="flex items-end gap-2">
        <Input
          className="grow"
          label={t<string>("acquisition.leads.add.label")}
          placeholder={t<string>("acquisition.leads.add.placeholder")}
          size="sm"
          value={newUrl}
          onValueChange={setNewUrl}
        />
        <Button
          isDisabled={!newUrl.trim()}
          isLoading={adding}
          size="sm"
          startContent={<AiOutlinePlus className="text-sm" />}
          onPress={addLead}
        >
          {t<string>("common.action.add")}
        </Button>
      </div>
    </div>
  );
};

AcquisitionPanel.displayName = "AcquisitionPanel";

export default AcquisitionPanel;
