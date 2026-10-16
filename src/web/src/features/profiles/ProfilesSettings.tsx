import type { TFunction } from "i18next";
import type { BakabaseAbstractionsComponentsAppDesktopProfileView } from "@/sdk/Api";
import type { DesktopProfilesController } from "./useDesktopProfiles";

import { useState } from "react";
import { useTranslation } from "react-i18next";
import { MoreOutlined } from "@ant-design/icons";

import BApi from "@/sdk/BApi";
import {
  SettingsRows,
  useSettingsSearchMatch,
} from "@/pages/configuration/components/SettingsSection";
import {
  Button,
  Dropdown,
  DropdownItem,
  DropdownMenu,
  DropdownTrigger,
  Input,
  Select,
} from "@/components/bakaui";
import { FileSystemSelectorModal } from "@/components/FileSystemSelector";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

type Profile = BakabaseAbstractionsComponentsAppDesktopProfileView;

export function matchesProfilesSettings(
  query: string | undefined,
  t: TFunction,
  profiles: Profile[] = [],
) {
  return (
    !query ||
    [
      t("profiles.title"),
      t("profiles.description"),
      t("profiles.manageSection"),
      t("profiles.select"),
      t("profiles.switch"),
      t("profiles.rename"),
      ...profiles.map((profile) => profile.name),
      "profiles workspaces appdata",
    ]
      .join(" ")
      .toLowerCase()
      .includes(query.trim().toLowerCase())
  );
}

