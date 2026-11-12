import { createContext, useContext, useEffect, type ReactNode } from "react";
import { createPortal } from "react-dom";

type ActionHost = {
  container: HTMLElement | null;
  disabled: boolean;
  active: boolean;
  onBusyChange?: (busy: boolean) => void;
};

/** Activity-owned actions keep their signals and validation, while the task owns the footer. */
export const ResumeActionsContext = createContext<ActionHost | undefined>(undefined);

/** Preserve ancestor guards when buttons are portalled outside a disabled/hidden form. */
export function ResumeActionsGuard({ disabled, hidden = false, children }: {
  disabled: boolean;
  hidden?: boolean;
  children: ReactNode;
}) {
  const host = useContext(ResumeActionsContext);
  return <ResumeActionsContext.Provider value={host && {
    ...host, disabled: host.disabled || disabled, active: host.active && !hidden,
  }}>{children}</ResumeActionsContext.Provider>;
}

export default function ResumeActions({ children, cancel, busy = false }: {
  children: ReactNode;
  cancel?: ReactNode;
  busy?: boolean;
}) {
  const host = useContext(ResumeActionsContext);
  const busyChanged = host?.active ? host.onBusyChange : undefined;
  useEffect(() => {
    busyChanged?.(busy);
    return () => busyChanged?.(false);
  }, [busy, busyChanged]);
  if (host) {
    if (!host.active || !host.container) return null;
    return createPortal(
      <fieldset disabled={host.disabled} className="flex min-w-0 flex-wrap items-center justify-end gap-2">
        {children}
      </fieldset>, host.container,
    );
  }
  return <div className="flex flex-wrap items-center justify-end gap-2">{cancel}{children}</div>;
}
