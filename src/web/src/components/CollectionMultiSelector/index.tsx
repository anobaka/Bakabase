"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { CollectionModel } from "@/stores/collections";

import React, { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  CheckCircleFilled,
  CloseCircleOutlined,
  MinusCircleFilled,
  UndoOutlined,
} from "@ant-design/icons";

import BApi from "@/sdk/BApi";
import { Button, Modal, Spinner, Tooltip, toast } from "@/components/bakaui";

type Props = {
  resourceIds: number[];
  onSubmit?: () => void;
} & DestroyableProps;

/** All of them, some of them, none of them. The three answers a multi-selection can give. */
enum Membership {
  None = "none",
  Partial = "partial",
  Full = "full",
}

/**
 * "Put these in…", for a whole selection at once.
 *
 * A partial state has to be visible and has to survive being left alone: clicking through
 * Partial → Full is a choice, but a submit that silently promoted every partial to full would
 * quietly add fifty resources to a collection nobody meant to touch.
 */
const CollectionMultiSelector = ({ resourceIds, onSubmit, onDestroyed }: Props) => {
  const { t } = useTranslation();

  const [visible, setVisible] = useState(true);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [collections, setCollections] = useState<CollectionModel[]>([]);
  const [original, setOriginal] = useState<Record<number, Set<number>>>({});
  const [current, setCurrent] = useState<Record<number, Set<number>>>({});

  useEffect(() => {
    const load = async () => {
      setLoading(true);
      try {
        const [collectionsRsp, mappingsRsp] = await Promise.all([
          BApi.collection.getAllCollections({ withProgress: false }),
          BApi.resource.getCollectionIdsByResourceIds({ resourceIds }),
        ]);

        const all = (collectionsRsp.data ?? []) as CollectionModel[];

        setCollections(all);

        const byCollection: Record<number, Set<number>> = {};

        for (const c of all) byCollection[c.id] = new Set();
        for (const [resourceIdStr, collectionIds] of Object.entries(mappingsRsp.data ?? {})) {
          const resourceId = parseInt(resourceIdStr, 10);

          for (const collectionId of collectionIds ?? []) {
            byCollection[collectionId]?.add(resourceId);
          }
        }

        setOriginal(byCollection);
        setCurrent(
          Object.fromEntries(Object.entries(byCollection).map(([k, v]) => [k, new Set(v)])),
        );
      } finally {
        setLoading(false);
      }
    };

    void load();
  }, [resourceIds]);

  const stateOf = (collectionId: number): Membership => {
    const members = current[collectionId];

    if (!members || members.size === 0) return Membership.None;
    if (members.size === resourceIds.length) return Membership.Full;

    return Membership.Partial;
  };

  const toggle = (collectionId: number) => {
    setCurrent((prev) => {
      const next = { ...prev };
      const members = new Set(prev[collectionId]);

      // None and Partial both mean "not all of them", and the useful next step from either is
      // all of them; Full goes back to none.
      if (stateOf(collectionId) === Membership.Full) {
        members.clear();
      } else {
        for (const resourceId of resourceIds) members.add(resourceId);
      }

      next[collectionId] = members;

      return next;
    });
  };

  const changed = useMemo(
    () =>
      collections.some((c) => {
        const before = original[c.id] ?? new Set<number>();
        const after = current[c.id] ?? new Set<number>();

        return before.size !== after.size || [...before].some((id) => !after.has(id));
      }),
    [collections, original, current],
  );

  const reset = () =>
    setCurrent(Object.fromEntries(Object.entries(original).map(([k, v]) => [k, new Set(v)])));

  const submit = async () => {
    setSubmitting(true);
    try {
      // Only what actually moved is sent, one collection at a time. Saying "these resources are
      // in exactly these collections" instead would force a decision about every partial one,
      // and a partial one is precisely the case where the user made none.
      for (const collection of collections) {
        const before = original[collection.id] ?? new Set<number>();
        const state = stateOf(collection.id);

        if (state === Membership.Full && before.size !== resourceIds.length) {
          await BApi.collection.addCollectionMembers(collection.id, { resourceIds });
        } else if (state === Membership.None && before.size > 0) {
          await BApi.collection.removeCollectionMembers(collection.id, { resourceIds });
        }
      }

      toast.success(t<string>("collection.selector.saved"));
      onSubmit?.();
      setVisible(false);
    } finally {
      setSubmitting(false);
    }
  };

  const renderButton = (collection: CollectionModel) => {
    const state = stateOf(collection.id);
    const [icon, color, tip] =
      state === Membership.Full
        ? [
            <CheckCircleFilled key="f" className="text-success" />,
            "success" as const,
            t<string>("collection.selector.all"),
          ]
        : state === Membership.Partial
          ? [
              <MinusCircleFilled key="p" className="text-warning" />,
              "warning" as const,
              t<string>("collection.selector.some"),
            ]
          : [
              <CloseCircleOutlined key="n" className="text-default-400" />,
              "default" as const,
              t<string>("collection.selector.none"),
            ];

    return (
      <Tooltip key={collection.id} content={tip}>
        <Button
          className="m-1"
          color={color}
          size="sm"
          startContent={icon}
          variant={state === Membership.None ? "flat" : "solid"}
          onPress={() => toggle(collection.id)}
        >
          {collection.name}
        </Button>
      </Tooltip>
    );
  };

  return (
    <Modal
      footer={false}
      size="lg"
      title={t<string>("collection.selector.title", { count: resourceIds.length })}
      visible={visible}
      onClose={() => setVisible(false)}
      onDestroyed={onDestroyed}
    >
      {loading ? (
        <div className="flex justify-center py-8">
          <Spinner />
        </div>
      ) : (
        <div>
          <div className="mb-3 text-sm text-default-500">
            {t<string>("collection.selector.description")}
          </div>

          {collections.length === 0 ? (
            <div className="py-4 text-center text-default-400">
              {t<string>("collection.selector.none.exists")}
            </div>
          ) : (
            <div className="flex flex-wrap">{collections.map(renderButton)}</div>
          )}

          <div className="mt-6 flex justify-end gap-2">
            <Button
              isDisabled={!changed}
              startContent={<UndoOutlined />}
              variant="flat"
              onPress={reset}
            >
              {t<string>("collection.selector.reset")}
            </Button>
            <Button color="primary" isDisabled={!changed} isLoading={submitting} onPress={submit}>
              {t<string>("collection.selector.submit")}
            </Button>
          </div>
        </div>
      )}
    </Modal>
  );
};

CollectionMultiSelector.displayName = "CollectionMultiSelector";

export default CollectionMultiSelector;
