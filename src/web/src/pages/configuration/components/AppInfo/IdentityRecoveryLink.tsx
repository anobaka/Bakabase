import { Link } from "react-router-dom";
import { useTranslation } from "react-i18next";

import { useIsPureClient, useRemoteAccessStore } from "@/stores/remoteAccess";

/** Configuration describes the host's data, so remote clients must act on that host. */
export default function IdentityRecoveryLink() {
  const { t } = useTranslation();
  const local = useRemoteAccessStore((state) => state.initialized && state.isLocal);
  const pureClient = useIsPureClient();

  return local && !pureClient ? (
    <Link className="text-xs text-primary underline" to="/federation/devices?section=identity">
      {t("configuration.appInfo.identityRecovery.link")}
    </Link>
  ) : (
    <span className="text-xs text-foreground-400">
      {t("configuration.appInfo.identityRecovery.onHost")}
    </span>
  );
}
