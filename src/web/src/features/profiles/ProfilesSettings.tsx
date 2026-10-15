import type { BakabaseAbstractionsComponentsAppDesktopProfileView } from "@/sdk/Api";
import type { DesktopProfilesController } from "./useDesktopProfiles";

import { useState } from "react";
import { useTranslation } from "react-i18next";
import { FolderOpenOutlined, PlusOutlined } from "@ant-design/icons";

import BApi from "@/sdk/BApi";
import { useSettingsSearchMatch } from "@/pages/configuration/components/SettingsSection";
import { Button, Input } from "@/components/bakaui";
import { FileSystemSelectorModal } from "@/components/FileSystemSelector";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

/** Local desktop launch settings; unavailable on headless hosts and managed-device relays. */
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
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const [notice, setNotice] = useState<string>();
  const [adding, setAdding] = useState<"new" | "existing">();
  const [name, setName] = useState("");
  const [directory, setDirectory] = useState("");
  const [renaming, setRenaming] = useState<string>();
  const [rename, setRename] = useState("");
  const [removing, setRemoving] = useState<BakabaseAbstractionsComponentsAppDesktopProfileView>();
  const matches =
    !query ||
    `${t("profiles.title")} ${t("profiles.description")} ${t("profiles.manageSection")} profiles workspaces appdata`
      .toLowerCase()
      .includes(query.trim().toLowerCase());

  useSettingsSearchMatch(
    "desktop-profiles",
    matches && (!!state?.available || (!!loadError && localDesktop)),
  );
  if (!state?.available)
    return localDesktop && loadError && matches ? (
      <p className="text-danger" role="alert">
        {loadError}
      </p>
    ) : null;
  if (!matches) return null;
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
  const label = (profile: BakabaseAbstractionsComponentsAppDesktopProfileView) =>
    profile.name || t("profiles.original");
  const open = (
    profile: BakabaseAbstractionsComponentsAppDesktopProfileView,
    closeCurrent = false,
  ) => {
    if (!profile.id) return;
    void run(
      () => BApi.app.openDesktopProfile(profile.id!, { closeCurrent }),
      () => {
        setNotice(t(closeCurrent ? "profiles.switching" : "profiles.opened"));
      },
    );
  };

  return (
    <section aria-label={t("profiles.manageSection")} className="border-t border-default-200 p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h3 className="text-sm font-medium">{t("profiles.manageSection")}</h3>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button
            isDisabled={busy}
            size="sm"
            startContent={<PlusOutlined />}
            variant="flat"
            onPress={() => {
              setAdding("new");
              setName("");
              setDirectory("");
            }}
          >
            {t("profiles.addNew")}
          </Button>
          <Button
            isDisabled={busy}
            size="sm"
            startContent={<FolderOpenOutlined />}
            variant="flat"
            onPress={() => {
              setAdding("existing");
              setName("");
              setDirectory("");
            }}
          >
            {t("profiles.addExisting")}
          </Button>
        </div>
      </div>
      {state.environmentOverride && (
        <div className="mt-3 rounded-lg bg-warning-50 p-3 text-sm text-warning-700">
          <p>{t("profiles.environment")}</p>
          <Button
            className="mt-2"
            isDisabled={busy}
            size="sm"
            variant="flat"
            onPress={() => {
              setAdding("existing");
              setName("");
              setDirectory(state.currentDirectory || "");
            }}
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
        <p className="mt-3 text-sm text-success" role="status">
          {notice}
        </p>
      )}
      {adding && (
        <form
          className="my-3 space-y-3 rounded-lg border border-default-200 p-3"
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
      <ul className="mt-3 divide-y divide-default-100">
        {(state.profiles || []).map((profile) => (
          <li key={profile.id} className="flex flex-wrap items-center gap-3 py-3">
            <div className="min-w-0 flex-1 basis-60">
              <div className="flex flex-wrap items-center gap-2 text-sm font-medium">
                {label(profile)}
                {profile.isCurrent && (
                  <span className="rounded bg-primary-50 px-2 py-0.5 text-xs text-primary">
                    {t("profiles.current")}
                  </span>
                )}
                {profile.isDefault && (
                  <span className="text-xs font-normal text-default-500">
                    {t("profiles.default")}
                  </span>
                )}
                {!profile.isCurrent && profile.isRunning && (
                  <span className="text-xs text-success">{t("profiles.running")}</span>
                )}
                {!profile.isAvailable && (
                  <span className="text-xs text-warning">{t("profiles.unavailable")}</span>
                )}
              </div>
              <p className="mt-1 break-all text-xs text-default-500">{profile.dataDirectory}</p>
              {renaming === profile.id && (
                <form
                  className="mt-2 flex items-center gap-2"
                  onSubmit={(event) => {
                    event.preventDefault();
                    void run(
                      () =>
                        BApi.app.updateDesktopProfile(profile.id!, {
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
            </div>
            <div className="flex flex-wrap gap-1">
              {!profile.isCurrent && (
                <Button
                  isDisabled={busy || !profile.isAvailable}
                  size="sm"
                  variant="flat"
                  onPress={() => open(profile)}
                >
                  {t(profile.isRunning ? "profiles.activate" : "profiles.open")}
                </Button>
              )}
              {!profile.isCurrent && (
                <Button
                  isDisabled={busy || !profile.isAvailable}
                  size="sm"
                  variant="light"
                  onPress={() => open(profile, true)}
                >
                  {t("profiles.switch")}
                </Button>
              )}
              <Button
                isDisabled={busy}
                size="sm"
                variant="light"
                onPress={() => {
                  setRenaming(profile.id!);
                  setRename(label(profile));
                }}
              >
                {t("profiles.rename")}
              </Button>
              {!profile.isDefault && (
                <Button
                  isDisabled={busy}
                  size="sm"
                  variant="light"
                  onPress={() =>
                    void run(() =>
                      BApi.app.updateDesktopProfile(profile.id!, {
                        name: label(profile),
                        makeDefault: true,
                      }),
                    )
                  }
                >
                  {t("profiles.makeDefault")}
                </Button>
              )}
              {!profile.isCurrent && !profile.isRunning && (
                <Button
                  color="danger"
                  isDisabled={busy || (state.profiles?.length ?? 0) < 2}
                  size="sm"
                  variant="light"
                  onPress={() => setRemoving(profile)}
                >
                  {t("profiles.remove")}
                </Button>
              )}
            </div>
          </li>
        ))}
      </ul>
      {removing && (
        <div
          className="mt-2 rounded-lg border border-warning-200 bg-warning-50 p-3 text-sm"
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
                  () => setRemoving(undefined),
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
      <p className="mt-2 text-xs text-default-500">{t("profiles.upgrade")}</p>
    </section>
  );
}
