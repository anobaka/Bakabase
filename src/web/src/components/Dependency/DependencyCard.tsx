import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Drawer, DrawerBody, DrawerContent, DrawerFooter, DrawerHeader } from "@heroui/react";
import { Button, Chip } from "@/components/bakaui";
import DependencyManager from "./DependencyManager";
import {
  dependencyDisplayName,
  dependencyPresentation,
  LocaleEmulatorId,
  PythonRuntimeId,
  type DependencyContext,
} from "./presentation";
import PlatformTags from "./PlatformTags";
import PythonRuntimeConfiguration from "./PythonRuntimeConfiguration";
import { useIsPureClient, useRemoteAccessStore } from "@/stores/remoteAccess";

export default function DependencyCard({
  context,
  focused = false,
}: {
  context: DependencyContext;
  focused?: boolean;
}) {
  const { t } = useTranslation();
  const isPureClient = useIsPureClient();
  const serverName = useRemoteAccessStore((state) => state.serverName);
  const [open, setOpen] = useState(false);
  const { Icon, purpose, group } = dependencyPresentation(context, t);
  const name = dependencyDisplayName(context, t);
  return (
    <>
      <article
        tabIndex={-1}
        aria-label={name}
        data-component-id={context.id}
        className={`flex h-full min-w-0 flex-col gap-2 rounded-xl border bg-content1 p-3 shadow-sm outline-none ${focused ? "border-primary ring-2 ring-primary/25" : "border-default-200"}`}
      >
        <header className="flex h-9 min-w-0 items-center gap-2.5">
          <span
            aria-hidden
            className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-default-100 text-lg text-default-600"
          >
            <Icon />
          </span>
          <h3 className="min-w-0 truncate text-sm font-semibold" title={name}>
            {name}
          </h3>
        </header>
        <p className="line-clamp-2 h-8 text-xs leading-4 text-default-500" title={purpose}>
          {purpose}
        </p>
        <div
          className="flex min-h-12 flex-wrap content-start items-start gap-1"
          aria-label={t("dependencies.capabilities")}
        >
          <Chip size="sm" variant="flat" color={context.isRequired ? "primary" : "default"}>
            {t(context.isRequired ? "dependencies.required" : "dependencies.optional")}
          </Chip>
          <Chip size="sm" variant="bordered">
            {t(`dependencies.group.${group}`)}
          </Chip>
          <PlatformTags context={context} />
        </div>
        <DependencyManager id={context.id} mode="summary" onDetails={() => setOpen(true)} />
      </article>
      <Drawer isOpen={open} placement="right" size="lg" onClose={() => setOpen(false)}>
        <DrawerContent>
          <DrawerHeader className="flex flex-col gap-1">
            <span>{name}</span>
            <span className="text-xs font-normal text-default-500">
              {t("dependencies.server", { name: serverName || t("dependencies.currentServer") })}
            </span>
          </DrawerHeader>
          <DrawerBody className="gap-5 pb-6">
            <p className="text-sm leading-relaxed text-default-600">{purpose}</p>
            {context.id === PythonRuntimeId && open && <PythonRuntimeConfiguration />}
            {isPureClient && context.id === LocaleEmulatorId && (
              <p className="rounded-lg bg-warning-50 p-3 text-xs text-warning-700">
                {t("configuration.dependency.localeEmulatorRunsOnThisMachine")}
              </p>
            )}
            <DependencyManager id={context.id} mode="details" />
            {context.supportedTargets?.length ? (
              <section className="space-y-2">
                <h3 className="text-sm font-semibold">{t("dependencies.platformDetails")}</h3>
                <ul className="space-y-2 text-xs text-default-500">
                  {context.supportedTargets.map((target, index) => (
                    <li
                      key={`${target.os}:${target.architecture}:${index}`}
                      className="rounded-lg bg-default-50 p-3"
                    >
                      <p>
                        {t(`dependencies.os.${target.os}`, { defaultValue: target.os })} ·{" "}
                        {target.architecture}
                        {target.runtime && ` · ${target.runtime}`}
                        {target.minimumVersion && ` · ≥ ${target.minimumVersion}`}
                      </p>
                      {target.canAutoInstall === false && (
                        <p className="mt-1 text-warning-600">
                          {t("dependencies.manualInstallation")}
                        </p>
                      )}
                      {target.notes && <p className="mt-1">{target.notes}</p>}
                    </li>
                  ))}
                </ul>
              </section>
            ) : null}
          </DrawerBody>
          <DrawerFooter>
            <Button variant="light" onPress={() => setOpen(false)}>
              {t("common.action.close")}
            </Button>
          </DrawerFooter>
        </DrawerContent>
      </Drawer>
    </>
  );
}
