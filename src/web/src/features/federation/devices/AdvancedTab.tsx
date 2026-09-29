import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";

import { buttonClass, panelClass } from "../components/common";
import { revealClass } from "../hooks/useSectionReveal";
import { federationPeerApi } from "../peerApi";

import { useDevicesPage } from "./context";
import TabHeading from "./TabHeading";

type IdentityCase = "copied" | "restored";

/**
 * Advanced: the device ID, and what to do on the computer whose data was copied from
 * another one or restored from a backup. The two cases are chosen first, then the one
 * action that case needs is offered, with what it clears and what it keeps — no button
 * is shown before the reader has said what happened.
 */
export default function AdvancedTab() {
  const { t } = useTranslation();
  const { data, busy, confirm, mounted, sharingForm, revealed } = useDevicesPage();
  const [chosen, setChosen] = useState<IdentityCase>();
  const [copied, setCopied] = useState<boolean>();
  const copyTimer = useRef<ReturnType<typeof setTimeout>>();
  const { status } = data;

  useEffect(() => () => clearTimeout(copyTimer.current), []);

  if (!status) return <TabHeading />;
  const nodeId = status.identity.nodeId;
  const copyId = async () => {
    let ok = true;

    try {
      await navigator.clipboard.writeText(nodeId);
    } catch {
      ok = false;
    }
    if (!mounted.current) return;
    setCopied(ok);
    clearTimeout(copyTimer.current);
    copyTimer.current = setTimeout(() => setCopied(undefined), 2000);
  };
  const reset = (identityCase: IdentityCase) =>
    identityCase === "copied"
      ? confirm(t("federation.identity.reset"), t("federation.identity.confirm"), async () => {
          // A copy: the install's own identity goes too, so nothing takes it for the original.
          await federationPeerApi.resetIdentity(true, true);
          if (mounted.current) sharingForm.setInvite(undefined);
        })
      : confirm(
          t("federation.identity.restore"),
          t("federation.identity.restoreConfirm"),
          async () => {
            await federationPeerApi.resetIdentity(false);
            if (mounted.current) sharingForm.setInvite(undefined);
          },
          t("federation.identity.restoreWarning"),
        );

  return (
    <>
      <TabHeading />
      <section className={`${panelClass} space-y-2`} data-testid="device-id" id="device-id">
        <h3 className="font-semibold">{t("federation.devices.id.title")}</h3>
        <div className="flex flex-wrap items-center gap-2">
          <code className="break-all font-mono text-sm">{nodeId}</code>
          <button
            aria-label={t("federation.devices.id.copy")}
            className={`${buttonClass} !px-2 !py-1 text-xs`}
            type="button"
            onClick={() => void copyId()}
          >
            {t(
              copied === undefined
                ? "federation.copy"
                : copied
                  ? "federation.copied"
                  : "federation.copyFailed",
            )}
          </button>
          {/* The button keeps its name; how the copy went is said here. */}
          <span className="sr-only" role="status">
            {copied === undefined ? "" : t(copied ? "federation.copied" : "federation.copyFailed")}
          </span>
        </div>
        <p className="text-xs text-default-500">{t("federation.devices.id.tip")}</p>
      </section>
      <section
        data-focus-section
        aria-labelledby="federation-identity-title"
        className={`${panelClass} space-y-3 ${revealClass(revealed === "identity")}`}
        data-highlighted={revealed === "identity" || undefined}
        data-testid="identity-recovery"
        id="federation-identity"
        tabIndex={-1}
      >
        <h3 className="font-semibold outline-none" id="federation-identity-title" tabIndex={-1}>
          {t("federation.identity.title")}
        </h3>
        <p className="text-sm text-default-500">{t("federation.identity.tip")}</p>
        <fieldset className="space-y-2">
          <legend className="text-sm font-medium">{t("federation.identity.question")}</legend>
          {(["copied", "restored"] as const).map((identityCase) => (
            <label
              key={identityCase}
              className="flex items-start gap-2 rounded-lg border border-default-200 p-3 text-sm"
            >
              <input
                checked={chosen === identityCase}
                className="mt-1"
                name="federation-identity-case"
                type="radio"
                value={identityCase}
                onChange={() => setChosen(identityCase)}
              />
              <span>{t(`federation.identity.${identityCase}.title`)}</span>
            </label>
          ))}
        </fieldset>
        {chosen && (
          <div className="space-y-2 rounded-lg bg-default-50 p-3 text-sm" data-case={chosen}>
            <p>{t(`federation.identity.${chosen}.removes`)}</p>
            <p>{t(`federation.identity.${chosen}.keeps`)}</p>
            <p className="text-default-500">{t(`federation.identity.${chosen}.after`)}</p>
            <button
              className={`${buttonClass} text-danger`}
              disabled={busy}
              type="button"
              onClick={() => reset(chosen)}
            >
              {t(chosen === "copied" ? "federation.identity.reset" : "federation.identity.restore")}
            </button>
          </div>
        )}
      </section>
      <Link className="inline-block text-xs text-primary underline" to="/configuration">
        {t("federation.management.allSettings")}
      </Link>
    </>
  );
}
