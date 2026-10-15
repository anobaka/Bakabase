import type { DestroyableProps } from "@/components/bakaui/types";
import type { ResourceScope } from "../scopeApi";
import type { ResourceProfile } from "../sourceProfiles";
import type { SearchFilterGroup } from "@/components/ResourceFilter/models";
import type { IProperty } from "@/components/Property/models";

import { useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import { resourceScopeApi } from "../scopeApi";
import { profileSourceApi } from "../sourceProfiles";
import { createSourceFilterConfig } from "../sourceFilterConfig";

import { useProfileModalSave } from "./useProfileModalSave";

import { Modal, Input } from "@/components/bakaui";
import { ResourceFilterController } from "@/components/ResourceFilter";
import { FilterDisplayMode } from "@/sdk/constants";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

export default function ResourceScopeEditorModal({
  scope,
  profile,
  onSaved,
  onDestroyed,
}: {
  scope?: ResourceScope;
  profile: ResourceProfile;
  onSaved: (id: number) => Promise<void> | void;
} & DestroyableProps) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const persistedScope = useRef(scope);
  const [name, setName] = useState(scope?.name ?? profile.name);
  const [search, setSearch] = useState<ResourceProfile["search"]>(
    structuredClone(scope?.search ?? profile.search ?? { page: 1, pageSize: 100 }),
  );
  const [properties, setProperties] = useState<IProperty[]>();
  const [loadError, setLoadError] = useState(false);
  const source = scope ?? profile;

  useEffect(() => {
    if (!source.sourceDeviceId) return;
    const controller = new AbortController();

    void Promise.all([
      profileSourceApi.properties(source, controller.signal),
      profileSourceApi.resolve(source, search, controller.signal),
    ])
      .then(([fields, resolved]) => {
        if (!controller.signal.aborted) {
          setProperties(fields);
          setSearch(resolved ?? search);
        }
      })
      .catch(() => {
        if (!controller.signal.aborted) setLoadError(true);
      });

    return () => controller.abort();
  }, []);
  const config = useMemo(
    () =>
      source.sourceDeviceId && properties
        ? createSourceFilterConfig(source, properties, createPortal)
        : undefined,
    [source.sourceDeviceId, properties, createPortal],
  );
  const ready = !source.sourceDeviceId || !!properties;
  const editor = useProfileModalSave(async () => {
    if (!name.trim() || !ready) throw new Error(t<string>("resourceProfile.scope.loadFailed"));
    const values = {
      sourceDeviceId: source.sourceDeviceId,
      sourceLibraryEpoch: source.sourceLibraryEpoch,
      name: name.trim(),
      search,
    };
    let saved = persistedScope.current;

    if (saved) {
      const result = await resourceScopeApi.update({ ...saved, ...values });

      saved = { ...saved, ...values, revision: result.revision };
    } else {
      const result = await resourceScopeApi.add(values);

      saved = { ...values, id: result.id, revision: result.revision, ruleCount: 0 };
    }
    // A successful create must not be repeated if linking the profile fails afterward.
    persistedScope.current = saved;
    const id = saved.id;

    await onSaved(id);
  }, t<string>("resourceProfile.editor.saveFailed"));

  return (
    <Modal
      footer={{
        actions: ["ok", "cancel"],
        okProps: { isDisabled: !ready || !name.trim(), isLoading: editor.saving },
        cancelProps: { isDisabled: editor.saving },
      }}
      hideCloseButton={editor.saving}
      isDismissable={!editor.saving}
      isKeyboardDismissDisabled={editor.saving}
      size="2xl"
      title={t<string>(scope ? "resourceProfile.scope.edit" : "resourceProfile.scope.create")}
      visible={editor.visible}
      onClose={editor.close}
      onDestroyed={onDestroyed}
      onOk={() => editor.save(undefined)}
    >
      <Input
        isDisabled={editor.saving}
        label={t<string>("resourceProfile.scope.name")}
        maxLength={128}
        value={name}
        onValueChange={setName}
      />
      <p className="my-3 text-xs text-default-500">
        {t("resourceProfile.scope.sharedHint", { count: scope?.ruleCount ?? 0 })}
      </p>
      {editor.error && (
        <p className="text-sm text-danger" role="alert">
          {editor.error}
        </p>
      )}
      {!ready && (
        <p className="text-sm" role="status">
          {t(loadError ? "resourceProfile.scope.loadFailed" : "resourceProfile.source.loading")}
        </p>
      )}
      {ready && (
        <ResourceFilterController
          config={config}
          defaultFilterDisplayMode={FilterDisplayMode.Simple}
          filterLayout="horizontal"
          group={(search?.group ?? { combinator: 1, disabled: false }) as SearchFilterGroup}
          isReadonly={editor.saving}
          showKeyword={false}
          showRecentFilters={!source.sourceDeviceId}
          tags={search?.tags}
          onGroupChange={(group) =>
            setSearch((current) => ({ ...current, group }) as ResourceProfile["search"])
          }
          onTagsChange={(tags) =>
            setSearch((current) => ({ page: 1, pageSize: 100, ...current, tags }))
          }
        />
      )}
    </Modal>
  );
}