/** Local desktop workspace switching; storage remains independent of this catalog. */
export default function ProfilesSettings({
  query,
  controller,
}: {
  query?: string;
  controller: DesktopProfilesController;
}) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const { state, error: loadError, localDesktop, load } = controller;
  const [selectedId, setSelectedId] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [switching, setSwitching] = useState(false);
  const [error, setError] = useState<string>();
  const [notice, setNotice] = useState<string>();
  const [adding, setAdding] = useState<"new" | "existing">();
  const [name, setName] = useState("");
  const [directory, setDirectory] = useState("");
  const [renaming, setRenaming] = useState<Profile>();
  const [rename, setRename] = useState("");
  const [removing, setRemoving] = useState<Profile>();
  const profiles = state?.profiles || [];
  const selected =
    profiles.find((profile) => profile.id === selectedId) ??
    profiles.find((profile) => profile.isCurrent);
  const matches = matchesProfilesSettings(query, t, profiles);
  const editing = !!adding || !!renaming || !!removing;

  useSettingsSearchMatch(
    "desktop-profiles",
    matches && (!!state?.available || (!!loadError && localDesktop)),
  );
  if (!state?.available)
    return localDesktop && loadError && matches ? (
      <p className="px-4 py-3 text-danger" role="alert">
        {loadError}
      </p>
    ) : null;
  if (!matches) return null;

  const label = (profile: Profile) => profile.name || t("profiles.original");
  const run = async (
    action: () => Promise<{ code?: number; message?: string | null }>,
    after?: () => void,
  ) => {
    setBusy(true);
    setError(undefined);
    setNotice(undefined);
    try {
      const result = await action();

      if (result.code) throw new Error(result.message || t("profiles.error.save"));
      after?.();
      await load();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : t("profiles.error.save"));
    } finally {
      setBusy(false);
    }
  };
  const switchWorkspace = async () => {
    if (!selected?.id || selected.isCurrent || !selected.isAvailable || busy) return;
    setBusy(true);
    setSwitching(true);
    setError(undefined);
    setNotice(undefined);
    try {
      const result = await BApi.app.openDesktopProfile(selected.id, { closeCurrent: true });

      if (result.code) throw new Error(result.message || t("profiles.error.save"));
      // Keep switching disabled until this workspace closes; no follow-up catalog read
      // should race shutdown and turn a successful handoff into a misleading error.
      setNotice(t("profiles.switching"));
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : t("profiles.error.save"));
      setBusy(false);
      setSwitching(false);
    }
  };
  const beginAdding = (kind: "new" | "existing", path = "") => {
    setAdding(kind);
    setName("");
    setDirectory(path);
    setError(undefined);
    setNotice(undefined);
  };

  const controls = (
    <>
      <div className="flex flex-wrap items-center gap-2">
        <Select
          disallowEmptySelection
          aria-label={t("profiles.select")}
          className="min-w-48 max-w-sm flex-1"
          dataSource={profiles
            .filter((profile) => profile.id)
            .map((profile) => ({
              value: profile.id!,
              label: [
                label(profile),
                profile.isCurrent && t("profiles.current"),
                profile.isDefault && t("profiles.default"),
                !profile.isAvailable && t("profiles.unavailable"),
              ]
                .filter(Boolean)
                .join(" · "),
              textValue: label(profile),
              description: profile.dataDirectory || undefined,
            }))}
          isDisabled={busy || editing}
          placeholder={t("profiles.select")}
          selectedKeys={selected?.id ? [selected.id] : []}
          size="sm"
          onSelectionChange={(keys) => {
            if (keys === "all") return;
            const next = Array.from(keys)[0];

            if (next !== undefined) setSelectedId(String(next));
            setError(undefined);
            setNotice(undefined);
          }}
        />
        <Button
          isDisabled={busy || editing || !selected?.id}
          size="sm"
          variant="light"
          onPress={() => {
            if (!selected) return;
            setRenaming(selected);
            setRename(label(selected));
          }}
        >
          {t("profiles.rename")}
        </Button>
        <Button
          color="primary"
          isDisabled={busy || editing || !selected?.isAvailable || selected.isCurrent}
          isLoading={switching}
          size="sm"
          onPress={() => void switchWorkspace()}
        >
          {t("profiles.switch")}
        </Button>
        <Dropdown isDisabled={busy || editing} placement="bottom-end">
          <DropdownTrigger>
            <Button
              aria-label={t("profiles.manageSection")}
              isDisabled={busy || editing}
              size="sm"
              startContent={<MoreOutlined />}
              variant="light"
            >
              {t("profiles.manage")}
            </Button>
          </DropdownTrigger>
          <DropdownMenu
            aria-label={t("profiles.manageSection")}
            disabledKeys={[
              ...(!selected?.id || selected.isDefault ? ["default"] : []),
              ...(!selected?.id || selected.isCurrent || selected.isRunning || profiles.length < 2
                ? ["remove"]
                : []),
            ]}
            onAction={(key) => {
              switch (key) {
                case "new":
                  beginAdding("new");
                  break;
                case "existing":
                  beginAdding("existing");
                  break;
                case "default":
                  if (selected?.id)
                    void run(() =>
                      BApi.app.updateDesktopProfile(selected.id!, {
                        name: label(selected),
                        makeDefault: true,
                      }),
                    );
                  break;
                case "remove":
                  if (selected?.id) setRemoving(selected);
                  break;
              }
            }}
          >
            <DropdownItem key="new">{t("profiles.addNew")}</DropdownItem>
            <DropdownItem key="existing">{t("profiles.addExisting")}</DropdownItem>
            <DropdownItem key="default" showDivider>
              {t("profiles.makeDefault")}
            </DropdownItem>
            <DropdownItem key="remove" className="text-danger" color="danger">
              {t("profiles.remove")}
            </DropdownItem>
          </DropdownMenu>
        </Dropdown>
      </div>
      {selected && !selected.isCurrent && (
        <div className="mt-2 text-xs text-default-500">
          <p className="break-all">
            {t("profiles.targetDirectory")}: {selected.dataDirectory}
          </p>
          <p className={selected.isAvailable ? "mt-1" : "mt-1 text-warning"}>
            {t(selected.isAvailable ? "profiles.switchHint" : "profiles.unavailable")}
          </p>
        </div>
      )}
      {state.environmentOverride && (
        <div className="mt-3 rounded-lg bg-warning-50 p-3 text-sm text-warning-700">
          <p>{t("profiles.environment")}</p>
          <Button
            className="mt-2"
            isDisabled={busy || editing}
            size="sm"
            variant="flat"
            onPress={() => beginAdding("existing", state.currentDirectory || "")}
          >
            {t("profiles.registerCurrent")}
          </Button>
        </div>
      )}
      {error && (
        <p className="mt-3 break-words text-sm text-danger" role="alert">
          {error}
        </p>
      )}
      {notice && (
        <p className="mt-3 text-sm text-default-500" role="status">
          {notice}
        </p>
      )}
      {adding && (
        <form
          className="mt-3 space-y-3 rounded-lg border border-default-200 p-3"
          onSubmit={(event) => {
            event.preventDefault();
            void run(
              () =>
                BApi.app.addDesktopProfile({
                  name: name.trim(),
                  directory: directory.trim(),
                  create: adding === "new",
                }),
              () => setAdding(undefined),
            );
          }}
        >
          <p className="text-sm text-default-500">
            {t(adding === "new" ? "profiles.newDescription" : "profiles.existingDescription")}
          </p>
          <Input
            isDisabled={busy}
            label={t("profiles.name")}
            maxLength={100}
            size="sm"
            value={name}
            onValueChange={setName}
          />
          <div className="flex items-center gap-2">
            <Input
              className="min-w-0 flex-1"
              isDisabled={busy}
              label={t("profiles.directory")}
              size="sm"
              value={directory}
              onValueChange={setDirectory}
            />
            <Button
              isDisabled={busy}
              size="sm"
              type="button"
              variant="flat"
              onPress={() =>
                createPortal(FileSystemSelectorModal, {
                  targetType: "folder",
                  startPath: directory || undefined,
                  onSelected: (entry) => setDirectory(entry.path),
                })
              }
            >
              {t("profiles.choose")}
            </Button>
          </div>
          <div className="flex gap-2">
            <Button
              color="primary"
              isDisabled={busy || !name.trim() || !directory.trim()}
              isLoading={busy}
              size="sm"
              type="submit"
            >
              {t("profiles.save")}
            </Button>
            <Button
              isDisabled={busy}
              size="sm"
              type="button"
              variant="light"
              onPress={() => setAdding(undefined)}
            >
              {t("profiles.cancel")}
            </Button>
          </div>
        </form>
      )}
      {renaming && (
        <form
          className="mt-3 flex max-w-lg items-center gap-2"
          onSubmit={(event) => {
            event.preventDefault();
            void run(
              () =>
                BApi.app.updateDesktopProfile(renaming.id!, {
                  name: rename.trim(),
                  makeDefault: false,
                }),
              () => setRenaming(undefined),
            );
          }}
        >
          <Input
            aria-label={t("profiles.name")}
            isDisabled={busy}
            maxLength={100}
            size="sm"
            value={rename}
            onValueChange={setRename}
          />
          <Button isDisabled={busy || !rename.trim()} size="sm" type="submit">
            {t("profiles.save")}
          </Button>
          <Button
            isDisabled={busy}
            size="sm"
            type="button"
            variant="light"
            onPress={() => setRenaming(undefined)}
          >
            {t("profiles.cancel")}
          </Button>
        </form>
      )}
      {removing && (
        <div
          className="mt-3 rounded-lg border border-warning-200 bg-warning-50 p-3 text-sm"
          role="alert"
        >
          <p>{t("profiles.removeConfirm", { name: label(removing) })}</p>
          <div className="mt-2 flex gap-2">
            <Button
              color="danger"
              isDisabled={busy}
              size="sm"
              onPress={() =>
                void run(
                  () => BApi.app.removeDesktopProfile(removing.id!),
                  () => {
                    setRemoving(undefined);
                    setSelectedId(undefined);
                  },
                )
              }
            >
              {t("profiles.remove")}
            </Button>
            <Button
              isDisabled={busy}
              size="sm"
              variant="light"
              onPress={() => setRemoving(undefined)}
            >
              {t("profiles.cancel")}
            </Button>
          </div>
        </div>
      )}
    </>
  );

  return (
    <section aria-label={t("profiles.manageSection")}>
      <SettingsRows
        items={[{ id: "workspace", label: t("profiles.select"), render: () => controls }]}
      />
    </section>
  );
}
