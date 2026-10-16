import type { ManagedPathMappingTarget } from "../managedPathMappingTarget";
import type { ManagedServerPathMapping } from "../types";
import type { ManagedPathMappingSnapshot } from "./ManagedServerPathMappings";
import type { DestroyableProps } from "@/components/bakaui/types";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import {
  loadManagedPathMappingTarget,
  mappingTreeLoader,
  saveManagedPathMappings,
} from "../managedPathMappingTarget";

import ManagedServerPathMappings from "./ManagedServerPathMappings";
import { ErrorNotice } from "./common";

import { Modal } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { FileSystemSelectorModal } from "@/components/FileSystemSelector";
import { nestedWindowModalClassNames, windowModalClassNames } from "@/components/Window/layers";

export { loadManagedPathMappingTarget } from "../managedPathMappingTarget";
export type { ManagedPathMappingTarget } from "../managedPathMappingTarget";

type Props = DestroyableProps & {
  target: ManagedPathMappingTarget;
  onSaved?: (mappings: ManagedServerPathMapping[]) => void;
};

/** The same editor used in device management, kept on the resource page when opened here. */
export default function ManagedPathMappingsModal(props: Props) {
  return <MappingDialog key={`${props.target.kind}:${props.target.serverId}`} {...props} />;
}

function MappingDialog({ target, onSaved, onDestroyed }: Props) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [server, setServer] = useState<ManagedPathMappingSnapshot>();
  const [error, setError] = useState<Error>();
  const [busy, setBusy] = useState(false);
  const [visible, setVisible] = useState(true);
  const mounted = useRef(true);
  const request = useRef<AbortController>();
  const loadTree = useMemo(() => mappingTreeLoader(target), [target.kind, target.serverId]);
  const chooseLocal = useCallback(
    (path: string, onSelected: (path: string) => void) => {
      createPortal(FileSystemSelectorModal, {
        targetType: "folder",
        startPath: path || undefined,
        defaultSelectedPath: path || undefined,
        modalClassNames: nestedWindowModalClassNames,
        onSelected: (entry) => onSelected(entry.path),
      });
    },
    [createPortal],
  );
  const load = useCallback(async () => {
    request.current?.abort();
    const current = new AbortController();

    request.current = current;
    setError(undefined);
    try {
      const fresh = await loadManagedPathMappingTarget(target, current.signal);

      if (mounted.current && request.current === current && !current.signal.aborted)
        setServer(fresh);
    } catch (failure) {
      if (mounted.current && request.current === current && !current.signal.aborted)
        setError(failure instanceof Error ? failure : new Error(String(failure)));
    }
  }, [target.kind, target.serverId]);

  useEffect(() => {
    mounted.current = true;
    void load();

    return () => {
      mounted.current = false;
      request.current?.abort();
    };
  }, [load]);

  const save = async (
    mappings: ManagedServerPathMapping[],
    expectedMappings?: ManagedServerPathMapping[],
  ) => {
    if (!server || !expectedMappings) return false;
    setBusy(true);
    try {
      await saveManagedPathMappings(target, mappings, expectedMappings);
      if (!mounted.current) return false;
      setServer((previous) => previous && { ...previous, pathMappings: mappings });
      onSaved?.(mappings);

      return true;
    } catch (failure) {
      // Reload before the editor offers an explicit conflict review. Never replace a
      // concurrent edit with the stale snapshot that opened this dialog.
      await load();
      throw failure;
    } finally {
      if (mounted.current) setBusy(false);
    }
  };

  return (
    <Modal
      classNames={windowModalClassNames}
      footer={{ actions: ["cancel"], cancelProps: { isDisabled: busy } }}
      hideCloseButton={busy}
      isDismissable={!busy}
      isKeyboardDismissDisabled={busy}
      size="xl"
      title={t("federation.managedMappings.dialog.title", {
        name: server?.name || target.serverId,
      })}
      visible={visible}
      onClose={() => {
        if (!busy) setVisible(false);
      }}
      onDestroyed={onDestroyed}
    >
      <ErrorNotice error={error} onRetry={() => void load()} />
      {!server && !error && <p role="status">{t("federation.loading")}</p>}
      {server && (
        <>
          {target.kind === "relay" && (
            <p className="text-xs text-default-500">
              {t("federation.managedMappings.relayLocalPath")}
            </p>
          )}
          <ManagedServerPathMappings
            expanded
            busy={busy}
            chooseLocal={target.kind === "relay" ? false : chooseLocal}
            className="pb-2"
            loadTree={loadTree}
            server={server}
            targetKind={target.kind}
            onSave={save}
          />
        </>
      )}
    </Modal>
  );
}
